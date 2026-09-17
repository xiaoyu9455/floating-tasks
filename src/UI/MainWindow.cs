using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FloatingTasks {
 public partial class MainWindow : Form {
  const int SideWidth = 190, Gap = 8, RowHeight = 78, CollapsedHeight = 40, LeftHeight = 384;
  readonly Color surface = Palette.Surface, side = Palette.Side, muted = Palette.Muted, ink = Palette.Ink, accent = Palette.Accent;
  readonly TaskSession session;
  readonly Motion motion;
  Database db { get { return session.Database; } }
  Entry selected;
  bool leftOpen, rightOpen, exiting, sliderDrag, collapsed;
  int mainScroll, childScroll, centerX, heightLogical;
  float scale = 1;
  Point dragMouse, dragWindow;
  bool dragging;
  string notice { get { return session.Notice; } }
  string hoverKey, hoverRow;
  readonly List<Hit> hits = new List<Hit>();
  readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
  int ticks;
  TextBox mainInput, childInput;
  NotifyIcon tray;
  System.Windows.Forms.Timer timer;
  ToolTip tip = new ToolTip();
  TaskItem Current { get { return db.Tasks[db.Selected]; } }
  class Hit { public RectangleF Bounds; public string Key, Tip; public Action Action; }
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hWnd,int msg,IntPtr wParam,string text);
  [DllImport("uxtheme.dll", CharSet=CharSet.Unicode)]
  static extern int SetWindowTheme(IntPtr window, string subAppName, string subIdList);
  static void UseDarkControlTheme(Control control) {
   control.HandleCreated += delegate { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); };
  }
  public MainWindow(TaskSession session) {
   this.session = session;
   CenterWidth = Math.Max(272, Math.Min(1200, db.PanelWidth ?? 272));
   listHeight = db.PanelListHeight.HasValue ? Math.Max(199, Math.Min(2000, db.PanelListHeight.Value)) : 0;
   motion = new Motion(this);
   Text = "浮记 · 把下一步放在眼前"; FormBorderStyle = FormBorderStyle.None; AutoScaleMode = AutoScaleMode.None;
   // The window Region already clips the cards and the gaps between them.
   // A chroma key would bleed into antialiased corner pixels.
   ShowInTaskbar = false; DoubleBuffered = true; BackColor = surface;
   using (Graphics g = CreateGraphics()) scale = Math.Max(1, g.DpiX / 96f);
   TopMost = db.Pinned; Opacity = db.Transparency / 100.0; StartPosition = FormStartPosition.Manual;
   mainInput = MakeInput("记下一件事", delegate(bool startNow) { AddEntry(Current.Entries, mainInput, startNow); });
   childInput = MakeInput("记下一个小步骤", delegate(bool startNow) { if (selected != null) AddEntry(selected.Children, childInput, startNow); });
   Controls.Add(mainInput); Controls.Add(childInput); InitializeQuickStep(); InitializeNotes();
   RefreshInputHints();
   using (var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("FloatingTasks.AppIcon"))
   { if (stream != null) using (var source = new Icon(stream)) appIcon = (Icon)source.Clone(); }
   Icon = appIcon;
   UpdateLayout(false);
   Rectangle area = Screen.PrimaryScreen.WorkingArea; Location = new Point(db.WindowX ?? (area.Right - Width - S(30)), db.WindowY ?? (area.Top + S(100)));
   ClampWindow();
   tray = new NotifyIcon { Icon = appIcon, Text = "浮记 · 双击显示", Visible = true };
   ContextMenuStrip menu = new ContextMenuStrip { BackColor=Palette.Panel, ForeColor=Palette.Ink, Renderer=new DarkMenuRenderer() };
   menu.Items.Add("显示浮记", null, delegate { Show(); Activate(); });
   menu.Items.Add("全部暂停", null, delegate { PauseAll(); });
   menu.Items.Add("退出并暂停", null, delegate { exiting = true; Close(); });
   tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { Show(); Activate(); };
   ContextMenuStrip cardMenu = new ContextMenuStrip { BackColor=Palette.Panel, ForeColor=Palette.Ink, Renderer=new DarkMenuRenderer() };
   cardMenu.Opening += CardMenuOpening;
   cardMenu.Items.Add("恢复默认大小", null, delegate { ResetPanelSize(); });
   cardMenu.Items.Add("已完成事项…", null, delegate { ShowCompleted(); });
   cardMenu.Items.Add("重命名当前任务", null, delegate { RenameCurrentTask(); });
   cardMenu.Items.Add("展开 / 收起操作", null, delegate { ToggleLeft(); });
   cardMenu.Items.Add("隐藏到托盘（继续计时）", null, delegate { Hide(); });
   cardMenu.Items.Add("退出并暂停", null, delegate { exiting = true; Close(); });
   ContextMenuStrip = cardMenu;
   timer = new System.Windows.Forms.Timer { Interval = 1000 };
   timer.Tick += delegate { bool running = session.HasRunning; string previousNotice = notice; Advance(); if (++ticks >= 2) { ticks = 0; Save(); } if (running || previousNotice != notice) Invalidate(); }; timer.Start();
   SystemEvents.PowerModeChanged += PowerChanged; SystemEvents.SessionEnding += SessionEnding; SystemEvents.SessionSwitch += SessionSwitch;
   FormClosing += ClosingWindow;
   Shown += delegate { if (notice != null) MessageBox.Show(this, notice, "浮记"); };
  }
  int S(float n) { return (int)Math.Round(n * scale); }
  const int WM_DPICHANGED = 0x02E0;
  protected override void WndProc(ref Message m) {
   if (m.Msg == WM_DPICHANGED) {
    float next = Math.Max(1, (m.WParam.ToInt32() & 0xFFFF) / 96f);
    if (Math.Abs(next - scale) > 0.001f) {
     scale = next;
     
     UpdateLayout(false);
    }
   }
   base.WndProc(ref m);
  }
  TextBox MakeInput(string hint, Action<bool> add) {
   TextBox box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Palette.Input, ForeColor = ink, Font = new Font("Microsoft YaHei UI", S(12), FontStyle.Regular, GraphicsUnit.Pixel), MaxLength = 250 };
   tip.SetToolTip(box, hint); box.AccessibleName = hint;
   box.HandleCreated += delegate { SendMessage(box.Handle,0x1501,new IntPtr(1),hint); };
   box.Enter += delegate { Invalidate(); }; box.Leave += delegate { Invalidate(); };
   box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; add(e.Control); } if (e.KeyCode == Keys.Escape) { box.Clear(); ActiveControl = null; } };
   return box;
  }
  int VisibleRows(List<Entry> entries) { return Math.Max(1, Math.Min(MaxVisibleRows, ActiveEntries(entries).Count)); }
  void UpdateLayout(bool keepCenter) {

   FitPanelSize();
   string inputFontKey = "input:" + S(12);
   Font inputFont;
   if (!fonts.TryGetValue(inputFontKey, out inputFont)) {
    inputFont = new Font("Microsoft YaHei UI", S(12), FontStyle.Regular, GraphicsUnit.Pixel);
    fonts.Add(inputFontKey, inputFont);
   }
   mainInput.Font = childInput.Font = quickStepInput.Font = inputFont;
   hits.Clear();
   mainInput.Visible = !collapsed; notesPanel.Visible = !collapsed && notesOpen;
   if (collapsed) {
    int anchor = Left + S(centerX);
    centerX = 0; heightLogical = CollapsedHeight;
    ClientSize = new Size(S(CenterWidth), S(CollapsedHeight));
    if (keepCenter) Left = anchor;
    childInput.Visible = false; quickStepInput.Visible = false; ActiveControl = null;
    using (GraphicsPath path = new GraphicsPath()) {
     AddRound(path, new RectangleF(0, 0, Width, Height), S(12));
     Region old = Region; Region = new Region(path); if (old != null) old.Dispose();
    }
    ClampWindow(); Invalidate(); return;
   }
   int oldCenter = Left + S(centerX);
   centerX = leftOpen ? SideWidth + Gap : 0;
   int mainHeight = MainListHeight;
   int rightHeight = RightListHeight;
   int notesBottom = mainHeight + NotesHeight; LayoutNotes(mainHeight); heightLogical = Math.Max(notesBottom, rightOpen ? rightHeight : 0);
   if (leftOpen) heightLogical = Math.Max(heightLogical, LeftHeight);
   ClientSize = new Size(S(centerX + CenterWidth + (rightOpen ? CenterWidth + Gap : 0)), S(heightLogical));
   if (keepCenter) Left = oldCenter - S(centerX);
   mainInput.SetBounds(S(centerX + 32), S(mainHeight - 30), S(CenterWidth - 78), S(20));
   childInput.Visible = rightOpen && selected != null;
   if (rightOpen && selected != null) childInput.SetBounds(S(centerX + CenterWidth + Gap + 32), S(rightHeight - 30), S(CenterWidth - 78), S(20));
   using (GraphicsPath path = new GraphicsPath()) {
    AddRound(path, new RectangleF(S(centerX), 0, S(CenterWidth), S(notesBottom)), S(16));
    if (leftOpen) AddRound(path, new RectangleF(0,0,S(SideWidth),S(LeftHeight)),S(16));
    if (rightOpen) AddRound(path, new RectangleF(S(centerX+CenterWidth+Gap),0,S(CenterWidth),S(rightHeight)),S(16));
    Region old = Region; Region = new Region(path); if (old != null) old.Dispose();
   }
   if (IsHandleCreated && Visible) {
    Rectangle area = Screen.FromPoint(new Point(Left + S(centerX), Top)).WorkingArea;
    Left = Math.Max(area.Left, Math.Min(Left, area.Right - Width)); Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
   }
   Invalidate();
  }
  void PauseAll(){session.PauseAll();Changed();}
  void PowerChanged(object sender,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Suspend&&IsHandleCreated)Invoke((Action)PauseAll);else if(e.Mode==PowerModes.Resume&&IsHandleCreated)BeginInvoke((Action)delegate{session.ResumeClock();});}
  void SessionSwitch(object sender,SessionSwitchEventArgs e){if(e.Reason==SessionSwitchReason.SessionLock&&IsHandleCreated)Invoke((Action)PauseAll);}
  void SessionEnding(object sender,SessionEndingEventArgs e){if(IsHandleCreated)Invoke((Action)PauseAll);}
  void ClosingWindow(object sender,FormClosingEventArgs e)
  {
   if(!exiting && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); return; }
   PauseAll();
   if(!session.Flush() && e.CloseReason==CloseReason.UserClosing
      && MessageBox.Show(this,"保存失败，仍要退出吗？\n"+notice,"浮记",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.No)
   { e.Cancel=true; exiting=false; return; }
   timer.Stop();
  }

  protected override void Dispose(bool disposing)
  {
   if(disposing)
   {
    SystemEvents.PowerModeChanged-=PowerChanged;
    SystemEvents.SessionEnding-=SessionEnding;
    SystemEvents.SessionSwitch-=SessionSwitch;
    disposingMenus=true; popupMenus.Dispose();
    if(timer!=null) timer.Dispose();
    if(tray!=null) { tray.Visible=false; if(tray.ContextMenuStrip!=null)tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
    tip.Dispose();
    if (appIcon != null) appIcon.Dispose();
    foreach(Font font in fonts.Values) font.Dispose();
    fonts.Clear();
   }
   base.Dispose(disposing);
  }
 }
}
