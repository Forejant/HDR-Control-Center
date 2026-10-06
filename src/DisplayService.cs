using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HdrCenter {
public sealed class MonitorInfo {
    public string Key, Name, Gdi;
    public Native.Luid Adapter;
    public uint Target;
    public bool Modern, Supported, Enabled, Active;
    public int White = -1;
    public string Error;
    public void UpdateState(MonitorInfo current) {
        Adapter=current.Adapter; Target=current.Target; Modern=current.Modern;
        Supported=current.Supported; Enabled=current.Enabled; Active=current.Active;
        White=current.White; Error=current.Error;
    }
    public override string ToString() { return Name + "  ·  " + Gdi.Replace("\\\\.\\", ""); }
}
public static class Native {
    [StructLayout(LayoutKind.Sequential)] public struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] public struct Header { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] public struct Source { public Luid Adapter; public uint Id, Mode, Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct Target { public Luid Adapter; public uint Id, Mode, Technology, Rotation, Scaling, RefreshN, RefreshD, Scan; public int Available; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct Path { public Source Source; public Target Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, Size=64)] public struct Mode { public uint Type, Id; public Luid Adapter; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct TargetName {
        public Header Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=64)] public string Friendly;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct SourceName {
        public Header Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string Gdi;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Color { public Header Header; public uint Flags, Encoding, Bits; }
    [StructLayout(LayoutKind.Sequential)] public struct Color2 { public Header Header; public uint Flags, Encoding, Bits, ActiveMode; }
    [StructLayout(LayoutKind.Sequential)] public struct State { public Header Header; public uint Value; }
    [StructLayout(LayoutKind.Sequential)] public struct White { public Header Header; public uint Value; }
    [StructLayout(LayoutKind.Sequential, Pack=4)] public struct SetWhite { public Header Header; public uint Value; public byte Final; }
    [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] Path[] p, ref uint modes, [Out] Mode[] m, IntPtr topology);
    [DllImport("user32.dll", EntryPoint="DisplayConfigGetDeviceInfo")] public static extern int Get(ref TargetName p);
    [DllImport("user32.dll", EntryPoint="DisplayConfigGetDeviceInfo")] public static extern int Get(ref SourceName p);
    [DllImport("user32.dll", EntryPoint="DisplayConfigGetDeviceInfo")] public static extern int Get(ref Color p);
    [DllImport("user32.dll", EntryPoint="DisplayConfigGetDeviceInfo")] public static extern int Get(ref Color2 p);
    [DllImport("user32.dll", EntryPoint="DisplayConfigGetDeviceInfo")] public static extern int Get(ref White p);
    [DllImport("user32.dll", EntryPoint="DisplayConfigSetDeviceInfo")] public static extern int Set(ref State p);
    [DllImport("user32.dll", EntryPoint="DisplayConfigSetDeviceInfo")] public static extern int Set(ref SetWhite p);
    public static Header H(uint type, Type structure, Luid adapter, uint id) {
        return new Header { Type=type, Size=(uint)Marshal.SizeOf(structure), Adapter=adapter, Id=id };
    }
    public static void Check(int result, string action) { if (result!=0) throw new Win32Exception(result, action + ": " + new Win32Exception(result).Message); }
}
public static class DisplayService {
    public static string TargetKey(string path,Native.Luid adapter,uint target) {
        return String.IsNullOrEmpty(path)?adapter.High+":"+adapter.Low+":"+target:path;
    }
    public static List<MonitorInfo> List() {
        for (int attempt=0; attempt<5; attempt++) {
            uint pc, mc; Native.Check(Native.GetDisplayConfigBufferSizes(2, out pc, out mc), "读取显示器数量");
            var p=new Native.Path[pc]; var modes=new Native.Mode[mc];
            int result=Native.QueryDisplayConfig(2, ref pc, p, ref mc, modes, IntPtr.Zero);
            if (result==122) continue;
            Native.Check(result, "读取显示器");
            var list=new List<MonitorInfo>(); var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i=0; i<pc; i++) {
                var t=new Native.TargetName { Header=Native.H(2, typeof(Native.TargetName), p[i].Target.Adapter, p[i].Target.Id) };
                Native.Check(Native.Get(ref t), "读取显示器名称");
                var s=new Native.SourceName { Header=Native.H(1, typeof(Native.SourceName), p[i].Source.Adapter, p[i].Source.Id) };
                Native.Check(Native.Get(ref s), "读取显示器编号");
                string key=TargetKey(t.DevicePath,p[i].Target.Adapter,p[i].Target.Id);
                if (!keys.Add(key)) continue;
                var m=new MonitorInfo { Key=key, Name=String.IsNullOrEmpty(t.Friendly)?"显示器":t.Friendly, Gdi=s.Gdi, Adapter=p[i].Target.Adapter, Target=p[i].Target.Id };
                Read(m); list.Add(m);
            }
            return list;
        }
        throw new Exception("显示器连接正在变化，请稍后刷新。");
    }
    public static void Read(MonitorInfo m) {
        m.Error=null; m.White=-1;
        var c2=new Native.Color2 { Header=Native.H(15, typeof(Native.Color2), m.Adapter, m.Target) };
        int r=Native.Get(ref c2); m.Modern=r==0;
        if (r==0) { m.Supported=(c2.Flags & 16)!=0; m.Enabled=(c2.Flags & 32)!=0; m.Active=c2.ActiveMode==2; }
        else {
            var c=new Native.Color { Header=Native.H(9, typeof(Native.Color), m.Adapter, m.Target) };
            r=Native.Get(ref c);
            if(r!=0) { m.Supported=false; m.Enabled=false; m.Active=false; m.Error="HDR 状态不可读取 ("+r+")"; return; }
            m.Supported=(c.Flags & 1)!=0 && (c.Flags & 8)==0;
            m.Enabled=(c.Flags & 2)!=0; m.Active=m.Enabled;
        }
        var w=new Native.White { Header=Native.H(11, typeof(Native.White), m.Adapter, m.Target) };
        if(Native.Get(ref w)==0) m.White=(int)w.Value;
    }
    public static MonitorInfo Resolve(string key) {
        foreach(var m in List()) if(String.Equals(m.Key,key,StringComparison.OrdinalIgnoreCase)) return m;
        throw new Exception("所选显示器已断开，请重新选择。操作未应用到其他显示器。");
    }
    public static MonitorInfo Hdr(string key, bool enabled) {
        var m=Resolve(key);
        if(!m.Supported) throw new Exception("该显示器当前不支持 HDR，或 HDR 被系统策略限制。");
        var p=new Native.State { Header=Native.H(m.Modern?16u:10u, typeof(Native.State), m.Adapter, m.Target), Value=enabled?1u:0u };
        Native.Check(Native.Set(ref p),"切换 HDR");
        // Read immediately, then wait only while the system is still applying.
        // Resolve on every poll also detects hotplug instead of reusing an ID.
        var wait=System.Diagnostics.Stopwatch.StartNew();
        do {
            m=Resolve(key);
            if(m.Error==null && m.Enabled==enabled)return m;
            System.Threading.Thread.Sleep(50);
        } while(wait.ElapsedMilliseconds<1500);
        throw new Exception("系统接受了请求，但 HDR 状态尚未达到目标。请刷新或检查 Windows 设置。");
    }
    public static int Percent(uint white) { return Math.Max(0,Math.Min(100,(int)Math.Round((white-1000.0)/50.0))); }
    public static uint Level(int percent) { return (uint)(1000+Math.Max(0,Math.Min(100,percent))*50); }
    public static void Brightness(string key, int percent) {
        Brightness(key,percent,true);
    }
    public static void Brightness(string key,int percent,bool verify) {
        Brightness(new BrightnessTarget(Resolve(key)),percent,verify);
    }
    public static void Brightness(BrightnessTarget target,int percent,bool verify) {
        ValidateBrightnessTarget(target);
        // Windows internal SET_SDR_WHITE_LEVEL. Never fall back to registry edits.
        // Every intermediate position must be committed to make it visible now.
        // 'verify' controls the extra readback, not whether the value is applied.
        var p=new Native.SetWhite { Header=Native.H(0xffffffee, typeof(Native.SetWhite), target.Adapter,target.Id), Value=Level(percent), Final=1 };
        Native.Check(Native.Set(ref p),"调整 SDR 内容亮度");
        if(verify) {
            var w=new Native.White { Header=Native.H(11,typeof(Native.White),target.Adapter,target.Id) };
            Native.Check(Native.Get(ref w),"确认 SDR 内容亮度");
            if(Math.Abs((int)w.Value-(int)p.Value)>55) throw new Exception("亮度写入后未能确认。请在 Windows HDR 设置中检查。");
        }
    }
    public static void ValidateBrightnessTarget(BrightnessTarget target) {
        var name=new Native.TargetName { Header=Native.H(2,typeof(Native.TargetName),target.Adapter,target.Id) };
        Native.Check(Native.Get(ref name),"校验所选显示器");
        if(!String.Equals(target.Key,TargetKey(name.DevicePath,target.Adapter,target.Id),StringComparison.OrdinalIgnoreCase))
            throw new Exception("所选显示器连接已变化，请重新选择。亮度未应用到其他显示器。");
        var state=new Native.Color2 { Header=Native.H(15,typeof(Native.Color2),target.Adapter,target.Id) };
        int result=Native.Get(ref state);
        if(result==0) { if(state.ActiveMode!=2)throw new Exception("请先启用所选显示器的 HDR。"); }
        else {
            var old=new Native.Color { Header=Native.H(9,typeof(Native.Color),target.Adapter,target.Id) };
            Native.Check(Native.Get(ref old),"校验所选显示器 HDR");
            if((old.Flags & 2)==0)throw new Exception("请先启用所选显示器的 HDR。");
        }
    }
    public static uint BrightnessLevel(BrightnessTarget target) {
        var value=new Native.White { Header=Native.H(11,typeof(Native.White),target.Adapter,target.Id) };
        Native.Check(Native.Get(ref value),"读取所选 SDR 内容亮度"); return value.Value;
    }
}
public sealed class BrightnessTarget {
    public readonly string Key; public readonly Native.Luid Adapter; public readonly uint Id;
    public BrightnessTarget(MonitorInfo monitor) { Key=monitor.Key; Adapter=monitor.Adapter; Id=monitor.Target; }
}
}
