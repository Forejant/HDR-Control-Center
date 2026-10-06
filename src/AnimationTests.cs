using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace HdrCenter {
public static class AnimationTests {
    static void Check(bool value,string text) { if(!value)throw new Exception("Switch motion: "+text); }
    public static void PolicyTests() {
        var motion=new SwitchMotion(); motion.SetValue(false,0,true);
        motion.SetLoading(true,20,true); motion.SetValue(true,100,true); motion.Step(470);
        Check(motion.Position==0 && motion.Loading && !motion.Moving && motion.Angle==180,"Loading freezes the old thumb while the confirmed value changes");
        motion.SetLoading(false,500,true); motion.Step(560);
        Check(!motion.Loading && motion.Moving && motion.Position>0 && motion.Position<1,"Spinner stops before thumb movement");
        double middle=motion.Position; motion.Step(650); Check(motion.Position>middle,"Movement is continuous");
        motion.Step(720); Check(motion.Position==1 && !motion.NeedsFrames,"Movement settles and idle stops requesting frames");
        motion.SetLoading(true,800,true); motion.SetLoading(false,900,true);
        Check(motion.Position==1 && !motion.Moving,"Failure/no new confirmed value keeps the old position");
        motion.SetValue(false,1000,true); motion.Step(1060); middle=motion.Position;
        motion.SetValue(true,1060,true); Check(Math.Abs(motion.Position-middle)<.00001,"Reversal starts from current position");
        motion.Step(1280); Check(motion.Position==1 && !motion.Moving,"Reversal settles");
        motion.SetValue(false,1300,false); Check(motion.Position==0 && !motion.Moving,"Reduced animation snaps to confirmed state");
        var unknown=new SwitchMotion(); unknown.SetLoading(true,0,true); unknown.SetValue(true,100,true);
        Check(unknown.Position==.5,"Unknown state loads in the middle without inventing a state");
        unknown.SetLoading(false,200,true); unknown.Step(300);
        Check(unknown.Position>.5 && unknown.Position<1,"Unknown state transitions after confirmation");
    }
}
public sealed partial class Center {
    static void Pump(int milliseconds) { var watch=Stopwatch.StartNew(); while(watch.ElapsedMilliseconds<milliseconds) { Application.DoEvents(); Thread.Sleep(5); } }
    public void AnimationUiTest(string path) {
        AnimationTests.PolicyTests(); Preview(null,false); geometryTimer.Stop(); var lines=new List<string>();
        foreach(var screen in Screen.AllScreens)foreach(bool dark in new[]{false,true}) {
            Location=new Point(screen.WorkingArea.Left+30,screen.WorkingArea.Top+30); Pump(120);
            ApplyTheme(UiTheme.Create(dark)); ReflowWindow(screen.WorkingArea,false); geometryTimer.Stop(); Pump(260);
            var before=Bounds; var selector=(ModernComboBox)displays; var selectorBounds=selector.Bounds; int paints=selector.PaintTransactions;
            bool old=snapshot.Video.Super.Enabled.Value; double oldPosition=superButton.Motion.Position;
            superButton.AccessibilityObject.DoDefaultAction();
            RequireLayout(superButton.IsLoading && !superButton.Enabled && superButton.Motion.Position==oldPosition,"Spinner begins in old thumb");
            Pump(60); int spinPaints=selector.PaintTransactions;
            float angle=superButton.Motion.Angle; Pump(150);
            RequireLayout(superButton.IsLoading && superButton.Motion.Angle!=angle && superButton.Motion.Position==oldPosition,"Spinner advances while thumb stays put");
            RequireLayout(selector.PaintTransactions==spinPaints,"Spinner frames do not repaint display selector");
            superButton.AccessibilityObject.DoDefaultAction(); WaitForAction(5000);
            RequireLayout(!superButton.IsLoading && snapshot.Video.Super.Enabled==!old && superButton.Motion.Moving,"Confirmed completion stops spinner then starts movement");
            Application.DoEvents(); int movementPaints=selector.PaintTransactions;
            int frames=0; var movement=Stopwatch.StartNew();
            while(superButton.Motion.Moving && movement.ElapsedMilliseconds<1500) {
                Pump(16); if(superButton.Motion.Position>0 && superButton.Motion.Position<1)frames++;
            }
            RequireLayout(frames>=2 && superButton.Motion.Position==(!old?1:0) && !superButton.Motion.NeedsFrames && superButton.Enabled,"Interpolated frames settle with idle timer stopped");
            RequireLayout(Bounds==before && selector.Bounds==selectorBounds && selector.PaintTransactions==movementPaints,"Animation stability: window "+before+" -> "+Bounds+" selector "+selectorBounds+" -> "+selector.Bounds+" movement selector paints="+(selector.PaintTransactions-movementPaints));
            double confirmed=superButton.Motion.Position;
            Act(()=> { Thread.Sleep(150); throw new Exception("Expected animation test failure"); },feedback:superButton);
            WaitForAction(); Pump(250);
            RequireLayout(!superButton.IsLoading && superButton.Motion.Position==confirmed && !superButton.Motion.Moving && superButton.Enabled,"Failure clears spinner and retains confirmed thumb");
            Act(()=> { Thread.Sleep(180); return "Windows HDR 设置已打开"; },opening:true);
            RequireLayout(message.Text.Contains("正在打开") && !message.Text.Contains("正在应用"),"Windows opening status"); WaitForAction();
            Act(()=> { Thread.Sleep(180); return "NVIDIA 视频设置已打开"; },opening:true);
            RequireLayout(message.Text.Contains("正在打开") && !message.Text.Contains("正在应用"),"NVIDIA opening status"); WaitForAction();
            lines.Add(screen.DeviceName+" dpi="+DeviceDpi+" dark="+dark+" glass="+theme.Glass+" interpolated frames="+frames+" loading/failure/opening=PASS animation-frame selector paints=0 state-change/opening selector paints="+(selector.PaintTransactions-paints));
        }
        var plain=UiTheme.Create(false); plain.Transparency=false; ApplyTheme(plain); Pump(100);
        RequireLayout(!theme.Glass && BackColor==plain.Background,"Transparency-disabled fallback remains opaque");
        var contrast=UiTheme.Create(false); contrast.HighContrast=true; ApplyTheme(contrast); Pump(100);
        RequireLayout(!theme.Glass && BackColor==contrast.Background,"High-contrast fallback remains opaque");
        RequireLayout(Opacity==1 && TransparencyKey.IsEmpty && (GetWindowLongPtr(Handle,-20).ToInt64() & 0x80000)==0,"Backdrop does not use whole-window opacity, colour-key holes or a layered HWND");
        lines.Add("PASS: spinner inside old thumb, delayed confirmed movement, duplicate click blocked, failure recovery, theme/DPI cycles, selector/window stability, opening copy, opaque accessibility fallback and no layered window. Preview fixtures only; no display or startup settings changed.");
        File.WriteAllLines(path,lines); exiting=true; Close();
    }
}
}
