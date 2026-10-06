using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace HdrCenter {
public static class VideoNavigationTests {
    static void Check(bool ok,string message) { if(!ok)throw new Exception("Video navigation: "+message); }
    public static void PolicyTests() {
        int actions=0,ticks=0; bool changed=false;
        VideoPageNavigation.Wait(()=>true,prefer=> { actions++; return VideoPageActivation.DoubleClicked; },()=>{},40);
        Check(actions==0,"An already loaded page does not navigate again");
        VideoPageNavigation.Wait(()=>changed && ticks>=4,prefer=> { actions++; changed=true; return VideoPageActivation.DoubleClicked; },()=>ticks++,40);
        Check(actions==1,"Delayed page load does not repeat a successful double-click");
        actions=0; ticks=0; changed=false;
        VideoPageNavigation.Wait(()=>changed,prefer=> { actions++; if(prefer)return VideoPageActivation.Invoked; changed=true; return VideoPageActivation.DoubleClicked; },()=>ticks++,40);
        Check(actions==2 && ticks==10,"Selection-only Invoke gets one double-click fallback after the page wait");
        actions=0; ticks=0; changed=false;
        VideoPageNavigation.Wait(()=>changed && ticks>=4,prefer=> { actions++; changed=true; return VideoPageActivation.Entered; },()=>ticks++,40);
        Check(actions==1,"Delayed Enter activation does not repeat before the page is ready");
        actions=0; ticks=0; changed=false;
        VideoPageNavigation.Wait(()=>changed,prefer=> { actions++; if(prefer)return VideoPageActivation.Entered; changed=true; return VideoPageActivation.DoubleClicked; },()=>ticks++,40);
        Check(actions==2 && ticks==10,"Enter without page readiness permits one double-click fallback");
        actions=0; ticks=0; changed=false;
        VideoPageNavigation.Wait(()=> { if(ticks<4)throw new ElementNotAvailableException(); return changed; },prefer=> { actions++; changed=true; return VideoPageActivation.DoubleClicked; },()=>ticks++,40);
        Check(actions==1,"Stale reads do not consume navigation attempts before the tree exists");
        actions=0; ticks=0; changed=false;
        VideoPageNavigation.Wait(()=>changed,prefer=> { if(ticks<3)return VideoPageActivation.Missing; actions++; changed=true; return VideoPageActivation.DoubleClicked; },()=>ticks++,40);
        Check(actions==1,"Missing startup tree waits for a fresh item");
        actions=0; bool failed=false;
        try { VideoPageNavigation.Wait(()=>false,prefer=> { actions++; return VideoPageActivation.DoubleClicked; },()=>{},20); } catch(Exception ex) { failed=ex.Message.Contains("视频页未准备好"); }
        Check(failed && actions==1,"Highlight/delivered input alone cannot report navigation success");
        actions=0; failed=false;
        try { VideoPageNavigation.Wait(()=>false,prefer=> { actions++; throw new ElementNotAvailableException(); },()=>{},20); } catch(Exception) { failed=true; }
        Check(failed && actions==3,"Activation failures are bounded");
    }
    public static void Run(string path) {
        PolicyTests(); var lines=new List<string>();
        foreach(var screen in Screen.AllScreens) {
            NavigationHost host=null; IntPtr handle=IntPtr.Zero;
            using(var ready=new ManualResetEvent(false)) {
                var thread=new Thread(()=> {
                    host=new NavigationHost(); host.Location=new Point(screen.WorkingArea.Left+30,screen.WorkingArea.Top+30);
                    host.Shown+=delegate { handle=host.Handle; ShowWindow(handle,5); ready.Set(); }; Application.Run(host);
                });
                thread.IsBackground=true; thread.SetApartmentState(ApartmentState.STA); thread.Start();
                Check(ready.WaitOne(5000),"Fixture opened");
                try {
                    Check(NvidiaWindow.IsPanelWindow(handle),"Settings host with a native navigation tree is selected");
                    var item=Item(handle);
                    ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                    Check(!PageReady(handle) && ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected,"Selection reproduces the highlighted-only failure");
                    int clicks=0;
                    VideoPageNavigation.Wait(()=>PageReady(handle),prefer=> {
                        clicks++; NativeTreeActivation.DoubleClick(Item(handle),handle,(uint)Process.GetCurrentProcess().Id,()=>IsWindow(handle));
                        return VideoPageActivation.DoubleClicked;
                    },()=>Thread.Sleep(20),40);
                    Check(clicks==1 && host.Activations==1 && PageReady(handle),"Double-click opens actual checkbox content");
                    int before=host.Activations;
                    VideoPageNavigation.Wait(()=>PageReady(handle),prefer=> { clicks++; throw new Exception("Do not navigate an already open page"); },()=>{},40);
                    Check(clicks==1 && host.Activations==before,"No repeat activation after ready");
                    NativeTreeActivation.Enter(Item(handle),handle,(uint)Process.GetCurrentProcess().Id,()=>IsWindow(handle));
                    Check(host.EnterActivations==1,"Directed Enter reaches the selected tree item notification");
                    Reject(()=>NativeTreeActivation.DoubleClick(Item(handle),handle,(uint)Process.GetCurrentProcess().Id+1,()=>true));
                    Reject(()=>NativeTreeActivation.DoubleClick(Item(handle),host.TreeHandle,(uint)Process.GetCurrentProcess().Id,()=>true));
                    Reject(()=>NativeTreeActivation.DoubleClick(Item(handle),handle,(uint)Process.GetCurrentProcess().Id,()=>false));
                    Reject(()=>NativeTreeActivation.Enter(Item(handle),handle,(uint)Process.GetCurrentProcess().Id+1,()=>true));
                    Reject(()=>NativeTreeActivation.Enter(Item(handle),handle,(uint)Process.GetCurrentProcess().Id,()=>false));
                    Check(host.Activations==before,"Wrong PID, wrong root and stale identity send no double-click");
                    lines.Add(screen.DeviceName+" selection-only page=False; native double-click page=True; activations="+host.Activations+" clicks="+clicks+" directed Enter="+host.EnterActivations+" launcher/hidden-helper/target guards=PASS");
                } finally {
                    if(host!=null && !host.IsDisposed)host.BeginInvoke((Action)delegate { host.Close(); });
                    Check(thread.Join(4000),"Fixture closed");
                }
            }
        }
        lines.Add("PASS: highlighted-only failure reproduced, native tree double-click opens checkbox page on three monitor DPIs, page readiness verified, already loaded page left alone, Invoke fallback/delayed load/stale reads/bounded attempts and target identity guards. Fixture only; no NVIDIA settings changed.");
        File.WriteAllLines(path,lines);
    }
    static void Reject(Action action) { bool rejected=false; try { action(); } catch(InvalidOperationException) { rejected=true; } Check(rejected,"Invalid target rejected"); }
    static AutomationElement Item(IntPtr root) {
        var item=AutomationElement.FromHandle(root).FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TreeItem),new PropertyCondition(AutomationElement.NameProperty,"调整视频图像设置")));
        Check(item!=null,"Unique video tree item exists"); return item;
    }
    static bool PageReady(IntPtr root) {
        var control=AutomationElement.FromHandle(root).FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,"Super Resolution"));
        object pattern; return control!=null && !control.Current.IsOffscreen && control.TryGetCurrentPattern(TogglePattern.Pattern,out pattern);
    }
    sealed class NavigationHost : Form {
        readonly TreeView tree=new TreeView { Dock=DockStyle.Left,Width=240 };
        readonly Panel video=new Panel { Dock=DockStyle.Fill,Visible=false };
        public volatile int Activations;
        public volatile int EnterActivations;
        public IntPtr TreeHandle { get { return tree.Handle; } }
        public NavigationHost() {
            AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi;
            Text="Video navigation fixture"; ClientSize=new Size(540,300); StartPosition=FormStartPosition.Manual; ShowInTaskbar=true;
            video.Controls.Add(new CheckBox { Text="Super Resolution",AutoSize=true,Location=new Point(12,30) });
            video.Controls.Add(new CheckBox { Text="高动态范围",AutoSize=true,Location=new Point(12,65) });
            Controls.Add(video); Controls.Add(tree);
            tree.Nodes.Add("3D 设置").Nodes.Add("管理 3D 设置"); tree.Nodes.Add("视频").Nodes.Add("调整视频图像设置"); tree.ExpandAll(); tree.SelectedNode=tree.Nodes[0].Nodes[0];
            Shown+=delegate {
                using(var launcher=new Form { Text="NVIDIA launcher fixture",ShowInTaskbar=true }) {
                    launcher.Show(); Check(!NvidiaWindow.IsPanelWindow(launcher.Handle),"Titled visible launcher without a tree is ignored"); launcher.Close();
                }
                using(var hidden=new Form { Text="NVIDIA hidden helper fixture",ShowInTaskbar=false }) {
                    hidden.Controls.Add(new TreeView()); var ignored=hidden.Handle;
                    Check(!NvidiaWindow.IsPanelWindow(ignored),"Hidden helper is ignored");
                }
            };
        }
        protected override void WndProc(ref Message m) {
            if(m.Msg==0x004e && m.LParam!=IntPtr.Zero) {
                var header=(Notification)Marshal.PtrToStructure(m.LParam,typeof(Notification));
                if(header.Source==tree.Handle && header.Code==-3 && tree.SelectedNode!=null && tree.SelectedNode.Text=="调整视频图像设置") { Activations++; video.Visible=true; }
                if(header.Source==tree.Handle && header.Code==-412 && tree.SelectedNode!=null && tree.SelectedNode.Text=="调整视频图像设置" && Marshal.ReadInt16(m.LParam,Marshal.SizeOf(typeof(Notification)))==13) { EnterActivations++; video.Visible=true; }
            }
            base.WndProc(ref m);
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct Notification { public IntPtr Source,Id; public int Code; }
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
}
}
