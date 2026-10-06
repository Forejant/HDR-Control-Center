using System;

namespace HdrCenter {
// Time-based presentation, independent of the driver operation. A new confirmed
// value is held during loading and becomes the movement target on completion.
public sealed class SwitchMotion {
    public SwitchMotion() { Position=.5; }
    bool initialized; double from,target,started,spinStarted;
    public bool? Value { get; private set; }
    public bool Loading { get; private set; }
    public bool Moving { get; private set; }
    public double Position { get; private set; }
    public float Angle { get; private set; }
    public bool NeedsFrames { get { return Loading || Moving; } }
    public void SetValue(bool? value,double now,bool animate) {
        Value=value;
        if(Loading)return;
        Move(now,animate);
    }
    void Move(double now,bool animate) {
        Step(now); target=Value.HasValue?(Value.Value?1:0):.5;
        if(!initialized || !animate) { Position=target; Moving=false; initialized=true; return; }
        from=Position; started=now; Moving=Math.Abs(target-from)>.001;
    }
    public void SetLoading(bool value,double now,bool animate) {
        if(Loading==value)return;
        Step(now); Loading=value;
        if(value) { initialized=true; Moving=false; spinStarted=now; }
        else Move(now,animate);
    }
    public void Step(double now) {
        if(Loading) { Angle=(float)(((Math.Max(0,now-spinStarted)%900)/900)*360); return; }
        if(!Moving)return;
        double progress=Math.Min(1,Math.Max(0,(now-started)/220));
        Position=from+(target-from)*(1-Math.Pow(1-progress,3));
        if(progress>=1) { Position=target; Moving=false; }
    }
}
}
