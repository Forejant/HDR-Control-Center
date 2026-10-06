using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Win32;

namespace HdrCenter {
public sealed class Feature {
    public bool? Enabled;
    public bool? Requested;
    public bool CanControl;
    public bool Cached;
    public string Activity="未知", Detail="未连接设置页面";
    public override string ToString() { return Requested.HasValue?"已提交"+(Requested.Value?"开启":"关闭")+"，待确认  ·  "+Activity:(Cached?"上次确认：":"")+(Enabled.HasValue?(Enabled.Value?"已开启":"已关闭"):"开关未知") + "  ·  " + Activity; }
}
public sealed class VideoState { public Feature Super=new Feature(), Hdr=new Feature(); public string CleanupNotice=""; }
public static class PanelBridge {
    static readonly object VideoLock=new object();
    public static string[] Diagnostics() {
        var lines=new List<string>();
        foreach(var name in new[]{"SystemSettings","nvcplui"}) {
            var root=Root(name); lines.Add("ROOT "+name+": "+(root==null?"missing":root.Current.Name+" class="+root.Current.ClassName+" pid="+root.Current.ProcessId));
            if(root==null)continue;
            foreach(var e in Elements(root)) {
                string n=e.Current.Name;
                if(Contains(n,DolbyNames)||Contains(n,PageNames)||Contains(n,SuperNames)||Contains(n,HdrNames)||e.Current.ControlType==ControlType.CheckBox||e.Current.ControlType==ControlType.ComboBox||e.Current.ControlType==ControlType.Button||e.GetSupportedPatterns().Contains(ExpandCollapsePattern.Pattern)||n.Contains("显示器")) {
                    string state=""; object p;
                    if(e.TryGetCurrentPattern(TogglePattern.Pattern,out p))state=" state="+((TogglePattern)p).Current.ToggleState;
                    lines.Add(e.Current.ControlType.ProgrammaticName+" name="+n+" id="+e.Current.AutomationId+" class="+e.Current.ClassName+" enabled="+e.Current.IsEnabled+" offscreen="+e.Current.IsOffscreen+" rect="+e.Current.BoundingRectangle+state+" patterns="+String.Join(",",e.GetSupportedPatterns().Select(x=>x.ProgrammaticName)));
                }
            }
        }
        return lines.ToArray();
    }
    static readonly string[] SuperNames={"super resolution","超分辨率","超级分辨率","超級解析度","超級解析"};
    static readonly string[] HdrNames={"high dynamic range","高动态范围","高動態範圍","rtx video hdr"};
    static readonly string[] DolbyNames={"dolby vision","杜比视界","杜比視界"};
    static readonly string[] PageNames={"adjust video image settings","调整视频图像设置","調整視訊影像設定"};
    static bool Contains(string s, string[] words) { return words.Any(w=>s.IndexOf(w,StringComparison.OrdinalIgnoreCase)>=0); }
    static AutomationElement Root(string processName) {
        if(processName=="nvcplui")return NvidiaRoot(NvidiaWindow.Find());
        var ids=Process.GetProcessesByName(processName).Select(p=>p.Id).ToArray();
        if(ids.Length==0)return null;
        var roots=AutomationElement.RootElement.FindAll(TreeScope.Children,Condition.TrueCondition).Cast<AutomationElement>().ToList();
        foreach(var e in roots)try { if(ids.Contains(e.Current.ProcessId) && !e.Current.IsOffscreen) return e; } catch(ElementNotAvailableException) { }
        // Windows Settings can be hosted by ApplicationFrameHost instead of SystemSettings.
        if(processName=="SystemSettings")foreach(var e in roots) {
            if(e.Current.ClassName!="ApplicationFrameWindow" && e.Current.Name!="设置" && e.Current.Name!="設定" && e.Current.Name!="Settings")continue;
            bool hosted=false;
            EnumChildWindows(new IntPtr(e.Current.NativeWindowHandle),(hwnd,data)=> {
                uint pid; GetWindowThreadProcessId(hwnd,out pid);
                if(ids.Contains((int)pid))hosted=true;
                return !hosted;
            },IntPtr.Zero);
            if(hosted)return e;
        }
        foreach(var e in roots)try { if(ids.Contains(e.Current.ProcessId))return e; } catch(ElementNotAvailableException) { }
        return null;
    }
    static AutomationElement NvidiaRoot(NvidiaWindow window) {
        if(window==null || !window.Alive())return null;
        try { return AutomationElement.FromHandle(window.Handle); } catch(ElementNotAvailableException) { return null; }
    }
    static List<AutomationElement> Elements(AutomationElement root) {
        if(root==null) return new List<AutomationElement>();
        return root.FindAll(TreeScope.Descendants,Condition.TrueCondition).Cast<AutomationElement>().ToList();
    }
    static AutomationElement Toggle(List<AutomationElement> list,string[] names) {
        var matches=list.Where(e=>Contains(e.Current.Name,names) && !e.Current.IsOffscreen && e.GetSupportedPatterns().Contains(TogglePattern.Pattern)).ToList();
        // Some NVIDIA versions expose HDR with the short label "HDR".
        if(matches.Count==0 && Object.ReferenceEquals(names,HdrNames))
            matches=list.Where(e=>e.Current.Name.Trim().Equals("HDR",StringComparison.OrdinalIgnoreCase) && !e.Current.IsOffscreen && e.GetSupportedPatterns().Contains(TogglePattern.Pattern)).ToList();
        return matches.Count==1?matches[0]:null;
    }
    static Feature ReadFeature(AutomationElement root,List<AutomationElement> list,string[] names) {
        var f=new Feature(); var e=Toggle(list,names);
        if(e==null) { f.Detail="未找到唯一且可操作的控件；请连接对应设置页"; return f; }
        var state=((TogglePattern)e.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState;
        f.CanControl=e.Current.IsEnabled;
        if(state!=ToggleState.Indeterminate) f.Enabled=state==ToggleState.On;
        f.Detail="从设置页面实时读取 " + DateTime.Now.ToString("HH:mm:ss");
        var parent=TreeWalker.ControlViewWalker.GetParent(e);
        if(parent==null || parent==root) return f;
        var siblings=Elements(parent);
        int featureCount=siblings.Count(x=>x.GetSupportedPatterns().Contains(TogglePattern.Pattern) && (Contains(x.Current.Name,SuperNames)||Contains(x.Current.Name,HdrNames)||x.Current.Name.Trim()=="HDR"));
        // Some NVCP versions place both RTX controls in the same group. Associate
        // status with the area directly below its own checkbox, never the whole page.
        var bounds=e.Current.BoundingRectangle;
        IEnumerable<AutomationElement> statusElements=siblings;
        if(featureCount!=1) {
            if(bounds.IsEmpty)return f;
            double bottom=bounds.Bottom+Math.Max(42,bounds.Height*3);
            foreach(var other in siblings.Where(x=>x.GetSupportedPatterns().Contains(TogglePattern.Pattern))) {
                var b=other.Current.BoundingRectangle;
                if(!b.IsEmpty && b.Top>bounds.Top+2)bottom=Math.Min(bottom,b.Top);
            }
            statusElements=siblings.Where(x=> {
                var b=x.Current.BoundingRectangle;
                return !b.IsEmpty && b.Top>=bounds.Bottom-2 && b.Top<bottom && b.Left>=bounds.Left-8 && b.Left<bounds.Left+Math.Max(200,bounds.Width);
            });
        }
        var texts=statusElements.Where(x=>x.Current.ControlType==ControlType.Text && !x.Current.IsOffscreen).Select(x=>x.Current.Name.Trim()).ToArray();
        var states=texts.Select(Activity).Where(x=>x!="未知").Distinct().ToArray();
        if(states.Length==1) f.Activity=states[0];
        return f;
    }
    public static string Activity(string s) {
        if(Regex.IsMatch(s,@"\b(inactive|not active)\b",RegexOptions.IgnoreCase)||s.Contains("未激活")||s.Contains("不活动")||s.Contains("非作用中")||s.Contains("未启用")||s.Contains("未啟用")||s.Contains("非活动")||s.Contains("非作用")) return "未生效";
        if(Regex.IsMatch(s,@"\bactive\b",RegexOptions.IgnoreCase)||s.Contains("已激活")||s.Contains("作用中")||s.Contains("活动")||s.Contains("作用")) return "正在生效";
        return "未知";
    }
    public static VideoState Video() {
        VideoState ui;
        try {
            var root=Root("nvcplui"); var list=Elements(root);
            ui=new VideoState { Super=ReadFeature(root,list,SuperNames), Hdr=ReadFeature(root,list,HdrNames) };
        } catch(Exception ex) {
            ui=new VideoState { Super=new Feature { Detail=ex.Message }, Hdr=new Feature { Detail=ex.Message } };
        }
        return NvidiaSavedSettings.Merge(ui,NvidiaSavedSettings.Read());
    }
    public static NvidiaWindow OpenNvidia(string custom) {
        var existing=NvidiaWindow.Find();
        if(existing!=null) {
            if(IsIconic(existing.Handle) || !existing.Shown)ShowWindowAsync(existing.Handle,9);
            bool read=false;
            for(int i=0;i<8;i++) {
                try {
                    var root=NvidiaRoot(existing);
                    if(root!=null) {
                        if(ApplyButtons(Elements(root)).Any(x=>x.Current.IsEnabled))throw new Exception("NVIDIA 控制面板有待保存的修改，请先应用或取消，再使用快捷开关。");
                        read=true; break;
                    }
                } catch(ElementNotAvailableException) { }
                Thread.Sleep(100);
            }
            if(!read)throw new Exception("已有 NVIDIA 窗口正在变化，请稍后重试。");
            return WaitVideoPage(existing);
        }
        var paths=new List<string>(); if(!String.IsNullOrWhiteSpace(custom)) paths.Add(custom);
        paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),@"NVIDIA Corporation\Control Panel Client\nvcplui.exe"));
        using(var k=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\nvcplui.exe"))
            if(k!=null && k.GetValue(null)!=null) paths.Add(k.GetValue(null).ToString());
        var apps=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WindowsApps");
        try { foreach(var d in Directory.GetDirectories(apps,"NVIDIACorp.NVIDIAControlPanel_*")) paths.Add(Path.Combine(d,"nvcplui.exe")); } catch(UnauthorizedAccessException) { } catch(IOException) { }
        string executable=paths.FirstOrDefault(File.Exists); string packageError=null;
        if(!String.IsNullOrWhiteSpace(custom) && File.Exists(custom))Process.Start(new ProcessStartInfo(custom){UseShellExecute=true});
        else if(!PackagedAppLauncher.TryOpenNvidia(out packageError)) {
            if(executable!=null)Process.Start(new ProcessStartInfo(executable){UseShellExecute=true});
            else {
            try { Process.Start(new ProcessStartInfo("nvcplui.exe"){UseShellExecute=true}); }
            catch { throw new Exception("未能启动 NVIDIA 控制面板。商店版启动失败："+packageError+"。请检查是否已安装控制面板，或从托盘菜单指定 nvcplui.exe 路径。"); }
            }
        }
        return WaitVideoPage(null);
    }
    static NvidiaWindow WaitVideoPage(NvidiaWindow window) {
        VideoPageNavigation.Wait(()=> {
            if(window==null) { window=NvidiaWindow.Find(); if(window!=null)window.Write("video page bound to settings window"); }
            if(window==null)return false;
            if(!window.Alive())throw new Exception("导航期间 NVIDIA 控制面板已关闭。");
            var controls=Elements(NvidiaRoot(window));
            return Toggle(controls,SuperNames)!=null || Toggle(controls,HdrNames)!=null;
        },preferInvoke=> {
            if(window==null)return VideoPageActivation.Missing;
            var root=NvidiaRoot(window); var controls=Elements(root);
            if(ApplyButtons(controls).Any(x=>x.Current.IsEnabled))throw new Exception("NVIDIA 控制面板有待保存的修改，请先应用或取消，再使用快捷开关。");
            return NavigateVideo(window,controls,preferInvoke);
        },()=>Thread.Sleep(200),80);
        window.Write("video page ready");
        return window;
    }
    static VideoPageActivation NavigateVideo(NvidiaWindow window,List<AutomationElement> controls,bool preferInvoke) {
        var nodes=controls.Where(e=>Contains(e.Current.Name,PageNames) && e.Current.ControlType==ControlType.TreeItem).ToList();
        if(nodes.Count==0)return VideoPageActivation.Missing;
        if(nodes.Count!=1)throw new Exception("NVIDIA 视频导航项不唯一，无法确认切换目标。");
        if(!window.Alive() || nodes[0].Current.ProcessId!=(int)window.ProcessId)throw new Exception("NVIDIA 导航窗口身份已变化。");
        object pattern;
        if(preferInvoke && nodes[0].TryGetCurrentPattern(InvokePattern.Pattern,out pattern)) {
            ((InvokePattern)pattern).Invoke(); window.Write("video navigation Invoke"); return VideoPageActivation.Invoked;
        }
        if(preferInvoke) {
            NativeTreeActivation.Enter(nodes[0],window.Handle,window.ProcessId,window.Alive);
            window.Write("video navigation Enter"); return VideoPageActivation.Entered;
        }
        NativeTreeActivation.DoubleClick(nodes[0],window.Handle,window.ProcessId,window.Alive);
        window.Write("video navigation double-click"); return VideoPageActivation.DoubleClicked;
    }
    static VideoState ReadVideo(AutomationElement root,List<AutomationElement> list) {
        return new VideoState { Super=ReadFeature(root,list,SuperNames),Hdr=ReadFeature(root,list,HdrNames) };
    }
    public static VideoState SetVideo(bool super,bool enabled,VideoState beforeState) {
        return SetVideoCore(super,enabled,beforeState,NvidiaWindow.Find());
    }
    static VideoState SetVideoCore(bool super,bool enabled,VideoState beforeState,NvidiaWindow window) {
        var root=NvidiaRoot(window); var list=Elements(root); var e=Toggle(list,super?SuperNames:HdrNames);
        if(e==null) throw new Exception("请先连接 NVIDIA 视频设置。当前版本未找到可操作的开关。");
        if(!e.Current.IsEnabled) throw new Exception("驱动当前禁用了该开关，请检查硬件与 HDR 设置。");
        var p=(TogglePattern)e.GetCurrentPattern(TogglePattern.Pattern);
        if(p.Current.ToggleState==(enabled?ToggleState.On:ToggleState.Off)) return ReadVideo(root,list);
        var before=ApplyButtons(list);
        if(before.Any(x=>x.Current.IsEnabled)) throw new Exception("NVIDIA 控制面板有待应用的设置，请先应用或取消，再使用快捷开关。");
        var savedBefore=NvidiaSavedSettings.Read();
        bool savedTransition=(super?savedBefore.Super:savedBefore.Hdr).Enabled!=enabled;
        bool changed=false, applied=false;
        try {
            p.Toggle(); changed=true;
            AutomationElement apply=null;
            for(int i=0;i<20;i++) {
                if(i>0)Thread.Sleep(100);
                List<AutomationElement> candidates;
                try { candidates=ApplyButtons(Elements(NvidiaRoot(window))).Where(x=>x.Current.IsEnabled).ToList(); }
                catch(ElementNotAvailableException) { continue; }
                if(candidates.Count>1) throw new Exception("发现多个应用按钮，无法确认目标。");
                if(candidates.Count==1) { apply=candidates[0]; break; }
            }
            if(apply==null) throw new Exception("切换后未出现可操作的“应用”按钮。");
            object invoke;
            if(!apply.TryGetCurrentPattern(InvokePattern.Pattern,out invoke)) throw new Exception("“应用”按钮不支持自动操作。");
            applied=true;
            try { ((InvokePattern)invoke).Invoke(); } catch(ElementNotAvailableException) { /* Apply can destroy its own UI; confirm saved state below. */ }
            int missing=0;
            for(int i=0;i<60;i++) {
                if(i>0)Thread.Sleep(100);
                // Persisted settings remain readable even if Apply closes the
                // panel. Confirm the actual saved value without a second launch.
                var saved=NvidiaSavedSettings.Read(); var savedTarget=super?saved.Super:saved.Hdr;
                // A saved value already equal to the requested state before
                // Toggle is not proof that this Apply completed. In that case
                // retain the checked-and-saved UI confirmation path below.
                if(savedTransition && savedTarget.Enabled==enabled)return saved;
                // Some drivers close the panel after Apply. Never launch a second
                // window just to verify. Wait through short UIA outages instead.
                try {
                    var freshRoot=NvidiaRoot(window);
                    if(freshRoot==null) { missing++; continue; }
                    missing=0;
                    var current=Elements(freshRoot); var toggle=Toggle(current,super?SuperNames:HdrNames);
                    bool pageSaved=!ApplyButtons(current).Any(x=>x.Current.IsEnabled);
                    if(toggle!=null && ((TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState==(enabled?ToggleState.On:ToggleState.Off) && pageSaved) {
                        var result=ReadVideo(freshRoot,current);
                        if((super?result.Super:result.Hdr).Enabled==enabled)return result;
                    }
                } catch(ElementNotAvailableException) { }
            }
            if(missing>0 && (window==null || !window.Alive()))return VideoApplyPolicy.Unverified(beforeState,super,enabled);
            throw new Exception("尚未确认驱动保存成功，请检查控制面板中的设置。");
        } catch {
            // No unrelated edits existed before this operation. Revert only our pending checkbox.
            if(changed && !applied) {
                try {
                    var fresh=Elements(NvidiaRoot(window)); var toggle=Toggle(fresh,super?SuperNames:HdrNames);
                    if(toggle!=null) {
                        var revert=(TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern);
                        if(revert.Current.ToggleState==(enabled?ToggleState.On:ToggleState.Off)) revert.Toggle();
                    }
                } catch { }
            }
            throw;
        }
    }
    public static VideoState VideoTransaction(string custom,bool super,bool? target) {
        lock(VideoLock) {
            var existing=NvidiaWindow.Find(); bool owned=existing==null || !existing.Shown;
            var window=OpenNvidia(custom); if(window==null || !window.Alive())throw new Exception("NVIDIA 窗口已关闭，请稍后重试。");
            window.Write("transaction target owned="+owned);
            // The transaction already owns a verified, ready video page. Read
            // that page directly instead of finding the window a second time
            // and scanning saved adapter configuration before the toggle.
            var root=NvidiaRoot(window); var before=ReadVideo(root,Elements(root));
            var state=super?before.Super:before.Hdr;
            if(!state.Enabled.HasValue)throw new Exception(state.Detail);
            var result=SetVideoCore(super,target??!state.Enabled.Value,before,window);
            // Snapshot enabled settings while leaving activity unknown after closing.
            if(owned) {
                VideoWindowCloseResult cleanup;
                try { cleanup=CloseNvidia(window); }
                catch(Exception ex) { cleanup=window.Alive()?VideoWindowCloseResult.Unavailable:VideoWindowCloseResult.Closed; window.Write("cleanup exception="+ex); }
                result.CleanupNotice=VideoWindowClosePolicy.Notice(cleanup);
                window.Write("cleanup result="+cleanup);
                if(cleanup==VideoWindowCloseResult.Closed) { result.Super.Activity="未检测（控制面板已关闭）"; result.Hdr.Activity="未检测（控制面板已关闭）"; }
            }
            return result;
        }
    }
    static VideoWindowCloseResult CloseNvidia(NvidiaWindow window) {
        return VideoWindowClosePolicy.Run(()=>window.Alive() && window.Shown,()=> {
            var root=NvidiaRoot(window); if(root==null)return (bool?)null;
            return ApplyButtons(Elements(root)).Any(x=>x.Current.IsEnabled);
        },window.RequestClose,()=>Thread.Sleep(100),10,30);
    }
    internal static string TestOpenedWindowClose(string custom) {
        var existing=NvidiaWindow.Find();
        if(existing!=null && existing.Shown)throw new Exception("Explicit close test requires NVIDIA to be closed beforehand.");
        OpenNvidia(custom); var window=NvidiaWindow.Find();
        if(window==null)throw new Exception("NVIDIA close test could not capture its window.");
        window.Write("explicit native close test");
        var closed=CloseNvidia(window); window.Write("explicit native close result="+closed);
        if(closed!=VideoWindowCloseResult.Closed)throw new Exception(VideoWindowClosePolicy.Notice(closed));
        return "NVIDIA 窗口关闭检查通过";
    }
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
    delegate bool ChildWindowCallback(IntPtr hwnd,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr hwnd,ChildWindowCallback callback,IntPtr data);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hwnd,int command);
    static void Restore(AutomationElement root) {
        if(root==null || root.Current.NativeWindowHandle==0)return;
        IntPtr hwnd=new IntPtr(root.Current.NativeWindowHandle);
        if(IsIconic(hwnd)) { ShowWindowAsync(hwnd,9); for(int i=0;i<20 && IsIconic(hwnd);i++)Thread.Sleep(50); }
    }
    public static bool IsApplyName(string name) {
        string normalized=Regex.Replace(name ?? "",@"[&\s]","");
        normalized=Regex.Replace(normalized,@"[（(][A-Za-z][）)]$","");
        return Regex.IsMatch(normalized,@"^(Apply|应用|套用)$",RegexOptions.IgnoreCase);
    }
    static List<AutomationElement> ApplyButtons(List<AutomationElement> list) {
        return list.Where(x=>x.Current.ControlType==ControlType.Button && !x.Current.IsOffscreen && IsApplyName(x.Current.Name)).ToList();
    }
    static AutomationElement Settings() { return Root("SystemSettings"); }
    public static void OpenWindows() { Process.Start(new ProcessStartInfo("ms-settings:display-hdr"){UseShellExecute=true}); }
    static T WaitSettings<T>(Func<T> read,Func<T,bool> ready,int attempts,string failure) {
        return SettingsWait.Run(read,ready,attempts,()=>Thread.Sleep(150),failure);
    }
    static bool HdrPageReady() {
        try { return Elements(Settings()).Any(e=>e.Current.AutomationId=="SystemSettings_Display_AdvancedColorSupport_ToggleSwitch"); }
        catch(ElementNotAvailableException) { return false; }
    }
    static AutomationElement MonitorCombo(AutomationElement root) {
        var combos=Elements(root).Where(e=>e.Current.ControlType==ControlType.ComboBox).ToList();
        return combos.FirstOrDefault(e=>e.Current.AutomationId=="SystemSettings_Display_AdvancedDisplaySettingsTargetSelection_ComboBox")??(combos.Count==1?combos[0]:null);
    }
    static bool SelectionMatches(AutomationElement combo,string name) {
        object p; if(combo==null || !combo.TryGetCurrentPattern(SelectionPattern.Pattern,out p))return false;
        var selection=((SelectionPattern)p).Current.GetSelection();
        return selection.Length==1 && SettingsWait.MonitorMatches(selection[0].Current.Name,name);
    }
    public static void PrepareDolby(string key) {
        var monitors=DisplayService.List(); var monitor=monitors.FirstOrDefault(x=>x.Key==key);
        if(monitor==null)throw new Exception("显示器已断开。");
        if(monitors.Count>1 && (monitor.Name=="显示器" || monitors.Count(x=>String.Equals(x.Name.Trim(),monitor.Name.Trim(),StringComparison.OrdinalIgnoreCase))!=1))
            throw new Exception("目标显示器名称不唯一，无法安全自动选择。请先在 Windows HDR 页面选中目标显示器。");
        try { Restore(Settings()); } catch(ElementNotAvailableException) { }
        if(!HdrPageReady())OpenWindows();
        WaitSettings(()=>HdrPageReady(),ready=>ready,80,"Windows HDR 页面未加载完成。请确认系统可打开 HDR 设置页。");
        if(monitors.Count>1) {
            WaitSettings(()=> {
                var combo=MonitorCombo(Settings());
                if(combo==null)return false;
                if(SelectionMatches(combo,monitor.Name))return true;
                object expand;
                if(!combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out expand))throw new Exception("HDR 显示器选择器不能自动展开。");
                var popup=(ExpandCollapsePattern)expand;
                if(popup.Current.ExpandCollapseState!=ExpandCollapseState.Expanded) { popup.Expand(); return false; }
                var choices=Elements(Settings()).Where(e=>e.Current.ControlType==ControlType.ListItem && SettingsWait.MonitorMatches(e.Current.Name,monitor.Name) && e.GetSupportedPatterns().Contains(SelectionItemPattern.Pattern)).ToList();
                if(choices.Count>1)throw new Exception("系统显示器列表中“"+monitor.Name+"”名称重复，无法安全自动选择。");
                if(choices.Count==0)return false;
                object pattern;
                if(choices[0].TryGetCurrentPattern(VirtualizedItemPattern.Pattern,out pattern))((VirtualizedItemPattern)pattern).Realize();
                if(choices[0].TryGetCurrentPattern(ScrollItemPattern.Pattern,out pattern))((ScrollItemPattern)pattern).ScrollIntoView();
                ((SelectionItemPattern)choices[0].GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                return false; // Verify the new selection from a fresh tree next time.
            },ready=>ready,40,"无法在系统 HDR 显示器列表中选中“"+monitor.Name+"”。请检查该显示器仍在线。");
        }
        WaitSettings(()=> {
            VerifyMonitor(key); ExpandHdr();
            var checkbox=FindDolby(); object scroll;
            if(checkbox!=null && checkbox.TryGetCurrentPattern(ScrollItemPattern.Pattern,out scroll))((ScrollItemPattern)scroll).ScrollIntoView();
            return checkbox;
        },checkbox=>checkbox!=null,40,"所选显示器的 HDR 页面未提供可操作的 Dolby Vision 选项。请确认已启用 HDR 且显示器支持杜比视界。");
    }
    static void ExpandHdr() {
        var root=Settings(); var all=Elements(root);
        var hdr=all.FirstOrDefault(e=>e.Current.AutomationId=="SystemSettings_Display_AdvancedColorSupport_ToggleSwitch");
        if(hdr==null)return;
        var parent=TreeWalker.ControlViewWalker.GetParent(hdr);
        for(int i=0;i<5 && parent!=null && parent!=root;i++) {
            var buttons=Elements(parent).Where(e=>e.GetSupportedPatterns().Contains(ExpandCollapsePattern.Pattern) && e.Current.ControlType==ControlType.Button).ToList();
            if(buttons.Count==1) {
                var pattern=(ExpandCollapsePattern)buttons[0].GetCurrentPattern(ExpandCollapsePattern.Pattern);
                // PrepareDolby's outer readiness loop waits only if the fresh
                // Dolby checkbox is still missing after expansion.
                if(pattern.Current.ExpandCollapseState==ExpandCollapseState.Collapsed)pattern.Expand();
                return;
            }
            parent=TreeWalker.ControlViewWalker.GetParent(parent);
        }
    }
    static AutomationElement FindDolby() {
        var list=Elements(Settings());
        var exact=list.Where(e=>e.Current.AutomationId=="SystemSettings_Display_UseDolbyVision_CheckBox" && e.GetSupportedPatterns().Contains(TogglePattern.Pattern)).ToList();
        return exact.Count==1?exact[0]:Toggle(list,DolbyNames);
    }
    public static string VerifyMonitor(string key) {
        var monitors=DisplayService.List(); var m=monitors.FirstOrDefault(x=>x.Key==key);
        if(m==null) throw new Exception("显示器已断开。");
        if(monitors.Count==1) return "已确认唯一显示器";
        if(monitors.Count(x=>String.Equals(x.Name.Trim(),m.Name.Trim(),StringComparison.OrdinalIgnoreCase))!=1 || m.Name=="显示器") throw new Exception("显示器名称不唯一，无法安全自动选择。请在 Windows HDR 设置中直接操作目标显示器。");
        var combos=Elements(Settings()).Where(e=>e.Current.ControlType==ControlType.ComboBox).ToList();
        foreach(var combo in combos) {
            object selection;
            if(!combo.TryGetCurrentPattern(SelectionPattern.Pattern,out selection)) continue;
            var selected=((SelectionPattern)selection).Current.GetSelection();
            if(selected.Length==1 && SettingsWait.MonitorMatches(selected[0].Current.Name,m.Name)) return "已确认 " + m.Name;
        }
        throw new Exception("当前系统 HDR 页尚未选中“"+m.Name+"”。快捷开关会尝试自动打开页面并选择目标。");
    }
    public static Feature Dolby(string key,bool confirmed) {
        var f=new Feature();
        try { if(!confirmed) VerifyMonitor(key); }
        catch(Exception ex) { f.Detail=ex.Message; return f; }
        AutomationElement e;
        try { e=FindDolby(); }
        catch(Exception ex) { f.Detail=ex.Message; return f; }
        if(e==null) { f.Detail="当前 HDR 页面尚未展开或未提供杜比视界选项。点击快捷开关会自动打开和展开页面。"; return f; }
        var state=((TogglePattern)e.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState;
        f.CanControl=e.Current.IsEnabled;
        if(state!=ToggleState.Indeterminate) f.Enabled=state==ToggleState.On;
        f.Activity="播放模式未检测"; f.Detail="从已确认的 Windows HDR 页面读取"; return f;
    }
    public static void SetDolby(string key,bool enabled,bool confirmed) {
        DisplayService.Resolve(key);
        bool submitted=false;
        WaitSettings(()=> {
            if(!confirmed)VerifyMonitor(key);
            var e=FindDolby(); if(e==null)return false;
            var p=(TogglePattern)e.GetCurrentPattern(TogglePattern.Pattern);
            if(p.Current.ToggleState==(enabled?ToggleState.On:ToggleState.Off))return true;
            if(!submitted && e.Current.IsEnabled) { p.Toggle(); submitted=true; }
            return false;
        },ready=>ready,40,"杜比视界切换后未能确认状态，或系统当前禁用了该开关。");
    }
    public static Feature DolbyTransaction(string key) {
        bool owned=Settings()==null;
        PrepareDolby(key);
        var window=CaptureOwnedSettings(owned);
        var before=WaitSettings(()=>Dolby(key,false),state=>state.Enabled.HasValue,20,"杜比视界初始状态读取失败。");
        if(!before.Enabled.HasValue)throw new Exception("所选显示器的系统 HDR 页面没有可操作的 Dolby Vision 选项。");
        SetDolby(key,!before.Enabled.Value,false);
        var result=WaitSettings(()=>Dolby(key,false),state=>state.Enabled==!before.Enabled.Value,20,"杜比视界切换后未能确认状态。");
        CloseOwnedSettings(window);
        return result;
    }
    public static Feature ForceDolbyOff(string key,bool closeOwnedWindow) {
        bool owned=Settings()==null;
        PrepareDolby(key);
        var window=CaptureOwnedSettings(owned && closeOwnedWindow);
        var before=WaitSettings(()=>Dolby(key,false),state=>state.Enabled.HasValue && state.CanControl,20,"所选显示器没有可操作的 Dolby Vision 选项。守护未执行。");
        if(!before.Enabled.HasValue || !before.CanControl)throw new Exception("所选显示器没有可操作的 Dolby Vision 选项。守护未执行。");
        DolbyReset.Execute(enabled=> {
            Exception error=null;
            for(int i=0;i<3;i++) {
                try { SetDolby(key,enabled,false); return; }
                catch(Exception ex) { error=ex; Thread.Sleep(500); }
            }
            throw error;
        },()=>Thread.Sleep(1000));
        var result=WaitSettings(()=>Dolby(key,false),state=>state.Enabled==false,20,"修复后未能确认杜比视界已关闭。");
        if(result.Enabled!=false)throw new Exception("修复后未能确认杜比视界已关闭。");
        CloseOwnedSettings(window);
        return result;
    }
    sealed class SettingsWindowIdentity { public IntPtr Handle; public uint ProcessId; }
    static SettingsWindowIdentity CaptureOwnedSettings(bool owned) {
        if(!owned)return null;
        var root=Settings(); if(root==null)return null;
        IntPtr hwnd=new IntPtr(root.Current.NativeWindowHandle); uint pid;
        if(hwnd==IntPtr.Zero || GetWindowThreadProcessId(hwnd,out pid)==0)return null;
        return new SettingsWindowIdentity { Handle=hwnd,ProcessId=pid };
    }
    static void CloseOwnedSettings(SettingsWindowIdentity window) {
        if(window==null)return;
        try {
            uint pid;
            if(GetWindowThreadProcessId(window.Handle,out pid)==0 || pid!=window.ProcessId || pid==(uint)Process.GetCurrentProcess().Id)return;
            using(var process=Process.GetProcessById((int)pid)) {
                if(!String.Equals(process.ProcessName,"SystemSettings",StringComparison.OrdinalIgnoreCase)) {
                    if(!String.Equals(process.ProcessName,"ApplicationFrameHost",StringComparison.OrdinalIgnoreCase))return;
                    // ApplicationFrameHost can host other apps. Require a child
                    // belonging to Windows Settings before closing this HWND.
                    var settingsIds=Process.GetProcessesByName("SystemSettings").Select(p=>(uint)p.Id).ToArray();
                    bool hosted=false;
                    EnumChildWindows(window.Handle,(child,data)=> { uint childPid; GetWindowThreadProcessId(child,out childPid); if(settingsIds.Contains(childPid))hosted=true; return !hosted; },IntPtr.Zero);
                    if(!hosted)return;
                }
            }
            // Close the captured window only after successful state readback.
            // Do not use a UI Automation window proxy or terminate processes.
            if(PostMessage(window.Handle,0x0010,IntPtr.Zero,IntPtr.Zero)) {
                for(int i=0;i<20;i++) {
                    Thread.Sleep(50);
                    if(GetWindowThreadProcessId(window.Handle,out pid)==0 || pid!=window.ProcessId)break;
                }
            }
        } catch(ArgumentException) { } catch(InvalidOperationException) { } catch(System.ComponentModel.Win32Exception) { }
    }
}
}
