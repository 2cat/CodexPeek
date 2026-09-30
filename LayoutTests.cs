using System;
using System.Drawing;
class LayoutTests {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main() {
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
        Console.WriteLine("PASS: native layout, 100% / 175% DPI, taskbar collision and popup bounds");
    }
}
