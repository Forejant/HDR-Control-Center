using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace HdrCenter {
public static class IconArtwork {
    public static Bitmap Bitmap(int size,bool tray,bool lightInk) {
        var image=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(image)) {
            g.Clear(Color.Transparent); g.SmoothingMode=SmoothingMode.AntiAlias; g.ScaleTransform(size/32F,size/32F);
            Color ink=tray?(lightInk?Color.FromArgb(235,255,255,255):Color.FromArgb(230,25,25,25)):Color.FromArgb(0,108,211);
            using(var pen=new Pen(ink,tray?2F:2.1F))using(var shape=Shapes.Round(new RectangleF(3.5F,5,25,19),3)) {
                pen.StartCap=pen.EndCap=LineCap.Round; pen.LineJoin=LineJoin.Round; g.DrawPath(pen,shape);
                g.DrawLine(pen,16,24,16,28); g.DrawLine(pen,10.5F,28,21.5F,28);
                g.DrawLine(pen,11,10,11,19); g.DrawLine(pen,21,10,21,19);
            }
            using(var fill=new SolidBrush(ink)) { g.FillEllipse(fill,8.5F,11,5,5); g.FillEllipse(fill,18.5F,14,5,5); }
        }
        return image;
    }
    public static Icon Tray(bool darkTaskbar) {
        using(var bitmap=Bitmap(32,true,darkTaskbar)) {
            IntPtr handle=bitmap.GetHicon(); try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { DestroyIcon(handle); }
        }
    }
    public static Icon ApplicationIcon() {
        using(var bitmap=Bitmap(48,false,false)) { IntPtr handle=bitmap.GetHicon(); try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { DestroyIcon(handle); } }
    }
    public static void Export(string folder) {
        Directory.CreateDirectory(folder);
        using(var image=Bitmap(128,false,false))image.Save(Path.Combine(folder,"control-center.png"),ImageFormat.Png);
        using(var image=Bitmap(64,true,true))image.Save(Path.Combine(folder,"tray-dark.png"),ImageFormat.Png);
        using(var image=Bitmap(64,true,false))image.Save(Path.Combine(folder,"tray-light.png"),ImageFormat.Png);
        int[] sizes={16,20,24,32,48,64}; var frames=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++)using(var image=Bitmap(sizes[i],false,false))frames[i]=Dib(image);
        using(var writer=new BinaryWriter(File.Create(Path.Combine(folder,"ControlCenter.ico")))) {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length); int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++) { writer.Write((byte)sizes[i]); writer.Write((byte)sizes[i]); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(frames[i].Length); writer.Write(offset); offset+=frames[i].Length; }
            foreach(var frame in frames)writer.Write(frame);
        }
    }
    static byte[] Dib(Bitmap image) {
        using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)) {
            int size=image.Width,maskStride=((size+31)/32)*4;
            writer.Write(40); writer.Write(size); writer.Write(size*2); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(size*size*4); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for(int y=size-1;y>=0;y--)for(int x=0;x<size;x++) { Color c=image.GetPixel(x,y); writer.Write(c.B); writer.Write(c.G); writer.Write(c.R); writer.Write(c.A); }
            for(int y=size-1;y>=0;y--) { var mask=new byte[maskStride]; for(int x=0;x<size;x++)if(image.GetPixel(x,y).A==0)mask[x/8]|=(byte)(0x80>>(x%8)); writer.Write(mask); }
            writer.Flush(); return stream.ToArray();
        }
    }
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
}
}
