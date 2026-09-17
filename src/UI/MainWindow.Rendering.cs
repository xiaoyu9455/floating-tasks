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
 public partial class MainWindow {
  const string UiFont = "Microsoft YaHei UI", MonoFont = "Consolas";
  // 字号阶梯（设计令牌）：10.5 / 11.5 / 13.5 / 15，字重只用 Regular/SemiBold(Bold)
  static void AddRound(GraphicsPath p, RectangleF r, float radius) {
   float d = radius * 2; p.StartFigure(); p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90); p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure();
  }
  void Round(Graphics g, RectangleF rect, Color color, float radius) { using(GraphicsPath p=new GraphicsPath()) using(SolidBrush b=new SolidBrush(color)) { AddRound(p,rect,radius); g.FillPath(b,p); } }
  void RoundLine(Graphics g, RectangleF rect, float radius, Color color) { RoundLine(g,rect,radius,color,1f); }
  void RoundLine(Graphics g, RectangleF rect, float radius, Color color, float width) { using(GraphicsPath p=new GraphicsPath()) using(Pen pen=new Pen(color,width)) { AddRound(p,rect,radius); g.DrawPath(pen,p); } }
  Pen Stroke(Color color, float width) { Pen p=new Pen(color,width); p.StartCap=LineCap.Round; p.EndCap=LineCap.Round; return p; }
  static Color Fade(Color c, double a) { return Color.FromArgb(Math.Max(0,Math.Min(255,(int)(255*a))),c); }
  static Color Blend(Color a, Color b, double t) { t=Math.Max(0,Math.Min(1,t)); return Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t)); }
  void TextAt(Graphics g, string text, RectangleF rect, Color color, float size, bool bold) { TextAt(g,text,rect,color,size,bold,UiFont); }
  void TextAt(Graphics g, string text, RectangleF rect, Color color, float size, bool bold, string family) {
   string key=family+size.ToString(System.Globalization.CultureInfo.InvariantCulture)+(bold?"b":"r"); Font f;
   if(!fonts.TryGetValue(key,out f)){f=new Font(family,size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);fonts.Add(key,f);}
   using(SolidBrush b=new SolidBrush(color)) using(StringFormat format=new StringFormat { LineAlignment=StringAlignment.Center, Trimming=StringTrimming.EllipsisCharacter, FormatFlags=StringFormatFlags.NoWrap }) g.DrawString(text,f,b,rect,format);
  }
  void Target(string key, RectangleF rect, string help, Action action) { hits.Add(new Hit {Key=key,Bounds=rect,Tip=help,Action=action}); }
  void PulseDot(Graphics g, float x, float y, bool running) {
   Color c = running ? Color.FromArgb(DateTime.Now.Second % 2 == 0 ? 255 : 110, Palette.Done) : Color.FromArgb(80, Palette.Muted);
   using(SolidBrush b=new SolidBrush(c)) g.FillEllipse(b,x,y,7,7);
  }
  // 图标统一 26×26 热区、16×16 视觉网格、1.6px 圆角笔触；emphasis 控制常驻/悬停浮现的透明度
  void DrawIcon(Graphics g, string key, string kind, float x, float y, string help, Action action, Color color) { DrawIcon(g,key,kind,x,y,help,action,color,1); }
  void DrawIcon(Graphics g, string key, string kind, float x, float y, string help, Action action, Color color, double emphasis) {
   RectangleF r=new RectangleF(x,y,26,26);
   double hover=motion.Value("icon:"+key,hoverKey==key?1:0,120);
   if(hover>0.01) Round(g,r,Fade(Palette.Hover,hover),8);
   Color effective=Fade(kind=="delete"&&hover>0.5?Palette.Danger:color,Math.Max(0.22,emphasis));
   using(Pen p=Stroke(effective,1.6f)) {
    if(kind=="done") g.DrawLines(p,new PointF[]{new PointF(x+7,y+13),new PointF(x+11,y+17),new PointF(x+19,y+8)});
    else if(kind=="left") g.DrawLines(p,new PointF[]{new PointF(x+15,y+8),new PointF(x+10,y+13),new PointF(x+15,y+18)});
    else if(kind=="right") g.DrawLines(p,new PointF[]{new PointF(x+11,y+8),new PointF(x+16,y+13),new PointF(x+11,y+18)});
    else if(kind=="plus") { g.DrawLine(p,x+8,y+13,x+18,y+13); g.DrawLine(p,x+13,y+8,x+13,y+18); }
    else if(kind=="pause") { g.DrawLine(p,x+10,y+8,x+10,y+18); g.DrawLine(p,x+16,y+8,x+16,y+18); }
    else if(kind=="play") { using(SolidBrush fill=new SolidBrush(effective)) g.FillPolygon(fill,new PointF[]{new PointF(x+10,y+8),new PointF(x+18,y+13),new PointF(x+10,y+18)}); }
    else if(kind=="delete") { g.DrawLine(p,x+8,y+9,x+18,y+9); g.DrawLine(p,x+11,y+6,x+15,y+6); g.DrawLines(p,new PointF[]{new PointF(x+9,y+11),new PointF(x+10,y+19),new PointF(x+16,y+19),new PointF(x+17,y+11)}); }
    else if(kind=="collapse" || kind=="expand") {
     float a=kind=="collapse"?16:10, b=kind=="collapse"?10:16;
     g.DrawLines(p,new PointF[]{new PointF(x+8,y+a),new PointF(x+13,y+b),new PointF(x+18,y+a)});
    }
    else if(kind=="hide") g.DrawLine(p,x+8,y+14,x+18,y+14);
    else { g.DrawLine(p,x+9,y+9,x+17,y+17); g.DrawLine(p,x+17,y+9,x+9,y+17); }
   }
   Target(key,r,help,action);
  }
  void MenuButton(Graphics g,string key,string text,int y,Action action,bool enabled) {
   RectangleF r=new RectangleF(12,y,SideWidth-24,29);
   double hover=motion.Value("menu:"+key,hoverKey==key&&enabled?1:0,120);
   if(key=="new") {
    // 实心蓝主按钮：新建任务
    Round(g,r,hover>0.01?Blend(Palette.Accent,Palette.AccentDark,hover):Palette.Accent,10);
    TextAt(g,text,new RectangleF(22,y,SideWidth-40,29),Palette.OnAccent,11.5f,false);
   } else {
    if(hover>0.01) Round(g,r,Fade(Palette.Hover,hover),8);
    TextAt(g,text,new RectangleF(22,y,SideWidth-40,29),enabled?ink:Palette.Faint,11.5f,false);
   }
   if(enabled) Target(key,r,text,action);
  }
  void Caption(Graphics g, string text, int y) { TextAt(g,text,new RectangleF(22,y,120,15),Palette.Faint,10.5f,false); }
  protected override void OnPaint(PaintEventArgs e) {
   base.OnPaint(e); Graphics g=e.Graphics; g.ScaleTransform(scale,scale); g.SmoothingMode=SmoothingMode.AntiAlias; hits.Clear(); LayoutQuickStep();
   if(collapsed) {
    Round(g,new RectangleF(0,0,CenterWidth,CollapsedHeight),surface,12);
    RoundLine(g,new RectangleF(0.5f,0.5f,CenterWidth-1,CollapsedHeight-1),12,Palette.Border);
    TextAt(g,Current.Name,TaskNameBounds,ink,13.5f,true);
    TextAt(g,Logic.Time(Current.Seconds),new RectangleF(CenterWidth-180,10,84,26),session.HasRunning?Palette.Accent:muted,11,false,MonoFont);
    DrawIcon(g,"collapse","expand",CenterWidth-93,10,"展开列表",ToggleCollapsed,muted);
    DrawIcon(g,"minimize","hide",CenterWidth-64,10,"隐藏到托盘（继续计时）",delegate{Hide();},muted);
    DrawIcon(g,"exit","close",CenterWidth-35,10,"退出并暂停",delegate { exiting=true; Close(); },muted);
    return;
   }
   int mainHeight=MainListHeight;
   Round(g,new RectangleF(centerX,0,CenterWidth,mainHeight+NotesHeight),surface,16);
   RoundLine(g,new RectangleF(centerX+0.5f,0.5f,CenterWidth-1,mainHeight+NotesHeight-1),16,Palette.Border);
   DrawIcon(g,"left",leftOpen?"right":"left",centerX+9,10,leftOpen?"收起操作":"展开操作",ToggleLeft,muted);
   TextAt(g,Current.Name,TaskNameBounds,ink,15,true);
   DrawIcon(g,"collapse","collapse",centerX+CenterWidth-93,10,"折叠为一行（继续计时）",ToggleCollapsed,muted);
   DrawIcon(g,"minimize","hide",centerX+CenterWidth-64,10,"隐藏到托盘（继续计时）",delegate { Hide(); },muted);
   DrawIcon(g,"exit","close",centerX+CenterWidth-35,10,"退出并暂停",delegate { exiting=true; Close(); },muted);
   bool runningNow=session.HasRunning;
   if(runningNow) {
    // 状态胶囊：绿软底 + 绿点 +「专注中 mm:ss」
    Round(g,new RectangleF(centerX+44,33,128,20),Palette.DoneSoft,10);
    PulseDot(g,centerX+54,40,true);
    TextAt(g,"专注中",new RectangleF(centerX+66,33,40,19),Palette.Done,11,false);
    TextAt(g,Logic.Time(Current.Seconds),new RectangleF(centerX+106,33,60,19),Palette.Done,11,false,MonoFont);
   } else {
    TextAt(g,"已暂停",new RectangleF(centerX+44,33,44,19),Palette.Faint,10.5f,false);
    TextAt(g,Logic.Time(Current.Seconds),new RectangleF(centerX+92,33,64,19),Palette.Faint,11,false,MonoFont);
   }
   PaintEntries(g,Current.Entries,centerX,mainScroll,false);
   PaintInput(g,centerX,mainHeight,mainInput,"添加记录",delegate { AddEntry(Current.Entries,mainInput); });
   PaintNotes(g,mainHeight); PaintResizeGrip(g); if(leftOpen) PaintLeft(g);
   if(rightOpen) {
    int rx=centerX+CenterWidth+Gap, rh=RightListHeight;
    Round(g,new RectangleF(rx,0,CenterWidth,rh),side,16);
    RoundLine(g,new RectangleF(rx+0.5f,0.5f,CenterWidth-1,rh-1),16,Palette.Border);
    DrawIcon(g,"back","left",rx+8,10,"返回上一级",delegate { Entry parent=ParentOf(selected); if(parent!=null) {selected=parent; childScroll=0; UpdateLayout(true);} else {rightOpen=false; UpdateLayout(true);} },muted);
    TextAt(g,selected==null?"子记录":"细分步骤",new RectangleF(rx+42,10,CenterWidth-87,25),ink,13.5f,true);
    TextAt(g,selected==null?"先在中间添加一条记录":selected.Title,new RectangleF(rx+42,35,CenterWidth-72,17),Palette.Faint,10.5f,false);
    DrawIcon(g,"closeRight","close",rx+CenterWidth-34,10,"收起子记录",delegate {rightOpen=false;UpdateLayout(true);},muted);
    if(selected!=null) { PaintEntries(g,selected.Children,rx,childScroll,true); PaintInput(g,rx,rh,childInput,"添加子记录",delegate {AddEntry(selected.Children,childInput);}); }
   }
  }
  void PaintInput(Graphics g,int x,int h,TextBox box,string placeholder,Action add) {
   RectangleF r=new RectangleF(x+12,h-38,CenterWidth-24,30);
   Round(g,r,Palette.Bg,9);
   RoundLine(g,new RectangleF(r.X+0.5f,r.Y+0.5f,r.Width-1,r.Height-1),9,box.Focused?Palette.Accent:Palette.InputBorder,box.Focused?1.5f:1f);
   Round(g,new RectangleF(x+CenterWidth-38,h-36,26,26),Palette.AccentSoft,13);
   DrawIcon(g,"add"+x,"plus",x+CenterWidth-38,h-36,db.EnterStarts?"添加并立即计时":"先记下（Ctrl + 点击立即开始）",add,Palette.Accent);
  }
  void PaintEntries(Graphics g,List<Entry> entries,int x,int offset,bool child) {
   entries=ActiveEntries(entries);
   if(entries.Count==0) {
    TextAt(g,child?"把这件事拆成小步骤":"此刻，从一件小事开始",new RectangleF(x+20,75,CenterWidth-40,25),muted,13.5f,false);
    TextAt(g,child?"步骤为最后一级，不可再拆分":(db.EnterStarts?"输入后 Enter 开始计时":"Enter 先记下 · Ctrl+Enter 开始"),new RectangleF(x+20,101,CenterWidth-40,19),Palette.Faint,10.5f,false);return;
   }
   int end=Math.Min(entries.Count,offset+MaxVisibleRows);
   for(int i=offset;i<end;i++) {
    Entry entry=entries[i]; int y=66+(i-offset)*RowHeight;
    bool chosen=rightOpen && selected==entry;
    double rowHover=motion.Value("row:"+entry.Id,hoverRow==entry.Id?1:0,120);
    double run=motion.Value("run:"+entry.Id,entry.Running?1:0,160);
    double em=0.45+0.55*rowHover; // 操作钮悬停增亮，完成圆圈常驻
    RectangleF rowRect=new RectangleF(x+10,y,CenterWidth-20,RowHeight-4);
    Target("row"+entry.Id,rowRect,"",delegate {});
    if(run>0.01) Round(g,rowRect,Fade(Palette.RunBg,run),10);
    if(chosen) { Round(g,rowRect,Palette.AccentSoft,10); RoundLine(g,new RectangleF(x+10.5f,y+0.5f,CenterWidth-21,RowHeight-5),10,Palette.AccentLine); }
    else if(rowHover>0.01) Round(g,rowRect,Fade(Palette.Hover,rowHover),10);
    if(i>offset) using(Pen div=new Pen(Palette.Divider,1)) g.DrawLine(div,x+24,y,x+CenterWidth-24,y);
    if(run>0.01) {
     using(SolidBrush bar=new SolidBrush(Fade(Palette.Accent,run))) using(GraphicsPath barPath=new GraphicsPath()) { AddRound(barPath,new RectangleF(x+12,y+19,2.5f,RowHeight-42),1.5f); g.FillPath(bar,barPath); }
    }
    // 完成圆圈（Things 式主操作）：空心 1.6px，悬停转绿并预显白勾
    double circleHover=motion.Value("circle:"+entry.Id,hoverKey=="complete"+entry.Id?1:0,120);
    RectangleF circle=new RectangleF(x+20,y+7,20,20);
    if(circleHover>0.01) using(SolidBrush soft=new SolidBrush(Fade(Palette.DoneSoft,circleHover))) g.FillEllipse(soft,circle);
    using(Pen cp=Stroke(Blend(Palette.CircleLine,Palette.Done,circleHover),1.6f)) g.DrawEllipse(cp,circle);
    if(circleHover>0.01) using(Pen tick=Stroke(Fade(Palette.Done,circleHover),1.6f))
     g.DrawLines(tick,new PointF[]{new PointF(x+25,y+17),new PointF(x+28.5f,y+20.5f),new PointF(x+35,y+12.5f)});
    Target("complete"+entry.Id,circle,"完成这件事及其小步骤（可在已完成中恢复）",delegate {CompleteEntry(entry);});
    TextAt(g,entry.Title,new RectangleF(x+50,y+5,CenterWidth-94,22),ink,13.5f,entry.Running);
    if(quickStepOwner != entry) {
     TextAt(g,String.IsNullOrEmpty(entry.NextStep)?"＋ 留下下一步":"→ "+entry.NextStep,new RectangleF(x+50,y+28,CenterWidth-94,19),String.IsNullOrEmpty(entry.NextStep)?Palette.Faint:Palette.Accent,11.5f,false);
     Target("next"+entry.Id,new RectangleF(x+44,y+27,CenterWidth-88,20),"下一步："+entry.NextStep,delegate{EditNextStep(entry);});
    } else {
     Round(g,new RectangleF(x+44,y+28,CenterWidth-88,23),Palette.Bg,6);
     RoundLine(g,new RectangleF(x+44.5f,y+28.5f,CenterWidth-89,22),6,Palette.InputBorder);
     DrawIcon(g,"submitStep"+entry.Id,"done",x+CenterWidth-41,y+26,"添加小步骤；Enter 可连续添加",delegate{SubmitQuickStep((ModifierKeys & Keys.Control)!=0);},Palette.Accent);
    }
    TextAt(g,Logic.Time(entry.Seconds),new RectangleF(x+50,y+50,58,17),Blend(Palette.Faint,Palette.Accent,run),11.5f,false,MonoFont);
    if(!child&&entry.Children.Count>0) TextAt(g,"·  "+entry.Children.Count+" 子项",new RectangleF(x+112,y+51,64,16),Palette.Faint,10.5f,false);
    Target("edit"+entry.Id,new RectangleF(x+44,y+4,CenterWidth-88,26),"双击编辑记录",delegate {});
    if(!child) DrawIcon(g,"quickStep"+entry.Id,quickStepOwner==entry?"hide":"plus",x+CenterWidth-95,y+44,quickStepOwner==entry?"收起小步骤输入（保留草稿）":"直接添加小步骤（无需展开）",delegate{ToggleQuickStep(entry);},quickStepOwner==entry?Palette.Accent:muted,quickStepOwner==entry?1:em);
    DrawIcon(g,"toggle"+entry.Id,entry.Running?"pause":"play",x+CenterWidth-68,y+44,entry.Running?"暂停本条及全部子记录":"继续",delegate {session.Toggle(entry);Changed();},entry.Running?Palette.Accent:muted,entry.Running?1:em);
    if(!child) DrawIcon(g,"children"+entry.Id,chosen?"left":"right",x+CenterWidth-41,y+44,chosen?"收起这条记录的小步骤":"展开这条记录的小步骤",delegate {ToggleSteps(entry);},chosen?Palette.Accent:muted,chosen?1:em);
   }
   if(entries.Count>MaxVisibleRows) TextAt(g,(offset+1)+"–"+end+" / "+entries.Count+"  · 滚轮翻阅",new RectangleF(x+18,55,CenterWidth-37,12),Palette.Faint,10.5f,false);
  }
  void PaintLeft(Graphics g) {
   Round(g,new RectangleF(0,0,SideWidth,LeftHeight),side,16);
   RoundLine(g,new RectangleF(0.5f,0.5f,SideWidth-1,LeftHeight-1),16,Palette.Border);
   TextAt(g,"工作台",new RectangleF(20,12,110,27),ink,15,true);
   using(Pen divider=new Pen(Palette.Border,1)){g.DrawLine(divider,20,46,170,46);g.DrawLine(divider,20,138,170,138);g.DrawLine(divider,20,244,170,244);}
   DrawIcon(g,"closeLeft","right",154,12,"收起操作",ToggleLeft,muted);
   Caption(g,"任务",52);
   MenuButton(g,"task","切换任务  ·  "+Current.Name,70,TaskMenu,true);
   MenuButton(g,"new","＋  新建任务",102,delegate {string value=Prompt("新建任务","");if(value==null)return;session.CreateTask(value);selected=null;mainScroll=childScroll=0;Changed();},true);
   MenuButton(g,"completed","已完成 "+Logic.Flatten(Current.Entries).Count(e=>e.Completed)+" · 今日 "+Logic.Flatten(Current.Entries).Count(e=>e.Completed&&e.CompletedAt.HasValue&&e.CompletedAt.Value.Date==DateTime.Today),144,ShowCompleted,true);
   MenuButton(g,"pauseAll","暂停全部记录",176,PauseAll,true);
   MenuButton(g,"undo","撤销上次删除",208,Undo,session.CanUndo);
   Caption(g,"窗口",250);
   TextAt(g,"透明度",new RectangleF(22,266,90,20),muted,11.5f,false);
   TextAt(g,(100-db.Transparency)+"%",new RectangleF(130,266,44,20),ink,11,false,MonoFont);
   // 透明度滑杆：4px 圆头轨道 + 蓝填充段 + 白芯蓝环滑块
   using(Pen track=Stroke(Palette.Track,4)) g.DrawLine(track,26,295,164,295);
   float knob=24+(100-db.Transparency)/65f*142;
   using(Pen fill=Stroke(Palette.Accent,4)) g.DrawLine(fill,26,295,Math.Max(26,knob),295);
   double slideHover=motion.Value("menu:slider",hoverKey=="slider"?1:0,120);
   float knobSize=(float)(12+2*slideHover);
   using(SolidBrush ring=new SolidBrush(Palette.Bg)) g.FillEllipse(ring,knob-knobSize/2,295-knobSize/2,knobSize,knobSize);
   using(Pen kp=new Pen(Palette.Accent,1.6f)) g.DrawEllipse(kp,knob-knobSize/2,295-knobSize/2,knobSize,knobSize);
   Target("slider",new RectangleF(18,283,154,22),"拖动调整透明度",delegate{});
   // 置顶：iOS 式拨杆（绿=开）
   RectangleF prow=new RectangleF(12,306,SideWidth-24,29);
   double pinHover=motion.Value("menu:pin",hoverKey=="pin"?1:0,120);
   if(pinHover>0.01) Round(g,prow,Fade(Palette.Hover,pinHover),8);
   TextAt(g,"窗口置顶",new RectangleF(22,306,SideWidth-40,29),ink,11.5f,false);
   double pinOn=motion.Value("switch:pin",db.Pinned?1:0,160);
   RectangleF sw=new RectangleF(128,310,38,21);
   Round(g,sw,Blend(Palette.SwitchOff,Palette.Done,pinOn),10.5f);
   float kx=(float)(sw.X+10.5+(38-21)*pinOn);
   using(SolidBrush kb=new SolidBrush(Palette.Bg)) g.FillEllipse(kb,kx-8,312.5f,16,16);
   Target("pin",prow,db.Pinned?"取消窗口置顶":"窗口置顶",delegate {session.SetPinned(!db.Pinned);TopMost=db.Pinned;Changed();});
   MenuButton(g,"backup","备份与更多  ›",338,MoreMenu,true);
   TextAt(g,notice??"已自动保存",new RectangleF(22,369,151,14),notice==null?Palette.Faint:Palette.Danger,10.5f,false);
  }
 }
}
