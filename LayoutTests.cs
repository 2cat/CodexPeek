using System;
using System.Drawing;
class LayoutTests {
    static void Check(bool value, string message) { if (!value) { Console.Error.WriteLine("FAIL: "+message); throw new Exception(message); } }
    [STAThread]
    static void Main() {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        Rectangle bar = new Rectangle(0,1516,2560,84);
        Rectangle rect = Placement.Widget(bar, 168, 840);
        Check(rect.Width==560 && rect.Height==70, "wider widget at 175% DPI");
        Check(rect.Top>=bar.Top && rect.Bottom<=bar.Bottom, "inside existing taskbar");
        Check(rect.Right<840, "never covers pinned applications");
        Check(Placement.Widget(bar,168,200).IsEmpty,"insufficient space uses tray");
        Check(Placement.Widget(new Rectangle(0,1032,1920,48),96,600).Size==new Size(320,40),"100% DPI dimensions");
        Check(Placement.Widget(bar,168,500).Right==486,"shrinks before the first taskbar button");
        Rectangle popup=Placement.Popup(new Rectangle(2500,1516,60,70),new Rectangle(0,0,2560,1516),735,800,14);
        Check(popup.Right<=2560 && popup.Bottom<=1516 && popup.Left>=0,"flyout stays on screen");
        using(var owner=new Peek()) using(var flyout=new Flyout(owner)) {
            owner.Data=View.Offline();owner.DpiScale=1.75f;
            flyout.RefreshData();
            Check(flyout.Height==280,"empty flyout is compact at 175% DPI");
            owner.Data.tasks=new[]{new TaskItem{id="a"}};
            flyout.RefreshData();
            Check(flyout.Height==402,"active flyout retains room for task details at 175% DPI");
            owner.Data=View.Offline();owner.DpiScale=1;
            flyout.RefreshData();
            Check(flyout.Height==160,"empty flyout is compact at 100% DPI");
        }
        Console.WriteLine("PASS: native layout, 100% / 175% DPI, taskbar collision and popup bounds");
    }
}
