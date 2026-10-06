using System;

namespace HdrCenter {
// Message timestamps preserve input order even if first-frame rendering takes
// long enough for more tray clicks to accumulate in the UI message queue.
public sealed class TrayInputGate {
    readonly uint interval;
    bool hasInput,hasOpening;
    int previousInput,openingStarted,openingCompleted;
    public TrayInputGate(int doubleClickTime) { interval=(uint)Math.Max(1,doubleClickTime); }
    public bool TryAccept(int messageTime) {
        bool repeated=hasInput && unchecked((uint)(messageTime-previousInput))<=interval;
        bool queuedDuringOpening=hasOpening && unchecked((uint)(messageTime-openingStarted))<=unchecked((uint)(openingCompleted-openingStarted));
        if(hasOpening && !queuedDuringOpening)hasOpening=false;
        previousInput=messageTime; hasInput=true;
        return !repeated && !queuedDuringOpening;
    }
    public void CompleteOpening(int started,int completed) {
        openingStarted=started;
        // Reject an invalid reversed interval instead of suppressing input for
        // half a tick-count cycle. A valid UI operation cannot last 24 days.
        openingCompleted=unchecked((uint)(completed-started))>Int32.MaxValue?started:completed;
        hasOpening=true;
    }
    public static void RunTests() {
        var gate=new TrayInputGate(500);
        Require(gate.TryAccept(1000) && !gate.TryAccept(1100) && !gate.TryAccept(1300) && gate.TryAccept(2000),"Burst coalescing preserves separate clicks");
        gate=new TrayInputGate(500);
        Require(gate.TryAccept(1000),"First click accepted"); gate.CompleteOpening(1000,2700);
        Require(!gate.TryAccept(2100) && gate.TryAccept(2900),"Clicks queued during slow opening cannot undo it");
        gate=new TrayInputGate(500);
        Require(gate.TryAccept(Int32.MaxValue-100) && !gate.TryAccept(Int32.MinValue+25) && gate.TryAccept(Int32.MinValue+1000),"Message timestamp wrap preserves interval");
        gate=new TrayInputGate(500); Require(gate.TryAccept(Int32.MaxValue-1000),"Wrap opening accepted"); gate.CompleteOpening(Int32.MaxValue-1000,Int32.MinValue+200);
        Require(!gate.TryAccept(Int32.MinValue+100) && gate.TryAccept(Int32.MinValue+1000),"Opening completion barrier survives timestamp wrap");
        gate=new TrayInputGate(500); Require(gate.TryAccept(1000),"Long-lived instance starts"); gate.CompleteOpening(1000,1300);
        Require(gate.TryAccept(unchecked(1300+Int32.MaxValue)),"An old completion barrier does not discard a click after weeks of idle time");
    }
    static void Require(bool ok,string message) { if(!ok)throw new Exception("Tray input test failed: "+message); }
}
}
