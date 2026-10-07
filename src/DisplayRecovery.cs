using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace HdrCenter {
// Schedule recovery from connection identity and display wake events. HDR readiness
// and generic mode changes also occur during ordinary HDR toggles, so neither is a reconnect.
public sealed class DolbyGuard {
    public bool Enabled { get; private set; }
    public bool Running { get; private set; }
    public bool Pending { get; private set; }
    public string Key { get; private set; }
    public string Reason { get; private set; }
    DateTime due;
    string signature;
    bool observed, displayOff, manualPending;
    int attempts, generation, runningGeneration;
    public void Configure(bool enabled,string key,DateTime now) {
        if(Enabled==enabled && Key==key)return;
        Enabled=enabled; Key=key; generation++; Pending=false; manualPending=false; observed=false; attempts=0;
        if(enabled && !String.IsNullOrEmpty(key)) Request("启用守护 / 启动恢复",now,true);
    }
    public void Request(string reason,DateTime now,bool physical,bool manual=false) {
        if(!Enabled || String.IsNullOrEmpty(Key) || !physical)return;
        if(Running || (Pending && manualPending && !manual))return;
        if(!Pending)attempts=0;
        Reason=reason; Pending=true; manualPending=manual; due=now.AddSeconds(2);
    }
    public void Observe(string current,bool ready,DateTime now,bool? hdrEnabled=null) {
        bool changed=observed && signature!=current;
        signature=current; observed=true;
        if(changed && current!=null)Request("显示器重新连接",now,true);
        // Do not defer automatic work from HDR OFF until the next HDR ON.
        // An explicit repair remains queued; an unavailable screen is not known OFF.
        if(hdrEnabled==false && Pending && !manualPending) { Pending=false; attempts=0; }
        // While absent, retain the pending request; never act on a different screen.
    }
    public void Power(int state,DateTime now) {
        if(state==0)displayOff=true;
        else if(state==1 && displayOff) { displayOff=false; Request("显示器重新点亮",now,true); }
    }
    public bool TryStart(bool ready,DateTime now) {
        if(!Enabled || Running || !Pending || !ready || now<due)return false;
        Running=true; Pending=false; attempts++; runningGeneration=generation; return true;
    }
    public void Finish(bool success,DateTime now) {
        Running=false;
        if(runningGeneration!=generation) { if(Enabled)Request("显示器选择已更新",now,true); return; }
        if(!success && Enabled && attempts<3) { Pending=true; due=now.AddSeconds(5*attempts); }
        else manualPending=false;
    }
    public void Defer(DateTime now) { if(Pending)due=now.AddSeconds(2); }
}
public static class WindowGeometry {
    public static int Pixels(double logical,int dpi) { return (int)Math.Round(logical*Math.Max(96,dpi)/96.0); }
    public static Rectangle Fit(Rectangle desired,Rectangle area,int margin) {
        margin=Math.Max(0,Math.Min(margin,Math.Min(area.Width,area.Height)/4));
        int width=Math.Max(1,Math.Min(desired.Width,area.Width-2*margin));
        int height=Math.Max(1,Math.Min(desired.Height,area.Height-2*margin));
        return new Rectangle(Math.Max(area.Left+margin,Math.Min(desired.Left,area.Right-margin-width)),
            Math.Max(area.Top+margin,Math.Min(desired.Top,area.Bottom-margin-height)),width,height);
    }
}
public static class DisplayEvents {
    public static readonly Guid SessionDisplay=new Guid("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
    [DllImport("user32.dll",SetLastError=true)] public static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient,ref Guid setting,uint flags);
    [DllImport("user32.dll")] public static extern bool UnregisterPowerSettingNotification(IntPtr handle);
    [DllImport("wtsapi32.dll")] public static extern bool WTSRegisterSessionNotification(IntPtr handle,uint flags);
    [DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(IntPtr handle);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool AreDpiAwarenessContextsEqual(IntPtr a,IntPtr b);
}
public static class DolbyReset {
    // Always issue ON then OFF, even when Windows reports OFF before the reset.
    // Attempt OFF in a finally block if enabling or its verification fails.
    public static void Execute(Action<bool> set,Action settle) {
        Exception primary=null;
        try { set(true); settle(); }
        catch(Exception ex) { primary=ex; }
        finally {
            try { set(false); }
            catch(Exception off) { throw new Exception("无法确认杜比视界已关闭，请检查 Windows HDR 设置。",primary==null?off:new AggregateException(primary,off)); }
        }
        if(primary!=null)throw new Exception("开启阶段未能确认；已执行关闭，守护将稍后重试。",primary);
    }
}
}
