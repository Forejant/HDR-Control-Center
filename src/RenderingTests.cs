using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace HdrCenter {
public static class RenderingTests {
    static void Require(bool value,string name) { if(!value)throw new Exception("Rendering/input test failed: "+name); }
    static void Key(Control control,Keys key) {
        foreach(string method in new[]{"OnKeyDown","OnKeyUp"})control.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(control,new object[]{new KeyEventArgs(key)});
    }
    public static void Run() {
        using(var button=new ModernButton { Text="Windows HDR 设置",AutoSize=false,Size=new Size(180,36) })
        using(var check=new ModernCheckBox { Text="登录 Windows 时启动",AutoSize=false,Size=new Size(200,32) }) {
            int clicks=0,changes=0; button.Click+=delegate { clicks++; }; check.CheckedChanged+=delegate { changes++; };
            using(var host=new Form { ShowInTaskbar=false }) {
                host.Controls.Add(button); host.Controls.Add(check); check.Location=new Point(0,50); host.Show(); Application.DoEvents();
                Require(button.Focus() && button.Focused,"Custom button accepts keyboard focus");
                Require(check.Focus() && check.Focused,"Custom checkbox accepts keyboard focus");
                host.Controls.Remove(button); host.Controls.Remove(check); host.Close();
            }
            button.AccessibilityObject.DoDefaultAction(); Key(button,Keys.Space); Key(button,Keys.Enter);
            Require(clicks==3,"Button accessibility, Space and Enter activate once");
            button.Enabled=false; button.AccessibilityObject.DoDefaultAction(); Key(button,Keys.Space); Require(clicks==3,"Disabled button cannot activate"); button.Enabled=true;
            check.AccessibilityObject.DoDefaultAction(); Require(check.Checked && changes==1 && (check.AccessibilityObject.State & AccessibleStates.Checked)!=0,"Checkbox action updates checked state and accessibility");
            Key(check,Keys.Space); Require(!check.Checked && changes==2,"Checkbox Space changes state once");
            check.Enabled=false; check.AccessibilityObject.DoDefaultAction(); Key(check,Keys.Space); Require(!check.Checked && changes==2,"Disabled checkbox cannot change"); check.Enabled=true;
            foreach(bool dark in new[]{false,true,false}) {
                var theme=UiTheme.Create(dark); button.Theme=theme; check.Theme=theme; button.BackColor=check.BackColor=theme.Surface;
                Paint(button,theme.Surface,true); Paint(check,theme.Surface,true);
                button.IsSwitch=true; button.SwitchValue=true; Paint(button,theme.Surface,false);
                button.SwitchValue=false; Paint(button,theme.Surface,false);
                button.IsSwitch=false;
            }
        }
    }
    static void Paint(Control control,Color background,bool requireText) {
        using(var bitmap=new Bitmap(control.Width,control.Height))using(var graphics=Graphics.FromImage(bitmap)) {
            graphics.Clear(Color.Magenta); var args=new PaintEventArgs(graphics,control.ClientRectangle);
            control.GetType().GetMethod("OnPaintBackground",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(control,new object[]{args});
            control.GetType().GetMethod("OnPaint",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(control,new object[]{args});
            Require(bitmap.GetPixel(0,0).ToArgb()==background.ToArgb() && bitmap.GetPixel(bitmap.Width-1,bitmap.Height-1).ToArgb()==background.ToArgb(),"Paint clears corners without ancestor/native remnants");
            int ink=0; for(int y=5;y<bitmap.Height-5;y++)for(int x=40;x<bitmap.Width-10;x++) {
                var pixel=bitmap.GetPixel(x,y); Require(pixel.ToArgb()!=Color.Magenta.ToArgb(),"Paint removes old buffer pixels");
                if(pixel.ToArgb()!=background.ToArgb())ink++;
            }
            if(requireText)Require(ink>20,"Labels stay visible in both themes");
        }
    }
}
}
