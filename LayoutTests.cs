using System;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Linq;
using System.Windows.Forms;
class LayoutTests {
    class PassiveForm : Form {
        public PassiveForm(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;}
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.Style|=unchecked((int)0x80000000)|0x00C40000;p.ExStyle|=0x08000080;return p;}}
        protected override void WndProc(ref Message m) {if(m.Msg==0x21){m.Result=new IntPtr(3);return;}if(m.Msg==0x83&&m.WParam!=IntPtr.Zero){m.Result=IntPtr.Zero;return;}if(m.Msg==0x84){m.Result=new IntPtr(1);return;}base.WndProc(ref m);}
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);BackColor=Native.PopupChrome(Handle,Theme.Transparency)?Color.Black:Theme.Bg;
            Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x37);
        }
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
            Check(flyout.Height==322,"empty flyout has readable spacing at 175% DPI");
            owner.Data.tasks=new[]{new TaskItem{id="a"}};
            flyout.RefreshData();
            Check(flyout.Height==455,"active flyout retains room for larger task details at 175% DPI");
            owner.Data=View.Offline();owner.DpiScale=1;
            flyout.RefreshData();
            Check(flyout.Height==184,"empty flyout is compact at 100% DPI");
            owner.Data.recentTasks=new[]{new TaskItem{id="recent-a",title="最近任务 A",state="completed"},new TaskItem{id="recent-b",title="最近任务 B",state="completed"},new TaskItem{id="recent-c",title="最近任务 C",state="completed"}};
            flyout.RefreshData();flyout.Show();Application.DoEvents();
            var historyList=flyout.Controls.OfType<Panel>().Single();
            Check(historyList.Controls.OfType<TaskRow>().Select(row=>row.Item.id).SequenceEqual(new[]{"recent-a","recent-b","recent-c"}),"idle popup shows the three recent task links");
            Check(!historyList.Controls.OfType<Label>().Any(label=>label.Text.StartsWith("暂无")&&label.Visible),"history replaces the empty state");
            owner.Data.recentTasks=new TaskItem[0];
            owner.Data.tasks=Enumerable.Range(0,12).Select(i=>new TaskItem{id=i.ToString(),title="任务 "+i}).ToArray();
            flyout.RefreshData();flyout.Show();Application.DoEvents();
            var list=flyout.Controls.OfType<Panel>().Single();
            Check(flyout.Height<=Screen.FromControl(owner).WorkingArea.Height*.7,"many tasks stay within the screen height cap");
            Check(list.VerticalScroll.Visible,"many tasks can scroll");
            Check(!list.HorizontalScroll.Visible,"showing a long list never adds a horizontal scrollbar; client="+list.ClientSize+" display="+list.DisplayRectangle+" rows="+String.Join(",",list.Controls.OfType<TaskRow>().Select(row=>row.Bounds.ToString())));
            Check(list.Controls.OfType<TaskRow>().All(row=>row.Right<=list.ClientSize.Width),"cards leave room for the scrollbar");
            list.AutoScrollPosition=new Point(0,100);flyout.RefreshData();
            Check(list.AutoScrollPosition.Y==-100,"live updates preserve the task list scroll position");
            owner.Data.tasks=owner.Data.tasks.Concat(new[]{new TaskItem{id="added",title="新增任务"}}).ToArray();
            flyout.RefreshData();
            Check(list.AutoScrollPosition.Y==-100,"adding a task preserves the task list scroll position");
            Check(list.Controls.OfType<TaskRow>().All(row=>row.Right<=list.ClientSize.Width),"added cards leave room for the scrollbar");
            Check(!list.HorizontalScroll.Visible,"added cards never need horizontal scrolling");
            owner.Data.tasks=owner.Data.tasks.Take(owner.Data.tasks.Length-1).ToArray();
            flyout.RefreshData();
            Check(list.AutoScrollPosition.Y==-100,"removing a task preserves the task list scroll position");
            Check(list.Controls.OfType<TaskRow>().All(row=>row.Right<=list.ClientSize.Width),"remaining cards leave room for the scrollbar");
            Check(!list.HorizontalScroll.Visible,"remaining cards never need horizontal scrolling");
            owner.Data.recentTasks=new[]{new TaskItem{id="recent-a",title="最近任务 A",state="completed"}};
            flyout.RefreshData();Application.DoEvents();
            Check(list.Controls.OfType<TaskRow>().Last().Item.id=="recent-a","active tasks stay above history");
            Check(!list.HorizontalScroll.Visible,"mixed active and recent tasks never need horizontal scrolling");
            Check(list.Controls.OfType<Label>().Single(label=>label.Text=="最近任务").Right<=list.ClientSize.Width,"history heading leaves room for the scrollbar");
            Check(list.AutoScrollPosition.Y==-100,"history updates preserve the task list scroll position");
            owner.Data.tasks=new TaskItem[0];
            owner.Data.recentTasks=new[]{new TaskItem{id="recent-a",state="completed"},new TaskItem{id="recent-b",state="completed"},new TaskItem{id="recent-c",state="completed"}};
            owner.DpiScale=1.75f;flyout.RefreshData();Application.DoEvents();
            Check(list.Controls.OfType<TaskRow>().All(row=>row.Height==217),"history cards use readable dimensions at 175% DPI");
            owner.DpiScale=1;flyout.RefreshData();Application.DoEvents();
            Check(!list.HorizontalScroll.Visible&&!list.VerticalScroll.Visible,"shrinking to three recent tasks removes scrollbars");
            Check(list.AutoScrollPosition==Point.Empty,"shrinking history clears the old scroll offset");
        }
        Console.WriteLine("PASS: native taskbar ownership and recovery, layout, 100% / 175% DPI, collision and popup bounds");
    }
}
