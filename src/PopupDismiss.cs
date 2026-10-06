using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace HdrCenter {
public sealed partial class Center {
    readonly Timer dismissTimer=new Timer { Interval=80 };
    bool autoHideSuppressed;
    IntPtr outsideMouseHook;
    MouseHookCallback outsideMouseCallback;
    void InitializeDismiss() {
        dismissTimer.Tick+=delegate { dismissTimer.Stop(); CheckFocusDismiss(); };
        Deactivate+=delegate {
            // Programmatic Settings/NVIDIA activation is expected during an
            // apply. A real outside click is handled separately, even then.
            if(!autoHideSuppressed && !preparingPopup && Visible && (!busy || openingOperation) && !guard.Running) { dismissTimer.Stop(); dismissTimer.Start(); }
        };
        Activated+=delegate { dismissTimer.Stop(); };
        VisibleChanged+=delegate { if(Visible)StartOutsideClickWatch(); else { dismissTimer.Stop(); StopOutsideClickWatch(); } };
        outsideMouseCallback=OutsideMouse;
    }
    void CheckFocusDismiss() {
        if(autoHideSuppressed || !Visible || preparingPopup || (busy && !openingOperation) || guard.Running)return;
        if(slider.IsDragging || displays.DroppedDown || (tray.ContextMenuStrip!=null && tray.ContextMenuStrip.Visible))return;
        var active=GetForegroundWindow();
        if(active==IntPtr.Zero || PanelWindowFamily(active) || IsShellTray(active))return;
        DismissPopup();
    }
    void DismissPopup() {
        if(autoHideSuppressed || !Visible || preparingPopup || Disposing || IsDisposed)return;
        dismissTimer.Stop(); userHidPanel=true; keepVideoPanelVisible=false;
        TracePanel("popup dismissed outside"); Hide();
    }
    bool PanelWindowFamily(IntPtr window) {
        if(window==IntPtr.Zero || !IsHandleCreated)return false;
        var root=GetAncestor(window,2); if(root==Handle)return true;
        uint process; GetWindowThreadProcessId(root,out process);
        if(process!=GetCurrentProcessId())return false;
        // WinForms can put a hidden parking HWND above a tool window. Walk
        // owners so a dialog owned by this panel is recognised on that chain.
        for(int i=0;i<32 && root!=IntPtr.Zero;i++) { if(root==Handle)return true; root=GetWindow(root,4); }
        return false;
    }
    static bool IsShellTray(IntPtr window) {
        var name=new StringBuilder(128); GetClassName(GetAncestor(window,2),name,name.Capacity);
        string value=name.ToString();
        return value=="Shell_TrayWnd" || value=="Shell_SecondaryTrayWnd" || value=="NotifyIconOverflowWindow";
    }
    void StartOutsideClickWatch() {
        if(outsideMouseHook!=IntPtr.Zero || outsideMouseCallback==null)return;
        // Observe only button-down events; never block, move or inject input.
        // Install while the popup is visible and release when it is hidden.
        outsideMouseHook=SetWindowsHookEx(14,outsideMouseCallback,GetModuleHandle(null),0);
    }
    void StopOutsideClickWatch() { if(outsideMouseHook!=IntPtr.Zero) { UnhookWindowsHookEx(outsideMouseHook); outsideMouseHook=IntPtr.Zero; } }
    IntPtr OutsideMouse(int code,IntPtr message,IntPtr data) {
        try {
        if(code>=0 && data!=IntPtr.Zero && !IsDisposed && !Disposing && !autoHideSuppressed && Visible && !preparingPopup && !slider.IsDragging) {
            long kind=message.ToInt64();
            if(kind==0x0201 || kind==0x0204 || kind==0x0207 || kind==0x020b) {
                var input=(MouseHookData)Marshal.PtrToStructure(data,typeof(MouseHookData));
                ObserveOutsideClick(input.Position);
            }
        }
        } catch(Exception) { /* Input must always continue if the popup vanishes. */ }
        return CallNextHookEx(IntPtr.Zero,code,message,data);
    }
    void ObserveOutsideClick(System.Drawing.Point point) {
        if(autoHideSuppressed || !Visible || preparingPopup || slider.IsDragging)return;
        if(tray.ContextMenuStrip!=null && tray.ContextMenuStrip.Visible && tray.ContextMenuStrip.Bounds.Contains(point))return;
        var target=WindowFromPoint(point);
        if(target!=IntPtr.Zero && !PanelWindowFamily(target) && !IsShellTray(target)) {
            try { BeginInvoke(new Action(DismissPopup)); } catch(InvalidOperationException) { }
        }
    }
    void DisposeDismiss() { dismissTimer.Dispose(); StopOutsideClickWatch(); }
    delegate IntPtr MouseHookCallback(int code,IntPtr message,IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct MouseHookData { public System.Drawing.Point Position; public uint MouseData,Flags,Time; public IntPtr Extra; }
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint kind);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int length);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string module);
    [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SetWindowsHookEx(int kind,MouseHookCallback callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
}
}
