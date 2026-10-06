using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace HdrCenter {
public sealed class BufferedPanel : Panel {
    public UiTheme Theme=UiTheme.Create(false);
    public BufferedPanel() { DoubleBuffered=true; ResizeRedraw=true; }
    protected override void OnPaintBackground(PaintEventArgs e) { if(Theme.Glass)Shapes.GlassSurface(this,Theme,e.Graphics); else base.OnPaintBackground(e); }
}
public sealed class BufferedTableLayoutPanel : TableLayoutPanel {
    public UiTheme Theme=UiTheme.Create(false);
    public BufferedTableLayoutPanel() { DoubleBuffered=true; ResizeRedraw=true; }
    protected override void OnPaintBackground(PaintEventArgs e) { if(Theme.Glass)Shapes.GlassSurface(this,Theme,e.Graphics); else base.OnPaintBackground(e); }
}
// Resume nested layouts only once, after all status controls have changed.
public sealed class LayoutBatch : IDisposable {
    readonly List<Control> containers=new List<Control>();
    public LayoutBatch(Control root) { Suspend(root); }
    void Suspend(Control control) {
        if(!control.HasChildren)return;
        control.SuspendLayout(); containers.Add(control);
        foreach(Control child in control.Controls)Suspend(child);
    }
    public void Dispose() {
        for(int i=containers.Count-1;i>=0;i--)containers[i].ResumeLayout(true);
    }
}
}
