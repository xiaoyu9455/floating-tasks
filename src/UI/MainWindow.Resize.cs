using System;
using System.Drawing;
using System.Windows.Forms;

namespace FloatingTasks {
 public partial class MainWindow {
  int CenterWidth = 272, listHeight;
  int resizeEdges, resizeWidth, resizeHeight;
  Point resizeMouse;
  int MaxVisibleRows { get { return listHeight == 0 ? 3 : Math.Max(1, (listHeight - 121) / RowHeight); } }
  int MainListHeight { get { return listHeight == 0 ? 121 + VisibleRows(Current.Entries) * RowHeight : listHeight; } }
  int RightListHeight { get { return listHeight != 0 ? listHeight : selected == null ? 175 : 121 + VisibleRows(selected.Children) * RowHeight; } }

  void FitPanelSize() {
   Rectangle area = Screen.FromPoint(Location).WorkingArea;
   int availableWidth = (int)(area.Width / scale) - (leftOpen && !collapsed ? SideWidth + Gap : 0);
   int maxWidth = (availableWidth - (rightOpen && !collapsed ? Gap : 0)) / (rightOpen && !collapsed ? 2 : 1);
   CenterWidth = Math.Max(272, Math.Min(CenterWidth, maxWidth));
   if(listHeight != 0) {
    // Reserve a usable, scrollable notes section before sizing the list.
    int maxHeight = (int)(area.Height / scale) - 32 - (notesOpen ? 260 : 0) - 16;
    listHeight = Math.Max(199, Math.Min(listHeight, maxHeight));
   }
   mainScroll = Math.Max(0, Math.Min(mainScroll, ActiveEntries(Current.Entries).Count - MaxVisibleRows));
   childScroll = selected == null ? 0 : Math.Max(0, Math.Min(childScroll, ActiveEntries(selected.Children).Count - MaxVisibleRows));
  }
  int ResizeEdgesAt(PointF p) {
   if(collapsed) return 0;
   float right = centerX + CenterWidth, bottom = MainListHeight + NotesHeight;
   if(p.X < centerX || p.X > right || p.Y < 0 || p.Y > bottom) return 0;
   if(p.X >= right - 18 && p.Y >= bottom - 18) return 3;
   if(p.X >= right - 6 && p.Y >= 16 && p.Y <= bottom - 16) return 1;
   if(p.Y >= bottom - 5 && p.X >= centerX + 16 && p.X <= right - 16) return 2;
   return 0;
  }
  bool BeginPanelResize(PointF p) {
   resizeEdges = ResizeEdgesAt(p);
   if(resizeEdges == 0) return false;
   resizeMouse = Cursor.Position; resizeWidth = CenterWidth; resizeHeight = MainListHeight;
   dragging = sliderDrag = false; Capture = true; return true;
  }
  void ResizePanelTo(Point screenPoint) {
   if((resizeEdges & 1) != 0) CenterWidth = Math.Max(272, resizeWidth + (int)Math.Round((screenPoint.X - resizeMouse.X) / scale));
   if((resizeEdges & 2) != 0) listHeight = Math.Max(199, resizeHeight + (int)Math.Round((screenPoint.Y - resizeMouse.Y) / scale));
   UpdateLayout(true); LayoutQuickStep();
  }
  void EndPanelResize() {
   if(resizeEdges == 0) return;
   resizeEdges = 0; Capture = false; Cursor = Cursors.Default;
   session.SetPanelSize(CenterWidth, listHeight == 0 ? (int?)null : listHeight);
   session.SetWindowPosition(Left, Top); Save();
  }
  protected override void OnMouseCaptureChanged(EventArgs e) {
   base.OnMouseCaptureChanged(e);
   if(!Capture && resizeEdges != 0) EndPanelResize();
  }
  void ResetPanelSize() {
   CenterWidth = 272; listHeight = 0; mainScroll = childScroll = 0;
   session.SetPanelSize(null, null); UpdateLayout(true); Save();
  }
  void PaintResizeGrip(Graphics g) {
   float x = centerX + CenterWidth - 7, y = MainListHeight + NotesHeight - 7;
   using(Pen p = new Pen(Palette.Muted, 1))
    for(int i = 4; i <= 10; i += 3) g.DrawLine(p, x-i, y, x, y-i);
  }
 }
}