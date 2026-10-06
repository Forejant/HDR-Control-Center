using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace HdrCenter {
// Read-only driver configuration. Never writes registry settings: the NVIDIA
// control panel remains responsible for applying and notifying the driver.
public static class NvidiaSavedSettings {
    const string ClassKey=@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    public static bool? Decode(bool super,uint value,uint marker) {
        if(marker!=0x80000001u)return null;
        // VSR stores its quality: 0=off, 1..4=manual quality, 5=Auto.
        if(super)return value<=5?(bool?)(value!=0):null;
        // Recognized video-HDR modes: off, enabled and the Auto-enabled mode.
        if(value==0)return false;
        return value==1 || value==5?(bool?)true:null;
    }
    static uint? Dword(RegistryKey key,string name) {
        object value=key.GetValue(name,null);
        if(value==null || key.GetValueKind(name)!=RegistryValueKind.DWord || !(value is int))return null;
        return unchecked((uint)(int)value);
    }
    static Feature ReadFeature(RegistryKey key,bool super) {
        string name=super?"SuperResolution":"TrueHDR";
        uint? value=Dword(key,"_User_Global_VAL_"+name),marker=Dword(key,"_User_Global_XEN_"+name);
        bool? enabled=value.HasValue && marker.HasValue?Decode(super,value.Value,marker.Value):null;
        return new Feature { Enabled=enabled,CanControl=enabled.HasValue,Activity="活动状态未检测",Detail=enabled.HasValue?"从 NVIDIA 驱动保存配置读取（控制面板可关闭）":"驱动未提供已识别的 "+name+" 保存配置" };
    }
    public static VideoState Read() {
        try {
            using(var root=Registry.LocalMachine.OpenSubKey(ClassKey)) {
                if(root==null)return Unknown("未找到显示适配器配置");
                var adapters=new List<string>();
                foreach(string name in root.GetSubKeyNames()) {
                    int index; if(name.Length!=4 || !Int32.TryParse(name,out index))continue;
                    try { using(var key=root.OpenSubKey(name)) {
                        if(key==null)continue;
                        string description=key.GetValue("DriverDesc","") as string;
                        if(description!=null && description.IndexOf("NVIDIA",StringComparison.OrdinalIgnoreCase)>=0)adapters.Add(name);
                    } } catch(System.Security.SecurityException) { } catch(UnauthorizedAccessException) { }
                }
                if(adapters.Count!=1)return Unknown(adapters.Count==0?"没有可读取的 NVIDIA 显卡配置":"存在多块 NVIDIA 显卡，保存配置不能唯一匹配；使用控制面板回读");
                using(var key=root.OpenSubKey(adapters[0]))return new VideoState { Super=ReadFeature(key,true),Hdr=ReadFeature(key,false) };
            }
        } catch(Exception ex) { return Unknown("NVIDIA 保存配置读取失败："+ex.Message); }
    }
    static VideoState Unknown(string detail) { return new VideoState { Super=new Feature { Detail=detail },Hdr=new Feature { Detail=detail } }; }
    public static VideoState Merge(VideoState ui,VideoState saved) {
        // Prefer current activity from the panel. Saved values fill only missing
        // switches, and never masquerade as a realtime playback status.
        return new VideoState { Super=ui.Super.Enabled.HasValue?ui.Super:saved.Super.Enabled.HasValue?saved.Super:ui.Super,
            Hdr=ui.Hdr.Enabled.HasValue?ui.Hdr:saved.Hdr.Enabled.HasValue?saved.Hdr:ui.Hdr };
    }
}
}
