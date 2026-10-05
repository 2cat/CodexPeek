using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;

class TaskItem {
    public string id {get;set;} public string title {get;set;} public string state {get;set;}
    public string detail {get;set;} public string progress {get;set;} public double? startedAt {get;set;} public double? waitingSince {get;set;}
}
class Limit {public double remaining {get;set;} public string label {get;set;} public double? resetsAt {get;set;}}
class View {
    public string headline {get;set;} public string quotaText {get;set;} public string tone {get;set;}
    public int running {get;set;} public int waiting {get;set;} public bool connected {get;set;}
    public double? updatedAt {get;set;} public string diagnostic {get;set;}
    public TaskItem[] tasks {get;set;} public TaskItem[] recentTasks {get;set;} public Limit[] windows {get;set;}
    public static View Offline() {return new View {headline="状态未同步",quotaText="额度暂不可用",tone="muted",tasks=new TaskItem[0],recentTasks=new TaskItem[0],windows=new Limit[0],diagnostic="正在连接 Codex 桌面端"};}
}

static class Program {
    [STAThread] static void Main(string[] args) {
        if(args.Contains("--taskbar-info")) {
            Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
            var bar=Native.FindWindow("Shell_TrayWnd",null);var root=AutomationElement.FromHandle(bar);
            var buttons=root.FindAll(TreeScope.Descendants,new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.CheckBox),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem)));
            Console.WriteLine("bar="+Native.Rect(bar)+" dpi="+Native.GetDpiForWindow(bar));
            foreach(AutomationElement button in buttons)Console.WriteLine(button.Current.ControlType.ProgrammaticName+" "+button.Current.BoundingRectangle+" offscreen="+button.Current.IsOffscreen);
            Console.WriteLine("foreground="+Native.Rect(Native.GetForegroundWindow())+" fullscreen="+Native.Fullscreen(IntPtr.Zero,IntPtr.Zero,Screen.FromHandle(bar).Bounds));
            foreach(var process in Process.GetProcessesByName("CodexPeek"))foreach(ProcessThread thread in process.Threads){var name=Native.DesktopName(thread.Id);if(name.Length>0)Console.WriteLine("app thread desktop="+name);}
            return;
        }
        bool first; using(var mutex=new Mutex(true,"Local\\CodexPeek-v1",out first)) {
            if(!first) return;
            Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            View preview=null;int at=Array.IndexOf(args,"--preview");
            if(at>=0&&at+1<args.Length)preview=new JavaScriptSerializer().Deserialize<View>(File.ReadAllText(args[at+1],Encoding.UTF8));
            Application.Run(new Peek(args.Contains("--open"),preview));
        }
    }
}

static class Theme {
    public static Color Bg=Color.FromArgb(32,32,34),Text=Color.FromArgb(242,242,242),Muted=Color.FromArgb(190,190,198),Line=Color.FromArgb(57,57,61);
    public static Color Accent(string tone) {return tone=="amber"?Color.FromArgb(235,182,82):tone=="red"?Color.FromArgb(245,119,126):tone=="blue"?Color.FromArgb(113,166,247):Muted;}
    public static Font Font(float pixels,bool bold=false) {return new Font(SystemFonts.MessageBoxFont.FontFamily,pixels,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);}
    public static bool Transparency {get{using(var key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))return !SystemInformation.HighContrast && (key==null || Convert.ToInt32(key.GetValue("EnableTransparency",1))!=0);}}
    public static void TextAt(Graphics g,string text,Font font,Color color,Rectangle rect,bool wrap=false) {
        g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using(var brush=new SolidBrush(color))using(var format=new StringFormat{LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=wrap?0:StringFormatFlags.NoWrap})g.DrawString(text??"",font,brush,rect,format);
    }
    public static void Round(Form form,int radius) {
        using(var path=Rounded(new Rectangle(0,0,form.Width,form.Height),radius)) {
            var old=form.Region;form.Region=new Region(path);if(old!=null)old.Dispose();
        }
    }
    public static GraphicsPath Rounded(Rectangle rect,int radius) {
        var path=new GraphicsPath();int d=radius*2;
        path.AddArc(rect.Left,rect.Top,d,d,180,90);path.AddArc(rect.Right-d,rect.Top,d,d,270,90);path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);path.AddArc(rect.Left,rect.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
}

class MenuColors : ProfessionalColorTable {
    public override Color ToolStripDropDownBackground {get{return Theme.Bg;}}
    public override Color MenuItemSelected {get{return Color.FromArgb(47,52,61);}}
    public override Color MenuItemBorder {get{return Theme.Line;}}
    public override Color MenuBorder {get{return Theme.Line;}}
    public override Color SeparatorDark {get{return Theme.Line;}}
    public override Color SeparatorLight {get{return Theme.Line;}}
}

class Peek : Form {
    public static bool Review=Environment.GetCommandLineArgs().Contains("--review");
    public bool Preview;
    public View Data=View.Offline();
    public float DpiScale=1;
    public int S(float dip) {return (int)Math.Round(dip*DpiScale);}
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly NotifyIcon tray=new NotifyIcon(); readonly ToolTip tip=new ToolTip();
    Process backend; Flyout flyout; DateTime lastData=DateTime.MinValue,nextStart=DateTime.MinValue,nextMeasure=DateTime.MinValue;
    bool closing,locked,measuring,hover,tabletMode; Rectangle safeArea,measuredBounds; IntPtr measuredTaskbar; int measuredDpi,postureGeneration; DateTime measured=DateTime.MinValue; Icon appIcon;
    public Peek(bool openAtStart=false,View preview=null) {
        Preview=preview!=null;
        Text="Codex Peek";AccessibleName="Codex 状态栏";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=Review;TopMost=true;StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None;BackColor=Theme.Bg;DoubleBuffered=true;Cursor=Cursors.Hand;Size=new Size(320,40);
        appIcon=MakeIcon();Icon=appIcon;tray.Icon=appIcon;tray.Text="Codex Peek";tray.Visible=true;
        var menu=new ContextMenuStrip();menu.Items.Add("查看本机任务",null,(s,e)=>Toggle());menu.Items.Add("打开 Codex",null,(s,e)=>OpenTask(null));menu.Items.Add(new ToolStripSeparator());menu.Items.Add("退出 Codex Peek",null,(s,e)=>Close());
        menu.ShowImageMargin=false;menu.ForeColor=Theme.Text;menu.Renderer=new ToolStripProfessionalRenderer(new MenuColors());
        menu.Opening+=(s,e)=>{tip.Hide(this);tip.Active=false;};menu.Closed+=(s,e)=>{tip.Active=flyout==null||!flyout.Visible;};
        tray.ContextMenuStrip=menu;ContextMenuStrip=menu;tray.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left)Toggle();};
        MouseEnter+=(s,e)=>{hover=true;Invalidate();};MouseLeave+=(s,e)=>{hover=false;Invalidate();};MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Left)Toggle();};
        SystemEvents.SessionSwitch+=SessionChanged;
        timer.Interval=750;timer.Tick+=(s,e)=>Tick();Shown+=(s,e)=>{Hide();if(Preview)Apply(preview);else StartBackend();Tick();timer.Start();if(openAtStart)BeginInvoke((Action)(()=>{if(!tabletMode)Toggle();}));};
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var cp=base.CreateParams;cp.Style|=unchecked((int)0x80000000);cp.ExStyle|=0x08080000;if(!Review)cp.ExStyle|=0x80;return cp;}}
    protected override void WndProc(ref Message m) {if(m.Msg==0x1A)UpdatePosture(Native.TabletMode());if(m.Msg==0x21){m.Result=new IntPtr(3);return;}if(m.Msg==0x83&&m.WParam!=IntPtr.Zero){m.Result=IntPtr.Zero;return;}if(m.Msg==0x84){m.Result=new IntPtr(1);return;}base.WndProc(ref m);}
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x37);
    }
    static Icon MakeIcon() {
        using(var bitmap=new Bitmap(32,32)) using(var g=Graphics.FromImage(bitmap)) {
            g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);
            using(var brush=new SolidBrush(Theme.Bg))g.FillEllipse(brush,1,1,30,30);
            using(var pen=new Pen(Theme.Accent("blue"),2.3f)){g.DrawLines(pen,new[]{new Point(10,10),new Point(6,16),new Point(10,22)});g.DrawLines(pen,new[]{new Point(22,10),new Point(26,16),new Point(22,22)});g.DrawLine(pen,18,9,14,23);}
            IntPtr h=bitmap.GetHicon();var icon=(Icon)Icon.FromHandle(h).Clone();Native.DestroyIcon(h);return icon;
        }
    }
    void SessionChanged(object sender,SessionSwitchEventArgs e){locked=e.Reason==SessionSwitchReason.SessionLock;if(locked){Hide();if(flyout!=null)flyout.Hide();}}
    void StartBackend() {
        if(Preview || closing || (backend!=null&&!backend.HasExited))return;
        var node=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"nodejs","node.exe");
        if(!File.Exists(node))node="node.exe";
        try {
            var p=new Process();p.StartInfo=new ProcessStartInfo(node,"--no-warnings \""+Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"backend.mjs")+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory};
            p.OutputDataReceived+=(s,e)=>{if(e.Data==null||closing)return;try{var view=new JavaScriptSerializer().Deserialize<View>(e.Data);BeginInvoke((Action)(()=>Apply(view)));}catch{}};
            p.ErrorDataReceived+=(s,e)=>{};p.Start();backend=p;p.BeginOutputReadLine();p.BeginErrorReadLine();
        }catch {Data=View.Offline();Data.diagnostic="无法启动数据组件，请确认 Node.js 24 已安装";}
        nextStart=DateTime.UtcNow.AddSeconds(10);
    }
    void Apply(View data) {
        if(data==null || data.tasks==null)return;
        Data=data;lastData=DateTime.UtcNow;AccessibleName=data.headline+"，"+data.quotaText;
        tray.Text=("Codex · "+data.headline+"\n"+data.quotaText).Substring(0,Math.Min(63,("Codex · "+data.headline+"\n"+data.quotaText).Length));
        string tooltip=String.Join("\n",(data.windows??new Limit[0]).Select(w=>w.label+"剩余 "+Math.Floor(w.remaining)+"%"+(w.resetsAt.HasValue?" · "+Epoch(w.resetsAt.Value*1000).ToLocalTime().ToString("M月d日 HH:mm")+" 重置":"")));
        tip.SetToolTip(this,data.headline+"\n"+(tooltip.Length>0?tooltip+"\n账户共享额度 · 点击查看任务":data.diagnostic));
        Invalidate();if(flyout!=null && flyout.Visible)flyout.RefreshData();
    }
    public static DateTime Epoch(double ms){return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(ms);}
    public void UpdatePosture(bool tablet) {
        if(tabletMode==tablet)return;
        tabletMode=tablet;postureGeneration++;
        safeArea=measuredBounds=Rectangle.Empty;measuredTaskbar=IntPtr.Zero;measuredDpi=0;measured=DateTime.MinValue;nextMeasure=DateTime.MinValue;
        Hide();tip.Hide(this);hover=false;
        if(tablet&&flyout!=null)flyout.Hide();
    }
    void Tick() {
        if(!Preview && lastData!=DateTime.MinValue && (DateTime.UtcNow-lastData).TotalSeconds>15 && Data.connected){Data=View.Offline();Data.diagnostic="连接中断，正在重新同步";Invalidate();if(flyout!=null)flyout.RefreshData();}
        if((backend==null||backend.HasExited)&&DateTime.UtcNow>=nextStart)StartBackend();
        UpdatePosture(Native.TabletMode());
        if(!tabletMode&&DateTime.UtcNow>=nextMeasure&&!measuring){measuring=true;nextMeasure=DateTime.UtcNow.AddSeconds(4);int generation=postureGeneration;ThreadPool.QueueUserWorkItem(s=>Measure(generation));}
        IntPtr bar=Native.FindWindow("Shell_TrayWnd",null);
        Rectangle rect=Native.Rect(bar);Rectangle screen=Screen.FromHandle(bar).Bounds;int dpi=(int)Native.GetDpiForWindow(bar);
        bool hidden=locked||bar==IntPtr.Zero||!Native.IsWindowVisible(bar)||rect.Top>=screen.Bottom-4||rect.Bottom>screen.Bottom+4||rect.Width<rect.Height||Native.Fullscreen(Handle,flyout==null?IntPtr.Zero:flyout.Handle,screen);
        if(Environment.GetCommandLineArgs().Contains("--diagnostics"))try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostics.json"),new JavaScriptSerializer().Serialize(new{hidden=tabletMode||hidden,tabletMode=tabletMode,place=safeArea.ToString(),bar=rect.ToString(),age=(DateTime.UtcNow-measured).TotalSeconds,measuring=measuring,visible=Visible,dpi=dpi,headline=Data.headline,handle=Handle.ToInt64(),owner=Native.GetWindow(Handle,4).ToInt64(),barHandle=bar.ToInt64()}));}catch{}
        DpiScale=dpi/96f;
        if(DpiScale<=0)DpiScale=1;
        if(tabletMode || hidden || safeArea.IsEmpty || bar!=measuredTaskbar || rect!=measuredBounds || dpi!=measuredDpi || (DateTime.UtcNow-measured).TotalSeconds>12){Hide();if(hidden&&flyout!=null)flyout.Hide();return;}
        if(Bounds!=safeArea)Bounds=safeArea;
        if(!Visible)Show();Native.AboveTaskbar(Handle,bar);
        if(flyout!=null&&flyout.Visible)flyout.Invalidate(true);
    }
    void Measure(int generation) {
        IntPtr bar=Native.FindWindow("Shell_TrayWnd",null);Rectangle rect=Native.Rect(bar);int dpi=(int)Native.GetDpiForWindow(bar);int? first=null;
        try {
            if(bar!=IntPtr.Zero){
                var root=AutomationElement.FromHandle(bar);
                var buttons=root.FindAll(TreeScope.Descendants,new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.CheckBox),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem)));
                foreach(AutomationElement button in buttons){var r=button.Current.BoundingRectangle;if(!button.Current.IsOffscreen&&r.Width>0&&r.Height>0&&r.Right>rect.Left&&r.Left<rect.Right&&r.Top<rect.Bottom&&r.Bottom>rect.Top)first=Math.Min(first??rect.Right,(int)r.Left);}
            }
        }catch{first=null;}
        if(!closing)try{BeginInvoke((Action)(()=>{
            measuring=false;if(generation!=postureGeneration||tabletMode)return;
            safeArea=Placement.Widget(rect,dpi,first,safeArea,bar==measuredTaskbar&&rect==measuredBounds&&dpi==measuredDpi);
            measuredTaskbar=bar;measuredBounds=rect;measuredDpi=dpi;measured=DateTime.UtcNow;
        }));}catch{}
    }
    protected override void OnPaint(PaintEventArgs e) {
        using(var bitmap=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb))using(var g=Graphics.FromImage(bitmap)) {
            // Alpha 1 keeps the whole entry clickable while revealing the real taskbar.
            g.Clear(Color.FromArgb(1,0,0,0));g.SmoothingMode=SmoothingMode.AntiAlias;
            bool transparent=Theme.Transparency,selected=flyout!=null&&flyout.Visible;
            if(!transparent||hover||selected)using(var path=Theme.Rounded(new Rectangle(1,1,Width-2,Height-2),S(4)))using(var brush=new SolidBrush(transparent?Color.FromArgb(hover?20:14,Color.White):SystemInformation.HighContrast?SystemColors.Window:Theme.Bg))g.FillPath(brush,path);
            Color text=SystemInformation.HighContrast?SystemColors.WindowText:Theme.Text,muted=SystemInformation.HighContrast?SystemColors.WindowText:Theme.Muted;
            using(var brush=new SolidBrush(Theme.Accent(Data.tone)))g.FillEllipse(brush,S(11),S(10),S(4),S(4));
            using(var font=Theme.Font(S(13)))Theme.TextAt(g,Data.headline,font,text,new Rectangle(S(20),S(2),Width-S(31),S(21)));
            using(var font=Theme.Font(S(11)))Theme.TextAt(g,Data.quotaText,font,muted,new Rectangle(S(20),S(22),Width-S(31),S(16)));
            Native.LayerEntry(Handle,bitmap,Location);
        }
    }
    public void Toggle() {
        if(flyout==null||flyout.IsDisposed)flyout=new Flyout(this);
        if(flyout.Visible){flyout.Hide();return;}
        tip.Hide(this);tip.Active=false;flyout.RefreshData();flyout.Show();Invalidate();
    }
    public void RestoreTooltip(){tip.Active=true;Invalidate();}
    public void OpenTask(string id) {
        if(Preview)return;
        if(flyout!=null)flyout.Hide();
        try {Process.Start(new ProcessStartInfo(id==null?"codex://launch":"codex://threads/"+Uri.EscapeDataString(id)){UseShellExecute=true});}
        catch {tray.ShowBalloonTip(4000,"无法打开 Codex","请从开始菜单打开 Codex，并选择对应任务。",ToolTipIcon.Info);}
    }
    protected override void OnFormClosed(FormClosedEventArgs e) {
        closing=true;timer.Stop();SystemEvents.SessionSwitch-=SessionChanged;
        if(flyout!=null)flyout.Dispose();tray.Visible=false;tray.Dispose();tip.Dispose();appIcon.Dispose();
        try {if(backend!=null&&!backend.HasExited)backend.StandardInput.Close();}catch{}
        base.OnFormClosed(e);
    }
}

class TaskList : Panel {
    public TaskList(){DoubleBuffered=true;}
    protected override void WndProc(ref Message m) {
        // Keep the full panel as client area; AutoScroll still handles wheel and focus.
        if(m.Msg==0x83||m.Msg==0x85){m.Result=IntPtr.Zero;return;}
        if(m.Msg==0x84){m.Result=new IntPtr(1);return;}
        base.WndProc(ref m);
    }
    protected override void OnPaintBackground(PaintEventArgs e) {var popup=FindForm() as Flyout;e.Graphics.Clear(popup==null?Theme.Bg:popup.Surface);}
}

class Flyout : Form {
    readonly Peek owner;readonly Panel list=new TaskList();readonly Button open=new Button();readonly Label empty=new Label(),hint=new Label(),recent=new Label();
    string ids="";bool layingOut,backdrop;Font footerFont,emptyFont,hintFont;
    // Limit background brightness so secondary text remains readable over Acrylic.
    public Color Surface {get{return backdrop?Color.FromArgb(85,Theme.Bg):Theme.Bg;}}
    int S(float v){return owner.S(v);}
    public Flyout(Peek parent) {
        owner=parent;Text="Codex 本机任务";AccessibleName=Text;AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=Peek.Review;StartPosition=FormStartPosition.Manual;TopMost=true;BackColor=Theme.Bg;DoubleBuffered=true;KeyPreview=true;
        list.AutoScroll=true;list.BackColor=Theme.Bg;list.Layout+=(s,e)=>{foreach(var row in list.Controls.OfType<TaskRow>())if(row.Width!=list.ClientSize.Width)row.Width=list.ClientSize.Width;recent.Width=Math.Max(0,list.ClientSize.Width-S(12));};Controls.Add(list);
        empty.TextAlign=ContentAlignment.MiddleCenter;empty.ForeColor=Theme.Text;empty.BackColor=Theme.Bg;list.Controls.Add(empty);
        hint.TextAlign=ContentAlignment.MiddleCenter;hint.ForeColor=Theme.Muted;hint.BackColor=Theme.Bg;list.Controls.Add(hint);
        recent.Text="最近任务";recent.TextAlign=ContentAlignment.MiddleLeft;recent.ForeColor=Theme.Muted;recent.BackColor=Theme.Bg;list.Controls.Add(recent);
        open.Text="打开 Codex  ↗";open.AccessibleName="打开 Codex";open.FlatStyle=FlatStyle.Flat;open.FlatAppearance.BorderSize=0;open.ForeColor=Theme.Text;open.BackColor=Theme.Bg;open.Cursor=Cursors.Hand;open.Click+=(s,e)=>owner.OpenTask(null);Controls.Add(open);
        Deactivate+=(s,e)=>Hide();VisibleChanged+=(s,e)=>{if(!Visible)owner.RestoreTooltip();};
    }
    // Preserve a native frame for DWM; remove its caption and resizing through client messages.
    protected override CreateParams CreateParams {get{var cp=base.CreateParams;cp.Style|=0x00C40000;return cp;}}
    protected override void WndProc(ref Message m) {if(m.Msg==0x83&&m.WParam!=IntPtr.Zero){m.Result=IntPtr.Zero;return;}if(m.Msg==0x84){m.Result=new IntPtr(1);return;}base.WndProc(ref m);}
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);backdrop=Native.PopupChrome(Handle,Theme.Transparency);
        if(backdrop){BackColor=Color.Black;empty.BackColor=hint.BackColor=recent.BackColor=open.BackColor=Color.Transparent;}
        open.FlatAppearance.MouseOverBackColor=open.FlatAppearance.MouseDownBackColor=Color.FromArgb(45,45,49);
        Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x37);
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys key){if(key==Keys.Escape){Hide();return true;}return base.ProcessCmdKey(ref msg,key);}
    public void RefreshData() {
        if(layingOut)return;layingOut=true;Point scroll=list.AutoScrollPosition;list.SuspendLayout();
        try {
            View data=owner.Data;AccessibleName="Codex 本机任务，"+data.running+" 项运行，"+data.waiting+" 项待处理";Rectangle work=Screen.FromControl(owner).WorkingArea;
            var history=data.recentTasks??new TaskItem[0];var items=data.tasks.Concat(history).ToArray();int divider=data.tasks.Length>0&&history.Length>0?36:0;
            AccessibleName+="，"+history.Length+" 个历史任务";
            int height=S(items.Length==0?184:128+items.Length*132+divider);height=Math.Min(height,work.Height-S(16));
            Rectangle anchor=owner.Visible?owner.Bounds:new Rectangle(Cursor.Position,new Size(1,1));
            Bounds=Placement.Popup(anchor,work,S(460),height,S(8));
            int top=items.Length==0?58:82;
            int contentHeight=Math.Max(0,items.Length*S(132)-S(8)+S(divider));
            int listHeight=Height-S(top+54);if(items.Length>0)listHeight=Math.Min(listHeight,contentHeight);
            list.Bounds=new Rectangle(S(16),S(top),Width-S(32),listHeight);
            list.AutoScrollMinSize=new Size(0,contentHeight);
            open.Bounds=new Rectangle(Width-S(142),Height-S(42),S(126),S(30));
            if(footerFont==null||footerFont.Size!=S(12)){var old=footerFont;footerFont=Theme.Font(S(12));open.Font=recent.Font=footerFont;if(old!=null)old.Dispose();}
            string next=String.Join("|",items.Select(t=>t.id));
            if(ids!=next){foreach(var row in list.Controls.OfType<TaskRow>().ToArray()){list.Controls.Remove(row);row.Dispose();}ids=next;foreach(var task in items){var row=new TaskRow(owner);row.Item=task;row.Click+=(s,e)=>owner.OpenTask(((TaskRow)s).Item.id);list.Controls.Add(row);}}
            int y=0,index=0;Point offset=list.AutoScrollPosition;
            foreach(var row in list.Controls.OfType<TaskRow>()){if(index==data.tasks.Length)y+=S(divider);row.Item=items[index++];row.Bounds=new Rectangle(0,y+offset.Y,list.ClientSize.Width,S(124));row.AccessibleName=row.Item.title+"，"+(row.Item.state=="completed"?"历史任务，":"")+row.Item.detail;row.AccessibleDescription=row.Item.progress;row.Invalidate();y+=S(132);}
            recent.Visible=divider>0;recent.Bounds=new Rectangle(S(6),data.tasks.Length*S(132)+offset.Y,list.ClientSize.Width-S(12),S(28));
            empty.Visible=hint.Visible=items.Length==0;empty.Bounds=new Rectangle(0,S(8),list.ClientSize.Width,S(28));empty.Text=data.connected?"暂无任务记录":"状态未同步";
            hint.Bounds=new Rectangle(0,S(39),list.ClientSize.Width,S(25));hint.Text=data.connected?"开始新任务后，执行详情会显示在这里":data.diagnostic;
            if(emptyFont==null||emptyFont.Size!=S(14)){var old=emptyFont;emptyFont=Theme.Font(S(14));empty.Font=emptyFont;if(old!=null)old.Dispose();}
            if(hintFont==null||hintFont.Size!=S(12)){var old=hintFont;hintFont=Theme.Font(S(12));hint.Font=hintFont;if(old!=null)old.Dispose();}
            Invalidate();
        }finally{list.ResumeLayout(true);list.PerformLayout();list.AutoScrollPosition=new Point(0,-scroll.Y);layingOut=false;}
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(Surface);View d=owner.Data;
        using(var font=Theme.Font(S(16),true))Theme.TextAt(g,"Codex",font,Theme.Text,new Rectangle(S(22),S(15),S(70),S(29)));
        using(var font=Theme.Font(S(13)))Theme.TextAt(g,"本机任务",font,Theme.Muted,new Rectangle(S(96),S(16),S(108),S(28)));
        if(d.tasks.Length>0)using(var font=Theme.Font(S(12)))Theme.TextAt(g,d.running+" 项运行   ·   "+d.waiting+" 项待处理",font,Theme.Muted,new Rectangle(S(22),S(48),Width-S(44),S(23)));
        else if(d.recentTasks!=null&&d.recentTasks.Length>0)using(var font=Theme.Font(S(12)))Theme.TextAt(g,"最近 "+d.recentTasks.Length+" 个任务",font,Theme.Muted,new Rectangle(S(22),S(48),Width-S(44),S(23)));
        using(var pen=new Pen(Color.FromArgb(35,Color.White))){g.DrawLine(pen,S(22),Height-S(48),Width-S(22),Height-S(48));}
        string sync=owner.Preview?"界面预览 · 演示数据":d.connected?"已连接":"状态未同步";
        using(var font=Theme.Font(S(12)))Theme.TextAt(g,sync,font,Theme.Muted,new Rectangle(S(22),Height-S(41),Width-S(176),S(28)));
    }
}

class TaskRow : Button {
    readonly Peek owner;public TaskItem Item;bool hover;
    int S(float v){return owner.S(v);}
    public TaskRow(Peek parent){owner=parent;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Cursor=Cursors.Hand;FlatStyle=FlatStyle.Flat;TabStop=true;MouseEnter+=(s,e)=>{hover=true;Invalidate();};MouseLeave+=(s,e)=>{hover=false;Invalidate();};}
    protected override void OnPaint(PaintEventArgs e) {
        if(Item==null)return;var g=e.Graphics;g.Clear(((Flyout)FindForm()).Surface);g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=Theme.Rounded(new Rectangle(1,1,Width-3,Height-3),S(8))) {
            using(var brush=new SolidBrush(Color.FromArgb(hover||Focused?24:10,Color.White)))g.FillPath(brush,path);
            using(var pen=new Pen(Focused&&ShowFocusCues?Theme.Accent("blue"):Color.FromArgb(hover?42:24,Color.White),Focused&&ShowFocusCues?S(1):1))g.DrawPath(pen,path);
        }
        bool completed=Item.state=="completed";
        string tone=completed?"muted":Item.state=="approval"||Item.state=="input"?"amber":Item.state=="failed"?"red":"blue";
        string label=completed?"已结束":Item.state=="approval"?"等待确认":Item.state=="input"?"等待回复":Item.state=="failed"?"异常":"运行中";
        using(var font=Theme.Font(S(14),true))Theme.TextAt(g,Item.title,font,Theme.Text,new Rectangle(S(13),S(8),Width-S(112),S(26)));
        using(var font=Theme.Font(S(12)))Theme.TextAt(g,label,font,Theme.Accent(tone),new Rectangle(Width-S(90),S(9),S(78),S(25)));
        using(var font=Theme.Font(S(13)))Theme.TextAt(g,Item.detail,font,Theme.Text,new Rectangle(S(13),S(38),Width-S(26),S(26)));
        using(var font=Theme.Font(S(12)))Theme.TextAt(g,String.IsNullOrWhiteSpace(Item.progress)?"点击打开原任务，查看执行记录":Item.progress,font,Theme.Muted,new Rectangle(S(13),S(67),Width-S(26),S(24)));
        double? since=Item.waitingSince??Item.startedAt;
        string elapsed="";if(completed){elapsed=Item.startedAt.HasValue?"最近运行 "+Peek.Epoch(Item.startedAt.Value).ToLocalTime().ToString("M月d日 HH:mm"):"历史任务";}else if(since.HasValue){var t=DateTime.UtcNow-Peek.Epoch(since.Value);if(t.TotalSeconds<0)t=TimeSpan.Zero;elapsed=(Item.waitingSince.HasValue?"已等待 ":"本轮已用 ")+(t.TotalHours>=1?((int)t.TotalHours)+"小时 ":"")+t.Minutes.ToString("00")+":"+t.Seconds.ToString("00");}
        using(var font=Theme.Font(S(11)))Theme.TextAt(g,elapsed,font,Theme.Muted,new Rectangle(S(13),S(96),Width-S(110),S(21)));
        using(var font=Theme.Font(S(12)))Theme.TextAt(g,completed?"查看  ↗":tone=="amber"?"去处理  ↗":"打开  ↗",font,Theme.Accent(tone),new Rectangle(Width-S(92),S(94),S(80),S(24)));
    }
}

static class Native {
    [StructLayout(LayoutKind.Sequential)]struct POINT {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]struct SIZE {public int Width,Height;}
    [StructLayout(LayoutKind.Sequential,Pack=1)]struct BLEND {public byte Operation,Flags,Alpha,Format;}
    [DllImport("gdi32.dll",SetLastError=true)]static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr h,IntPtr target,ref POINT position,ref SIZE size,IntPtr source,ref POINT origin,uint key,ref BLEND blend,uint flags);
    public static void LayerEntry(IntPtr h,Bitmap bitmap,Point location) {
        IntPtr dc=CreateCompatibleDC(IntPtr.Zero),image=IntPtr.Zero,previous=IntPtr.Zero;
        if(dc==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
        try {
            image=bitmap.GetHbitmap(Color.FromArgb(0));previous=SelectObject(dc,image);
            if(previous==IntPtr.Zero||previous==new IntPtr(-1))throw new System.ComponentModel.Win32Exception();
            var position=new POINT{X=location.X,Y=location.Y};var size=new SIZE{Width=bitmap.Width,Height=bitmap.Height};var origin=new POINT();var blend=new BLEND{Alpha=255,Format=1};
            if(!UpdateLayeredWindow(h,IntPtr.Zero,ref position,ref size,dc,ref origin,0,ref blend,2))throw new System.ComponentModel.Win32Exception();
        }finally{if(previous!=IntPtr.Zero&&previous!=new IntPtr(-1))SelectObject(dc,previous);if(image!=IntPtr.Zero)DeleteObject(image);DeleteDC(dc);}
    }
    [StructLayout(LayoutKind.Sequential)]struct MARGINS {public int Left,Right,Top,Bottom;}
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr h,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")]static extern int DwmExtendFrameIntoClientArea(IntPtr h,ref MARGINS margins);
    public static bool PopupChrome(IntPtr h,bool transparency) {
        int dark=1,round=2,border=0x3D3D3D,material=3;
        DwmSetWindowAttribute(h,20,ref dark,4);DwmSetWindowAttribute(h,33,ref round,4);DwmSetWindowAttribute(h,34,ref border,4);
        if(!transparency || DwmSetWindowAttribute(h,38,ref material,4)!=0)return false;
        var margins=new MARGINS{Left=-1,Right=-1,Top=-1,Bottom=-1};return DwmExtendFrameIntoClientArea(h,ref margins)==0;
    }
    [StructLayout(LayoutKind.Sequential)]public struct RECT {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindow(string cls,string title);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out RECT rect);
    [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")]public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]static extern bool GetAutoRotationState(out uint state);
    // Slate zero is meaningful only with integrated touch ready and rotation hardware (not AR_NOSENSOR / AR_NOT_SUPPORTED).
    public static bool IsTabletMode(int slate,int digitizer,bool rotationAvailable,uint rotation) {return slate==0&&(digitizer&0x81)==0x81&&rotationAvailable&&(rotation&0x30)==0;}
    public static bool TabletMode() {uint rotation;bool available=GetAutoRotationState(out rotation);return IsTabletMode(GetSystemMetrics(0x2003),GetSystemMetrics(94),available,rotation);}
    [DllImport("user32.dll")]public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")]public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
    [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder name,int count);
    [DllImport("user32.dll")]public static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")]public static extern IntPtr GetWindow(IntPtr h,uint command);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowLongPtr(IntPtr h,int index,IntPtr value);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")]static extern int SetWindowLong(IntPtr h,int index,int value);
    public static void AboveTaskbar(IntPtr h,IntPtr bar) {
        // Ownership keeps the entry above the taskbar without repeatedly raising shell menus.
        // Re-check after Show and when Explorer supplies a replacement taskbar handle.
        if(GetWindow(h,4)==bar)return;
        if(IntPtr.Size==8)SetWindowLongPtr(h,-8,bar);else SetWindowLong(h,-8,bar.ToInt32());
        SetWindowPos(h,new IntPtr(-1),0,0,0,0,0x13);
    }
    public static Rectangle Rect(IntPtr h){RECT r;if(h==IntPtr.Zero||!GetWindowRect(h,out r))return Rectangle.Empty;return Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);}
    public static bool Fullscreen(IntPtr widget,IntPtr popup,Rectangle screen){IntPtr h=GetForegroundWindow();if(h==widget||h==popup)return false;var cls=new StringBuilder(128);GetClassName(h,cls,128);if(new[]{"Progman","WorkerW","Shell_TrayWnd"}.Contains(cls.ToString()))return false;var r=Rect(h);return r.Left<=screen.Left&&r.Top<=screen.Top&&r.Right>=screen.Right&&r.Bottom>=screen.Bottom;}
    [DllImport("user32.dll")]static extern IntPtr GetThreadDesktop(int id);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetUserObjectInformation(IntPtr h,int index,StringBuilder text,int length,out int needed);
    public static string DesktopName(int thread){var text=new StringBuilder(256);int needed;GetUserObjectInformation(GetThreadDesktop(thread),2,text,512,out needed);return text.ToString();}
}

static class Placement {
    public static Rectangle Widget(Rectangle bar, int dpi, int? firstButton, Rectangle previous, bool sameTaskbar) {
        // ponytail: reuse occupancy only on the unchanged primary taskbar; fresh collisions or context changes clear it.
        return firstButton.HasValue ? Widget(bar,dpi,firstButton.Value) : sameTaskbar ? previous : Rectangle.Empty;
    }
    public static Rectangle Widget(Rectangle bar, int dpi, int firstButton) {
        double scale=dpi/96.0;
        int margin=(int)Math.Round(8*scale),height=(int)Math.Round(40*scale);
        int width=Math.Min((int)Math.Round(320*scale),firstButton-bar.Left-margin*2);
        if(width<180*scale || bar.Height<height) return Rectangle.Empty;
        return new Rectangle(bar.Left+margin,bar.Top+(bar.Height-height)/2,width,height);
    }
    public static Rectangle Popup(Rectangle anchor, Rectangle work, int width, int height, int gap) {
        width=Math.Min(width,work.Width);height=Math.Min(height,work.Height);
        return new Rectangle(Math.Max(work.Left,Math.Min(anchor.Left,work.Right-width)),Math.Max(work.Top,Math.Min(anchor.Top-gap-height,work.Bottom-height)),width,height);
    }
}
