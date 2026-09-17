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
  RectangleF TaskNameBounds { get { return collapsed ? new RectangleF(12,10,CenterWidth-195,26) : new RectangleF(centerX+44,10,CenterWidth-140,25); } }
  void RenameCurrentTask() {
   string value=Prompt("重命名任务",Current.Name);
   if(value!=null) {session.RenameTask(value);Changed();}
  }
  protected override void OnMouseDown(MouseEventArgs e) {
   base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
   PointF p=new PointF(e.X/scale,e.Y/scale);
   if (BeginPanelResize(p)) return;
   Hit hit=hits.LastOrDefault(h=>h.Bounds.Contains(p));
   if(hit!=null) {if(hit.Key=="slider"){sliderDrag=true;Capture=true;Slide(p.X);}else hit.Action();return;}
   if(p.Y<60){dragging=true;dragMouse=Cursor.Position;dragWindow=Location;Capture=true;}
  }
  protected override void OnMouseMove(MouseEventArgs e) {
   base.OnMouseMove(e);if(resizeEdges!=0){ResizePanelTo(Cursor.Position);return;}if(dragging){Point p=Cursor.Position;Location=new Point(dragWindow.X+p.X-dragMouse.X,dragWindow.Y+p.Y-dragMouse.Y);return;}
   if(sliderDrag){Slide(e.X/scale);return;}
   PointF logical=new PointF(e.X/scale,e.Y/scale);
   int edge=ResizeEdgesAt(logical);
   if(edge!=0){Cursor=edge==3?Cursors.SizeNWSE:edge==1?Cursors.SizeWE:Cursors.SizeNS; hoverKey=null; tip.SetToolTip(this,"拖动调整大小"); return;}
   if(hoverKey==null)Cursor=Cursors.Default;
   string row=null;
   foreach(Hit h in hits) if(h.Key.StartsWith("row")&&h.Bounds.Contains(logical)) {row=h.Key.Substring(3);break;}
   if(row!=hoverRow){hoverRow=row;Invalidate();}
   Hit hit=hits.LastOrDefault(h=>h.Bounds.Contains(logical));
   if(hit!=null&&hit.Key.StartsWith("row"))hit=null; // 行本体只驱动悬停底色，不改变光标与提示
   bool overName=TaskNameBounds.Contains(logical);
   string key=hit==null?(overName?"taskName":null):hit.Key;if(key!=hoverKey){hoverKey=key;Cursor=hit==null?Cursors.Default:Cursors.Hand;tip.SetToolTip(this,hit==null?(overName?"双击重命名任务；拖动移动窗口":"拖动顶部移动窗口"):hit.Tip);Invalidate();}
  }
  protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(resizeEdges!=0){EndPanelResize();return;}if(sliderDrag)Save();if(dragging){ClampWindow();session.SetWindowPosition(Left,Top);}dragging=sliderDrag=false;Capture=false;}
  protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hoverKey=null;hoverRow=null;Invalidate();}
  protected override void OnMouseDoubleClick(MouseEventArgs e){
   base.OnMouseDoubleClick(e);
   if(e.Button!=MouseButtons.Left)return;
   PointF point=new PointF(e.X/scale,e.Y/scale);
   if(TaskNameBounds.Contains(point)){dragging=false;Capture=false;RenameCurrentTask();return;}
   Hit hit=hits.LastOrDefault(h=>h.Bounds.Contains(point));
   if(hit!=null&&hit.Key.StartsWith("edit")){
    Entry entry=Logic.Flatten(Current.Entries).FirstOrDefault(item=>item.Id==hit.Key.Substring(4));
    if(entry!=null){string v=Prompt("编辑记录",entry.Title);if(v!=null){session.Rename(entry,v);Changed();}}
   }
  }
  protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(collapsed)return;int delta=e.Delta>0?-1:1;if(rightOpen&&selected!=null&&e.X/scale>centerX+CenterWidth){childScroll=Math.Max(0,Math.Min(Math.Max(0,ActiveEntries(selected.Children).Count-MaxVisibleRows),childScroll+delta));}else mainScroll=Math.Max(0,Math.Min(Math.Max(0,ActiveEntries(Current.Entries).Count-MaxVisibleRows),mainScroll+delta));Invalidate();}
  void Slide(float x){session.SetOpacity(100-(int)(Math.Max(0,Math.Min(1,(x-24)/142))*65));Opacity=db.Transparency/100.0;Invalidate();}
  void ToggleCollapsed(){collapsed=!collapsed;hoverKey=null;UpdateLayout(true);}
  void ToggleLeft(){collapsed=false;leftOpen=!leftOpen;UpdateLayout(true);}
  void ToggleSteps(Entry entry){
   if(!Current.Entries.Contains(entry))return;
   if(rightOpen&&selected==entry)rightOpen=false;
   else {selected=entry;rightOpen=true;childScroll=0;}
   UpdateLayout(true);
  }
  Entry ParentOf(Entry child){return child==null?null:Logic.Flatten(Current.Entries).FirstOrDefault(e=>e.Children.Contains(child));}
  void AddEntry(List<Entry> list,TextBox box,bool forceStart=false){string value=box.Text.Trim();if(value.Length==0){box.Focus();return;}session.Add(list,value,forceStart || db.EnterStarts || (ModifierKeys & Keys.Control) != 0);box.Clear();if(list==Current.Entries)mainScroll=Math.Max(0,ActiveEntries(list).Count-MaxVisibleRows);else childScroll=Math.Max(0,ActiveEntries(list).Count-MaxVisibleRows);Changed();box.Focus();}
  void Advance(){session.Advance();}
  void Changed(){mainScroll=Math.Min(mainScroll,Math.Max(0,ActiveEntries(Current.Entries).Count-MaxVisibleRows));childScroll=selected==null?0:Math.Min(childScroll,Math.Max(0,ActiveEntries(selected.Children).Count-MaxVisibleRows));Save();UpdateLayout(true);}
  void Save(){session.Flush();}
  void Undo(){session.UndoDelete();selected=null;Changed();}
 }
}
