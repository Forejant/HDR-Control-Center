using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace HdrCenter {
public enum VideoPageActivation { Missing, Invoked, Entered, DoubleClicked }
public static class VideoPageNavigation {
    // Read the actual page, not the selection highlight. Invoke or Enter once;
    // allow one native double-click fallback only if the page has not loaded.
    public static void Wait(Func<bool> ready,Func<bool,VideoPageActivation> activate,Action pause,int polls) {
        int stage=0,fallbackAt=0,failures=0; string last="";
        for(int i=0;i<polls;i++) {
            bool activating=false;
            try {
                if(ready())return;
                if(stage==0 || (stage==1 && i>=fallbackAt)) {
                    activating=true; var action=activate(stage==0); activating=false;
                    if(action==VideoPageActivation.Invoked || action==VideoPageActivation.Entered) { stage=1; fallbackAt=i+10; }
                    else if(action==VideoPageActivation.DoubleClicked)stage=2;
                    if(ready())return;
                }
            } catch(ElementNotAvailableException ex) { last=ex.Message; if(activating && ++failures>=3)stage=2; }
            catch(InvalidOperationException ex) { last=ex.Message; if(activating && ++failures>=3)stage=2; }
            if(i+1<polls)pause();
        }
        throw new Exception("NVIDIA 视频页未准备好；导航选中不代表页面已打开。"+last);
    }
}
public static class NativeTreeActivation {
    static IntPtr Select(AutomationElement item,IntPtr root,uint processId,Func<bool> alive) {
        if(!alive() || item.Current.ProcessId!=(int)processId)throw new InvalidOperationException("NVIDIA 导航窗口身份已变化。");
        var ancestor=item; IntPtr tree=IntPtr.Zero;
        for(int i=0;i<12 && ancestor!=null;i++) {
            IntPtr handle=new IntPtr(ancestor.Current.NativeWindowHandle);
            if(handle!=IntPtr.Zero && TreeClass(handle)) { tree=handle; break; }
            ancestor=TreeWalker.ControlViewWalker.GetParent(ancestor);
        }
        if(!Valid(tree,root,processId,alive))throw new InvalidOperationException("当前导航控件不支持定向激活。");
        object pattern;
        if(item.TryGetCurrentPattern(ScrollItemPattern.Pattern,out pattern))((ScrollItemPattern)pattern).ScrollIntoView();
        if(!item.TryGetCurrentPattern(SelectionItemPattern.Pattern,out pattern))throw new InvalidOperationException("NVIDIA 导航项不支持选择。");
        var selection=(SelectionItemPattern)pattern; selection.Select();
        if(!Valid(tree,root,processId,alive) || !selection.Current.IsSelected)throw new InvalidOperationException("NVIDIA 视频导航项未选中。");
        return tree;
    }
    public static void Enter(AutomationElement item,IntPtr root,uint processId,Func<bool> alive) {
        var tree=Select(item,root,processId,alive);
        // TVN_KEYDOWN activation uses the selected item, unlike NVIDIA's
        // double-click handler which also consults the global pointer position.
        // Messages go only to the validated native tree; no global key injection.
        try { Send(tree,root,processId,alive,0x0100,new IntPtr(13),new IntPtr(0x001c0001)); }
        finally { if(Valid(tree,root,processId,alive))Send(tree,root,processId,alive,0x0101,new IntPtr(13),new IntPtr(unchecked((int)0xc01c0001))); }
    }
    public static void DoubleClick(AutomationElement item,IntPtr root,uint processId,Func<bool> alive) {
        var tree=Select(item,root,processId,alive);
        var bounds=item.Current.BoundingRectangle;
        Rect client; Point origin=new Point();
        if(bounds.IsEmpty || item.Current.IsOffscreen || !GetClientRect(tree,out client) || !ClientToScreen(tree,ref origin))throw new InvalidOperationException("NVIDIA 视频导航项当前不可见。");
        double left=Math.Max(bounds.Left,origin.X),top=Math.Max(bounds.Top,origin.Y);
        double right=Math.Min(bounds.Right,origin.X+client.Right),bottom=Math.Min(bounds.Bottom,origin.Y+client.Bottom);
        if(right-left<2 || bottom-top<2)throw new InvalidOperationException("NVIDIA 视频导航项不在导航窗口内。");
        var point=new Point { X=(int)Math.Floor((left+right)/2),Y=(int)Math.Floor((top+bottom)/2) };
        if(!ScreenToClient(tree,ref point) || point.X<0 || point.Y<0 || point.X>=client.Right || point.Y>=client.Bottom || point.X>32767 || point.Y>32767)throw new InvalidOperationException("NVIDIA 导航坐标无效。");
        var coordinates=new IntPtr((point.Y<<16)|(point.X & 0xffff));
        // Scalar window messages target only the captured NVIDIA tree HWND.
        // They neither move the user's pointer nor send input to another app.
        try {
            // Selection is already confirmed by UIA. Sending a separate first
            // button-down synchronously can enter native drag tracking and wait
            // for an up message that the caller has not yet been able to send.
            Send(tree,root,processId,alive,0x0203,new IntPtr(1),coordinates);
        } finally {
            if(Valid(tree,root,processId,alive))Send(tree,root,processId,alive,0x0202,IntPtr.Zero,coordinates);
        }
    }
    static void Send(IntPtr tree,IntPtr root,uint pid,Func<bool> alive,uint message,IntPtr wparam,IntPtr lparam) {
        if(!Valid(tree,root,pid,alive))throw new InvalidOperationException("NVIDIA 导航窗口已关闭或改变。");
        IntPtr result;
        if(SendMessageTimeout(tree,message,wparam,lparam,0x0002,500,out result)==IntPtr.Zero)throw new InvalidOperationException("NVIDIA 导航激活请求未完成（消息 "+message.ToString("X")+"，错误 "+Marshal.GetLastWin32Error()+"）。");
    }
    static bool Valid(IntPtr tree,IntPtr root,uint pid,Func<bool> alive) {
        uint actual;
        return tree!=IntPtr.Zero && root!=IntPtr.Zero && alive() && IsWindow(tree) && IsChild(root,tree) && GetAncestor(tree,2)==root && GetWindowThreadProcessId(tree,out actual)!=0 && actual==pid && TreeClass(tree);
    }
    static bool TreeClass(IntPtr hwnd) {
        var name=new StringBuilder(256); GetClassName(hwnd,name,name.Capacity);
        string value=name.ToString(); return value.Equals("SysTreeView32",StringComparison.OrdinalIgnoreCase) || value.StartsWith("WindowsForms10.SysTreeView32.",StringComparison.OrdinalIgnoreCase);
    }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsChild(IntPtr parent,IntPtr child);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder value,int size);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd,ref Point point);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd,ref Point point);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam,uint flags,uint timeout,out IntPtr result);
}
}
