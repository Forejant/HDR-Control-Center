using System;
using System.Windows.Automation;
namespace HdrCenter {
public static class VideoWindowTests {
    static void Check(bool ok,string name) { if(!ok)throw new Exception("Video cleanup: "+name); }
    public static void Run() {
        int posts=0,reads=0; bool alive=true;
        var result=VideoWindowClosePolicy.Run(()=>alive,()=> { reads++; if(reads==1)throw new ElementNotAvailableException(); return false; },()=> { posts++; alive=false; return true; },()=>{},3,3);
        Check(result==VideoWindowCloseResult.Closed && reads==2 && posts==1,"Transient stale Apply control retries fresh and closes exactly once");
        posts=0; reads=0; alive=true;
        result=VideoWindowClosePolicy.Run(()=>alive,()=> { reads++; alive=false; throw new ElementNotAvailableException(); },()=> { posts++; return true; },()=>{},3,3);
        Check(result==VideoWindowCloseResult.Closed && reads==1 && posts==0,"Driver destroys HWND during stale-control read; already closed is successful");
        result=VideoWindowClosePolicy.Run(()=>false,()=> { throw new Exception("Should not read destroyed/reused HWND"); },()=> { posts++; return true; },()=>{},3,3);
        Check(result==VideoWindowCloseResult.Closed && posts==0,"Missing or rejected window identity never receives close");
        result=VideoWindowClosePolicy.Run(()=>true,()=>true,()=> { posts++; return true; },()=>{},3,3);
        Check(result==VideoWindowCloseResult.PendingChanges && posts==0,"Unrelated pending changes retained");
        result=VideoWindowClosePolicy.Run(()=>true,()=> { throw new ElementNotAvailableException(); },()=> { posts++; return true; },()=>{},3,3);
        Check(result==VideoWindowCloseResult.Unavailable && posts==0,"Persistent UI outage leaves window open without discarding successful setting");
        result=VideoWindowClosePolicy.Run(()=>true,()=>false,()=> { posts++; return true; },()=>{},3,3);
        Check(result==VideoWindowCloseResult.TimedOut && posts==1,"Close is bounded, timeout reported, no repeated close requests");
        result=VideoWindowClosePolicy.Run(()=>true,()=>false,()=>false,()=>{},3,3);
        Check(result==VideoWindowCloseResult.RequestFailed && VideoWindowClosePolicy.Notice(result).Contains("设置已应用"),"Closure failure does not erase confirmed setting");
        Check(VideoWindowClosePolicy.Notice(VideoWindowCloseResult.Closed)=="","No false cleanup warning on success");
    }
}
}
