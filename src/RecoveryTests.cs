using System;
using System.Drawing;

namespace HdrCenter {
public static class RecoveryTests {
    static void Check(bool value,string name) { if(!value)throw new Exception("Recovery test failed: "+name); }
    public static void Run() {
        var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
        Check(!serializer.Deserialize<Preferences>("{\"MonitorKey\":\"old\"}").AlwaysDisableDolby,"Old configuration defaults OFF");
        Check(serializer.Deserialize<Preferences>(serializer.Serialize(new Preferences { AlwaysDisableDolby=true })).AlwaysDisableDolby,"Guard preference persisted");
        var calls=new System.Collections.Generic.List<bool>();
        DolbyReset.Execute(v=>calls.Add(v),()=>{});
        Check(calls.Count==2 && calls[0] && !calls[1],"Reported OFF still receives ON then OFF");
        calls.Clear(); bool failed=false;
        try { DolbyReset.Execute(v=>{ calls.Add(v); if(v)throw new Exception("Transient verification failure"); },()=>{}); } catch { failed=true; }
        Check(failed && calls.Count==2 && !calls[1],"OFF cleanup after ON failure");
        var start=new DateTime(2026,1,1); var g=new DolbyGuard();
        g.Configure(true,"A",start); g.Observe("A/1",true,start);
        Check(!g.TryStart(true,start.AddSeconds(1)) && g.TryStart(true,start.AddSeconds(3)),"Settle before startup reset");
        g.Request("Own display event",start.AddSeconds(4),false); g.Observe("A/2",true,start.AddSeconds(4));
        Check(!g.Pending,"Own mode changes cannot queue an endless loop");
        g.Finish(true,start.AddSeconds(5)); g.Request("Late own event",start.AddSeconds(6),false);
        Check(!g.Pending,"Suppress late mode notification");
        g.Observe(null,false,start.AddSeconds(7)); g.Observe("A/2",true,start.AddSeconds(8));
        Check(g.Pending && !g.TryStart(false,start.AddSeconds(12)) && g.TryStart(true,start.AddSeconds(12)),"Reconnect survives cooldown, requires selected screen ready");
        g.Finish(false,start.AddSeconds(13)); Check(g.Pending,"Retry transient failure");
        Check(g.TryStart(true,start.AddSeconds(19)),"Retry delay"); g.Finish(false,start.AddSeconds(20));
        Check(g.TryStart(true,start.AddSeconds(31)),"Third attempt"); g.Finish(false,start.AddSeconds(32));
        Check(!g.Pending,"Retries bounded at three");
        g.Power(1,start.AddSeconds(33)); Check(!g.Pending,"Initial power status is not a wake");
        g.Power(0,start.AddSeconds(34)); g.Power(1,start.AddSeconds(35)); g.Request("Resume",start.AddSeconds(36),true);
        Check(!g.TryStart(true,start.AddSeconds(37)) && g.TryStart(true,start.AddSeconds(39)),"Wake notifications coalesced");
        g.Configure(true,"B",start.AddSeconds(40)); g.Finish(true,start.AddSeconds(41));
        Check(g.Pending && g.Key=="B","New target scheduled after old transaction finishes");
        g.Configure(false,"B",start.AddSeconds(42)); Check(!g.Pending && !g.TryStart(true,start.AddSeconds(50)),"Disabling cancels future work");
        var hdrGuard=new DolbyGuard();
        hdrGuard.Configure(true,"A",start); hdrGuard.Observe("A/1",false,start,false);
        Check(!hdrGuard.Pending,"HDR OFF cancels automatic startup repair");
        hdrGuard.Observe("A/1",true,start.AddSeconds(20),true);
        hdrGuard.Request("HDR mode notification",start.AddSeconds(21),false);
        Check(!hdrGuard.Pending && !hdrGuard.TryStart(true,start.AddSeconds(24)),"Ordinary HDR ON does not reset Dolby");
        hdrGuard.Observe("A/1",false,start.AddSeconds(25),false);
        hdrGuard.Observe("A/1",true,start.AddSeconds(26),true);
        Check(!hdrGuard.Pending,"Repeated HDR OFF and ON remain idle");
        hdrGuard.Observe(null,false,start.AddSeconds(27));
        hdrGuard.Observe("A/1",true,start.AddSeconds(28),true);
        Check(hdrGuard.TryStart(true,start.AddSeconds(31)),"Actual reconnect to same target still repairs Dolby");
        hdrGuard.Finish(true,start.AddSeconds(32));
        hdrGuard.Power(0,start.AddSeconds(33)); hdrGuard.Power(1,start.AddSeconds(34));
        hdrGuard.Observe("A/1",true,start.AddSeconds(34),true);
        Check(hdrGuard.TryStart(true,start.AddSeconds(37)),"Display wake still repairs Dolby");
        hdrGuard.Finish(true,start.AddSeconds(38));
        hdrGuard.Request("Reconnect with HDR OFF",start.AddSeconds(39),true);
        hdrGuard.Observe("A/1",false,start.AddSeconds(39),false);
        hdrGuard.Observe("A/1",true,start.AddSeconds(40),true);
        Check(!hdrGuard.Pending,"HDR ON cannot execute an earlier automatic request");
        hdrGuard.Request("Manual repair",start.AddSeconds(41),true,true);
        hdrGuard.Observe("A/1",false,start.AddSeconds(41),false);
        Check(hdrGuard.Pending && !hdrGuard.TryStart(false,start.AddSeconds(44)),"Explicit repair waits for HDR readiness");
        hdrGuard.Observe("A/1",true,start.AddSeconds(45),true);
        Check(hdrGuard.TryStart(true,start.AddSeconds(45)),"Explicit repair survives ordinary HDR ON");
        hdrGuard.Finish(true,start.AddSeconds(46));
        hdrGuard.Request("Wake while state unavailable",start.AddSeconds(47),true);
        hdrGuard.Observe("A/1",false,start.AddSeconds(47),null);
        Check(hdrGuard.Pending,"Unknown HDR state does not cancel physical recovery");
        hdrGuard.Observe("A/1",true,start.AddSeconds(48),true);
        Check(hdrGuard.TryStart(true,start.AddSeconds(50)),"Physical recovery waits for a valid HDR state");
        foreach(int dpi in new[]{96,120,144,192,96,192,96}) {
            var area=new Rectangle(-1920,-100,1920,1080);
            var fit=WindowGeometry.Fit(new Rectangle(-4000,2000,WindowGeometry.Pixels(460,dpi),WindowGeometry.Pixels(760,dpi)),area,WindowGeometry.Pixels(12,dpi));
            Check(area.Contains(fit) && fit.Width==WindowGeometry.Pixels(460,dpi),"Mixed DPI, negative coordinates, oversized height");
        }
        Check(WindowGeometry.Pixels(460,96)==460,"No accumulated scaling");
    }
}
}
