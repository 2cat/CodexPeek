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
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr h,int index);
    static void CheckNoScrollbars(Panel list) {
        Point edge=list.PointToScreen(new Point(list.Width-1,list.Height/2));
        int hit=SendMessage(list.Handle,0x84,IntPtr.Zero,new IntPtr((edge.Y<<16)|(edge.X&0xFFFF))).ToInt32();
        Check(list.ClientSize==list.Size&&hit!=6&&hit!=7,"task list keeps its full client area with no scrollbar at the right edge; hit="+hit+" offset="+list.AutoScrollPosition+" client="+list.ClientSize+" size="+list.Size);
    }
    static void DockingCheck() {
        using(var fixture=new Process())using(var widget=new PassiveForm()) {
            fixture.StartInfo=new ProcessStartInfo(Application.ExecutablePath,"--owner-fixture"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardInput=true};
            fixture.Start();
            try {
                var work=Screen.PrimaryScreen.WorkingArea;
                widget.Bounds=new Rectangle(work.Right-340,work.Top+10,320,40);
                Exception failure=null;
                widget.Shown+=(s,e)=>widget.BeginInvoke((Action)(()=>{
                    try {
                        IntPtr bar=new IntPtr(Int64.Parse(fixture.StandardOutput.ReadLine()));
                        widget.Bounds=Native.Rect(bar);Application.DoEvents();
                        Native.AboveTaskbar(widget.Handle,bar);
                        Native.SetWindowPos(bar,new IntPtr(-1),0,0,0,0,0x13);
                        Application.DoEvents();
                        var point=new POINT{X=widget.Left+20,Y=widget.Top+20};
                        IntPtr hit=GetAncestor(WindowFromPoint(point),2);
                        Check(hit==widget.Handle,"raising the taskbar never covers its entry; hit="+hit+" widget="+widget.Handle+" owner="+Native.GetWindow(widget.Handle,4)+" bar="+bar+" bounds="+widget.Bounds+" barBounds="+Native.Rect(bar));
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
                    }catch(Exception ex){failure=ex;}
                    finally{widget.Close();}
                }));
                Application.Run(widget);
                if(failure!=null)throw failure;
            }finally {if(!fixture.HasExited){fixture.Kill();fixture.WaitForExit();}}
        }
    }
    static void Check(bool value, string message) { if (!value) { Console.Error.WriteLine("FAIL: "+message); throw new Exception(message); } }
    // Public window operations need no Explorer; --posture also waits for real taskbar recovery. System posture is unchanged.
    static void PostureCheck(bool freshMeasurement) {
        var entryView=View.Offline();entryView.tone="red";
        using(var owner=new Peek(false,entryView))using(var poll=new System.Windows.Forms.Timer()) {
            Exception failure=null;var watch=Stopwatch.StartNew();int stage=0;
            poll.Interval=50;poll.Tick+=(s,e)=>{
                try {
                    Check(watch.ElapsedMilliseconds<10000,"entry appears after a fresh taskbar measurement");
                    if(!owner.Visible)return;
                    if(stage==0) {
                        Check((GetWindowLong(owner.Handle,-20)&0x80000)!=0,"entry supports per-pixel transparent rendering");
                        var blank=new POINT{X=owner.Right-8,Y=owner.Top+owner.Height/2};
                        Check(GetAncestor(WindowFromPoint(blank),2)==owner.Handle,"blank background remains part of the clickable entry");
                        using(var pixel=new Bitmap(1,1))using(var g=Graphics.FromImage(pixel)) {
                            g.CopyFromScreen(owner.Left+owner.S(13),owner.Top+owner.S(12),0,0,new Size(1,1));var color=pixel.GetPixel(0,0);
                            Check(Math.Abs(color.R-245)<=2&&Math.Abs(color.G-119)<=2&&Math.Abs(color.B-126)<=2,"the composed transparent entry actually displays its status indicator; color="+color);
                        }
                        owner.Toggle();var popup=Application.OpenForms.OfType<Flyout>().Single();
                        Check(popup.Visible,"task list opens before entering tablet posture");
                        owner.UpdatePosture(true);
                        Check(!owner.Visible&&!popup.Visible,"tablet posture hides the entry and closes the task list");
                        owner.Toggle();Check(popup.Visible,"tray action can still open the task list in tablet posture");
                        owner.UpdatePosture(true);Check(popup.Visible,"repeated tablet notifications keep a manually opened task list available");
                        owner.Toggle();owner.UpdatePosture(false);
                        Check(!owner.Visible,"keyboard reconnection waits for a new measurement before showing the entry");
                        if(!freshMeasurement){Console.WriteLine("PASS: native tablet hide, flyout close, tray access and hidden reconnection");poll.Stop();owner.Close();return;}
                        stage=1;return;
                    }
                    IntPtr bar=Native.FindWindow("Shell_TrayWnd",null);Rectangle rect=Native.Rect(bar);
                    Check(rect.Contains(owner.Bounds),"recovered entry fits the current taskbar");
                    Console.WriteLine("PASS: native tablet hide, flyout close, tray access and fresh-measurement recovery");
                }catch(Exception ex){failure=ex;}
                poll.Stop();owner.Close();
            };
            owner.Shown+=(s,e)=>{if(!freshMeasurement){owner.UpdatePosture(false);owner.Show();}poll.Start();};Application.Run(owner);
            if(failure!=null)throw failure;
        }
    }
    // Run on an unlocked, idle desktop: this checks the composed Acrylic pixels.
    static void PopupPaintCheck() {
        using(var background=new Form())using(var owner=new Peek())using(var timer=new System.Windows.Forms.Timer()) {
            background.FormBorderStyle=FormBorderStyle.None;background.StartPosition=FormStartPosition.Manual;
            background.Bounds=new Rectangle(40,60,920,680);background.BackColor=Color.FromArgb(90,120,150);
            owner.Preview=true;owner.DpiScale=1.75f;owner.Data=View.Offline();owner.Data.connected=true;
            owner.Data.tasks=new[]{new TaskItem{id="paint",title="固定演示任务",state="running",detail="检查面板背景"}};
            Flyout popup=null;Exception failure=null;var watch=new Stopwatch();int min=255,max=0;
            background.Shown+=(s,e)=>background.BeginInvoke((Action)(()=>{
                try {
                    owner.Toggle();popup=Application.OpenForms.OfType<Flyout>().Single();
                    popup.Location=new Point(80,100);watch.Start();timer.Start();
                }catch(Exception ex){failure=ex;owner.Close();background.Close();}
            }));
            timer.Interval=50;timer.Tick+=(s,e)=>{
                try {
                    var point=new POINT{X=popup.Right-50,Y=popup.Top+30};
                    Check(popup.Visible&&GetAncestor(WindowFromPoint(point),2)==popup.Handle,"popup remains visible over the fixed backdrop");
                    if(watch.ElapsedMilliseconds>400)using(var pixel=new Bitmap(1,1))using(var g=Graphics.FromImage(pixel)) {
                        g.CopyFromScreen(point.X,point.Y,0,0,new Size(1,1));int level=pixel.GetPixel(0,0).R;
                        min=Math.Min(min,level);max=Math.Max(max,level);
                    }
                    popup.Invalidate(true);
                    if(watch.ElapsedMilliseconds<3500)return;
                    Check(max-min<=3,"idle popup background is stable after opening; range="+min+".."+max);
                    owner.Toggle();Check(!popup.Visible,"clicking the entry again closes the popup");
                }catch(Exception ex){failure=ex;}
                timer.Stop();owner.Close();background.Close();
            };
            Application.Run(background);if(failure!=null)throw failure;
            Console.WriteLine("PASS: public task-list toggle, stable composed background and close; range="+min+".."+max);
        }
    }
    [STAThread]
    static void Main(string[] args) {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        if(Array.IndexOf(args,"--posture")>=0) {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            PostureCheck(true);return;
        }
        if(Array.IndexOf(args,"--popup-paint")>=0) {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            PopupPaintCheck();return;
        }
        if(Array.IndexOf(args,"--owner-fixture")>=0) {
            using(var form=new PassiveForm()) {
                var work=Screen.PrimaryScreen.WorkingArea;form.Bounds=new Rectangle(work.Right-340,work.Top+10,320,40);
                form.Shown+=(s,e)=>form.BeginInvoke((Action)(()=>{Console.WriteLine(form.Handle.ToInt64());Console.Out.Flush();ThreadPool.QueueUserWorkItem(_=>{Console.ReadLine();form.BeginInvoke((Action)(()=>form.Close()));});}));
                Application.Run(form);
            }
            return;
        }
        Rectangle bar = new Rectangle(0,1516,2560,84);
        Check(Native.IsTabletMode(0,197,true,0),"integrated touch and a supported rotation sensor enable slate posture");
        Check(!Native.IsTabletMode(1,197,true,128),"attached keyboard keeps the taskbar entry");
        Check(!Native.IsTabletMode(0,0,true,0),"ordinary desktop zero slate metric does not hide the entry");
        Check(!Native.IsTabletMode(0,197,true,0x10)&&!Native.IsTabletMode(0,197,true,0x20)&&!Native.IsTabletMode(0,197,false,0),"touch alone or an unavailable rotation sensor does not enable slate detection");
        Check(Native.IsTabletMode(0,197,true,1|8|64),"rotation lock, multiple monitors and docking do not disable supported posture detection");
        Rectangle rect = Placement.Widget(bar, 168, 840);
        Check(rect.Width==560 && rect.Height==70, "wider widget at 175% DPI");
        Check(rect.Top>=bar.Top && rect.Bottom<=bar.Bottom, "inside existing taskbar");
        Check(rect.Right<840, "never covers pinned applications");
        Check(Placement.Widget(bar,168,200).IsEmpty,"insufficient space uses tray");
        Check(Placement.Widget(new Rectangle(0,1032,1920,48),96,600).Size==new Size(320,40),"100% DPI dimensions");
        Check(Placement.Widget(bar,168,500).Right==486,"shrinks before the first taskbar button");
        Check(Placement.Widget(bar,168,null,rect,true)==new Rectangle(14,1523,560,70),"opening Start with unavailable taskbar buttons preserves the last measured area");
        Check(Placement.Widget(bar,168,null,Rectangle.Empty,true).IsEmpty,"unavailable buttons at startup do not invent space");
        Check(Placement.Widget(bar,168,null,rect,false).IsEmpty,"a changed taskbar handle, bounds or DPI invalidates the old area");
        Rectangle occupied=Placement.Widget(bar,168,200,rect,true);
        Check(occupied.IsEmpty && Placement.Widget(bar,168,null,occupied,true).IsEmpty,"known insufficient space stays cleared through an unavailable read");
        Check(Placement.Widget(bar,168,500,rect,true)==new Rectangle(14,1523,472,70),"recovered button data replaces the previous placement");
        DockingCheck();
        PostureCheck(false);
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
            owner.DpiScale=1.75f;
            owner.Data.tasks=new[]{new TaskItem{id="active",title="正在运行的任务",state="running"}};
            flyout.RefreshData();Application.DoEvents();
            int availableHeight=Screen.FromControl(owner).WorkingArea.Height-28;
            Check(flyout.Height==Math.Min(1211,availableHeight),"one active and three recent tasks expand to their full height within the screen");
            if(availableHeight>=1211)Check(historyList.Controls.OfType<TaskRow>().All(row=>row.Top>=0&&row.Bottom<=historyList.ClientSize.Height),"all four task cards are fully visible without scrolling");
            CheckNoScrollbars(historyList);
            owner.DpiScale=1;
            owner.Data.recentTasks=new TaskItem[0];
            owner.Data.tasks=Enumerable.Range(0,12).Select(i=>new TaskItem{id=i.ToString(),title="任务 "+i}).ToArray();
            flyout.RefreshData();flyout.Show();Application.DoEvents();
            var list=flyout.Controls.OfType<Panel>().Single();
            Check(flyout.Height<=Screen.FromControl(owner).WorkingArea.Height-16,"many tasks stay within the screen with margins");
            Check(list.VerticalScroll.Visible,"many tasks can scroll");
            CheckNoScrollbars(list);
            Check(!list.HorizontalScroll.Visible,"showing a long list never adds a horizontal scrollbar; client="+list.ClientSize+" display="+list.DisplayRectangle+" rows="+String.Join(",",list.Controls.OfType<TaskRow>().Select(row=>row.Bounds.ToString())));
            Check(list.Controls.OfType<TaskRow>().All(row=>row.Right<=list.ClientSize.Width),"cards fit the available width");
            list.AutoScrollPosition=new Point(0,100);flyout.RefreshData();
            Check(list.AutoScrollPosition.Y==-100,"live updates preserve the task list scroll position");
            CheckNoScrollbars(list);
            owner.Data.tasks=owner.Data.tasks.Concat(new[]{new TaskItem{id="added",title="新增任务"}}).ToArray();
            flyout.RefreshData();
            Check(list.AutoScrollPosition.Y==-100,"adding a task preserves the task list scroll position");
            Check(list.Controls.OfType<TaskRow>().All(row=>row.Right<=list.ClientSize.Width),"added cards fit the available width");
            CheckNoScrollbars(list);
            Check(!list.HorizontalScroll.Visible,"added cards never need horizontal scrolling");
            owner.Data.tasks=owner.Data.tasks.Take(owner.Data.tasks.Length-1).ToArray();
            flyout.RefreshData();
            Check(list.AutoScrollPosition.Y==-100,"removing a task preserves the task list scroll position");
            Check(list.Controls.OfType<TaskRow>().All(row=>row.Right<=list.ClientSize.Width),"remaining cards fit the available width");
            CheckNoScrollbars(list);
            Check(!list.HorizontalScroll.Visible,"remaining cards never need horizontal scrolling");
            owner.Data.recentTasks=new[]{new TaskItem{id="recent-a",title="最近任务 A",state="completed"}};
            flyout.RefreshData();Application.DoEvents();
            Check(list.Controls.OfType<TaskRow>().Last().Item.id=="recent-a","active tasks stay above history");
            Check(!list.HorizontalScroll.Visible,"mixed active and recent tasks never need horizontal scrolling");
            Check(list.Controls.OfType<Label>().Single(label=>label.Text=="最近任务").Right<=list.ClientSize.Width,"history heading fits the available width");
            Check(list.AutoScrollPosition.Y==-100,"history updates preserve the task list scroll position");
            SendMessage(list.Handle,0x20A,new IntPtr(unchecked((int)0xFF880000)),IntPtr.Zero);Application.DoEvents();
            Check(list.AutoScrollPosition.Y<-100,"mouse wheel reaches overflow tasks without a scrollbar");
            CheckNoScrollbars(list);
            var last=list.Controls.OfType<TaskRow>().Last();last.Select();Application.DoEvents();
            Check(last.Focused&&last.Top>=0&&last.Bottom<=list.ClientSize.Height,"keyboard focus brings the final task fully into view");
            CheckNoScrollbars(list);
            owner.Data.tasks=new TaskItem[0];
            owner.Data.recentTasks=new[]{new TaskItem{id="recent-a",state="completed"},new TaskItem{id="recent-b",state="completed"},new TaskItem{id="recent-c",state="completed"}};
            owner.DpiScale=1.75f;flyout.RefreshData();Application.DoEvents();
            Check(list.Controls.OfType<TaskRow>().All(row=>row.Height==217),"history cards use readable dimensions at 175% DPI");
            if(Screen.FromControl(owner).WorkingArea.Height>=945)Check(list.Controls.OfType<TaskRow>().All(row=>row.Top>=0&&row.Bottom<=list.ClientSize.Height),"all three recent tasks fit at 175% DPI");
            CheckNoScrollbars(list);
            owner.DpiScale=1;flyout.RefreshData();Application.DoEvents();
            Check(!list.HorizontalScroll.Visible,"shrinking history never adds a horizontal scrollbar");
            // A genuinely small work area can still overflow; shrinking clears its offset.
            owner.Data.recentTasks=owner.Data.recentTasks.Take(1).ToArray();flyout.RefreshData();Application.DoEvents();
            Check(!list.HorizontalScroll.Visible&&!list.VerticalScroll.Visible,"shrinking to a recent task that fits removes scrollbars");
            Check(list.AutoScrollPosition==Point.Empty,"shrinking history clears the old scroll offset");
        }
        Console.WriteLine("PASS: native taskbar ownership and recovery, layout, 100% / 175% DPI, collision and popup bounds");
    }
}
