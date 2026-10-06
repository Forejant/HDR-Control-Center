using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace HdrCenter {
public sealed partial class Center {
    sealed class BrightnessCall { public string Key; public uint Adapter; public int Value; public bool Final; }
    public void RealtimePopupTest(string path,bool waitForFocus=false) {
        autoHideSuppressed=true; Preview(null,false); geometryTimer.Stop(); var lines=new List<string>();
        if(waitForFocus) {
            File.WriteAllText(path+".ready","Awaiting explicit desktop activation");
            var watch=Stopwatch.StartNew(); while(GetForegroundWindow()!=Handle && watch.ElapsedMilliseconds<15000)Pump(50);
            RequireLayout(GetForegroundWindow()==Handle,"Desktop activation establishes valid test focus");
            Pump(400); // Let the activating click/key release before drag capture.
        }
        PanelTrace=line=>File.AppendAllText(path+".trace.txt",line+Environment.NewLine);
        var calls=new List<BrightnessCall>(); int activeWrites=0,peakWrites=0;
        using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)) {
            brightnessWriter=(target,value,final)=> {
                int count=Interlocked.Increment(ref activeWrites); peakWrites=Math.Max(peakWrites,count);
                try {
                    lock(calls)calls.Add(new BrightnessCall { Key=target.Key,Adapter=target.Adapter.Low,Value=value,Final=final });
                    if(!entered.WaitOne(0)) { entered.Set(); if(!release.WaitOne(3000))throw new Exception("Test writer timed out"); }
                    Thread.Sleep(20);
                } finally { Interlocked.Decrement(ref activeWrites); }
            };
            SliderMouse("OnMouseDown",25); RequireLayout(entered.WaitOne(1000),"Drag starts a write immediately");
            uint firstAdapter=Selected.Adapter.Low; Selected.Adapter=new Native.Luid { Low=42 };
            for(int i=30;i<=68;i++) { slider.Value=i; QueueBrightness(); }
            brightnessTimer.Stop(); release.Set();
            WaitBrightness(()=> { lock(calls)return calls.Count>=2 && calls[calls.Count-1].Value==68; });
            RequireLayout(slider.IsDragging,"Latest pending value is drained before mouse release");
            lock(calls)RequireLayout(calls.Count==2 && calls[0].Adapter==firstAdapter && calls[1].Adapter==42 && calls.All(c=>!c.Final),"Stale values are coalesced and targets are immutable snapshots");
            SliderMouse("OnMouseUp",68); WaitBrightness(()=>!brightnessWriting && !pendingBrightness.HasValue);
            lock(calls)RequireLayout(calls[calls.Count-1].Final && calls[calls.Count-1].Value==slider.Value,"Release writes and verifies the exact final value");
            RequireLayout(peakWrites==1,"Driver writes never overlap");
            lines.Add("PASS: first drag write immediate, pending latest value drained during drag with timer stopped, 39 intermediate requests coalesced, immutable targets, one concurrent writer, final release verification.");
        }
        using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)) {
            calls.Clear(); int attempt=0;
            brightnessWriter=(target,value,final)=> {
                lock(calls)calls.Add(new BrightnessCall { Key=target.Key,Value=value,Final=final });
                if(Interlocked.Increment(ref attempt)==1) { entered.Set(); if(!release.WaitOne(3000))throw new Exception("Test writer timed out"); throw new Exception("Expected disconnected old target"); }
            };
            slider.Value=40; QueueBrightness(); RequireLayout(entered.WaitOne(1000),"Old target writing");
            var replacement=new MonitorInfo { Key="new-target",Name="New display",Gdi=@"\\.\DISPLAY1",Supported=true,Enabled=true,Active=true,White=2500 };
            BindMonitors(new List<MonitorInfo>{replacement},replacement.Key); Render(); slider.Value=61; QueueBrightness();
            brightnessTimer.Stop(); release.Set(); WaitBrightness(()=>!brightnessWriting && !pendingBrightness.HasValue);
            lock(calls)RequireLayout(calls.Count==2 && calls[1].Key==replacement.Key && calls[1].Value==61,"Old-target failure never discards a new monitor's pending request");
            lines.Add("PASS: a disconnected old display cannot erase the pending update to a newly selected display.");
        }
        brightnessWriter=null; brightnessTimer.Stop(); autoHideSuppressed=false;
        var bounds=Bounds; var handle=Handle; int hidden=0;
        VisibleChanged+=delegate { if(!Visible)hidden++; };
        var area=Screen.FromRectangle(Bounds).WorkingArea;
        using(var other=new Form { Text="外部窗口测试（示例）",StartPosition=FormStartPosition.Manual,Location=new Point(area.Left+40,area.Top+40),Size=new Size(200,120) }) {
            other.Show(); other.Activate(); Pump(220);
            RequireLayout(!Visible && !IsWindowVisible(handle) && !IsDisposed && hidden==1 && outsideMouseHook==IntPtr.Zero && Bounds==bounds,"External focus hides once to tray, releases mouse observation, preserves HWND and geometry");
            HandleTrayClick(Environment.TickCount); Pump(120);
            RequireLayout(PanelPresented && outsideMouseHook!=IntPtr.Zero,"One tray click reopens and resumes outside-click observation");
            using(var owned=new Form { Text="所属窗口测试（示例）",ShowInTaskbar=false }) {
                owned.Show(this); owned.Activate(); Pump(180);
                RequireLayout(Visible && PanelWindowFamily(owned.Handle),"Owned dialog does not dismiss the popup: visible="+Visible+" family="+PanelWindowFamily(owned.Handle)+" panel="+Handle+" dialog="+owned.Handle+" owner="+GetWindow(owned.Handle,4)+" root="+GetAncestor(owned.Handle,3)+" foreground="+GetForegroundWindow()+" busy="+busy);
                ObserveOutsideClick(new Point(owned.Left+30,owned.Top+50)); Pump(100); RequireLayout(Visible,"Owned-dialog click stays open");
                owned.Close(); Pump(100);
            }
            Activate(); Act(()=> { Thread.Sleep(800); return "Simulated RTX transaction"; },true); other.Activate(); Pump(180);
            RequireLayout(Visible && busy && NativeTopMost(handle),"Programmatic video focus transfer preserves the active transaction panel");
            var outsidePoint=new Point(other.Left+40,other.Top+60);
            ObserveOutsideClick(outsidePoint); Pump(100);
            RequireLayout(!Visible && !NativeTopMost(handle),"Explicit outside click dismisses even a protected operation and releases pinning");
            WaitForAction(); Pump(100); RequireLayout(!Visible,"Operation completion does not reopen the dismissed popup");
            Popup(); Pump(100);
            SliderMouse("OnMouseDown",45); ObserveOutsideClick(outsidePoint); Pump(80);
            RequireLayout(Visible && slider.IsDragging,"Dragging beyond popup bounds is not an outside click");
            SliderMouse("OnMouseUp",45);
            other.Activate(); Activate(); Pump(180); RequireLayout(Visible,"Returning focus cancels a queued dismissal");
            RequireLayout(Opacity==1 && theme.Glass && Shapes.CardGlass(theme).A==100,"Slightly stronger tint keeps native glass and opaque text");
            lines.Add("PASS: external focus dismisses, single tray reopen, owned-dialog exemption, actual hook installed/released, operation focus protection, outside click during operation dismisses without reopen, drag capture, activation race, stronger tint.");
        }
        File.WriteAllLines(path,lines); exiting=true; Close();
    }
    void SliderMouse(string method,int percent) {
        double inset=12*Math.Max(96,slider.DeviceDpi)/96.0;
        int x=(int)Math.Round(inset+(slider.Width-2*inset)*percent/100.0);
        typeof(BrightnessSlider).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(slider,new object[]{new MouseEventArgs(MouseButtons.Left,1,x,slider.Height/2,0)});
    }
    void WaitBrightness(Func<bool> done) {
        var watch=Stopwatch.StartNew(); while(!done() && watch.ElapsedMilliseconds<4000)Pump(10);
        RequireLayout(done(),"Brightness pipeline reached requested state: writing="+brightnessWriting+" pending="+pendingBrightness+" slider="+slider.Value+" dragging="+slider.IsDragging+" visible="+Visible);
    }
}
public static class BrightnessLiveTests {
    public static void Run(string path) {
        var lines=new List<string>(); var monitors=DisplayService.List(); var prefs=Preferences.Load();
        var selected=monitors.FirstOrDefault(m=>m.Key==prefs.MonitorKey);
        if(selected==null || !selected.Active) { File.WriteAllText(path,"SKIP: saved monitor absent or HDR inactive. No settings changed."); return; }
        var target=new BrightnessTarget(selected); uint original=DisplayService.BrightnessLevel(target); int percent=DisplayService.Percent(original);
        try {
            var watch=Stopwatch.StartNew(); for(int i=0;i<3;i++)DisplayService.Resolve(target.Key);
            lines.Add("Legacy full monitor enumeration mean ms="+(watch.Elapsed.TotalMilliseconds/3).ToString("F2"));
            foreach(int value in new[]{Math.Min(100,percent+1),Math.Max(0,percent-1),percent}) {
                watch.Restart(); DisplayService.Brightness(target,value,true);
                lines.Add("Direct selected-target write requested="+value+" readback="+DisplayService.BrightnessLevel(target)+" ms="+watch.Elapsed.TotalMilliseconds.ToString("F2"));
            }
        } finally {
            DisplayService.ValidateBrightnessTarget(target);
            var restore=new Native.SetWhite { Header=Native.H(0xffffffee,typeof(Native.SetWhite),target.Adapter,target.Id),Value=original,Final=1 };
            Native.Check(Native.Set(ref restore),"恢复原始 SDR 内容亮度");
            if(DisplayService.BrightnessLevel(target)!=original)throw new Exception("Original SDR white level restoration not confirmed");
            lines.Add("PASS: real selected-monitor writes confirmed; original exact SDR white level restored="+original+". HDR/Dolby/RTX states untouched.");
            File.WriteAllLines(path,lines);
        }
    }
}
}
