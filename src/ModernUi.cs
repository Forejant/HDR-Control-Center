using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HdrCenter {
public sealed class UiTheme {
    public bool Dark,TaskbarDark,HighContrast,Transparency=true,Glass;
    public Color Background,Surface,Text,Muted,Accent,Border,Hover,Track;
    public static UiTheme Read() {
        bool dark=false,taskbar=true,transparent=true;
        try { using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
            if(key!=null) { dark=Convert.ToInt32(key.GetValue("AppsUseLightTheme",1))==0; taskbar=Convert.ToInt32(key.GetValue("SystemUsesLightTheme",0))==0; transparent=Convert.ToInt32(key.GetValue("EnableTransparency",1))!=0; }
        } } catch { }
        var theme=Create(dark); theme.TaskbarDark=taskbar; theme.HighContrast=SystemInformation.HighContrast; theme.Transparency=transparent;
        if(theme.HighContrast) { theme.Background=SystemColors.Window; theme.Surface=SystemColors.Window; theme.Text=SystemColors.WindowText; theme.Muted=SystemColors.WindowText; theme.Accent=SystemColors.Highlight; theme.Border=SystemColors.WindowText; theme.Hover=SystemColors.Control; theme.Track=SystemColors.GrayText; }
        return theme;
    }
    public static UiTheme Create(bool dark) {
        return new UiTheme { Dark=dark,TaskbarDark=dark,
            Background=dark?Color.FromArgb(32,32,32):Color.FromArgb(243,243,243),
            Surface=dark?Color.FromArgb(43,43,43):Color.White,
            Text=dark?Color.FromArgb(247,247,247):Color.FromArgb(27,27,27),
            Muted=dark?Color.FromArgb(178,178,178):Color.FromArgb(100,100,100),
            Accent=dark?Color.FromArgb(96,205,255):Color.FromArgb(0,95,184),
            Border=dark?Color.FromArgb(65,65,65):Color.FromArgb(221,221,221),
            Hover=dark?Color.FromArgb(56,56,56):Color.FromArgb(235,235,235),
            Track=dark?Color.FromArgb(105,105,105):Color.FromArgb(169,169,169) };
    }
    public bool Same(UiTheme other) { return other!=null && Dark==other.Dark && TaskbarDark==other.TaskbarDark && HighContrast==other.HighContrast && Transparency==other.Transparency; }
}
public static class Shapes {
    public static Color CardGlass(UiTheme theme) { return Color.FromArgb(140,theme.Dark?Color.FromArgb(72,72,72):theme.Surface); }
    public static Bitmap GlassLayer(Control owner,UiTheme theme,float dpiX,float dpiY) {
        var layer=new Bitmap(Math.Max(1,owner.Width),Math.Max(1,owner.Height),PixelFormat.Format32bppPArgb);
        layer.SetResolution(dpiX,dpiY); using(var g=Graphics.FromImage(layer))PaintGlass(owner,theme,g,owner.ClientRectangle);
        return layer;
    }
    public static void GlassSurface(Control owner,UiTheme theme,Graphics graphics) {
        using(var layer=GlassLayer(owner,theme,graphics.DpiX,graphics.DpiY))AlphaCopy.Draw(graphics,layer);
    }
    public static void PaintGlass(Control owner,UiTheme theme,Graphics graphics,Rectangle rectangle) {
        ClearGlass(graphics,rectangle);
        using(var tint=new SolidBrush(Color.FromArgb(theme.Dark?180:120,theme.Background)))graphics.FillRectangle(tint,rectangle);
        for(var ancestor=owner.Parent;ancestor!=null;ancestor=ancestor.Parent)if(ancestor is RoundedCard) {
            // Buttons/checks lie inside the card padding. Reconstruct those two
            // material layers locally instead of repainting all their parents.
            using(var surface=new SolidBrush(CardGlass(theme)))graphics.FillRectangle(surface,rectangle);
            break;
        }
    }
    public static void ClearGlass(Graphics graphics,Rectangle rectangle) {
        var previous=graphics.CompositingMode; graphics.CompositingMode=CompositingMode.SourceCopy;
        using(var brush=new SolidBrush(Color.FromArgb(0,0,0,0)))graphics.FillRectangle(brush,rectangle);
        graphics.CompositingMode=previous;
    }
    public static void Text(Graphics graphics,string text,Font font,Rectangle rectangle,Color color,bool centered) {
        using(var layer=new Bitmap(Math.Max(1,rectangle.Width),Math.Max(1,rectangle.Height),PixelFormat.Format32bppPArgb)) {
        layer.SetResolution(graphics.DpiX,graphics.DpiY);
        using(var ink=Graphics.FromImage(layer)) {
        ink.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        ink.TextContrast=4;
        using(var brush=new SolidBrush(color))using(var format=new StringFormat { Alignment=centered?StringAlignment.Center:StringAlignment.Near,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap }) {
            ink.DrawString(text,font,brush,new Rectangle(Point.Empty,layer.Size),format);
        }
        }
        graphics.DrawImageUnscaled(layer,rectangle.Location);
        }
    }
    public static GraphicsPath Round(RectangleF rect,float radius) {
        var path=new GraphicsPath(); float d=Math.Min(radius*2,Math.Min(rect.Width,rect.Height));
        if(d<=0) { path.AddRectangle(rect); return path; }
        path.AddArc(rect.X,rect.Y,d,d,180,90); path.AddArc(rect.Right-d,rect.Y,d,d,270,90);
        path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90); path.AddArc(rect.X,rect.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
    }
}
public sealed class RoundedCard : TableLayoutPanel {
    public UiTheme Theme=UiTheme.Create(false);
    public RoundedCard() { DoubleBuffered=true; ResizeRedraw=true; SetStyle(ControlStyles.SupportsTransparentBackColor,true); BackColor=Color.Transparent; AutoSize=true; AutoSizeMode=AutoSizeMode.GrowAndShrink; Dock=DockStyle.Top; ColumnCount=1; ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); Padding=new Padding(14); Margin=new Padding(0,0,0,14); }
    protected override void OnPaintBackground(PaintEventArgs e) {
        if(Theme.Glass)return;
        using(var brush=new SolidBrush(Parent==null?Theme.Background:Parent.BackColor))e.Graphics.FillRectangle(brush,ClientRectangle);
    }
    protected override void OnPaint(PaintEventArgs e) {
        if(Theme.Glass) {
            using(var layer=Shapes.GlassLayer(this,Theme,e.Graphics.DpiX,e.Graphics.DpiY)) {
                using(var g=Graphics.FromImage(layer))PaintCard(g);
                AlphaCopy.Draw(e.Graphics,layer);
            }
            return;
        }
        PaintCard(e.Graphics); base.OnPaint(e);
    }
    void PaintCard(Graphics graphics) {
        var e=new PaintEventArgs(graphics,ClientRectangle);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=Shapes.Round(new RectangleF(.5F,.5F,Width-1,Height-1),10*DeviceDpi/96F))
        using(var fill=new SolidBrush(Theme.Glass?Shapes.CardGlass(Theme):Theme.Surface)) using(var border=new Pen(Theme.Glass?Color.FromArgb(100,Theme.Border):Theme.Border)) { e.Graphics.FillPath(fill,path); e.Graphics.DrawPath(border,path); }
    }
}
public sealed class ModernButton : Control {
    public AutoSizeMode AutoSizeMode { get; set; }
    public UiTheme Theme=UiTheme.Create(false); public bool IsSwitch,IsClose; bool hover,pressed; bool? switchValue;
    readonly SwitchMotion motion=new SwitchMotion();
    readonly Timer frameTimer=new Timer { Interval=16 };
    bool animationDisposed;
    static double Now { get { return Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency; } }
    public bool? SwitchValue { get { return switchValue; } set { if(switchValue==value)return; switchValue=value; motion.SetValue(value,Now,!Theme.HighContrast); UpdateFrames(); Invalidate(); } }
    public bool IsLoading { get { return motion.Loading; } set { motion.SetLoading(value,Now,!Theme.HighContrast); UpdateFrames(); Invalidate(); AccessibilityNotifyClients(AccessibleEvents.StateChange,-1); } }
    internal SwitchMotion Motion { get { return motion; } }
    void UpdateFrames() { if(animationDisposed)return; if(!IsDisposed && IsHandleCreated && Visible && motion.NeedsFrames)frameTimer.Start(); else frameTimer.Stop(); }
    public ModernButton() {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);
        SetStyle(ControlStyles.Selectable,true); BackColor=Color.Transparent; AutoSize=true; AutoSizeMode=AutoSizeMode.GrowAndShrink; TabStop=true; AccessibleRole=AccessibleRole.PushButton;
        frameTimer.Tick+=delegate { motion.Step(Now); Invalidate(); UpdateFrames(); };
    }
    new float Scale { get { return Math.Max(96,DeviceDpi)/96F; } }
    public override Size GetPreferredSize(Size proposed) {
        if(IsClose)return new Size((int)(32*Scale),(int)(32*Scale));
        if(IsSwitch)return new Size((int)(84*Scale),(int)(32*Scale));
        var text=TextRenderer.MeasureText(Text,Font); return new Size(text.Width+(int)(24*Scale),Math.Max(text.Height+(int)(12*Scale),(int)(34*Scale)));
    }
    protected override void OnPaintBackground(PaintEventArgs e) {
        // Clear the whole control, including corners outside the rounded shape.
        if(Theme.Glass)return;
        using(var brush=new SolidBrush(BackColor.A==255?BackColor:Theme.Surface))e.Graphics.FillRectangle(brush,ClientRectangle);
    }
    protected override void OnPaint(PaintEventArgs e) {
        if(Theme.Glass) {
            using(var layer=Shapes.GlassLayer(this,Theme,e.Graphics.DpiX,e.Graphics.DpiY)) {
                using(var g=Graphics.FromImage(layer))PaintButton(g);
                AlphaCopy.Draw(e.Graphics,layer);
            }
            return;
        }
        PaintButton(e.Graphics);
    }
    void PaintButton(Graphics graphics) {
        var e=new PaintEventArgs(graphics,ClientRectangle);
        var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
        Color text=Enabled?Theme.Text:Theme.Muted;
        if(IsSwitch && (switchValue.HasValue || IsLoading)) {
            float w=42*Scale,h=22*Scale; var r=new RectangleF((Width-w)/2,(Height-h)/2,w,h);
            double position=motion.Position; Color on=(Enabled || IsLoading)?Theme.Accent:Theme.Track,off=hover?Theme.Hover:Theme.Surface;
            Color fill=Blend(off,on,position);
            using(var path=Shapes.Round(r,h/2)) using(var brush=new SolidBrush(fill)) using(var pen=new Pen(Blend(Theme.Muted,on,position),Scale)) { g.FillPath(brush,path); g.DrawPath(pen,path); }
            float size=14*Scale, x=(float)(r.X+4*Scale+position*(w-size-8*Scale)),y=r.Y+(h-size)/2;
            Color thumb=Blend(Theme.Muted,Theme.Dark?Color.FromArgb(24,24,24):Color.White,position);
            using(var brush=new SolidBrush(thumb))g.FillEllipse(brush,x,y,size,size);
            if(IsLoading) {
                var ring=new RectangleF(x+3*Scale,y+3*Scale,size-6*Scale,size-6*Scale);
                using(var pen=new Pen(position>.5?Theme.Accent:Theme.Surface,1.4F*Scale)) { pen.StartCap=pen.EndCap=LineCap.Round; g.DrawArc(pen,ring,motion.Angle,265); }
            }
        } else {
            var r=new RectangleF(.5F,.5F,Width-1,Height-1);
            Color fill=IsClose?(hover?Color.FromArgb(196,43,28):Color.Transparent):(hover||pressed?Theme.Hover:Theme.Surface);
            using(var path=Shapes.Round(r,6*Scale)) {
                if(fill.A>0)using(var brush=new SolidBrush(fill))g.FillPath(brush,path);
                if(!IsClose)using(var pen=new Pen(Theme.Border))g.DrawPath(pen,path);
            }
            if(IsClose)using(var pen=new Pen(hover?Color.White:Theme.Muted,1.3F*Scale)) {
                float k=5*Scale,cx=Width/2F,cy=Height/2F; g.DrawLine(pen,cx-k,cy-k,cx+k,cy+k); g.DrawLine(pen,cx+k,cy-k,cx-k,cy+k);
            } else Shapes.Text(g,Text,Font,ClientRectangle,text,true);
        }
        if(Focused && ShowFocusCues)using(var path=Shapes.Round(new RectangleF(2,2,Width-5,Height-5),5*Scale))using(var pen=new Pen(Theme.Accent,Scale))g.DrawPath(pen,path);
    }
    static Color Blend(Color a,Color b,double amount) { return Color.FromArgb((int)(a.R+(b.R-a.R)*amount),(int)(a.G+(b.G-a.G)*amount),(int)(a.B+(b.B-a.B)*amount)); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); motion.Step(Now); UpdateFrames(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateFrames(); }
    protected override void Dispose(bool disposing) { if(disposing) { animationDisposed=true; frameTimer.Dispose(); } base.Dispose(disposing); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover=true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover=false; pressed=false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if(Enabled && e.Button==MouseButtons.Left) { Focus(); pressed=true; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed=false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); pressed=false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); pressed=false; Invalidate(); }
    protected override AccessibleObject CreateAccessibilityInstance() { return new ButtonAccessibility(this); }
    sealed class ButtonAccessibility : ControlAccessibleObject {
        readonly ModernButton owner; public ButtonAccessibility(ModernButton control):base(control) { owner=control; }
        public override AccessibleStates State { get { return base.State | AccessibleStates.Focusable | (owner.Focused?AccessibleStates.Focused:AccessibleStates.None) | (owner.IsLoading?AccessibleStates.Busy:AccessibleStates.None); } }
        public override string DefaultAction { get { return "按下"; } }
        public override void DoDefaultAction() { if(owner.Enabled) { owner.Focus(); owner.OnClick(EventArgs.Empty); } }
    }
    protected override bool IsInputKey(Keys keyData) { return keyData==Keys.Space || keyData==Keys.Enter || base.IsInputKey(keyData); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if(Enabled && (e.KeyCode==Keys.Space || e.KeyCode==Keys.Enter)) { pressed=true; Invalidate(); e.Handled=true; } }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if(Enabled && pressed && (e.KeyCode==Keys.Space || e.KeyCode==Keys.Enter)) { pressed=false; Invalidate(); OnClick(EventArgs.Empty); e.Handled=true; } }
}
public sealed class ModernCheckBox : Control {
    bool isChecked; public event EventHandler CheckedChanged;
    public bool Checked { get { return isChecked; } set { if(isChecked==value)return; isChecked=value; Invalidate(); if(CheckedChanged!=null)CheckedChanged(this,EventArgs.Empty); AccessibilityNotifyClients(AccessibleEvents.StateChange,-1); } }
    public UiTheme Theme=UiTheme.Create(false);
    public ModernCheckBox() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor|ControlStyles.Selectable,true); BackColor=Color.Transparent; AutoSize=true; TabStop=true; AccessibleRole=AccessibleRole.CheckButton; }
    public override Size GetPreferredSize(Size proposed) { float s=Math.Max(96,DeviceDpi)/96F; var size=TextRenderer.MeasureText(Text,Font); return new Size(size.Width+(int)(28*s),Math.Max(size.Height,(int)(24*s))); }
    protected override void OnPaintBackground(PaintEventArgs e) {
        if(Theme.Glass)return;
        using(var brush=new SolidBrush(BackColor.A==255?BackColor:Theme.Surface))e.Graphics.FillRectangle(brush,ClientRectangle);
    }
    protected override void OnPaint(PaintEventArgs e) {
        if(Theme.Glass) {
            using(var layer=Shapes.GlassLayer(this,Theme,e.Graphics.DpiX,e.Graphics.DpiY)) {
                using(var g=Graphics.FromImage(layer))PaintCheck(g);
                AlphaCopy.Draw(e.Graphics,layer);
            }
            return;
        }
        PaintCheck(e.Graphics);
    }
    void PaintCheck(Graphics graphics) {
        var e=new PaintEventArgs(graphics,ClientRectangle);
        float s=Math.Max(96,DeviceDpi)/96F, side=16*s; var rect=new RectangleF(s,(Height-side)/2,side,side); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=Shapes.Round(rect,3*s))using(var brush=new SolidBrush(Checked?Theme.Accent:Theme.Surface))using(var pen=new Pen(Checked?Theme.Accent:Theme.Muted,s)) { g.FillPath(brush,path); g.DrawPath(pen,path); }
        if(Checked)using(var pen=new Pen(Theme.Dark?Color.FromArgb(24,24,24):Color.White,1.8F*s)) { pen.StartCap=pen.EndCap=LineCap.Round; g.DrawLines(pen,new[]{new PointF(rect.X+4*s,rect.Y+8*s),new PointF(rect.X+7*s,rect.Y+11*s),new PointF(rect.X+12*s,rect.Y+5*s)}); }
        Shapes.Text(g,Text,Font,new Rectangle((int)(25*s),0,Width-(int)(25*s),Height),Enabled?Theme.Text:Theme.Muted,false);
        if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle((int)(23*s),2,Width-(int)(24*s),Height-4),Theme.Text,BackColor);
    }
    protected override void OnClick(EventArgs e) { if(Enabled)Checked=!Checked; base.OnClick(e); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if(Enabled && e.Button==MouseButtons.Left)Focus(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override bool IsInputKey(Keys keyData) { return keyData==Keys.Space || base.IsInputKey(keyData); }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if(Enabled && e.KeyCode==Keys.Space) { OnClick(EventArgs.Empty); e.Handled=true; } }
    protected override AccessibleObject CreateAccessibilityInstance() { return new CheckAccessibility(this); }
    sealed class CheckAccessibility : ControlAccessibleObject {
        readonly ModernCheckBox owner; public CheckAccessibility(ModernCheckBox control):base(control) { owner=control; }
        public override AccessibleStates State { get { return base.State | AccessibleStates.Focusable | (owner.Focused?AccessibleStates.Focused:AccessibleStates.None) | (owner.Checked?AccessibleStates.Checked:AccessibleStates.None); } }
        public override string DefaultAction { get { return "切换"; } }
        public override void DoDefaultAction() { if(owner.Enabled) { owner.Focus(); owner.OnClick(EventArgs.Empty); } }
    }
}
public class SmoothLabel : Label {
    public UiTheme Theme=UiTheme.Create(false);
    public SmoothLabel() { UseCompatibleTextRendering=true; SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true); }
    protected override void OnPaint(PaintEventArgs e) {
        using(var layer=RenderTextLayer(e.Graphics.DpiX,e.Graphics.DpiY))AlphaCopy.Draw(e.Graphics,layer);
    }
    internal Bitmap RenderTextLayer(float dpiX,float dpiY) {
        var layer=new Bitmap(Math.Max(1,Width),Math.Max(1,Height),PixelFormat.Format32bppPArgb);
        layer.SetResolution(dpiX,dpiY);
        using(var g=Graphics.FromImage(layer)) {
        if(Theme.Glass)Shapes.PaintGlass(this,Theme,g,ClientRectangle); else g.Clear(BackColor.A==255?BackColor:Theme.Background);
        g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit; g.TextContrast=4;
        using(var format=new StringFormat())using(var brush=new SolidBrush(Enabled?ForeColor:SystemColors.GrayText)) {
            int alignment=(int)TextAlign;
            format.Alignment=(alignment & 0x111)!=0?StringAlignment.Near:(alignment & 0x222)!=0?StringAlignment.Center:StringAlignment.Far;
            format.LineAlignment=(alignment & 0x007)!=0?StringAlignment.Near:(alignment & 0x070)!=0?StringAlignment.Center:StringAlignment.Far;
            format.HotkeyPrefix=UseMnemonic?HotkeyPrefix.Show:HotkeyPrefix.None;
            format.Trimming=AutoEllipsis?StringTrimming.EllipsisCharacter:StringTrimming.None;
            if(RightToLeft==RightToLeft.Yes)format.FormatFlags|=StringFormatFlags.DirectionRightToLeft;
            var bounds=new RectangleF(Padding.Left,Padding.Top,Math.Max(0,Width-Padding.Horizontal),Math.Max(0,Height-Padding.Vertical));
            g.DrawString(Text,Font,brush,bounds,format);
        }
        }
        return layer;
    }
}
public static class AlphaCopy {
    // Native glass needs valid premultiplied BGRA, including opaque glyphs.
    // Ordinary GDI text/compatible bitmap drawing can discard those alpha bits.
    // Copy a top-down 32-bit DIB without converting its alpha channel.
    [StructLayout(LayoutKind.Sequential)] struct Info { public int Size,Width,Height; public short Planes,Bits; public int Compression,ImageSize,X,Y,Used,Important; }
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc,ref Info info,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dc,int x,int y,int w,int h,IntPtr source,int sx,int sy,uint op);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    public static void Draw(Graphics target,Bitmap bitmap) {
        IntPtr dc=target.GetHdc(),memory=IntPtr.Zero,dib=IntPtr.Zero,old=IntPtr.Zero;
        try {
            memory=CreateCompatibleDC(dc); IntPtr bits;
            var info=new Info { Size=40,Width=bitmap.Width,Height=-bitmap.Height,Planes=1,Bits=32 };
            dib=CreateDIBSection(dc,ref info,0,out bits,IntPtr.Zero,0);
            if(dib==IntPtr.Zero)throw new InvalidOperationException("Cannot allocate alpha text surface.");
            var data=bitmap.LockBits(new Rectangle(Point.Empty,bitmap.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
            try { var row=new byte[bitmap.Width*4]; for(int y=0;y<bitmap.Height;y++) { Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),row,0,row.Length); Marshal.Copy(row,0,IntPtr.Add(bits,y*row.Length),row.Length); } }
            finally { bitmap.UnlockBits(data); }
            old=SelectObject(memory,dib); BitBlt(dc,0,0,bitmap.Width,bitmap.Height,memory,0,0,0x00cc0020);
        } finally { if(old!=IntPtr.Zero)SelectObject(memory,old); if(dib!=IntPtr.Zero)DeleteObject(dib); if(memory!=IntPtr.Zero)DeleteDC(memory); target.ReleaseHdc(dc); }
    }
}
public sealed class BrightnessReadout : SmoothLabel {
    public BrightnessReadout() { AutoSize=false; UseCompatibleTextRendering=true; TextAlign=ContentAlignment.MiddleRight; UpdateSize(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateSize(); }
    protected override void ScaleControl(SizeF factor,BoundsSpecified specified) { base.ScaleControl(factor,specified); UpdateSize(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateSize(); }
    void UpdateSize() {
        // Reserve the widest value so each new percentage only repaints this
        // label, without asking the parent table to lay out the whole panel.
        Size=TextRenderer.MeasureText("100%  /  480 nits",Font)+new Size(12,6);
    }
}
public sealed class ModernComboBox : ComboBox {
    public int PaintTransactions { get; private set; }
    public UiTheme Theme=UiTheme.Create(false);
    public ModernComboBox() { FlatStyle=FlatStyle.Flat; DrawMode=DrawMode.OwnerDrawFixed; AccessibleName="显示器选择"; }
    protected override void OnDrawItem(DrawItemEventArgs e) {
        if(e.Index<0)return; bool selected=(e.State & DrawItemState.Selected)!=0;
        using(var brush=new SolidBrush(selected?Theme.Hover:Theme.Surface))e.Graphics.FillRectangle(brush,e.Bounds);
        Shapes.Text(e.Graphics,Items[e.Index].ToString(),Font,new Rectangle(e.Bounds.X+8,e.Bounds.Y,e.Bounds.Width-12,e.Bounds.Height),Theme.Text,false);
    }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateRound(); }
    protected override void WndProc(ref Message message) {
        if(message.Msg==0x0014) { message.Result=new IntPtr(1); return; }
        if(message.Msg==0x000f) {
            PaintTransactions++;
            if(message.WParam!=IntPtr.Zero)PaintBuffered(message.WParam);
            else {
                PaintData paint; IntPtr dc=BeginPaint(Handle,out paint);
                try { if(dc!=IntPtr.Zero)PaintBuffered(dc); } finally { EndPaint(Handle,ref paint); }
            }
            message.Result=IntPtr.Zero; return;
        }
        if((message.Msg==0x0318 || message.Msg==0x0317) && message.WParam!=IntPtr.Zero) {
            PaintBuffered(message.WParam); message.Result=IntPtr.Zero; return;
        }
        base.WndProc(ref message);
    }
    void PaintBuffered(IntPtr dc) {
        if(Width<1 || Height<1)return;
        using(var target=Graphics.FromHdc(dc))using(var buffer=BufferedGraphicsManager.Current.Allocate(target,ClientRectangle)) { PaintClosed(buffer.Graphics); buffer.Render(target); }
    }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); Invalidate(); }
    [StructLayout(LayoutKind.Sequential)] struct PaintData { public IntPtr Dc; public int Erase,Left,Top,Right,Bottom,Restore,Incremental; [MarshalAs(UnmanagedType.ByValArray,SizeConst=32)] public byte[] Reserved; }
    [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr hwnd,out PaintData paint);
    [DllImport("user32.dll")] static extern bool EndPaint(IntPtr hwnd,ref PaintData paint);
    void PaintClosed(Graphics graphics) {
        float scale=Math.Max(96,DeviceDpi)/96F; graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var background=new SolidBrush(Parent==null?Theme.Background:Parent.BackColor))graphics.FillRectangle(background,ClientRectangle);
        using(var path=Shapes.Round(new RectangleF(.5F,.5F,Width-1,Height-1),6*scale))using(var fill=new SolidBrush(Theme.Surface))using(var pen=new Pen(Focused?Theme.Accent:Theme.Border,scale)) { graphics.FillPath(fill,path); graphics.DrawPath(pen,path); }
        Shapes.Text(graphics,SelectedItem==null?Text:SelectedItem.ToString(),Font,new Rectangle((int)(10*scale),0,Width-(int)(38*scale),Height),Theme.Text,false);
        float cx=Width-16*scale,cy=Height/2F; using(var pen=new Pen(Theme.Muted,1.2F*scale))graphics.DrawLines(pen,new[]{new PointF(cx-4*scale,cy-2*scale),new PointF(cx,cy+2*scale),new PointF(cx+4*scale,cy-2*scale)});
    }
    public void UpdateRound() {
        if(Width<1 || Height<1)return;
        using(var path=Shapes.Round(new RectangleF(0,0,Width,Height),6*Math.Max(96,DeviceDpi)/96F)) { var old=Region; Region=new Region(path); if(old!=null)old.Dispose(); }
    }
}
public sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer {
    readonly UiTheme theme; public ThemeMenuRenderer(UiTheme value) { theme=value; RoundedEdges=true; }
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { using(var brush=new SolidBrush(theme.Surface))e.Graphics.FillRectangle(brush,e.AffectedBounds); }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) { if(e.Item.Selected)using(var path=Shapes.Round(new RectangleF(3,1,e.Item.Width-6,e.Item.Height-2),5))using(var brush=new SolidBrush(theme.Hover))e.Graphics.FillPath(brush,path); }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor=theme.Text; base.OnRenderItemText(e); }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using(var pen=new Pen(theme.Border))e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1); }
}
public static class WindowChrome {
    public static bool ApplyBackdrop(IntPtr hwnd,UiTheme theme) {
        try {
            int kind=theme.Transparency && !theme.HighContrast?3:1;
            bool enabled=DwmSetWindowAttribute(hwnd,38,ref kind,4)>=0 && kind==3;
            var margins=new GlassMargins { Left=enabled?-1:0,Right=enabled?-1:0,Top=enabled?-1:0,Bottom=enabled?-1:0 };
            if(DwmExtendFrameIntoClientArea(hwnd,ref margins)<0)enabled=false;
            if(!enabled) { kind=1; DwmSetWindowAttribute(hwnd,38,ref kind,4); margins=new GlassMargins(); DwmExtendFrameIntoClientArea(hwnd,ref margins); }
            return enabled;
        } catch(DllNotFoundException) { return false; } catch(EntryPointNotFoundException) { return false; }
    }
    public static void Apply(IntPtr hwnd,UiTheme theme) {
        try {
            int dark=theme.Dark?1:0,corner=2,border=ColorTranslator.ToWin32(theme.Border);
            DwmSetWindowAttribute(hwnd,20,ref dark,4); DwmSetWindowAttribute(hwnd,33,ref corner,4); DwmSetWindowAttribute(hwnd,34,ref border,4);
        } catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { }
    }
    public static bool DisablePopupTransitions(IntPtr hwnd) {
        // This tray panel presents its prepared frame without a show animation.
        // TRANSITIONS_FORCEDISABLED supports setting, not querying via Get.
        try { int value=1; return DwmSetWindowAttribute(hwnd,3,ref value,4)>=0; }
        catch(DllNotFoundException) { return false; } catch(EntryPointNotFoundException) { return false; }
    }
    public static bool SetPopupCloak(IntPtr hwnd,bool cloaked) {
        try { int value=cloaked?1:0; return DwmSetWindowAttribute(hwnd,13,ref value,4)>=0; }
        catch(DllNotFoundException) { return false; } catch(EntryPointNotFoundException) { return false; }
    }
    public static bool IsPopupCloaked(IntPtr hwnd) {
        try { int value; return DwmGetWindowAttribute(hwnd,14,out value,4)>=0 && (value & 1)!=0; }
        catch(DllNotFoundException) { return false; } catch(EntryPointNotFoundException) { return false; }
    }
    public static bool RevealPopup(IntPtr hwnd) {
        // Confirm that this application's cloak was removed. A second bounded
        // attempt handles a transient failed request without a Hide/Show cycle.
        for(int attempt=0;attempt<2;attempt++)if(SetPopupCloak(hwnd,false) && !IsPopupCloaked(hwnd))return true;
        return false;
    }
    public static void PrepareActiveBackdrop(IntPtr hwnd,UiTheme theme) {
        // Only called behind the application's cloak after native foreground
        // activation. Recreate the backdrop in its active state rather than
        // revealing a retained inactive brush and its activation transition.
        if(!theme.Glass || !theme.Transparency || theme.HighContrast)return;
        try {
            int none=1,acrylic=3;
            if(DwmSetWindowAttribute(hwnd,38,ref none,4)>=0)
                DwmSetWindowAttribute(hwnd,38,ref acrylic,4);
        } catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { }
    }
    public static bool PaintPopup(IntPtr hwnd) {
        // RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW.
        return RedrawWindow(hwnd,IntPtr.Zero,IntPtr.Zero,0x0001|0x0004|0x0080|0x0100);
    }
    public static void FlushPopup() {
        // Commit GDI and allow the compositor to consume activation, material
        // and surface updates before reveal. Only the concealed popup path
        // calls this; dragging and animation do not wait for the compositor.
        GdiFlush();
        try { DwmFlush(); } catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { }
    }
    public static void Drag(Form form) { ReleaseCapture(); SendMessage(form.Handle,0x00a1,new IntPtr(2),IntPtr.Zero); }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
    [StructLayout(LayoutKind.Sequential)] struct GlassMargins { public int Left,Right,Top,Bottom; }
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref GlassMargins margins);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attr,out int value,int size);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hwnd,IntPtr update,IntPtr region,uint flags);
    [DllImport("gdi32.dll")] static extern bool GdiFlush();
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
}
}
