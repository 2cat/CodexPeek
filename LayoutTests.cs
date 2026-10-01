using System;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
class LayoutTests {
    class PassiveForm : Form {
        public PassiveForm(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;}
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.Style|=unchecked((int)0x80000000);p.ExStyle|=0x08000080;return p;}}
    }
    [StructLayout(LayoutKind.Sequential)]struct POINT {public int X,Y;}
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr h,uint flags);
    static void DockingCheck() {
        using(var fixture=new Process())using(var widget=new PassiveForm()) {
            fixture.StartInfo=new ProcessStartInfo(Application.ExecutablePath,"--owner-fixture"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardInput=true};
            fixture.Start();
            try {
                IntPtr bar=new IntPtr(Int64.Parse(fixture.StandardOutput.ReadLine()));
                widget.Bounds=Native.Rect(bar);widget.Show();Application.DoEvents();
                Native.AboveTaskbar(widget.Handle,bar);
                Native.SetWindowPos(bar,new IntPtr(-1),0,0,0,0,0x13);
                Application.DoEvents();
                var point=new POINT{X=widget.Left+20,Y=widget.Top+20};
                Check(GetAncestor(WindowFromPoint(point),2)==widget.Handle,"raising the taskbar never covers its entry");
                fixture.StandardInput.WriteLine("close");fixture.StandardInput.Flush();
                Check(fixture.WaitForExit(5000),"owner fixture exits");Application.DoEvents();
                Check(!widget.IsDisposed && widget.Visible,"entry survives loss of its external taskbar owner");
                using(var replacement=new PassiveForm()) {
                    replacement.Bounds=widget.Bounds;replacement.Show();
                    Native.AboveTaskbar(widget.Handle,replacement.Handle);
                    Native.SetWindowPos(replacement.Handle,new IntPtr(-1),0,0,0,0,0x13);
                    Application.DoEvents();
                    Check(GetAncestor(WindowFromPoint(point),2)==widget.Handle,"entry reattaches to the replacement taskbar");
                    Native.AboveTaskbar(widget.Handle,IntPtr.Zero);
                }
            }finally {if(!fixture.HasExited){fixture.Kill();fixture.WaitForExit();}}
        }
    }
    static void Check(bool value, string message) { if (!value) { Console.Error.WriteLine("FAIL: "+message); throw new Exception(message); } }
    [STAThread]
    static void Main(string[] args) {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        if(Array.IndexOf(args,"--owner-fixture")>=0) {
            using(var form=new PassiveForm()) {
                var work=Screen.PrimaryScreen.WorkingArea;form.Bounds=new Rectangle(work.Right-340,work.Top+10,320,40);
                form.Shown+=(s,e)=>form.BeginInvoke((Action)(()=>{Console.WriteLine(form.Handle.ToInt64());Console.Out.Flush();ThreadPool.QueueUserWorkItem(_=>{Console.ReadLine();form.BeginInvoke((Action)(()=>form.Close()));});}));
                Application.Run(form);
            }
            return;
        }
        DockingCheck();
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
        Console.WriteLine("PASS: native taskbar ownership and recovery, layout, 100% / 175% DPI, collision and popup bounds");
    }
}
