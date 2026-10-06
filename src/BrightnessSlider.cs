using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HdrCenter {
public sealed class BrightnessSlider : Control {
    public UiTheme Theme=UiTheme.Create(false);
    int value; double position; bool dragging;
    public event EventHandler ValueChanged;
    public event EventHandler InteractionEnded;
    public bool IsDragging { get { return dragging; } }
    public int Value { get { return value; } set { SetValue(value,false); } }
    public BrightnessSlider() {
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable|ControlStyles.SupportsTransparentBackColor,true);
        TabStop=true; Height=38; MinimumSize=new Size(80,38); AccessibleName="SDR 内容亮度"; AccessibleRole=AccessibleRole.Slider;
    }
    void SetValue(int next,bool user) {
        next=Math.Max(0,Math.Min(100,next)); bool changed=value!=next; value=next;
        if(!dragging) position=next/100.0;
        Invalidate();
        if(changed && user && ValueChanged!=null) ValueChanged(this,EventArgs.Empty);
    }
    double DpiScale { get { return Math.Max(96,DeviceDpi)/96.0; } }
    void FromMouse(int x) { double inset=12*DpiScale; position=Math.Max(0,Math.Min(1,(x-inset)/Math.Max(1,Width-2*inset))); SetValue((int)Math.Round(position*100),true); }
    protected override void OnPaint(PaintEventArgs e) {
        if(Theme.Glass) {
            using(var layer=Shapes.GlassLayer(this,Theme,e.Graphics.DpiX,e.Graphics.DpiY)) {
                using(var g=Graphics.FromImage(layer))PaintSlider(g);
                AlphaCopy.Draw(e.Graphics,layer);
            }
        } else { base.OnPaint(e); PaintSlider(e.Graphics); }
    }
    void PaintSlider(Graphics graphics) {
        var e=new PaintEventArgs(graphics,ClientRectangle); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        int y=Height/2; float inset=(float)(12*DpiScale), radius=(float)(8*DpiScale), x=(float)(inset+position*(Width-2*inset));
        using(var basePen=new Pen(Theme.Track,(float)(4*DpiScale))) using(var fillPen=new Pen(Enabled?Theme.Accent:Theme.Track,(float)(4*DpiScale))) using(var thumb=new SolidBrush(Theme.Surface)) using(var inner=new SolidBrush(Enabled?Theme.Accent:Theme.Track)) using(var outline=new Pen(Theme.Border,(float)DpiScale)) {
            basePen.StartCap=basePen.EndCap=LineCap.Round; fillPen.StartCap=fillPen.EndCap=LineCap.Round;
            e.Graphics.DrawLine(basePen,inset,y,Width-inset,y); e.Graphics.DrawLine(fillPen,inset,y,x,y); e.Graphics.FillEllipse(thumb,x-radius,y-radius,2*radius,2*radius); e.Graphics.DrawEllipse(outline,x-radius,y-radius,2*radius,2*radius);
            float innerRadius=radius*(dragging?.72F:.6F); e.Graphics.FillEllipse(inner,x-innerRadius,y-innerRadius,2*innerRadius,2*innerRadius);
        }
        if(Focused) ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(2,2,Width-4,Height-4));
    }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if(e.Button!=MouseButtons.Left || !Enabled)return; Focus(); dragging=true; Capture=true; FromMouse(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if(dragging) FromMouse(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if(!dragging)return; FromMouse(e.X); dragging=false; Capture=false; if(InteractionEnded!=null)InteractionEnded(this,EventArgs.Empty); }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if(dragging && !Capture) { dragging=false; if(InteractionEnded!=null)InteractionEnded(this,EventArgs.Empty); } }
    protected override bool IsInputKey(Keys key) { return key==Keys.Left||key==Keys.Right||key==Keys.Home||key==Keys.End||key==Keys.PageUp||key==Keys.PageDown||base.IsInputKey(key); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); int v=Value;
        if(e.KeyCode==Keys.Left)v--; else if(e.KeyCode==Keys.Right)v++; else if(e.KeyCode==Keys.Home)v=0; else if(e.KeyCode==Keys.End)v=100; else if(e.KeyCode==Keys.PageUp)v+=5; else if(e.KeyCode==Keys.PageDown)v-=5; else return;
        SetValue(v,true); e.Handled=true;
    }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if(InteractionEnded!=null) InteractionEnded(this,EventArgs.Empty); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); if(!Enabled)return; SetValue(Value+Math.Sign(e.Delta),true); if(InteractionEnded!=null)InteractionEnded(this,EventArgs.Empty); }
}
}
