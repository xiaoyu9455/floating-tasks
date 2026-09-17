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
  readonly System.ComponentModel.Container popupMenus = new System.ComponentModel.Container();
  bool disposingMenus;
  ContextMenuStrip MakeMenu(){
   var font = new Font("Microsoft YaHei UI",9);
   var menu = new ContextMenuStrip(popupMenus){BackColor=side,ForeColor=ink,ShowImageMargin=false,Font=font,Renderer=new DarkMenuRenderer()};
   menu.Disposed += delegate { font.Dispose(); };
   menu.Closed += delegate {
    // WinForms still accesses the menu after Closed while completing the click.
    // Keep it alive until that message has fully unwound.
    if (!disposingMenus && !IsDisposed && !Disposing && IsHandleCreated)
     BeginInvoke((Action)delegate { if (!menu.IsDisposed) menu.Dispose(); });
   };
   return menu;
  }
  void TaskMenu(){ContextMenuStrip m=MakeMenu();for(int i=0;i<db.Tasks.Count;i++){int index=i;ToolStripMenuItem item=new ToolStripMenuItem(db.Tasks[i].Name){Checked=i==db.Selected};item.Click+=delegate{session.Switch(index);selected=null;mainScroll=childScroll=0;Changed();};m.Items.Add(item);}m.Show(this,new Point(S(20),S(80)));}
  void MoreMenu(){ContextMenuStrip m=MakeMenu();m.Items.Add(db.EnterStarts?"✓ Enter 立即开始（点击改为先记下）":"Enter 先记下（点击改为立即开始）",null,delegate{session.SetEnterStarts(!db.EnterStarts);RefreshInputHints();Changed();});m.Items.Add(db.ExclusiveFocus?"✓ 单项专注（点击改为并行计时）":"并行计时（点击改为单项专注）",null,delegate{session.SetExclusiveFocus(!db.ExclusiveFocus);Changed();});m.Items.Add("已完成事项…",null,delegate{ShowCompleted();});m.Items.Add(new ToolStripSeparator());m.Items.Add("导出全部任务…",null,delegate{Export();});m.Items.Add("导入备份…",null,delegate{Import();});m.Items.Add("隐藏到托盘（继续计时）",null,delegate{Hide();});m.Items.Add("退出并暂停",null,delegate{exiting=true;Close();});m.Show(Cursor.Position);}
  string Prompt(string title,string value,bool allowEmpty=false){
   using(Form f=new Form{Text=title,FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.CenterParent,BackColor=Palette.Bg,ForeColor=ink,ClientSize=new Size(S(270),S(130)),TopMost=TopMost,AutoScaleMode=AutoScaleMode.None}){
    using(GraphicsPath p=new GraphicsPath()){AddRound(p,new RectangleF(0,0,f.Width,f.Height),S(14));f.Region=new Region(p);}
    Label label=new Label{Text=title,Location=new Point(S(18),S(14)),Size=new Size(S(230),S(23)),Font=new Font("Microsoft YaHei UI",S(13),FontStyle.Bold,GraphicsUnit.Pixel)};f.Controls.Add(label);
    TextBox box=new TextBox{Text=value,BorderStyle=BorderStyle.FixedSingle,BackColor=Palette.Bg,ForeColor=ink,Location=new Point(S(18),S(49)),Size=new Size(S(233),S(25)),Font=new Font("Microsoft YaHei UI",S(13),FontStyle.Regular,GraphicsUnit.Pixel),MaxLength=250};f.Controls.Add(box);
    Button cancel=new Button{Text="取消",FlatStyle=FlatStyle.Flat,ForeColor=muted,BackColor=Palette.Bg,Location=new Point(S(108),S(86)),Size=new Size(S(65),S(28)),DialogResult=DialogResult.Cancel};cancel.FlatAppearance.BorderColor=Palette.InputBorder;cancel.FlatAppearance.MouseOverBackColor=Palette.Hover;f.Controls.Add(cancel);
    Button ok=new Button{Text="确定",FlatStyle=FlatStyle.Flat,ForeColor=Palette.OnAccent,BackColor=Palette.Accent,Location=new Point(S(182),S(86)),Size=new Size(S(70),S(28))};ok.FlatAppearance.BorderSize=0;ok.FlatAppearance.MouseOverBackColor=Palette.AccentDark;ok.Click+=delegate{if(allowEmpty || !String.IsNullOrWhiteSpace(box.Text))f.DialogResult=DialogResult.OK;};f.Controls.Add(ok);f.AcceptButton=ok;f.CancelButton=cancel;f.Shown+=delegate{box.Focus();box.SelectAll();};return f.ShowDialog(this)==DialogResult.OK?box.Text.Trim():null;
   }
  }
  void Export(){using(SaveFileDialog d=new SaveFileDialog{Filter="JSON 备份|*.json",FileName="浮记备份-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json"})if(d.ShowDialog(this)==DialogResult.OK){try{Advance();File.WriteAllText(d.FileName,TaskJson.Encode(db),Encoding.UTF8);}catch(Exception ex){MessageBox.Show(this,ex.Message,"导出失败");}}}
  void Import(){using(OpenFileDialog d=new OpenFileDialog{Filter="JSON 备份|*.json"})if(d.ShowDialog(this)==DialogResult.OK){try{Database incoming=TaskJson.Decode(File.ReadAllText(d.FileName,Encoding.UTF8));session.Import(incoming);selected=null;mainScroll=childScroll=0;Changed();}catch(Exception ex){MessageBox.Show(this,ex.Message,"导入失败");}}}
 }
}
