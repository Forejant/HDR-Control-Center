using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Forms;

namespace HdrCenter {
public static class MaterialTests {
    public static void Run(string path) {
        var lines=new List<string>();
        foreach(bool dark in new[]{false,true})foreach(int dpi in new[]{96,144,192}) {
            var theme=UiTheme.Create(dark); theme.Glass=true;
            using(var image=new Bitmap(520,100,PixelFormat.Format32bppPArgb))using(var font=new Font("Microsoft YaHei UI",12F)) {
                image.SetResolution(dpi,dpi);
                using(var g=Graphics.FromImage(image))Shapes.Text(g,"HDR 控制中心 · 正在应用",font,new Rectangle(Point.Empty,image.Size),theme.Text,false);
                int soft=0,solid=0;
                for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++) {
                    var p=image.GetPixel(x,y); if(p.A>15 && p.A<240) { soft++; if(Math.Abs(p.R-p.G)>1 || Math.Abs(p.R-p.B)>1)throw new Exception("Text has coloured edge artifacts"); }
                    if(p.A>240)solid++;
                }
                if(soft<100 || solid<100)throw new Exception("Text must have antialiased alpha edges and opaque strokes");
                lines.Add("dpi="+dpi+" dark="+dark+" soft alpha edges="+soft+" opaque strokes="+solid+" PASS");
            }
            using(var label=new SmoothLabel { AutoSize=false,Size=new Size(480,80),Font=new Font("Microsoft YaHei UI",12F),Text="HDR 控制中心 · 正在应用",ForeColor=theme.Text,Theme=theme })
            using(var image=label.RenderTextLayer(dpi,dpi)) {
                int opaque=0; for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)if(image.GetPixel(x,y).A>240)opaque++;
                if(opaque<100)throw new Exception("Label strokes must retain opaque alpha on glass");
                if(image.GetPixel(0,0).A==0 || image.GetPixel(0,0).A==255)throw new Exception("Label must preserve translucent background");
                lines.Add("label premultiplied-alpha copy dpi="+dpi+" dark="+dark+" PASS");
            }
        }
        using(var host=new Form())using(var image=new Bitmap(160,50,PixelFormat.Format32bppPArgb))using(var g=host.CreateGraphics()) {
            int before=GetGuiResources(Process.GetCurrentProcess().Handle,0);
            for(int i=0;i<1000;i++)AlphaCopy.Draw(g,image);
            int after=GetGuiResources(Process.GetCurrentProcess().Handle,0);
            if(after>before+2)throw new Exception("Alpha painting leaked GDI objects");
            lines.Add("1000 alpha surface copies: GDI objects "+before+" -> "+after+" PASS");
        }
        lines.Add("PASS: Chinese/Latin antialiasing, opaque glyphs, alpha edges, translucent label surface; no system settings changed.");
        File.WriteAllLines(path,lines);
    }
    [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process,int flag);
    public static void Visual() {
        var backgrounds=new List<Form>();
        try {
            foreach(var screen in Screen.AllScreens) {
                var form=new Pattern { Bounds=screen.WorkingArea }; backgrounds.Add(form); form.Show();
            }
            using(var center=new Center(new Preferences(),true)) {
                center.Preview(null,false);
                center.FormClosing+=delegate(object sender,FormClosingEventArgs e) { e.Cancel=false; };
                Application.Run(center);
            }
        } finally { foreach(var form in backgrounds)form.Dispose(); }
    }
    sealed class Pattern : Form {
        public Pattern() { Text="毛玻璃测试背景（示例）"; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; StartPosition=FormStartPosition.Manual; DoubleBuffered=true; }
        protected override void OnPaint(PaintEventArgs e) {
            Color[] colors={Color.FromArgb(239,92,113),Color.FromArgb(69,135,225),Color.FromArgb(244,193,76)};
            for(int i=0;i<3;i++)using(var brush=new SolidBrush(colors[i]))e.Graphics.FillRectangle(brush,i*Width/3,0,Width/3+1,Height);
            using(var pen=new Pen(Color.White,3))for(int x=-Height;x<Width;x+=30)e.Graphics.DrawLine(pen,x,0,x+Height,Height);
            using(var font=new Font("Microsoft YaHei UI",18F))using(var brush=new SolidBrush(Color.Black))for(int y=30;y<Height;y+=120)e.Graphics.DrawString("毛玻璃验证背景 ABC 0123",font,brush,30,y);
        }
    }
}
}
