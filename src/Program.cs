using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.Versioning;

[assembly: TargetFramework(".NETFramework,Version=v4.8")]

namespace HdrCenter {
public sealed class Preferences {
    public string MonitorKey="", NvidiaPath="";
    public bool AlwaysDisableDolby;
    public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HdrControlCenter"); } }
    public static Preferences Load() {
        try { return new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(Path.Combine(Folder,"settings.json"))) ?? new Preferences(); }
        catch { return new Preferences(); }
    }
    public void Save() {
        Directory.CreateDirectory(Folder); string path=Path.Combine(Folder,"settings.json");
        File.WriteAllText(path+".tmp",new JavaScriptSerializer().Serialize(this));
        if(File.Exists(path)) File.Replace(path+".tmp",path,null); else File.Move(path+".tmp",path);
    }
}
public static class Startup {
    const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run", Name="HdrControlCenter";
    public static bool Enabled { get { using(var k=Registry.CurrentUser.OpenSubKey(Key)) return k!=null && String.Equals(k.GetValue(Name) as string,Command,StringComparison.OrdinalIgnoreCase); } }
    public static string Command { get { return "\""+Application.ExecutablePath+"\" --tray"; } }
    public static void Set(bool value) { using(var k=Registry.CurrentUser.CreateSubKey(Key)) { if(value) k.SetValue(Name,Command); else k.DeleteValue(Name,false); } }
}
public static class Worker {
    public static Task<T> Run<T>(Func<T> action) {
        var completion=new TaskCompletionSource<T>();
        var thread=new Thread(()=> { try { completion.SetResult(action()); } catch(Exception ex) { completion.SetException(ex); } });
        thread.IsBackground=true; thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
public sealed class Snapshot { public List<MonitorInfo> Monitors; public VideoState Video; public Feature Dolby; }
public sealed partial class Center : Form {
    public Action<string> BrightnessTrace;
    public Action<string> PanelTrace;
    readonly Preferences prefs;
    readonly ComboBox displays=new ModernComboBox();
    readonly Label hdr=new SmoothLabel(), white=new BrightnessReadout(), dolby=new SmoothLabel(), super=new SmoothLabel(), videoHdr=new SmoothLabel(), message=new SmoothLabel();
    readonly ModernButton hdrButton=new ModernButton(), dolbyButton=new ModernButton(), superButton=new ModernButton(), videoButton=new ModernButton();
    readonly BrightnessSlider slider=new BrightnessSlider(); readonly ModernCheckBox startup=new ModernCheckBox(), dolbyGuardToggle=new ModernCheckBox();
    readonly Label guardStatus=new SmoothLabel(); readonly DolbyGuard guard=new DolbyGuard();
    readonly System.Windows.Forms.Timer recoveryTimer=new System.Windows.Forms.Timer(), geometryTimer=new System.Windows.Forms.Timer();
    IntPtr powerNotification; bool sessionNotification,guardPolling,reflowing; double logicalWidth=460;
    readonly NotifyIcon tray=new NotifyIcon(); readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer(), brightnessTimer=new System.Windows.Forms.Timer();
    readonly TrayInputGate trayInput=new TrayInputGate(SystemInformation.DoubleClickTime);
    readonly TableLayoutPanel layout=new BufferedTableLayoutPanel();
    UiTheme theme=UiTheme.Read(); bool themeQueued,forcedTheme; Icon trayOwnedIcon;
    Color bg { get { return theme.Background; } } Color muted { get { return theme.Muted; } } Color accent { get { return theme.Accent; } }
    TableLayoutPanel targetLayout;
    Action<Rectangle> popupShowObserver;
    bool preparingPopup,popupTransitionsDisabled; Rectangle popupArea;
    bool popupLayoutReady; Rectangle preparedPopupBounds,preparedPopupArea;
    Size preparedLayoutSize; int preparedPopupDpi;
    bool binding,busy,refreshing,exiting,preview,brightnessWriting,videoInProgress,userHidPanel,keepVideoPanelVisible;
    bool testNoDialogs; Exception testOperationError;
    ModernButton operationButton; bool openingOperation;
    DateTime panelCloseInput=DateTime.MinValue;
    string videoNotice="";
    bool videoLayerPinned,videoLayerWasTopMost;
    int? pendingBrightness; BrightnessTarget pendingTarget;
    internal Action<BrightnessTarget,int,bool> brightnessWriter;
    DateTime brightnessTouched=DateTime.MinValue;
    DateTime brightnessSent=DateTime.MinValue;
    Snapshot snapshot; VideoState lastVideo=new VideoState(); readonly Dictionary<string,Feature> lastDolby=new Dictionary<string,Feature>(); int version,brightnessRevision;
    public Center(Preferences settings,bool previewMode) {
        prefs=settings; preview=previewMode;
        SuspendLayout(); layout.SuspendLayout();
        DoubleBuffered=true;
        Text="HDR 控制中心"; Font=new Font("Microsoft YaHei UI",9F); BackColor=bg; ForeColor=theme.Text;
        AutoScaleDimensions=new SizeF(96F,96F); AutoScaleMode=AutoScaleMode.Dpi; ClientSize=new Size(460,700); FormBorderStyle=FormBorderStyle.SizableToolWindow;
        MinimumSize=new Size(390,500);
        MaximizeBox=false; MinimizeBox=false; ShowInTaskbar=false; StartPosition=FormStartPosition.Manual;
        Icon=IconArtwork.ApplicationIcon();
        var scroll=new BufferedPanel { Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(20,16,20,16) }; scroll.SuspendLayout(); Controls.Add(scroll);
        layout.Dock=DockStyle.Top; layout.AutoSize=true; layout.AutoSizeMode=AutoSizeMode.GrowAndShrink; layout.ColumnCount=1; layout.RowCount=0;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); scroll.Controls.Add(layout);
        var header=Columns(); header.Margin=new Padding(0,0,0,20); Add(header);
        var heading=new BufferedTableLayoutPanel { AutoSize=true,Dock=DockStyle.Fill,ColumnCount=2,Margin=new Padding(0) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var logo=new PictureBox { Image=IconArtwork.Bitmap(40,false,false),Size=new Size(40,40),SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(0,3,10,0) }; heading.Controls.Add(logo,0,0); heading.SetRowSpan(logo,2);
        heading.Controls.Add(Label("HDR 控制中心",17,theme.Text),1,0); heading.Controls.Add(Label("显示与视频 · 快速控制",8.5F,muted),1,1); header.Controls.Add(heading,0,0);
        var close=new ModernButton { IsClose=true,Text="关闭",AccessibleName="关闭面板并保留托盘",Anchor=AnchorStyles.Top|AnchorStyles.Right,Margin=new Padding(8,0,0,0) }; header.Controls.Add(close,1,0);
        close.Click+=delegate { userHidPanel=true; Close(); };
        WireDrag(header); WireDrag(heading); foreach(Control child in heading.Controls)WireDrag(child);
        AddLabel("显示器",8.5F,muted,new Padding(0,0,0,6));
        displays.DropDownStyle=ComboBoxStyle.DropDownList; displays.Dock=DockStyle.Fill; displays.Margin=new Padding(0,0,0,12);
        displays.BackColor=Color.FromArgb(37,44,58); displays.ForeColor=Color.White; Add(displays);
        displays.SelectedIndexChanged+=delegate {
            if(binding) return; version++; pendingBrightness=null; brightnessTouched=DateTime.MinValue; var m=Selected;
            if(m!=null) { prefs.MonitorKey=m.Key; SafeSave(); }
            guard.Configure(prefs.AlwaysDisableDolby,prefs.MonitorKey,DateTime.UtcNow);
            Render(); RefreshState(false);
        };
        AddLabel("WINDOWS  ·  显示器",8,muted,new Padding(0,4,0,8));
        var windowsCard=new RoundedCard(); Add(windowsCard); targetLayout=windowsCard;
        Row("HDR",hdr,hdrButton); hdrButton.Click+=delegate {
            var m=Selected; if(m==null)return;
            if(preview) { PreviewToggle(hdrButton,()=>m.Enabled=!m.Enabled); return; }
            // Clear any startup repair while the selected screen is still HDR OFF,
            // even if the first background observation has not happened yet.
            if(!m.Enabled)ObserveRecovery(m);
            MonitorInfo confirmed=null;
            Act(()=> { confirmed=DisplayService.Hdr(m.Key,!m.Enabled); return "HDR 设置已更新"; },feedback:hdrButton,confirmed:()=> {
                if(Selected!=null && Selected.Key==confirmed.Key)Selected.UpdateState(confirmed);
                if(snapshot!=null)foreach(var current in snapshot.Monitors.Where(x=>x.Key==confirmed.Key))current.UpdateState(confirmed);
            });
        };
        var brightRow=Columns(); var brightTitle=Label("SDR 内容亮度",10,Color.White);
        brightRow.Controls.Add(brightTitle,0,0); white.ForeColor=muted; white.Tag="muted"; white.Anchor=AnchorStyles.Right; white.Margin=new Padding(4,0,0,0); brightRow.Controls.Add(white,1,0); Add(brightRow);
        slider.BackColor=bg; slider.Dock=DockStyle.Fill; slider.Margin=new Padding(0,4,0,14); Add(slider);
        slider.ValueChanged+=delegate { white.Text=slider.Value+"%  /  "+(80+slider.Value*4)+" nits"; QueueBrightness(); };
        slider.InteractionEnded+=delegate { QueueBrightness(); FlushBrightness(); };
        brightnessTimer.Interval=16; brightnessTimer.Tick+=delegate { FlushBrightness(); };
        Row("杜比视界",dolby,dolbyButton); dolbyButton.Click+=delegate { DolbyAction(); };
        dolbyGuardToggle.AutoSize=true; dolbyGuardToggle.Text="始终关闭杜比视界（所选显示器）"; dolbyGuardToggle.ForeColor=accent; dolbyGuardToggle.Margin=new Padding(0,0,0,3);
        dolbyGuardToggle.Checked=prefs.AlwaysDisableDolby; Add(dolbyGuardToggle);
        guardStatus.AutoSize=true; guardStatus.ForeColor=muted; guardStatus.Tag="muted"; guardStatus.Font=new Font(Font.FontFamily,8F); guardStatus.Margin=new Padding(0,0,0,0); Add(guardStatus);
        dolbyGuardToggle.CheckedChanged+=delegate {
            if(preview || binding)return;
            prefs.AlwaysDisableDolby=dolbyGuardToggle.Checked; SafeSave();
            guard.Configure(prefs.AlwaysDisableDolby,prefs.MonitorKey,DateTime.UtcNow);
            guardStatus.Text=prefs.AlwaysDisableDolby?"等待屏幕就绪；将执行开启→关闭。":"重新连接 / 点亮后自动开启一次再关闭。";
            Render();
        };
        guardStatus.Text=prefs.AlwaysDisableDolby?"守护已启用；等待屏幕就绪。":"重新连接 / 点亮后自动开启一次再关闭。";
        targetLayout=null;
        AddLabel("NVIDIA  ·  RTX 视频增强",8,muted,new Padding(0,0,0,8));
        var videoCard=new RoundedCard(); Add(videoCard); targetLayout=videoCard;
        Row("Super Resolution",super,superButton);
        superButton.Click+=delegate { VideoAction(true); };
        Row("高动态范围",videoHdr,videoButton);
        videoButton.Click+=delegate { VideoAction(false); };
        targetLayout=null;
        var tools=new BufferedTableLayoutPanel { AutoSize=true,Dock=DockStyle.Fill,ColumnCount=2,Margin=new Padding(0,2,0,12) };
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); Add(tools);
        var win=MakeButton("Windows HDR 设置"); tools.Controls.Add(win,0,0); win.Click+=delegate { Act(()=>OpenSettings(false),opening:true); };
        var nv=MakeButton("NVIDIA 视频设置"); win.Margin=new Padding(0,0,4,0); nv.Margin=new Padding(4,0,0,0); tools.Controls.Add(nv,1,0); nv.Click+=delegate { Act(()=>OpenSettings(true),opening:true); };
        var bottom=Columns(); Add(bottom); startup.AutoSize=true; startup.Anchor=AnchorStyles.Left; startup.Text="登录 Windows 时启动"; startup.ForeColor=muted; startup.Checked=Startup.Enabled; bottom.Controls.Add(startup,0,0);
        startup.CheckedChanged+=delegate { if(binding || preview) return; try { Startup.Set(startup.Checked); } catch(Exception ex) { binding=true; startup.Checked=Startup.Enabled; binding=false; Error(ex); } };
        var refresh=MakeButton("刷新"); bottom.Controls.Add(refresh,1,0); refresh.Click+=delegate { RefreshState(true); };
        message.AutoSize=true; message.ForeColor=muted; message.Tag="muted"; message.Font=new Font(Font.FontFamily,8F); message.Margin=new Padding(0,12,0,0); Add(message);
        layout.SizeChanged+=delegate { int w=Math.Max(120,layout.ClientSize.Width-4); message.MaximumSize=new Size(w,0); guardStatus.MaximumSize=new Size(w,0); };
        var menu=new ContextMenuStrip();
        menu.Items.Add("打开控制中心",null,delegate { Popup(); });
        menu.Items.Add("Windows HDR 设置",null,delegate { Act(()=>OpenSettings(false),opening:true); });
        menu.Items.Add("连接 NVIDIA 视频设置",null,delegate { Act(()=>OpenSettings(true),opening:true); });
        menu.Items.Add("指定 NVIDIA 控制面板路径…",null,delegate {
            using(var dialog=new OpenFileDialog { Filter="NVIDIA 控制面板 (nvcplui.exe)|nvcplui.exe",Title="选择 nvcplui.exe" })
                if(dialog.ShowDialog(this)==DialogResult.OK) { prefs.NvidiaPath=dialog.FileName; SafeSave(); }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出",null,delegate { exiting=true; Close(); });
        tray.Text="HDR 控制中心"; tray.ContextMenuStrip=menu; ApplyTheme(theme); tray.Visible=!preview;
        tray.MouseClick+=delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left)HandleTrayClick(GetMessageTime()); };
        FormClosing+=delegate(object sender,FormClosingEventArgs e) {
            TracePanel("close received reason="+e.CloseReason+" explicit="+userHidPanel+" exit="+exiting);
            if(e.CloseReason==CloseReason.WindowsShutDown)return;
            if(videoInProgress && exiting) { e.Cancel=true; message.Text="正在完成 RTX 设置，完成后退出。"; }
            else if(ProtectVideoPanel && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; TracePanel("RTX close blocked"); }
            else if(guard.Running) { e.Cancel=true; if(!exiting)Hide(); else message.Text="正在完成杜比视界关闭，完成后退出。"; }
            else if(!exiting && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); }
        };
        timer.Interval=3000; timer.Tick+=delegate { if(Visible && !slider.IsDragging && !brightnessWriting && !pendingBrightness.HasValue) RefreshState(false); };
        Shown+=delegate { if(!preview) { RefreshState(true); timer.Start(); } };
        recoveryTimer.Interval=1500; recoveryTimer.Tick+=delegate { RefreshTheme(); PollRecovery(); };
        geometryTimer.Interval=250; geometryTimer.Tick+=delegate { geometryTimer.Stop(); ReflowWindow(Screen.FromRectangle(Bounds).WorkingArea,false); };
        DpiChanged+=delegate { QueueGeometry(); };
        ResizeEnd+=delegate { if(!reflowing)logicalWidth=Math.Max(390,ClientSize.Width*96.0/Math.Max(96,DeviceDpi)); };
        Render(); InitializeDismiss();
        layout.ResumeLayout(true); scroll.ResumeLayout(true); ResumeLayout(true);
    }
    MonitorInfo Selected { get { return displays.SelectedItem as MonitorInfo; } }
    void Add(Control control) { var parent=targetLayout??layout; int row=parent.RowCount++; parent.RowStyles.Add(new RowStyle(SizeType.AutoSize)); parent.Controls.Add(control,0,row); }
    Label Label(string text,float size,Color color) { return new SmoothLabel { Text=text,ForeColor=color,Tag=color==muted?"muted":null,Font=new Font(Font.FontFamily,size),AutoSize=true,Margin=new Padding(0),Anchor=AnchorStyles.Left }; }
    Label AddLabel(string text,float size,Color color,Padding margin) {
        var l=Label(text,size,color); l.Margin=margin; Add(l); return l;
    }
    ModernButton MakeButton(string text) {
        var b=new ModernButton { Text=text };
        b.AutoSize=true; b.AutoSizeMode=AutoSizeMode.GrowAndShrink; b.Padding=new Padding(8,5,8,5); b.Dock=DockStyle.Fill; b.Margin=new Padding(2);
        return b;
    }
    string OpenSettings(bool video) {
        if(preview)Thread.Sleep(900);
        else if(video)PanelBridge.OpenNvidia(prefs.NvidiaPath);
        else PanelBridge.OpenWindows();
        return (video?"NVIDIA 视频设置已打开":"Windows HDR 设置已打开")+(preview?"（示例，未访问系统设置）":"");
    }
    void WireDrag(Control control) { control.MouseDown+=delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left)WindowChrome.Drag(this); }; }
    void RefreshTheme() { if(forcedTheme || IsDisposed || Disposing)return; var current=UiTheme.Read(); if(!theme.Same(current))ApplyTheme(current); }
    void ApplyTheme(UiTheme current) {
        popupLayoutReady=false;
        theme=current;
        if(IsHandleCreated) { WindowChrome.Apply(Handle,current); current.Glass=WindowChrome.ApplyBackdrop(Handle,current); }
        using(var batch=new LayoutBatch(layout))ThemeControl(this,current.Glass?Color.Black:current.Background);
        var previous=trayOwnedIcon; trayOwnedIcon=IconArtwork.Tray(current.TaskbarDark); tray.Icon=trayOwnedIcon; if(previous!=null)previous.Dispose();
        if(tray.ContextMenuStrip!=null) { tray.ContextMenuStrip.Renderer=new ThemeMenuRenderer(current); tray.ContextMenuStrip.BackColor=current.Surface; tray.ContextMenuStrip.ForeColor=current.Text; }
        if(IsHandleCreated)WindowChrome.Apply(Handle,current);
        Invalidate(true);
    }
    void ThemeControl(Control control,Color background) {
        var card=control as RoundedCard;
        control.BackColor=control is ComboBox?theme.Surface:(theme.Glass && control!=this?Color.Transparent:(card==null?background:Color.Transparent));
        control.ForeColor=(control.Tag as string)=="muted"?(theme.Glass?Color.FromArgb(theme.Dark?208:72,theme.Dark?208:72,theme.Dark?208:72):theme.Muted):theme.Text;
        var label=control as Label; if(label!=null)label.UseCompatibleTextRendering=true;
        var smooth=control as SmoothLabel; if(smooth!=null)smooth.Theme=theme;
        var panel=control as BufferedPanel; if(panel!=null)panel.Theme=theme;
        var table=control as BufferedTableLayoutPanel; if(table!=null)table.Theme=theme;
        if(card!=null)card.Theme=theme;
        var button=control as ModernButton; if(button!=null)button.Theme=theme;
        var check=control as ModernCheckBox; if(check!=null)check.Theme=theme;
        var brightness=control as BrightnessSlider; if(brightness!=null)brightness.Theme=theme;
        var combo=control as ModernComboBox; if(combo!=null) { combo.Theme=theme; combo.BackColor=theme.Surface; combo.ItemHeight=Font.Height+Math.Max(6,DeviceDpi/12); combo.UpdateRound(); }
        foreach(Control child in control.Controls)ThemeControl(child,card==null?background:(theme.Glass?Color.Transparent:theme.Surface));
        control.Invalidate();
    }
    protected override void SetClientSizeCore(int width,int height) {
        base.SetClientSizeCore(width,height);
        // The entire window is client space after WM_NCCALCSIZE. Correct the
        // default WinForms caption/frame calculation without recreating HWND.
        if(IsHandleCreated && (ClientSize.Width!=width || ClientSize.Height!=height))Size=new Size(Width+width-ClientSize.Width,Height+height-ClientSize.Height);
    }
    protected override CreateParams CreateParams {
        get { var parameters=base.CreateParams; parameters.Style &= ~0x00c00000; return parameters; }
    }
    TableLayoutPanel Columns() {
        var p=new BufferedTableLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Fill,ColumnCount=2,Margin=new Padding(0) };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); return p;
    }
    void Row(string title,Label status,ModernButton button) {
        var row=Columns(); row.Margin=new Padding(0,0,0,12);
        var text=new BufferedTableLayoutPanel { AutoSize=true,Dock=DockStyle.Fill,ColumnCount=1,Margin=new Padding(0) };
        text.Controls.Add(Label(title,10.5F,theme.Text),0,0);
        status.AutoSize=true; status.ForeColor=muted; status.Tag="muted"; status.Font=new Font(Font.FontFamily,8.5F); status.Margin=new Padding(0,3,0,0); text.Controls.Add(status,0,1); row.Controls.Add(text,0,0);
        ((ModernButton)button).IsSwitch=true;
        button.AutoSize=true; button.AutoSizeMode=AutoSizeMode.GrowAndShrink; button.Padding=new Padding(12,5,12,5); button.Margin=new Padding(12,0,0,0); button.Anchor=AnchorStyles.Right;
        button.BackColor=Color.FromArgb(35,44,60); button.ForeColor=accent;
        row.Controls.Add(button,1,0); Add(row);
    }
    void HandleTrayClick(int inputTime) {
        if(preparingPopup || !trayInput.TryAccept(inputTime)) { TracePanel("tray repeated/queued click ignored"); return; }
        bool opening=!PanelPresented;
        TracePanel("tray click "+(opening?"open":"hide"));
        TogglePanel();
        if(opening)trayInput.CompleteOpening(inputTime,Environment.TickCount);
    }
    bool PanelPresented { get { return Visible && IsHandleCreated && IsWindowVisible(Handle) && WindowState!=FormWindowState.Minimized && !WindowChrome.IsPopupCloaked(Handle); } }
    void TogglePanel() {
        // Clicking the tray transfers focus to Explorer. Presented state decides
        // whether to collapse; focusing another app must not turn this into a
        // Show/Activate/reposition cycle before the next click can collapse.
        if(PanelPresented) { userHidPanel=true; Hide(); } else Popup();
    }
    public void Popup() {
        PopupAt(Screen.FromPoint(Cursor.Position).WorkingArea);
    }
    void PopupAt(Rectangle area) {
        userHidPanel=false;
        if(preparingPopup)return;
        if(PanelPresented) { RaisePopup(); ActivatePopup(); RefreshState(false); return; }
        bool reuseLayout=popupLayoutReady && !geometryTimer.Enabled && preparedPopupArea==area && preparedPopupDpi==DeviceDpi && preparedPopupBounds==Bounds && preparedLayoutSize==layout.Size && WindowState==FormWindowState.Normal;
        preparingPopup=true; popupArea=area; geometryTimer.Stop();
        var handle=Handle;
        bool cloaked=WindowChrome.SetPopupCloak(handle,true);
        try {
            // Moving an invisible HWND selects the destination DPI without
            // exposing the temporary placement. Finish layout before Show.
            if(!reuseLayout) {
                if(WindowState!=FormWindowState.Normal)WindowState=FormWindowState.Normal;
                Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
                PerformAutoScale(); PerformLayout(); ReflowWindow(area,true);
            }
            Show();
            if(!IsWindowVisible(handle))ShowWindow(handle,5);
            RaisePopup();
            // Activate while the prepared surface is still concealed. Showing
            // the inactive Acrylic fallback first caused a visible material
            // change when foreground activation arrived after reveal.
            ActivatePopup();
            if(cloaked && theme.Glass && GetForegroundWindow()==handle)
                WindowChrome.PrepareActiveBackdrop(handle,theme);
            // Refresh/UpdateWindow alone does not paint every child HWND.
            // Keep the DWM surface concealed until the complete tree is ready.
            if(!WindowChrome.PaintPopup(handle))Refresh();
            if(cloaked)WindowChrome.FlushPopup();
        } finally {
            if((cloaked || WindowChrome.IsPopupCloaked(handle)) && !WindowChrome.RevealPopup(handle))Log(new Exception("控制中心首次绘制后解除 DWM 遮罩失败。"));
            geometryTimer.Stop(); preparingPopup=false;
        }
        // Windows can reject foreground activation while cloaked. Retry only
        // in that case; successful preparation needs no second activation.
        if(GetForegroundWindow()!=handle)ActivatePopup();
        preparedPopupBounds=Bounds; preparedPopupArea=area; preparedPopupDpi=DeviceDpi; preparedLayoutSize=layout.Size; popupLayoutReady=true;
        TracePanel("popup presented"); RefreshState(false);
    }
    void RaisePopup() {
        // Raise inside the existing normal/topmost band before revealing; this
        // neither changes geometry nor makes ordinary popups permanently pinned.
        if(!SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x0001|0x0002|0x0010|0x0200))Log(new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"唤起控制中心到前面失败。"));
    }
    void ActivatePopup() {
        // Native foreground state is authoritative after a cloaked Show;
        // ContainsFocus can already be true without reaching the foreground.
        SetForegroundWindow(Handle);
        if(GetForegroundWindow()!=Handle)Activate();
    }
    protected override void SetVisibleCore(bool value) {
        // Keep the existing panel visible instead of hiding and showing it again
        // after the NVIDIA transaction. Explicit tray hiding still takes effect.
        if(!value && ProtectVideoPanel && Visible) {
            TracePanel("RTX hide blocked"); return;
        }
        base.SetVisibleCore(value);
        if(!value && videoLayerPinned)ReleaseVideoLayer();
    }
    void PinVideoLayer() {
        if(videoLayerPinned || !Visible || WindowState==FormWindowState.Minimized || !IsHandleCreated)return;
        videoLayerWasTopMost=NativeTopMost(Handle);
        if(!SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010|0x0200)) {
            Log(new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"保持控制中心置顶失败。")); return;
        }
        videoLayerPinned=true; TracePanel("layer pinned");
    }
    void ReleaseVideoLayer() {
        if(!videoLayerPinned)return;
        if(IsHandleCreated && !IsDisposed)
            SetWindowPos(Handle,new IntPtr(videoLayerWasTopMost?-1:-2),0,0,0,0,0x0001|0x0002|0x0010|0x0200);
        videoLayerPinned=false; TracePanel("layer released");
    }
    static bool NativeTopMost(IntPtr hwnd) { return (GetWindowLongPtr(hwnd,-20).ToInt64() & 0x0008)!=0; }
    bool ProtectVideoPanel { get { return keepVideoPanelVisible && !userHidPanel && !exiting && !Disposing; } }
    void ObserveCloseInput(int msg,IntPtr wparam,IntPtr lparam) {
        // Arm closing only for input addressed to this panel, not any SC_CLOSE.
        long hit=wparam.ToInt64();
        if(((msg==0x00a1 || msg==0x00a3) && (hit==20 || hit==3)) || (msg==0x00a4 && (hit==2 || hit==3)) ||
           (msg==0x0104 && hit==0x73 && (lparam.ToInt64() & 0x20000000)!=0)) {
            panelCloseInput=DateTime.UtcNow; TracePanel("close input observed");
        }
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData) {
        if(preview && forcedTheme && keyData==Keys.F6) { ApplyTheme(UiTheme.Create(!theme.Dark)); return true; }
        if(keyData==(Keys.Alt|Keys.F4)) { panelCloseInput=DateTime.UtcNow; TracePanel("close input Alt+F4"); }
        return base.ProcessCmdKey(ref msg,keyData);
    }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if(keepVideoPanelVisible)TracePanel("visibility changed"); }
    protected override void OnPaintBackground(PaintEventArgs e) {
        if(theme==null || !theme.Glass) { base.OnPaintBackground(e); return; }
        Shapes.GlassSurface(this,theme,e.Graphics);
    }
    protected override void OnLoad(EventArgs e) {
        base.OnLoad(e); ReflowWindow(preparingPopup?popupArea:Screen.FromRectangle(Bounds).WorkingArea,preparingPopup);
    }
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        WindowChrome.Apply(Handle,theme);
        theme.Glass=WindowChrome.ApplyBackdrop(Handle,theme);
        using(var batch=new LayoutBatch(layout))ThemeControl(this,theme.Glass?Color.Black:theme.Background);
        popupTransitionsDisabled=WindowChrome.DisablePopupTransitions(Handle);
        if(preview)return;
        var guid=DisplayEvents.SessionDisplay;
        powerNotification=DisplayEvents.RegisterPowerSettingNotification(Handle,ref guid,0);
        sessionNotification=DisplayEvents.WTSRegisterSessionNotification(Handle,0);
        if(powerNotification==IntPtr.Zero)Log(new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"注册屏幕电源通知失败；继续使用连接检测。"));
        guard.Configure(prefs.AlwaysDisableDolby,prefs.MonitorKey,DateTime.UtcNow);
        recoveryTimer.Start(); timer.Start();
    }
    protected override void OnHandleDestroyed(EventArgs e) {
        popupTransitionsDisabled=false;
        if(powerNotification!=IntPtr.Zero) { DisplayEvents.UnregisterPowerSettingNotification(powerNotification); powerNotification=IntPtr.Zero; }
        if(sessionNotification) { DisplayEvents.WTSUnRegisterSessionNotification(Handle); sessionNotification=false; }
        base.OnHandleDestroyed(e);
    }
    void QueueGeometry() { popupLayoutReady=false; if(IsDisposed || Disposing || preparingPopup)return; geometryTimer.Stop(); geometryTimer.Start(); }
    void ReflowWindow(Rectangle area,bool anchor) {
        if(reflowing || IsDisposed)return;
        var previousBounds=Bounds; var previousClient=ClientSize;
        reflowing=true;
        try {
            int dpi=Math.Max(96,DeviceDpi),margin=WindowGeometry.Pixels(12,dpi);
            MinimumSize=Size.Empty;
            int frameW=Width-ClientSize.Width,frameH=Height-ClientSize.Height;
            ClientSize=new Size(Math.Max(1,Math.Min(WindowGeometry.Pixels(logicalWidth,dpi),area.Width-2*margin-frameW)),ClientSize.Height);
            layout.PerformLayout();
            int required=layout.GetPreferredSize(new Size(layout.Width,0)).Height+WindowGeometry.Pixels(40,dpi);
            var desired=new Rectangle(Location,new Size(ClientSize.Width+frameW,required+frameH));
            if(anchor)desired.Location=new Point(area.Right-desired.Width-margin,area.Bottom-desired.Height-margin);
            Bounds=WindowGeometry.Fit(desired,area,margin);
            MinimumSize=new Size(Math.Min(WindowGeometry.Pixels(390,dpi),Width),Math.Min(WindowGeometry.Pixels(320,dpi),Height));
            layout.PerformLayout();
            if(Bounds!=previousBounds || ClientSize!=previousClient) { Invalidate(true); TracePanel("geometry changed"); }
        } finally { reflowing=false; }
    }
    void Render() {
        using(var batch=new LayoutBatch(layout))RenderControls();
    }
    void RenderControls() {
        var m=Selected; hdr.Text=m==null?"请选择在线显示器":m.Error??(m.Supported?(m.Enabled?"已开启":"已关闭")+"  ·  "+(m.Active?"正在生效":"未生效"):"当前连接不支持 HDR");
        bool blocked=busy || operationButton!=null;
        hdrButton.Text=m!=null && m.Enabled?"关闭":"开启"; hdrButton.Enabled=!blocked && m!=null && m.Supported && m.Error==null;
        binding=true;
        bool adjusting=slider.IsDragging || brightnessWriting || pendingBrightness.HasValue || (DateTime.UtcNow-brightnessTouched).TotalMilliseconds<700;
        if(!adjusting && m!=null && m.White>=0) slider.Value=DisplayService.Percent((uint)m.White);
        slider.Enabled=!blocked && m!=null && m.Active && m.White>=0;
        white.Text=m!=null && m.White>=0?slider.Value+"%  /  "+(adjusting?(80+slider.Value*4):Math.Round(m.White*0.08))+" nits":"不可读取";
        binding=false;
        dolby.Text=snapshot==null?"未连接 Windows HDR 页面":snapshot.Dolby.ToString(); dolbyButton.Text=prefs.AlwaysDisableDolby?"修复关闭":"切换…"; dolbyButton.Enabled=!blocked && m!=null && m.Active;
        super.Text=snapshot==null?"开关未知  ·  活动状态未知":snapshot.Video.Super.ToString();
        videoHdr.Text=snapshot==null?"开关未知  ·  活动状态未知":snapshot.Video.Hdr.ToString();
        superButton.Text=snapshot==null || !snapshot.Video.Super.Enabled.HasValue?"切换…":snapshot.Video.Super.Enabled==true?"关闭":"开启";
        videoButton.Text=snapshot==null || !snapshot.Video.Hdr.Enabled.HasValue?"切换…":snapshot.Video.Hdr.Enabled==true?"关闭":"开启";
        superButton.Enabled=!blocked && (snapshot==null || !snapshot.Video.Super.Enabled.HasValue || snapshot.Video.Super.CanControl);
        videoButton.Enabled=!blocked && (snapshot==null || !snapshot.Video.Hdr.Enabled.HasValue || snapshot.Video.Hdr.CanControl);
        ((ModernButton)hdrButton).SwitchValue=m!=null && m.Supported?(bool?)m.Enabled:null;
        ((ModernButton)dolbyButton).SwitchValue=prefs.AlwaysDisableDolby || snapshot==null?null:snapshot.Dolby.Enabled;
        ((ModernButton)superButton).SwitchValue=snapshot==null?null:snapshot.Video.Super.Enabled;
        ((ModernButton)videoButton).SwitchValue=snapshot==null?null:snapshot.Video.Hdr.Enabled;
        hdrButton.AccessibleName="HDR · "+hdrButton.Text; dolbyButton.AccessibleName="杜比视界 · "+dolbyButton.Text;
        superButton.AccessibleName="Super Resolution · "+superButton.Text; videoButton.AccessibleName="RTX 高动态范围 · "+videoButton.Text;
    }
    MonitorInfo BindMonitors(List<MonitorInfo> monitors,string key) {
        binding=true;
        try {
            bool same=displays.Items.Count==monitors.Count;
            for(int i=0;same && i<monitors.Count;i++) {
                var old=(MonitorInfo)displays.Items[i]; var current=monitors[i];
                same=old.Key==current.Key && old.Name==current.Name && old.Gdi==current.Gdi;
            }
            if(same) {
                // Retain the native combo contents and selected object on a
                // status refresh. Rebuild only when display identity changes.
                for(int i=0;i<monitors.Count;i++)((MonitorInfo)displays.Items[i]).UpdateState(monitors[i]);
            } else {
                TracePanel("display list rebuilt");
                displays.BeginUpdate();
                try { displays.Items.Clear(); displays.Items.AddRange(monitors.Cast<object>().ToArray()); }
                finally { displays.EndUpdate(); }
            }
            var selected=displays.Items.Cast<MonitorInfo>().FirstOrDefault(m=>m.Key==key);
            if(selected==null && String.IsNullOrEmpty(key))selected=displays.Items.Cast<MonitorInfo>().FirstOrDefault();
            if(!Object.ReferenceEquals(Selected,selected))displays.SelectedItem=selected;
            return selected;
        } finally { binding=false; }
    }
    async void RefreshState(bool report) {
        if(busy || refreshing || preview || slider.IsDragging || brightnessWriting || pendingBrightness.HasValue) return; refreshing=true;
        int request=version,readBrightness=brightnessRevision; string key=prefs.MonitorKey;
        try {
            var monitors=await Task.Run(()=>DisplayService.List());
            if(IsDisposed || request!=version)return;
            // Publish native display controls first on cold startup. UIA calls
            // to other processes must not hold the monitor/HDR UI back.
            if(snapshot==null) {
                snapshot=new Snapshot { Monitors=monitors,Video=new VideoState(),Dolby=new Feature() };
                BindMonitors(monitors,key); Render();
                message.Text="显示器已就绪，正在读取视频和杜比视界状态…";
            }
            var result=await Worker.Run(()=> {
                string selected=String.IsNullOrEmpty(key) && monitors.Count>0?monitors[0].Key:key;
                var rawVideo=PanelBridge.Video();
                var dolby=PanelBridge.Dolby(selected,false);
                return new Snapshot { Monitors=monitors,Video=rawVideo,Dolby=dolby };
            });
            if(IsDisposed || request!=version) return;
            // Discard stale reads before changing caches; an older refresh must
            // not overwrite a more recent applied/pending RTX result.
            var video=result.Video;
            if(video.Super.Enabled.HasValue)lastVideo.Super=video.Super;
            else if(lastVideo.Super.Enabled.HasValue || lastVideo.Super.Requested.HasValue)video.Super=CachedFeature(lastVideo.Super);
            if(video.Hdr.Enabled.HasValue)lastVideo.Hdr=video.Hdr;
            else if(lastVideo.Hdr.Enabled.HasValue || lastVideo.Hdr.Requested.HasValue)video.Hdr=CachedFeature(lastVideo.Hdr);
            string selectedKey=String.IsNullOrEmpty(key) && result.Monitors.Count>0?result.Monitors[0].Key:key;
            lock(lastDolby) {
                if(result.Dolby.Enabled.HasValue)lastDolby[selectedKey]=result.Dolby;
                else if(lastDolby.ContainsKey(selectedKey)) { result.Dolby=CachedFeature(lastDolby[selectedKey]); result.Dolby.Activity="播放模式未检测"; }
            }
            var previous=Selected;
            if(readBrightness!=brightnessRevision && previous!=null) {
                var current=result.Monitors.FirstOrDefault(x=>x.Key==previous.Key);
                if(current!=null)current.White=previous.White;
            }
            snapshot=result;
            var m=BindMonitors(result.Monitors,key);
            if(m!=null && String.IsNullOrEmpty(key)) { prefs.MonitorKey=m.Key; SafeSave(); guard.Configure(prefs.AlwaysDisableDolby,prefs.MonitorKey,DateTime.UtcNow); }
            message.Text=(m==null?"上次选择的显示器未连接，请选择在线显示器。":"状态已刷新 "+DateTime.Now.ToString("HH:mm:ss")+"。NVIDIA 活动状态需控制面板提供可读取的状态文字。")+(String.IsNullOrEmpty(videoNotice)?"":Environment.NewLine+videoNotice);
            tray.Text="HDR 控制中心"+(m==null?"":" · "+(m.Enabled?"HDR 开":"HDR 关"));
        } catch(Exception ex) { message.Text=ex.Message; if(report) Log(ex); }
        finally { refreshing=false; if(!IsDisposed) { Render(); if(request!=version) RefreshState(false); else if(!busy)FinishButtonLoading(); } }
    }
    void FinishButtonLoading() {
        if(operationButton==null)return; var button=operationButton; operationButton=null;
        // Reconcile/enabled-state layout while the thumb is still held. Start
        // the movement clock afterwards so layout work cannot skip its first frames.
        if(!IsDisposed)Render();
        button.IsLoading=false;
    }
    async void Act(Func<string> action,bool videoOperation=false,ModernButton feedback=null,bool opening=false,Action confirmed=null) {
        if(busy || operationButton!=null || brightnessWriting || pendingBrightness.HasValue) { message.Text=openingOperation?"正在打开，请稍候…":"正在应用设置，请稍候。"; return; }
        userHidPanel=false; videoInProgress=videoOperation;
        if(videoOperation) { keepVideoPanelVisible=Visible; videoNotice=""; PinVideoLayer(); }
        if(videoOperation)TracePanel("RTX begin");
        busy=true; openingOperation=opening; operationButton=feedback; if(feedback!=null)feedback.IsLoading=true;
        version++; Render(); message.Text=opening?"正在打开，请稍候…":"正在应用，请稍候…";
        string result=null; bool confirmedApplied=false;
        try {
            result=await Worker.Run(action);
            if(!IsDisposed && confirmed!=null) { confirmed(); confirmedApplied=true; }
        }
        catch(Exception ex) {
            if(!IsDisposed) {
                if(preview)message.Text="预览操作失败："+ShortError(ex);
                else if(videoOperation) { videoNotice="RTX 操作失败："+ShortError(ex); message.Text=videoNotice; Log(ex); if(testNoDialogs)testOperationError=ex; }
                else Error(ex);
            }
        }
        finally {
            busy=false; videoInProgress=false; openingOperation=false;
            if(!IsDisposed) {
                if(exiting)Close();
                else {
                    if(videoOperation && result!=null && !String.IsNullOrEmpty(lastVideo.CleanupNotice))videoNotice=lastVideo.CleanupNotice;
                    // The requested operation has its own confirmed readback.
                    // Enable the controls and release its spinner now; an
                    // unrelated process-wide status refresh can finish later.
                    if(confirmedApplied) { Render(); FinishButtonLoading(); }
                    if(preview)Render();
                    if(result==null || preview)FinishButtonLoading();
                    RefreshState(false); if(result!=null)message.Text=result;
                    if(videoOperation)TracePanel("RTX end");
                }
            }
        }
    }
    void VideoAction(bool superResolution) {
        if(preview) { var feature=superResolution?snapshot.Video.Super:snapshot.Video.Hdr; PreviewToggle(superResolution?superButton:videoButton,()=>feature.Enabled=feature.Enabled!=true); return; }
        Feature known=snapshot==null?null:(superResolution?snapshot.Video.Super:snapshot.Video.Hdr);
        bool? target=known!=null && known.Enabled.HasValue?(bool?)!known.Enabled.Value:null;
        Act(()=> {
            lastVideo=PanelBridge.VideoTransaction(prefs.NvidiaPath,superResolution,target);
            if(!String.IsNullOrEmpty(lastVideo.CleanupNotice))return lastVideo.CleanupNotice;
            var result=superResolution?lastVideo.Super:lastVideo.Hdr;
            if(result.Requested.HasValue)return "已提交 RTX 设置；控制面板已关闭，待下次连接确认保存结果。";
            return superResolution?"Super Resolution 设置已应用":"RTX 视频 HDR 设置已应用";
        },true,superResolution?superButton:videoButton,confirmed:()=> { if(snapshot!=null)snapshot.Video=lastVideo; });
    }
    static Feature CachedFeature(Feature previous) {
        return new Feature { Enabled=previous.Enabled,Requested=previous.Requested,CanControl=true,Cached=previous.Enabled.HasValue,Activity="活动状态未检测",Detail=previous.Requested.HasValue?previous.Detail:"控制面板未提供实时读取，显示本次运行中上次确认的开关设置" };
    }
    internal void TracePanel(string change) {
        if(PanelTrace==null && (preview || (change!="RTX begin" && change!="RTX end" && !change.Contains("blocked") && !change.StartsWith("close") && !change.StartsWith("layer") && !change.StartsWith("tray ") && !change.StartsWith("popup ") && change!="visibility changed")))return;
        string line=DateTime.Now.ToString("O")+" "+change+" visible="+Visible+" nativeVisible="+(IsHandleCreated && IsWindowVisible(Handle))+" topMost="+(IsHandleCreated && NativeTopMost(Handle))+" cloak="+(IsHandleCreated && WindowChrome.IsPopupCloaked(Handle))+" foreground="+GetForegroundWindow().ToInt64()+" state="+WindowState+" bounds="+Bounds+" hwnd="+(IsHandleCreated?Handle.ToInt64():0);
        if(PanelTrace!=null) { PanelTrace(line); return; }
        try {
            Directory.CreateDirectory(Preferences.Folder); string path=Path.Combine(Preferences.Folder,"window.log");
            if(File.Exists(path) && new FileInfo(path).Length>200000)File.WriteAllText(path,line+Environment.NewLine);
            else File.AppendAllText(path,line+Environment.NewLine);
        } catch { }
    }
    void QueueBrightness() {
        if(binding || (preview && brightnessWriter==null) || Selected==null || !slider.Enabled)return;
        pendingBrightness=slider.Value; pendingTarget=new BrightnessTarget(Selected); brightnessTouched=DateTime.UtcNow; brightnessRevision++; brightnessTimer.Start();
        FlushBrightness();
    }
    async void FlushBrightness() {
        if((preview && brightnessWriter==null) || busy || brightnessWriting || !pendingBrightness.HasValue)return;
        var target=pendingTarget; string key=target.Key; int value=pendingBrightness.Value; bool final=!slider.IsDragging;
        pendingBrightness=null; brightnessWriting=true; brightnessSent=DateTime.UtcNow;
        try {
            await Task.Run(()=> {
                var watch=Stopwatch.StartNew();
                if(brightnessWriter!=null)brightnessWriter(target,value,final); else DisplayService.Brightness(target,value,final);
                if(BrightnessTrace!=null)BrightnessTrace(DateTime.Now.ToString("HH:mm:ss.fff")+" requested="+value+" dragging="+(!final)+" milliseconds="+watch.ElapsedMilliseconds);
            });
            if(!IsDisposed && Selected!=null && Selected.Key==key) { Selected.White=(int)DisplayService.Level(value); if(final) message.Text="SDR 内容亮度已保存"; }
        } catch(Exception ex) {
            if(pendingTarget!=null && pendingTarget.Key==key)pendingBrightness=null;
            if(!IsDisposed && Selected!=null && Selected.Key==key) { message.Text=ex.Message; if(!preview)Log(ex); }
        } finally {
            brightnessWriting=false;
            if(!IsDisposed && !pendingBrightness.HasValue && !slider.IsDragging) brightnessTimer.Stop();
            // Drain the latest requested value while dragging too. There is
            // never more than one driver write, and intermediate stale values
            // are coalesced. Mouse release queues the final verified write.
            if(!IsDisposed && pendingBrightness.HasValue)FlushBrightness();
        }
    }
    void DolbyAction() {
        if(preview) { PreviewToggle(dolbyButton,()=>snapshot.Dolby.Enabled=snapshot.Dolby.Enabled!=true); return; }
        if(Selected==null)return; string key=Selected.Key;
        bool force=prefs.AlwaysDisableDolby;
        if(force) { guard.Request("手动修复关闭",DateTime.UtcNow,true,true); guardStatus.Text="已安排开启→关闭修复。"; return; }
        Feature confirmed=null;
        Act(()=> { confirmed=PanelBridge.DolbyTransaction(key); lock(lastDolby)lastDolby[key]=confirmed; return "杜比视界已"+(confirmed.Enabled==true?"开启":"关闭"); },feedback:dolbyButton,confirmed:()=> { if(snapshot!=null && Selected!=null && Selected.Key==key)snapshot.Dolby=confirmed; });
    }
    void PreviewToggle(ModernButton button,Action update) { Act(()=> { Thread.Sleep(1100); update(); return "预览切换完成；未修改系统设置。"; },feedback:button); }
    void ObserveRecovery(MonitorInfo m) {
        string signature=m==null?null:m.Key+"|"+m.Adapter.High+":"+m.Adapter.Low+":"+m.Target;
        bool ready=m!=null && m.Active && m.Error==null;
        bool? hdrEnabled=m!=null && m.Error==null?(bool?)m.Enabled:null;
        bool wasPending=guard.Pending;
        guard.Observe(signature,ready,DateTime.UtcNow,hdrEnabled);
        if(wasPending && !guard.Pending && !guard.Running)guardStatus.Text="守护已启用；等待重新连接 / 点亮。";
    }
    async void PollRecovery() {
        if(preview || IsDisposed || exiting || guardPolling || !prefs.AlwaysDisableDolby || (busy && operationButton==hdrButton))return;
        guardPolling=true;
        try {
            string key=prefs.MonitorKey;
            var monitors=await Worker.Run(()=>DisplayService.List());
            if(IsDisposed || exiting || key!=prefs.MonitorKey || !prefs.AlwaysDisableDolby)return;
            var m=monitors.FirstOrDefault(x=>x.Key==key);
            bool ready=m!=null && m.Active && m.Error==null;
            if(busy && operationButton==hdrButton)return;
            ObserveRecovery(m);
            if(busy || refreshing || brightnessWriting || pendingBrightness.HasValue || slider.IsDragging)return;
            if(!guard.TryStart(ready,DateTime.UtcNow))return;
            RecoveryLog("开始："+guard.Reason);
            busy=true; version++; Render(); guardStatus.Text="正在修复：开启杜比视界 → 关闭…";
            bool success=false;
            try {
                var state=await Worker.Run(()=>PanelBridge.ForceDolbyOff(key,true));
                lock(lastDolby)lastDolby[key]=state;
                success=true;
                if(!IsDisposed)guardStatus.Text="已确认关闭 "+DateTime.Now.ToString("HH:mm:ss")+"；继续监测连接 / 点亮。";
            } catch(Exception ex) {
                Log(ex);
                if(!IsDisposed)guardStatus.Text="自动关闭失败："+ex.Message;
            } finally {
                guard.Finish(success,DateTime.UtcNow); busy=false;
                RecoveryLog(success?"完成：已回读确认杜比视界关闭。":"失败："+(guard.Pending?"已安排重试。":"达到重试上限，等待下一次连接 / 点亮。"));
                if(!IsDisposed) {
                    if(!prefs.AlwaysDisableDolby)guardStatus.Text="守护已关闭；本次关闭操作已完成。";
                    if(!success && guard.Pending)guardStatus.Text+=" 将稍后重试（最多 3 次）。";
                    if(exiting)Close(); else { Render(); RefreshState(false); }
                }
            }
        } catch(Exception ex) { Log(ex); if(!IsDisposed)guardStatus.Text="等待显示器恢复："+ex.Message; }
        finally { guardPolling=false; }
    }
    void SafeSave() { try { prefs.Save(); } catch(Exception ex) { Error(ex); } }
    void Error(Exception ex) { message.Text=ex.Message; Log(ex); if(testNoDialogs) { testOperationError=ex; return; } MessageBox.Show(this,ex.Message,"HDR 控制中心",MessageBoxButtons.OK,MessageBoxIcon.Information); }
    static string ShortError(Exception ex) { string first=(ex.Message??"未知错误").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"未知错误"; return first.Length>110?first.Substring(0,110)+"…":first; }
    static void Log(Exception ex) { try { Directory.CreateDirectory(Preferences.Folder); var p=Path.Combine(Preferences.Folder,"errors.log"); if(File.Exists(p) && new FileInfo(p).Length>200000) File.Delete(p); File.AppendAllText(p,DateTime.Now.ToString("s")+" "+ex+Environment.NewLine); } catch { } }
    static void RecoveryLog(string text) { try { Directory.CreateDirectory(Preferences.Folder); var p=Path.Combine(Preferences.Folder,"recovery.log"); if(File.Exists(p) && new FileInfo(p).Length>200000)File.Delete(p); File.AppendAllText(p,DateTime.Now.ToString("s")+" "+text+Environment.NewLine); } catch { } }
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x0085) { m.Result=IntPtr.Zero; return; }
        // Let DWM observe activation so Desktop Acrylic remains live. -1
        // suppresses the old non-client caption repaint on this custom frame.
        if(m.Msg==0x0086) { m.LParam=new IntPtr(-1); base.WndProc(ref m); return; }
        if(m.Msg==0x0083 && m.WParam!=IntPtr.Zero) { m.Result=IntPtr.Zero; return; }
        if(m.Msg==0x0084 && WindowState==FormWindowState.Normal) {
            long coordinates=m.LParam.ToInt64(); var point=PointToClient(new Point((short)(coordinates & 0xffff),(short)((coordinates>>16)&0xffff)));
            int edge=Math.Max(5,DeviceDpi*6/96),hit=1;
            bool left=point.X<edge,right=point.X>=ClientSize.Width-edge,top=point.Y<edge,bottom=point.Y>=ClientSize.Height-edge;
            if(top)hit=left?13:right?14:12; else if(bottom)hit=left?16:right?17:15; else if(left)hit=10; else if(right)hit=11;
            m.Result=new IntPtr(hit); return;
        }
        if((m.Msg==0x001a || m.Msg==0x0015 || m.Msg==0x031a || m.Msg==0x0320) && IsHandleCreated && !themeQueued && !IsDisposed) {
            themeQueued=true; BeginInvoke((Action)delegate { themeQueued=false; RefreshTheme(); });
        }
        // Native owner-window notifications bypass Form.Hide/SetVisibleCore.
        // Keep the HWND visible before Windows applies the change, including
        // delayed messages after the video worker has already returned.
        ObserveCloseInput(m.Msg,m.WParam,m.LParam);
        if(m.Msg==0x0112 && (m.WParam.ToInt64() & 0xfff0)==0xf060) {
            bool input=(DateTime.UtcNow-panelCloseInput).TotalSeconds<=5;
            TracePanel("close system command input="+input);
            if(ProtectVideoPanel && !input) { TracePanel("system close blocked"); m.Result=IntPtr.Zero; return; }
            userHidPanel=true; panelCloseInput=DateTime.MinValue;
        }
        if(ProtectVideoPanel && Visible) {
            if(m.Msg==0x0018 && m.WParam==IntPtr.Zero) {
                TracePanel("native hide blocked reason="+m.LParam.ToInt64()); m.Result=IntPtr.Zero; return;
            }
            if(m.Msg==0x0046 && m.LParam!=IntPtr.Zero) {
                var position=(PanelWindowPosition)Marshal.PtrToStructure(m.LParam,typeof(PanelWindowPosition));
                if((position.Flags & 0x0080)!=0) {
                    position.Flags &= ~0x0080u;
                    Marshal.StructureToPtr(position,m.LParam,false); TracePanel("native position hide blocked");
                }
            }
        }
        if(m.Msg==0x007e) { version++; snapshot=null; QueueGeometry(); PollRecovery(); if(Visible)RefreshState(false); }
        else if(m.Msg==0x001a && m.WParam.ToInt64()==0x002f)QueueGeometry(); // SPI_SETWORKAREA
        else if(m.Msg==0x0218) {
            int kind=m.WParam.ToInt32();
            if(kind==0x8013 && m.LParam!=IntPtr.Zero) {
                var id=(Guid)Marshal.PtrToStructure(m.LParam,typeof(Guid));
                if(id==DisplayEvents.SessionDisplay && Marshal.ReadInt32(m.LParam,16)>=4)guard.Power(Marshal.ReadInt32(m.LParam,20),DateTime.UtcNow);
            } else if(kind==0x12 || kind==0x7) { QueueGeometry(); guard.Request("睡眠恢复",DateTime.UtcNow,true); }
        } else if(m.Msg==0x02b1 && m.WParam.ToInt32()==8)guard.Request("解锁恢复",DateTime.UtcNow,true);
        base.WndProc(ref m);
        // WM_SHOWWINDOW precedes the first OnLoad layout. The completed native
        // show transaction is the point at which the surface becomes visible.
        if(m.Msg==0x0047 && m.LParam!=IntPtr.Zero && popupShowObserver!=null) {
            var position=(PanelWindowPosition)Marshal.PtrToStructure(m.LParam,typeof(PanelWindowPosition));
            if((position.Flags & 0x0040)!=0 && IsWindowVisible(Handle))popupShowObserver(Bounds);
        }
    }
    protected override void Dispose(bool disposing) { if(disposing) { DisposeDismiss(); timer.Dispose(); brightnessTimer.Dispose(); recoveryTimer.Dispose(); geometryTimer.Dispose(); tray.Visible=false; tray.Dispose(); if(trayOwnedIcon!=null) { trayOwnedIcon.Dispose(); trayOwnedIcon=null; } } base.Dispose(disposing); }
    public void Preview(string path,bool? dark) {
        if(dark.HasValue) { forcedTheme=true; ApplyTheme(UiTheme.Create(dark.Value)); }
        if(path!=null) { theme.Transparency=false; ApplyTheme(theme); }
        binding=true; var m=new MonitorInfo { Key="preview",Name="显示器预览（示例）",Gdi=@"\\.\DISPLAY1",Supported=true,Enabled=true,Active=true,White=2500 };
        displays.Items.Add(m); displays.SelectedItem=m; binding=false;
        snapshot=new Snapshot { Monitors=new List<MonitorInfo>{m},Dolby=new Feature { Enabled=false,CanControl=true,Activity="播放模式未检测" },Video=new VideoState { Super=new Feature { Enabled=true,CanControl=true,Activity="活动状态未检测" },Hdr=new Feature { Enabled=false,CanControl=true,Activity="活动状态未检测" } } };
        message.Text="界面预览：显示器与 HDR 数值为示例；未读取或修改系统设置。"; Render();
        if(path==null) { FormBorderStyle=FormBorderStyle.Sizable; ShowInTaskbar=true; }
        Popup(); AssertLayout();
        if(path==null)return;
        using(var b=new Bitmap(ClientSize.Width,ClientSize.Height)) {
            Controls[0].DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));
            using(var g=Graphics.FromImage(b))using(var corners=new System.Drawing.Drawing2D.GraphicsPath()) {
                corners.AddRectangle(new Rectangle(0,0,b.Width,b.Height)); using(var round=Shapes.Round(new RectangleF(0,0,b.Width,b.Height),8*DeviceDpi/96F))corners.AddPath(round,false);
                g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy; using(var clear=new SolidBrush(Color.Transparent))g.FillPath(clear,corners);
            }
            b.Save(path);
        }
        exiting=true; Close();
    }
    public void LayoutTest(string path) {
        binding=true; var m=new MonitorInfo { Key="test",Name="Mi Monitor",Gdi=@"\\.\DISPLAY5",Supported=true,Enabled=true,Active=true,White=2500 };
        displays.Items.Add(m); displays.SelectedItem=m; binding=false;
        snapshot=new Snapshot { Monitors=new List<MonitorInfo>{m},Dolby=new Feature(),Video=new VideoState() };
        var lines=new List<string>(); Render(); Show();
        RequireLayout(DisplayEvents.AreDpiAwarenessContextsEqual(DisplayEvents.GetWindowDpiAwarenessContext(Handle),new IntPtr(-4)),"PerMonitorV2 enabled");
        // Alternate monitors each round to exercise repeated DPI transitions.
        for(int repeat=0;repeat<3;repeat++)foreach(var screen in Screen.AllScreens) {
            MinimumSize=Size.Empty;
            Bounds=new Rectangle(screen.WorkingArea.Left+30,screen.WorkingArea.Top+30,60,80);
            Application.DoEvents();
            var notification=Message.Create(Handle,0x007e,IntPtr.Zero,IntPtr.Zero); WndProc(ref notification);
            ReflowWindow(screen.WorkingArea,false); Application.DoEvents();
            AssertLayout();
            var handle=Handle; var bounds=Bounds;
            ApplyTheme(UiTheme.Create(repeat%2==0)); Render(); Application.DoEvents(); AssertLayout();
            RequireLayout(Handle==handle && Bounds==bounds,"Theme change keeps window handle and geometry stable");
            RequireLayout(screen.WorkingArea.Contains(Bounds),"Window within work area");
            int expected=Math.Min(WindowGeometry.Pixels(logicalWidth,DeviceDpi),screen.WorkingArea.Width-WindowGeometry.Pixels(24,DeviceDpi)-(Width-ClientSize.Width));
            RequireLayout(Math.Abs(ClientSize.Width-expected)<=2,"Logical width recovered without cumulative scaling");
            lines.Add(screen.DeviceName+" iteration="+repeat+" dpi="+DeviceDpi+" client="+ClientSize+" bounds="+Bounds);
        }
        File.WriteAllLines(path,lines); exiting=true; Close();
    }
    public void BrightnessUiTest(string path) {
        forcedTheme=true;
        binding=true; var monitor=new MonitorInfo { Key="test",Name="Mi Monitor",Gdi=@"\\.\DISPLAY5",Supported=true,Enabled=true,Active=true,White=2500 };
        displays.Items.Add(monitor); displays.SelectedItem=monitor; binding=false;
        snapshot=new Snapshot { Monitors=new List<MonitorInfo>{monitor},Dolby=new Feature(),Video=new VideoState() };
        Render(); Show(); Application.DoEvents(); geometryTimer.Stop();
        var down=typeof(BrightnessSlider).GetMethod("OnMouseDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        var move=typeof(BrightnessSlider).GetMethod("OnMouseMove",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        var up=typeof(BrightnessSlider).GetMethod("OnMouseUp",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        int invalidations=0,moves=0,selectionChanges=0; var lines=new List<string>();
        displays.Invalidated+=delegate { invalidations++; }; displays.SizeChanged+=delegate { moves++; }; displays.LocationChanged+=delegate { moves++; }; displays.SelectedIndexChanged+=delegate { selectionChanges++; };
        foreach(var screen in Screen.AllScreens)foreach(bool dark in new[]{false,true}) {
            Location=new Point(screen.WorkingArea.Left+30,screen.WorkingArea.Top+30); Application.DoEvents();
            ApplyTheme(UiTheme.Create(dark)); ReflowWindow(screen.WorkingArea,false); Application.DoEvents(); geometryTimer.Stop();
            var mouse=new MouseEventArgs(MouseButtons.Left,1,slider.Width/2,slider.Height/2,0); down.Invoke(slider,new object[]{mouse}); Application.DoEvents();
            int paints=((ModernComboBox)displays).PaintTransactions; invalidations=moves=selectionChanges=0;
            var bounds=displays.Bounds; var handle=displays.Handle; var readoutSize=white.Size;
            var widest=TextRenderer.MeasureText("100%  /  480 nits",white.Font);
            RequireLayout(white.Width>=widest.Width && white.Width<=widest.Width+12 && white.Height>=widest.Height,"Fixed readout follows its actual font after DPI transitions");
            for(int i=0;i<120;i++) {
                int percent=new[]{0,9,10,99,100,1,88,20}[i%8]; int inset=WindowGeometry.Pixels(12,DeviceDpi);
                mouse=new MouseEventArgs(MouseButtons.Left,1,inset+(slider.Width-2*inset)*percent/100,slider.Height/2,0); move.Invoke(slider,new object[]{mouse});
                var setting=Message.Create(Handle,0x001a,IntPtr.Zero,IntPtr.Zero); WndProc(ref setting); Application.DoEvents();
                RequireLayout(white.Text.StartsWith(slider.Value+"%") && slider.IsDragging && !geometryTimer.Enabled,"Held brightness positions update without unrelated settings scheduling geometry");
            }
            int selectorPaints=((ModernComboBox)displays).PaintTransactions-paints;
            lines.Add("dpi="+DeviceDpi+" dark="+dark+" samples=120 selector paints="+selectorPaints+" invalidations="+invalidations+" bounds changes="+moves+" selection changes="+selectionChanges+" readout before="+readoutSize+" after="+white.Size);
            File.WriteAllLines(path,lines);
            RequireLayout(selectorPaints==0 && invalidations==0 && moves==0 && selectionChanges==0 && displays.Bounds==bounds && displays.Handle==handle && white.Size==readoutSize,"Continuous brightness changes never repaint, resize or rebind the selector");
            up.Invoke(slider,new object[]{mouse});
        }
        var workArea=Message.Create(Handle,0x001a,new IntPtr(0x002f),IntPtr.Zero); WndProc(ref workArea);
        RequireLayout(geometryTimer.Enabled,"Work area changes still schedule geometry");
        lines.Add("PASS: live readout, selector isolation, work-area notification, both themes and all monitor DPIs. No display settings changed."); File.WriteAllLines(path,lines);
        exiting=true; Close();
    }
    static void RequireLayout(bool ok,string name) { if(!ok)throw new Exception("Layout test failed: "+name); }
    public void PopupOpeningTest(string path) {
        binding=true;
        var monitor=new MonitorInfo { Key="test",Name="Mi Monitor",Gdi=@"\\.\DISPLAY5",Supported=true,Enabled=true,Active=true,White=2500 };
        displays.Items.Add(monitor); displays.SelectedItem=monitor; binding=false;
        snapshot=new Snapshot { Monitors=new List<MonitorInfo>{monitor},Dolby=new Feature(),Video=new VideoState() }; Render();
        var shows=new List<Rectangle>(); var visibleGeometry=new List<Rectangle>(); var lines=new List<string>();
        var showCloaks=new List<int>(); var painted=new HashSet<Control>(); int exposedPaints=0;
        // The fill-docked scroll surface covers the form's entire client area;
        // WS_CLIPCHILDREN can legitimately leave the form itself no paint area.
        var paintedControls=new Control[]{Controls[0],layout,hdrButton,white,slider,superButton,dolbyGuardToggle};
        var paintWatches=new List<PopupPaintWatch>();
        foreach(var control in paintedControls)paintWatches.Add(new PopupPaintWatch(control,delegate {
            if(!preparingPopup || !IsWindowVisible(Handle))return;
            painted.Add(control); if(PopupCloakedState()==0)exposedPaints++;
        }));
        popupShowObserver=bounds=> { shows.Add(bounds); showCloaks.Add(PopupCloakedState()); };
        EventHandler record=delegate { if(IsHandleCreated && IsWindowVisible(Handle))visibleGeometry.Add(Bounds); };
        LocationChanged+=record; SizeChanged+=record;
        forcedTheme=true;
        for(int turn=0;turn<6;turn++)foreach(var screen in Screen.AllScreens)for(int repeat=0;repeat<2;repeat++) {
            if(Visible)TogglePanel(); ApplyTheme(UiTheme.Create(turn>=3));
            shows.Clear(); visibleGeometry.Clear(); showCloaks.Clear(); painted.Clear(); exposedPaints=0;
            int selectorPaints=((ModernComboBox)displays).PaintTransactions;
            var opening=Stopwatch.StartNew();
            PopupAt(screen.WorkingArea);
            long openingMilliseconds=opening.ElapsedMilliseconds;
            var finalBounds=Bounds; var handle=Handle;
            var settled=Stopwatch.StartNew(); while(settled.ElapsedMilliseconds<400) { Application.DoEvents(); Thread.Sleep(5); }
            lines.Add(screen.DeviceName+" turn="+turn+" repeat="+repeat+" dark="+theme.Dark+" dpi="+DeviceDpi+" first show="+(shows.Count==0?"missing":shows[0].ToString())+" final="+Bounds+" visible geometry changes="+visibleGeometry.Count+" delayed timer="+geometryTimer.Enabled+" show cloak="+String.Join(",",showCloaks)+" prepared controls="+painted.Count+" exposed paints="+exposedPaints+" popup ms="+openingMilliseconds);
            File.WriteAllLines(path,lines);
            if(painted.Count!=paintedControls.Length)File.AppendAllText(path,"Missing paints: "+String.Join(",",paintedControls.Where(c=>!painted.Contains(c)).Select(c=>c.GetType().Name+"/"+c.Text))+Environment.NewLine);
            RequireLayout(showCloaks.Count==1 && showCloaks[0]!=0 && PopupCloakedState()==0 && painted.Count==paintedControls.Length && exposedPaints==0 && ((ModernComboBox)displays).PaintTransactions>selectorPaints,"Initial surface stays cloaked until the form, child controls and selector have painted; popup returns fully revealed");
            RequireLayout(popupTransitionsDisabled,"DWM accepted disabling only this panel's show/hide transitions");
            RequireLayout(shows.Count==1 && shows[0]==finalBounds && visibleGeometry.All(b=>b==finalBounds) && Bounds==finalBounds && Handle==handle && !geometryTimer.Enabled,"Popup reaches its final bounds before native show, with no visible move/resize or delayed reflow");
            RequireLayout(screen.WorkingArea.Contains(Bounds),"Popup is within the target monitor work area");
            AssertLayout();
            shows.Clear(); visibleGeometry.Clear(); var visibleBounds=Bounds;
            PopupAt(screen.WorkingArea); Application.DoEvents();
            RequireLayout(shows.Count==0 && visibleGeometry.Count==0 && Bounds==visibleBounds,"Explicit open of an already visible panel never shows/repositions it again");
        }
        popupShowObserver=null; LocationChanged-=record; SizeChanged-=record;
        foreach(var watch in paintWatches)watch.Dispose();
        lines.Add("PASS: complete first-frame painting while DWM-cloaked, reveal after paint, 36 cold/repeated popup cycles, both themes, three monitor DPIs, no visible geometry changes or delayed reflow, visible open. No display settings changed."); File.WriteAllLines(path,lines);
        exiting=true; Close();
    }
    int PopupCloakedState() {
        int state; int result=DwmGetWindowAttribute(Handle,14,out state,4);
        RequireLayout(result>=0,"DWM cloak state can be read"); return state;
    }
    sealed class PopupPaintWatch : NativeWindow,IDisposable {
        readonly Action painted;
        public PopupPaintWatch(Control control,Action notify) { painted=notify; AssignHandle(control.Handle); }
        protected override void WndProc(ref Message m) { base.WndProc(ref m); if(m.Msg==0x000f)painted(); }
        public void Dispose() { ReleaseHandle(); }
    }
    public void TrayInteractionTest(string path) {
        // This suite isolates tray callbacks. Outside-click/focus dismissal has
        // its own native suite, including tray reopen after automatic dismissal.
        autoHideSuppressed=true;
        binding=true;
        var monitor=new MonitorInfo { Key="test",Name="Mi Monitor",Gdi=@"\\.\DISPLAY5",Supported=true,Enabled=true,Active=true,White=2500 };
        displays.Items.Add(monitor); displays.SelectedItem=monitor; binding=false;
        snapshot=new Snapshot { Monitors=new List<MonitorInfo>{monitor},Dolby=new Feature(),Video=new VideoState() }; Render();
        var lines=new List<string>(); int visibleChanges=0;
        VisibleChanged+=delegate { visibleChanges++; };
        int time=Environment.TickCount,spacing=Math.Max(1000,SystemInformation.DoubleClickTime+100);
        HandleTrayClick(time); Application.DoEvents(); var bounds=Bounds; var handle=Handle;
        RequireLayout(Visible && IsWindowVisible(handle) && PopupCloakedState()==0,"One tray click opens the panel");
        HandleTrayClick(unchecked(time+100)); Application.DoEvents();
        File.WriteAllText(path,"After a rapid follow-up click: visible="+Visible+" changes="+visibleChanges+Environment.NewLine);
        RequireLayout(Visible && visibleChanges==1 && Bounds==bounds && Handle==handle,"A rapid follow-up click does not undo opening");
        time=unchecked(time+spacing+100); HandleTrayClick(time); Application.DoEvents();
        RequireLayout(!Visible && !IsWindowVisible(handle),"A separate click still hides immediately");
        HandleTrayClick(unchecked(time+100)); Application.DoEvents();
        RequireLayout(!Visible,"A rapid follow-up click does not undo hiding");
        time=unchecked(time+spacing+100); HandleTrayClick(time); Application.DoEvents();
        RequireLayout(Visible && PopupCloakedState()==0,"A separate click reopens immediately");
        RequireLayout(WindowChrome.SetPopupCloak(handle,true) && PopupCloakedState()!=0,"Simulate a visible-but-cloaked panel");
        time=unchecked(time+spacing); HandleTrayClick(time); Application.DoEvents();
        RequireLayout(Visible && IsWindowVisible(handle) && PopupCloakedState()==0 && Handle==handle,"One click repairs a stale cloak without first hiding the panel");
        WindowState=FormWindowState.Minimized; Application.DoEvents();
        time=unchecked(time+spacing); HandleTrayClick(time); Application.DoEvents();
        RequireLayout(Visible && WindowState==FormWindowState.Normal && PopupCloakedState()==0,"One click restores a minimized panel");
        using(var external=new Form { Text="Tray foreground test",ShowInTaskbar=false }) {
            external.Show(); external.Activate(); Application.DoEvents();
            RequireLayout(!ContainsFocus,"The visible panel has lost focus");
            time=unchecked(time+spacing); HandleTrayClick(time); Application.DoEvents();
            RequireLayout(!Visible,"One click hides an unfocused visible panel");
            time=unchecked(time+spacing); HandleTrayClick(time); Application.DoEvents();
            RequireLayout(Visible && AboveWindow(handle,external.Handle) && !NativeTopMost(handle),"Reopening raises the panel above a normal external window without permanent topmost");
            File.AppendAllText(path,"Reopen foreground="+GetForegroundWindow()+" panel="+handle+" focus="+ContainsFocus+Environment.NewLine);
            RequireLayout(GetForegroundWindow()==handle,"Reopening activates the actual native foreground window");
        }
        lines.Add("PASS: single click opens, rapid repeated clicks do not undo opening/hiding, separate click hides immediately, stale DWM cloak repaired in one click, minimized window restored, unfocused visible window hides in one click, reopening reaches native foreground and stays in the normal Z-order band. No display settings changed.");
        File.WriteAllLines(path,lines); exiting=true; Close();
    }
    public void PanelLifecycleTest(string path) {
        // Test programmatic native-close/Z-order guards separately from the
        // new dismissal policy, exercised in RealtimePopupTest.
        autoHideSuppressed=true;
        using(var externalPanel=new Form { Text="Control-panel focus test",ShowInTaskbar=false }) {
            Show(); Activate(); Application.DoEvents();
            externalPanel.Show(); externalPanel.Activate(); Application.DoEvents();
            RequireLayout(Visible && !ContainsFocus && externalPanel.ContainsFocus,"An opened control-panel window owns focus");
            var initialBounds=Bounds; var initialHandle=Handle; int activated=0;
            EventHandler observeActivation=delegate { activated++; }; Activated+=observeActivation;
            TogglePanel(); Application.DoEvents(); Activated-=observeActivation;
            RequireLayout(!Visible && !IsWindowVisible(Handle) && activated==0 && Bounds==initialBounds && Handle==initialHandle,"One tray click hides an ordinary unfocused panel without jumping or activating");
            TogglePanel(); Application.DoEvents(); geometryTimer.Stop();
            RequireLayout(Visible && IsWindowVisible(Handle),"One tray click reopens a hidden ordinary panel");
        }
        Show(); bool intercepted=false; int visibilityChanges=0,sizeChanges=0;
        VisibleChanged+=delegate { visibilityChanges++; }; SizeChanged+=delegate { sizeChanges++; };
        FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(videoInProgress)intercepted=e.Cancel; };
        Act(()=>{ Thread.Sleep(80); return "test"; },true);
        Close(); // External closing request while a video transaction is active.
        Hide(); // Simulate a driver window/focus transition hiding the tool panel.
        RequireLayout(intercepted && Visible && visibilityChanges==0,"Close and hide are blocked before visibility changes");
        WaitForAction();
        RequireLayout(Visible && visibilityChanges==0,"Video completion never hides and reopens the panel");
        var nativeHide=Message.Create(Handle,0x0018,IntPtr.Zero,new IntPtr(1));
        WndProc(ref nativeHide); Application.DoEvents();
        RequireLayout(IsWindowVisible(Handle) && Visible && visibilityChanges==0,"Delayed native owner-hide after transaction does not hide the production tool window");
        SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x0080|0x0010|0x0004|0x0002|0x0001);
        Application.DoEvents();
        RequireLayout(IsWindowVisible(Handle) && Visible && visibilityChanges==0,"Native SetWindowPos hiding is prevented without reopening");
        var delayedClose=Message.Create(Handle,0x0010,IntPtr.Zero,IntPtr.Zero); WndProc(ref delayedClose);
        RequireLayout(IsWindowVisible(Handle) && Visible && visibilityChanges==0,"Delayed native close is blocked after completion");
        var externalCommand=Message.Create(Handle,0x0112,new IntPtr(0xf060),IntPtr.Zero); WndProc(ref externalCommand);
        RequireLayout(IsWindowVisible(Handle) && Visible && visibilityChanges==0,"System close without panel input cannot impersonate a user close");
        using(var ownProcess=Process.GetCurrentProcess()) {
            var wrongWindow=new NvidiaWindow(Handle,(uint)ownProcess.Id,ownProcess.StartTime.ToUniversalTime().Ticks);
            RequireLayout(!wrongWindow.Alive() && !wrongWindow.RequestClose() && IsWindowVisible(Handle),"Native NVIDIA cleanup rejects this control-center HWND before posting any message");
        }
        userHidPanel=true;
        Hide(); Act(()=>{ Thread.Sleep(30); return "test"; },true); WaitForAction();
        RequireLayout(!Visible && !NativeTopMost(Handle),"Hidden tray operation does not open or pin a panel");
        Show(); Act(()=>{ Thread.Sleep(30); return "test"; },true); userHidPanel=true; Hide(); WaitForAction();
        RequireLayout(!Visible && !NativeTopMost(Handle),"Explicit tray hide releases the temporary layer");
        Show(); ReflowWindow(Screen.FromRectangle(Bounds).WorkingArea,false); geometryTimer.Stop();
        var stableBounds=Bounds; var stableHandle=Handle; visibilityChanges=0; sizeChanges=0;
        Act(()=>{ Thread.Sleep(30); return "test"; },true); WaitForAction();
        RequireLayout(Visible && Bounds==stableBounds && Handle==stableHandle && visibilityChanges==0 && sizeChanges==0 && !geometryTimer.Enabled,"Visible video panel is not reshown, resized, recreated or unnecessarily reflowed");
        using(var other=new Form { Text="Panel lifecycle focus test",StartPosition=FormStartPosition.Manual,Location=Location,Size=new Size(260,120) }) {
            Activate(); Application.DoEvents();
            Act(()=>{ Thread.Sleep(100); return "test"; },true);
            other.Show(); other.Activate(); Application.DoEvents();
            RequireLayout(other.ContainsFocus && !ContainsFocus,"Focus moved to the other window during the operation");
            RequireLayout(NativeTopMost(Handle) && AboveWindow(Handle,other.Handle),"Other active normal window cannot cover the pinned panel");
            int activations=0; EventHandler activated=delegate { activations++; }; Activated+=activated;
            WaitForAction(); Application.DoEvents(); Activated-=activated;
            RequireLayout(Visible && other.ContainsFocus && !ContainsFocus && activations==0 && NativeTopMost(Handle) && AboveWindow(Handle,other.Handle),"After completion the panel stays above the active window without taking focus back");
            int toggleActivations=0,toggleVisibility=0,toggleSizes=0;
            var toggleBounds=Bounds; var toggleHandle=Handle;
            EventHandler trackActivation=delegate { toggleActivations++; },trackVisibility=delegate { toggleVisibility++; },trackSize=delegate { toggleSizes++; };
            Activated+=trackActivation; VisibleChanged+=trackVisibility; SizeChanged+=trackSize;
            TogglePanel(); Application.DoEvents();
            Activated-=trackActivation; VisibleChanged-=trackVisibility; SizeChanged-=trackSize;
            RequireLayout(!Visible && !IsWindowVisible(Handle) && !NativeTopMost(Handle) && toggleActivations==0 && toggleVisibility==1 && toggleSizes==0 && Bounds==toggleBounds && Handle==toggleHandle,"First tray click hides an unfocused RTX-protected panel without activation, repositioning or reopening");
            TogglePanel(); Application.DoEvents(); geometryTimer.Stop();
            RequireLayout(Visible && IsWindowVisible(Handle) && !userHidPanel,"Next tray click opens the hidden panel");
        }
        Act(()=>{ Thread.Sleep(30); return "test"; },true); WindowState=FormWindowState.Minimized; WaitForAction();
        RequireLayout(WindowState==FormWindowState.Minimized,"Completion does not undo minimization");
        WindowState=FormWindowState.Normal;
        ObserveCloseInput(0x00a1,new IntPtr(20),IntPtr.Zero);
        var userClose=Message.Create(Handle,0x0112,new IntPtr(0xf060),IntPtr.Zero); WndProc(ref userClose);
        RequireLayout(!Visible && !NativeTopMost(Handle),"Explicit title-bar close hides to tray and restores original normal layer"); Show();
        TopMost=true; Act(()=>{ Thread.Sleep(30); return "test"; },true); WaitForAction(); userHidPanel=true; Hide();
        RequireLayout(!Visible && NativeTopMost(Handle),"An originally topmost panel keeps its original layer after hiding"); TopMost=false; Show();
        var first=new MonitorInfo { Key="test-A",Name="Mi Monitor",Gdi=@"\\.\DISPLAY5",White=2500 };
        BindMonitors(new List<MonitorInfo>{first},first.Key);
        int selectionChanges=0; displays.SelectedIndexChanged+=delegate { selectionChanges++; };
        BindMonitors(new List<MonitorInfo>{new MonitorInfo { Key=first.Key,Name=first.Name,Gdi=first.Gdi,White=3050,Enabled=true,Active=true,Target=42 }},first.Key);
        RequireLayout(Object.ReferenceEquals(Selected,first) && first.White==3050 && first.Enabled && first.Target==42 && selectionChanges==0,"Status refresh retains selection and updates actual monitor state");
        BindMonitors(new List<MonitorInfo>{new MonitorInfo { Key="test-B",Name="Other monitor",Gdi=@"\\.\DISPLAY1" }},first.Key);
        RequireLayout(Selected==null && displays.Items.Count==1,"Removed monitor clears selection without choosing a different display");
        File.WriteAllText(path,"PASS: first tray click hides both ordinary and RTX-protected unfocused panels with no activation/jump, next click reopens, pinned native Z-order above another active window during and after RTX, no focus stealing or visibility/bounds/handle changes, explicit hide restores original normal/topmost layer, hidden operations stay unpinned, native close/hide and external SC_CLOSE guards, own HWND rejected, selection retained. No driver settings changed.");
        exiting=true; Close();
    }
    void WaitForAction(int timeout=5000) {
        var watch=Stopwatch.StartNew();
        while(busy && watch.ElapsedMilliseconds<timeout) { Application.DoEvents(); Thread.Sleep(5); }
        RequireLayout(!busy,"Operation completion");
        if(testOperationError!=null)throw new Exception("Video operation failed",testOperationError);
    }
    public void ProductionVideoTest(string path) {
        testNoDialogs=true; timer.Stop();
        var initialWait=Stopwatch.StartNew();
        while((refreshing || snapshot==null || geometryTimer.Enabled) && initialWait.ElapsedMilliseconds<10000) { Application.DoEvents(); Thread.Sleep(10); }
        RequireLayout(snapshot!=null && !refreshing,"Initial real-state refresh");
        RequireLayout(FormBorderStyle==FormBorderStyle.SizableToolWindow && !ShowInTaskbar,"Actual production window style");
        var baseline=PanelBridge.Video();
        var initialWindow=NvidiaWindow.Find(); bool panelWasOpen=initialWindow!=null && initialWindow.Shown;
        RequireLayout(baseline.Super.Enabled.HasValue && baseline.Hdr.Enabled.HasValue,"Readable initial RTX values");
        var bounds=Bounds; var hwnd=Handle; int hidden=0,resized=0,destroyed=0;
        PanelTrace=line=>File.AppendAllText(path+".trace.txt",line+Environment.NewLine);
        VisibleChanged+=delegate { if(!Visible)hidden++; TracePanel("visibility changed"); };
        SizeChanged+=delegate { resized++; TracePanel("size changed"); };
        HandleDestroyed+=delegate { destroyed++; TracePanel("handle destroyed"); };
        Activated+=delegate { TracePanel("activated"); }; Deactivate+=delegate { TracePanel("deactivated"); };
        try {
            if(!panelWasOpen) {
                Act(()=>PanelBridge.TestOpenedWindowClose(prefs.NvidiaPath),true); WaitForAction(45000);
                var settled=Stopwatch.StartNew();
                while((refreshing || settled.ElapsedMilliseconds<750) && settled.ElapsedMilliseconds<10000) { Application.DoEvents(); Thread.Sleep(10); }
                RequireLayout(Visible && IsWindowVisible(hwnd) && NativeTopMost(hwnd) && Bounds==bounds && Handle==hwnd && hidden==0 && resized==0 && destroyed==0,"Explicit native NVIDIA close leaves the control-center window visible and topmost");
            }
            foreach(bool superResolution in new[]{true,false})for(int turn=0;turn<2;turn++) {
                VideoAction(superResolution); WaitForAction(45000);
                var refreshWait=Stopwatch.StartNew();
                while(refreshing && refreshWait.ElapsedMilliseconds<10000) { Application.DoEvents(); Thread.Sleep(10); }
                // Include late native messages after the worker has finished.
                var late=Stopwatch.StartNew();
                while(late.ElapsedMilliseconds<750) { Application.DoEvents(); Thread.Sleep(10); }
                RequireLayout(!refreshing && Visible && IsWindowVisible(hwnd) && NativeTopMost(hwnd) && Bounds==bounds && Handle==hwnd && hidden==0 && resized==0 && destroyed==0,"Production video panel stays visible and topmost with stable native bounds and handle");
                RequireLayout(String.IsNullOrEmpty(lastVideo.CleanupNotice),"NVIDIA cleanup completed without a warning or stale-element failure");
                var remaining=NvidiaWindow.Find();
                if(panelWasOpen && initialWindow.Alive())RequireLayout(initialWindow.Shown,"An existing NVIDIA window is not hidden by cleanup");
                else {
                    RequireLayout(remaining==null || !remaining.Shown,"Automatically opened NVIDIA window closed");
                    if(panelWasOpen)File.AppendAllText(path+".trace.txt","Original NVIDIA window closed by driver Apply; no forced reopen.\r\n");
                }
                var confirmed=superResolution?lastVideo.Super:lastVideo.Hdr;
                RequireLayout(confirmed.Enabled.HasValue && !confirmed.Requested.HasValue,"Real RTX operation confirmed, not merely submitted");
            }
            var final=PanelBridge.Video();
            RequireLayout(final.Super.Enabled==baseline.Super.Enabled && final.Hdr.Enabled==baseline.Hdr.Enabled,"Real RTX switches restored");
            File.WriteAllText(path,"PASS: four confirmed real RTX operations using production tool-window style"+(panelWasOpen?" from an existing NVIDIA window":" plus explicit NVIDIA close, all operations starting with NVIDIA closed")+". Panel remains natively topmost after every operation, no visibility/bounds/handle changes or cleanup warnings. An existing NVIDIA window is left open while it survives Apply; driver-initiated closure does not force a reopen. Both RTX switches restored.");
        } finally {
            // Restore the baseline even if a window assertion fails mid-pair.
            var current=PanelBridge.Video();
            foreach(bool superResolution in new[]{true,false}) {
                var value=superResolution?current.Super:current.Hdr;
                var original=superResolution?baseline.Super:baseline.Hdr;
                if(value.Enabled!=original.Enabled) {
                    var restore=Worker.Run(()=>PanelBridge.VideoTransaction(prefs.NvidiaPath,superResolution,original.Enabled));
                    while(!restore.IsCompleted) { Application.DoEvents(); Thread.Sleep(10); }
                    var restored=restore.GetAwaiter().GetResult();
                    RequireLayout((superResolution?restored.Super:restored.Hdr).Enabled==original.Enabled,"Failure cleanup restores RTX baseline");
                }
            }
        }
        exiting=true; Close();
    }
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int GetMessageTime();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attr,out int value,int size);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    static bool AboveWindow(IntPtr front,IntPtr back) {
        for(int i=0;i<1000 && back!=IntPtr.Zero;i++) { back=GetWindow(back,3); if(back==front)return true; }
        return false;
    }
    [StructLayout(LayoutKind.Sequential)] struct PanelWindowPosition { public IntPtr Hwnd,After; public int X,Y,Width,Height; public uint Flags; }
    void AssertLayout() {
        var leaves=new List<Control>(); GatherLeaves(layout,leaves);
        for(int i=0;i<leaves.Count;i++)for(int j=i+1;j<leaves.Count;j++) {
            var a=leaves[i].RectangleToScreen(leaves[i].ClientRectangle); var b=leaves[j].RectangleToScreen(leaves[j].ClientRectangle);
            if(a.Width>0 && a.Height>0 && b.Width>0 && b.Height>0 && a.IntersectsWith(b))
                throw new Exception("Layout overlap: "+leaves[i].Text+" / "+leaves[j].Text);
        }
        // Verify that a stale display refresh cannot pull back a drag in progress.
        var down=typeof(BrightnessSlider).GetMethod("OnMouseDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        var up=typeof(BrightnessSlider).GetMethod("OnMouseUp",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        var args=new MouseEventArgs(MouseButtons.Left,1,slider.Width*3/4,slider.Height/2,0);
        down.Invoke(slider,new object[]{args}); int requested=slider.Value; Render();
        if(slider.Value!=requested || !slider.Enabled)throw new Exception("Refresh interfered with active brightness drag");
        up.Invoke(slider,new object[]{args}); brightnessTouched=DateTime.MinValue; Render();
    }
    static void GatherLeaves(Control parent,List<Control> result) {
        foreach(Control child in parent.Controls) {
            if(!child.Visible)continue;
            if(child is Label || child is ModernButton || child is ComboBox || child is ModernCheckBox || child is BrightnessSlider)result.Add(child);
            else GatherLeaves(child,result);
        }
    }
}
public static class Program {
    [STAThread] public static void Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try {
            if(args.Length>1 && args[0]=="--brightness-live-test") { BrightnessLiveTests.Run(args[1]); return; }
            if(args.Length>1 && args[0]=="--realtime-popup-test") {
                using(var f=new Center(new Preferences(),true)) {
                    f.Shown+=delegate { f.BeginInvoke(new Action(()=> {
                        try { f.RealtimePopupTest(args[1],args.Contains("--wait-for-focus")); }
                        catch(Exception ex) { File.WriteAllText(args[1]+".failed.txt",ex.ToString()); Environment.ExitCode=1; f.Dispose(); Application.ExitThread(); }
                    })); }; Application.Run(f);
                }
                return;
            }
            if(args.Length>1 && args[0]=="--material-render-test") { MaterialTests.Run(args[1]); return; }
            if(args.Length>0 && args[0]=="--material-visual-test") { MaterialTests.Visual(); return; }
            if(args.Length>0 && args[0]=="--self-test") { SelfTest(args.Length>1?args[1]:"self-test.txt"); return; }
            if(args.Length>0 && args[0]=="--diagnose") { Diagnose(args.Length>1?args[1]:"diagnostics.txt"); return; }
            if(args.Length>1 && args[0]=="--automation-diagnostics") { File.WriteAllLines(args[1],PanelBridge.Diagnostics()); return; }
            if(args.Length>0 && new[]{"--visual-ui-dark","--visual-ui-light"}.Contains(args[0])) { using(var f=new Center(new Preferences(),true)) { f.Preview(null,args[0]=="--visual-ui-dark"); Application.Run(f); } return; }
            if(args.Length>1 && args[0]=="--export-icons") { IconArtwork.Export(args[1]); return; }
            if(args.Length>1 && new[]{"--preview","--preview-dark","--preview-light"}.Contains(args[0])) { using(var f=new Center(new Preferences(),true)) f.Preview(args[1],args[0]=="--preview"?(bool?)null:args[0]=="--preview-dark"); return; }
            if(args.Length>1 && args[0]=="--layout-test") { using(var f=new Center(new Preferences(),true)) f.LayoutTest(args[1]); return; }
            if(args.Length>1 && args[0]=="--brightness-ui-test") { using(var f=new Center(new Preferences(),true)) f.BrightnessUiTest(args[1]); return; }
            if(args.Length>1 && args[0]=="--popup-opening-test") { using(var f=new Center(new Preferences(),true)) f.PopupOpeningTest(args[1]); return; }
            if(args.Length>1 && args[0]=="--tray-interaction-test") { using(var f=new Center(new Preferences(),true)) f.TrayInteractionTest(args[1]); return; }
            if(args.Length>1 && args[0]=="--video-navigation-test") { VideoNavigationTests.Run(args[1]); return; }
            if(args.Length>1 && args[0]=="--animation-ui-test") {
                using(var f=new Center(new Preferences(),true)) {
                    f.Shown+=delegate { f.BeginInvoke(new Action(()=> {
                        try { f.AnimationUiTest(args[1]); }
                        catch(Exception ex) { File.WriteAllText(args[1]+".failed.txt",ex.ToString()); Environment.ExitCode=1; f.Dispose(); Application.ExitThread(); }
                    })); };
                    Application.Run(f);
                }
                return;
            }
            if(args.Length>1 && args[0]=="--panel-lifecycle-test") {
                using(var f=new Center(new Preferences(),true)) {
                    // Exercise async UI continuations inside the same message
                    // loop as the application, including orderly loop shutdown.
                    f.Shown+=delegate { f.BeginInvoke(new Action(()=> {
                        try { f.PanelLifecycleTest(args[1]); }
                        catch(Exception ex) { File.WriteAllText(args[1]+".failed.txt",ex.ToString()); Environment.ExitCode=1; f.Dispose(); Application.ExitThread(); }
                    })); };
                    Application.Run(f);
                }
                return;
            }
            if(args.Length>1 && args[0]=="--production-video-test") {
                var settings=Preferences.Load(); settings.AlwaysDisableDolby=false;
                using(var f=new Center(settings,false)) {
                    f.Shown+=delegate { f.BeginInvoke(new Action(()=> {
                        try { f.ProductionVideoTest(args[1]); }
                        catch(Exception ex) { File.WriteAllText(args[1]+".failed.txt",ex.ToString()); Environment.ExitCode=1; f.Dispose(); Application.ExitThread(); }
                    })); };
                    Application.Run(f);
                }
                return;
            }
            bool created;
            using(var mutex=new Mutex(true,args.Contains("--test-ui")?@"Local\HdrControlCenter.TestInstance":@"Local\HdrControlCenter.Singleton",out created)) {
                if(!created) { MessageBox.Show("HDR 控制中心已在运行，请单击右下角托盘显示器图标。"); return; }
                var settings=Preferences.Load();
                if(args.Contains("--test-ui"))settings.AlwaysDisableDolby=false;
                using(var form=new Center(settings,false)) {
                    // A hidden tray-only startup still needs an HWND for power/display events.
                    var handle=form.Handle;
                    if(args.Contains("--test-ui")) { form.FormBorderStyle=FormBorderStyle.Sizable; form.ShowInTaskbar=true; }
                    int trace=Array.IndexOf(args,"--brightness-trace");
                    if(args.Contains("--test-ui") && trace>=0 && trace+1<args.Length) { string path=args[trace+1]; form.BrightnessTrace=line=>File.AppendAllText(path,line+Environment.NewLine); }
                    int panelTrace=Array.IndexOf(args,"--panel-trace");
                    if(args.Contains("--test-ui") && panelTrace>=0 && panelTrace+1<args.Length) {
                        string path=args[panelTrace+1]; form.PanelTrace=line=>File.AppendAllText(path,line+Environment.NewLine);
                        form.VisibleChanged+=delegate { form.TracePanel("visibility changed"); };
                        form.SizeChanged+=delegate { form.TracePanel("size changed"); };
                        form.HandleCreated+=delegate { form.TracePanel("handle created"); };
                        form.HandleDestroyed+=delegate { form.TracePanel("handle destroyed"); };
                        form.Activated+=delegate { form.TracePanel("activated"); };
                        form.Deactivate+=delegate { form.TracePanel("deactivated"); };
                    }
                    if(args.Contains("--tray")) { var context=new ApplicationContext(form); Application.Run(context); }
                    else { form.Popup(); Application.Run(form); }
                }
            }
        } catch(Exception ex) {
            if(args.Length>1 && new[]{"--brightness-live-test","--material-render-test","--self-test","--preview","--preview-dark","--preview-light","--layout-test","--brightness-ui-test","--popup-opening-test","--tray-interaction-test","--video-navigation-test","--animation-ui-test","--panel-lifecycle-test","--production-video-test"}.Contains(args[0])) { File.WriteAllText(args[1]+".failed.txt",ex.ToString()); Environment.ExitCode=1; }
            else MessageBox.Show(ex.ToString(),"HDR 控制中心启动失败");
        }
    }
    static void Require(bool condition,string name) { if(!condition) throw new Exception("FAILED: "+name); }
    static void SelfTest(string path) {
        TrayInputGate.RunTests();
        VideoNavigationTests.PolicyTests();
        AnimationTests.PolicyTests();
        Require(Marshal.SizeOf(typeof(Native.Header))==20,"header ABI");
        Require(Marshal.SizeOf(typeof(Native.Path))==72,"path ABI");
        Require(Marshal.SizeOf(typeof(Native.Mode))==64,"mode ABI");
        Require(Marshal.SizeOf(typeof(Native.TargetName))==420,"target name ABI");
        Require(Marshal.SizeOf(typeof(Native.SourceName))==84,"source name ABI");
        Require(Marshal.SizeOf(typeof(Native.Color2))==36,"color2 ABI");
        Require(Marshal.SizeOf(typeof(Native.SetWhite))==28,"white ABI");
        for(int p=0;p<=100;p++) Require(DisplayService.Percent(DisplayService.Level(p))==p,"brightness round trip");
        Require(DisplayService.Level(-1)==1000 && DisplayService.Level(101)==6000,"brightness bounds");
        Require(PanelBridge.Activity("Status: Inactive")=="未生效","inactive distinction");
        Require(PanelBridge.Activity("Status: Active")=="正在生效","active distinction");
        Require(PanelBridge.Activity("unknown")=="未知","unknown status");
        Require(PanelBridge.Activity("未激活")=="未生效","Chinese inactive");
        Require(PanelBridge.Activity("状态：非活动")=="未生效","Chinese negative activity");
        Require(PanelBridge.IsApplyName("应用(A)") && PanelBridge.IsApplyName("应用(&A)") && PanelBridge.IsApplyName("Apply") && PanelBridge.IsApplyName("套用（A）"),"NVIDIA Apply labels");
        Require(!PanelBridge.IsApplyName("取消"),"Do not confuse Apply and Cancel");
        var p0=new Preferences { MonitorKey="\\\\?\\DISPLAY#测试",NvidiaPath="C:\\Program Files\\nvcplui.exe" };
        var p1=new JavaScriptSerializer().Deserialize<Preferences>(new JavaScriptSerializer().Serialize(p0));
        Require(p0.MonitorKey==p1.MonitorKey && p0.NvidiaPath==p1.NvidiaPath,"settings serialization");
        RecoveryTests.Run();
        VideoWindowTests.Run();
        RenderingTests.Run();
        var beforeVideo=new VideoState { Super=new Feature { Enabled=true,CanControl=true },Hdr=new Feature { Enabled=false,CanControl=true } };
        var closedVideo=VideoApplyPolicy.Unverified(beforeVideo,true,false);
        Require(!closedVideo.Super.Enabled.HasValue && closedVideo.Super.Requested==false,"Closed panel cannot falsely confirm applied setting");
        Require(closedVideo.Hdr.Enabled==false && closedVideo.Hdr.Cached && beforeVideo.Super.Enabled==true,"Preserve unrelated setting and prior snapshot");
        Require(closedVideo.Super.ToString().Contains("待确认"),"Unconfirmed result visibly labelled");
        var closedHdr=VideoApplyPolicy.Unverified(beforeVideo,false,true);
        Require(closedHdr.Hdr.Requested==true && !closedHdr.Hdr.Enabled.HasValue && closedHdr.Super.Enabled==true,"HDR pending result keeps VSR setting");
        Require(NvidiaSavedSettings.Decode(true,0,0x80000001)==false && NvidiaSavedSettings.Decode(true,1,0x80000001)==true,"VSR saved-state mapping");
        for(uint quality=1;quality<=5;quality++)Require(NvidiaSavedSettings.Decode(true,quality,0x80000001)==true,"VSR manual and Auto quality enabled");
        Require(NvidiaSavedSettings.Decode(false,0,0x80000001)==false && NvidiaSavedSettings.Decode(false,5,0x80000001)==true,"HDR known modes");
        Require(!NvidiaSavedSettings.Decode(true,6,0x80000001).HasValue && !NvidiaSavedSettings.Decode(false,4,0x80000001).HasValue && !NvidiaSavedSettings.Decode(true,1,0).HasValue,"Unknown driver schemas never guessed");
        var savedVideo=new VideoState { Super=new Feature { Enabled=false,Activity="未检测" },Hdr=new Feature { Enabled=true,Activity="未检测" } };
        var merged=NvidiaSavedSettings.Merge(new VideoState(),savedVideo);
        Require(merged.Super.Enabled==false && merged.Hdr.Enabled==true && merged.Super.Activity=="未检测","Closed panel reads saved state without fake activity");
        var liveVideo=new VideoState { Super=new Feature { Enabled=true,Activity="正在生效" },Hdr=new Feature() };
        merged=NvidiaSavedSettings.Merge(liveVideo,savedVideo);
        Require(merged.Super.Enabled==true && merged.Super.Activity=="正在生效" && merged.Hdr.Enabled==true,"Live playback state retained, per-feature fallback");
        Require(SettingsWait.MonitorMatches("显示器 3: Mi Monitor","Mi Monitor") && SettingsWait.MonitorMatches("Display 3： Mi Monitor ","mi monitor"),"Localized exact monitor selection");
        Require(!SettingsWait.MonitorMatches("显示器 3: Mi Monitor Pro","Mi Monitor") && !SettingsWait.MonitorMatches("Mi Monitor Pro","Mi Monitor"),"Similar model names cannot select wrong display");
        int reads=0,pauses=0;
        bool ready=SettingsWait.Run(()=> { reads++; if(reads==1)throw new System.Windows.Automation.ElementNotAvailableException(); return reads>=3; },value=>value,4,()=>pauses++,"timeout");
        Require(ready && reads==3 && pauses==2,"Stale control and delayed page wait use fresh reads");
        reads=0; pauses=0; bool timedOut=false;
        try { SettingsWait.Run(()=> { reads++; return false; },value=>value,3,()=>pauses++,"timeout"); } catch(Exception ex) { timedOut=ex.Message=="timeout"; }
        Require(timedOut && reads==3 && pauses==2,"Page wait is bounded");
        reads=0; bool refused=false;
        try { SettingsWait.Run<bool>(()=> { reads++; throw new InvalidOperationException("ambiguous target"); },value=>value,3,()=>{},"timeout"); } catch(InvalidOperationException) { refused=true; }
        Require(refused && reads==1,"Permanent target errors are not retried");
        File.WriteAllText(path,"PASS: native ABI, brightness mapping, Dolby recovery, DPI geometry, custom control painting, keyboard/accessibility input and focus, pending state, NVIDIA saved-state decoding, exact monitor matching, stale UI controls, bounded Settings waits, stale-control closure race, already closed/rejected window, pending-change preservation, bounded close timeout, tray input coalescing and video-page activation/readiness policy.\r\nNo display settings or startup entries changed.\r\n");
    }
    static void Diagnose(string path) {
        var lines=new List<string>();
        try { foreach(var m in DisplayService.List()) lines.Add(m+" | HDR supported="+m.Supported+" enabled="+m.Enabled+" active="+m.Active+" modern="+m.Modern+" white="+m.White+" error="+m.Error); }
        catch(Exception ex) { lines.Add("Display query: "+ex.Message); }
        try { var v=PanelBridge.Video(); lines.Add("RTX VSR: "+v.Super+" | "+v.Super.Detail); lines.Add("RTX HDR: "+v.Hdr+" | "+v.Hdr.Detail); }
        catch(Exception ex) { lines.Add("NVIDIA query: "+ex.Message); }
        try { var d=PanelBridge.Dolby(Preferences.Load().MonitorKey,false); lines.Add("Dolby Vision: "+d+" | "+d.Detail); }
        catch(Exception ex) { lines.Add("Dolby query: "+ex.Message); }
        File.WriteAllLines(path,lines);
    }
}
}
