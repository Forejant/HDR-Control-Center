using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace HdrCenter {
public enum VideoWindowCloseResult { Closed, PendingChanges, Unavailable, RequestFailed, TimedOut }
public static class VideoWindowClosePolicy {
    // A stale UI tree is not evidence that settings are pending or the HWND
    // still exists. Query a fresh tree, boundedly, before issuing one close.
    public static VideoWindowCloseResult Run(Func<bool> alive,Func<bool?> pending,Func<bool> close,Action pause,int reads,int waits) {
        bool? last=null; bool ready=false;
        for(int i=0;i<reads;i++) {
            if(!alive())return VideoWindowCloseResult.Closed;
            try { last=pending(); } catch(ElementNotAvailableException) { last=null; }
            if(!alive())return VideoWindowCloseResult.Closed;
            if(last==false) { ready=true; break; }
            if(i+1<reads)pause();
        }
        if(!ready)return last==true?VideoWindowCloseResult.PendingChanges:VideoWindowCloseResult.Unavailable;
        if(!alive())return VideoWindowCloseResult.Closed;
        if(!close())return alive()?VideoWindowCloseResult.RequestFailed:VideoWindowCloseResult.Closed;
        for(int i=0;i<waits;i++) { if(!alive())return VideoWindowCloseResult.Closed; pause(); }
        return alive()?VideoWindowCloseResult.TimedOut:VideoWindowCloseResult.Closed;
    }
    public static string Notice(VideoWindowCloseResult result) {
        switch(result) {
            case VideoWindowCloseResult.Closed:return "";
            case VideoWindowCloseResult.PendingChanges:return "设置已应用；NVIDIA 页面仍有待保存修改，窗口已保留。";
            case VideoWindowCloseResult.Unavailable:return "设置已应用；暂时无法确认窗口可关闭，NVIDIA 页面已保留。";
            case VideoWindowCloseResult.RequestFailed:return "设置已应用；未能发送 NVIDIA 窗口关闭请求，请手动关闭。";
            default:return "设置已应用；NVIDIA 窗口未在等待时间内关闭，请手动关闭。";
        }
    }
}
// Capture the HWND, PID and process lifetime before changing settings.
// Cleanup never locates an arbitrary new UIA root or closes our own window.
public sealed class NvidiaWindow {
    public readonly IntPtr Handle; public readonly uint ProcessId; readonly long start;
    internal NvidiaWindow(IntPtr hwnd,uint pid,long started) { Handle=hwnd; ProcessId=pid; start=started; }
    public static NvidiaWindow Find() {
        var starts=new Dictionary<uint,long>();
        foreach(var process in Process.GetProcessesByName("nvcplui"))using(process) {
            try { starts[(uint)process.Id]=process.StartTime.ToUniversalTime().Ticks; } catch(InvalidOperationException) { } catch(System.ComponentModel.Win32Exception) { }
        }
        if(starts.Count==0)return null;
        var all=new List<NvidiaWindow>();
        EnumWindows((hwnd,data)=> {
            uint pid; GetWindowThreadProcessId(hwnd,out pid);
            if(starts.ContainsKey(pid) && GetWindowTextLength(hwnd)>0)all.Add(new NvidiaWindow(hwnd,pid,starts[pid]));
            return true;
        },IntPtr.Zero);
        // NVIDIA creates titled launcher/helper windows before its settings
        // window. Do not bind the transaction to one of those placeholders.
        var candidates=all.Where(w=>IsPanelWindow(w.Handle)).ToList();
        if(candidates.Count>1)throw new Exception("发现多个 NVIDIA 窗口，无法确定操作目标，请保留一个控制面板窗口。");
        return candidates.FirstOrDefault();
    }
    internal static bool IsPanelWindow(IntPtr hwnd) {
        if(!IsWindow(hwnd) || !IsWindowVisible(hwnd) || GetWindow(hwnd,4)!=IntPtr.Zero)return false;
        bool tree=false;
        EnumChildWindows(hwnd,(child,data)=> {
            var name=new StringBuilder(256); GetClassName(child,name,name.Capacity);
            string value=name.ToString();
            if(value.Equals("SysTreeView32",StringComparison.OrdinalIgnoreCase) || value.StartsWith("WindowsForms10.SysTreeView32.",StringComparison.OrdinalIgnoreCase))tree=true;
            return !tree;
        },IntPtr.Zero);
        return tree;
    }
    public bool Alive() {
        uint pid;
        if(Handle==IntPtr.Zero || !IsWindow(Handle) || GetWindowThreadProcessId(Handle,out pid)==0 || pid!=ProcessId || pid==(uint)Process.GetCurrentProcess().Id)return false;
        try { using(var process=Process.GetProcessById((int)pid))return process.ProcessName.Equals("nvcplui",StringComparison.OrdinalIgnoreCase) && process.StartTime.ToUniversalTime().Ticks==start; }
        catch(ArgumentException) { return false; } catch(InvalidOperationException) { return false; } catch(System.ComponentModel.Win32Exception) { return false; }
    }
    public bool RequestClose() {
        bool valid=Alive(); Write("close target valid="+valid);
        return valid && PostMessage(Handle,0x0010,IntPtr.Zero,IntPtr.Zero);
    }
    public bool Shown { get { return Alive() && IsWindowVisible(Handle); } }
    public void Write(string text) {
        try {
            Directory.CreateDirectory(Preferences.Folder); string path=Path.Combine(Preferences.Folder,"video-window.log");
            string line=DateTime.Now.ToString("O")+" "+text+" hwnd="+Handle.ToInt64()+" pid="+ProcessId+Environment.NewLine;
            if(File.Exists(path) && new FileInfo(path).Length>200000)File.WriteAllText(path,line); else File.AppendAllText(path,line);
        } catch { }
    }
    delegate bool EnumCallback(IntPtr hwnd,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr hwnd,EnumCallback callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder value,int size);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
}
}
