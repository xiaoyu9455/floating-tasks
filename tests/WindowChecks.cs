using System;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FloatingTasks
{
    public partial class MainWindow
    {
        public void CheckMenuLifetime()
        {
            timer.Stop();
            var handle = Handle;
            session.CreateTask("菜单切换测试");
            session.Switch(0);
            for (int i = 0; i < 3; i++) {
                TaskMenu();
                var menu = popupMenus.Components.OfType<ContextMenuStrip>().Last();
                menu.Items[1].PerformClick();
                if (menu.Visible) menu.Close(ToolStripDropDownCloseReason.ItemClicked);
                Require(!menu.IsDisposed, "Menu survives the closing click stack");
                Require(session.Database.Selected == 1, "Task menu click switches task");
                Application.DoEvents();
                Require(menu.IsDisposed, "Closed menu is disposed on next UI turn");
                session.Switch(0);
            }
            MoreMenu();
            var more = popupMenus.Components.OfType<ContextMenuStrip>().Last();
            bool previous = db.EnterStarts;
            more.Items[0].PerformClick();
            if (more.Visible) more.Close(ToolStripDropDownCloseReason.ItemClicked);
            Require(db.EnterStarts != previous && !more.IsDisposed, "More menu action completes before disposal");
            Application.DoEvents();
            Require(more.IsDisposed && popupMenus.Components.Count == 0, "Temporary menus do not accumulate");
        }

        public void CheckInterface()
        {
            timer.Stop(); Opacity = 1;
            session.CreateTask("方案设计");
            Entry root = session.Add(Current.Entries, "设计桌面悬浮窗");
            root.Seconds = 1235;
            session.Add(root.Children, "调整卡片与透明度").Seconds = 428;
            Entry child = session.Add(root.Children, "检查折叠交互");
            child.Seconds = 182; session.Toggle(child);
            Entry second = session.Add(Current.Entries, "记录灵感");
            second.Seconds = 305; session.Toggle(second);
            Current.Seconds = 1235;
            UpdateLayout(false);
            CaptureTest("preview-compact.png");
            int anchor = Left + S(centerX);
            ToggleLeft();
            Require(Left + S(centerX) == anchor, "Left drawer moved center");
            ToggleSteps(root);
            CaptureTest("preview-expanded.png");
            Require(FormBorderStyle == FormBorderStyle.None && rightOpen, "Borderless drawers");
            Require(childInput.Bounds.X > mainInput.Bounds.Right, "Child input bounds");
            Require(!Region.IsVisible(S(centerX - Gap / 2), S(20)), "Drawer gap must not capture clicks");
            Require(TransparencyKey.IsEmpty, "No chroma key at antialiased edges");
            int active = Logic.Flatten(Current.Entries).Count(e => e.Running);
            Require(!hits.Any(hit=>hit.Key=="right"),"No task-level steps button");
            ToggleSteps(second);
            Require(rightOpen && selected==second && selected.Children.Count==0,"Each record shows its own steps");
            ToggleSteps(second);
            Require(!rightOpen,"Same record button collapses steps");
            ToggleSteps(root);
            Require(rightOpen && selected==root && selected.Children.Count==2,"Reopening preserves this record's steps");
            ToggleLeft(); ToggleSteps(root);
            Require(Logic.Flatten(Current.Entries).Count(e => e.Running) == active, "Folding changed timers");
            Require(Width == S(CenterWidth), "Compact width");
            // Exercise rendering at 200% DPI and overflow without touching user data.
            scale = 2;
            for(int i=0;i<5;i++)session.Add(Current.Entries,"更多记录 " + i);
            UpdateLayout(false); mainScroll = 3;
            CaptureTest("preview-dpi.png");
            Require(mainInput.Bottom <= Height, "High-DPI input bounds");
            Require(hits.Count(h=>h.Key.StartsWith("edit")) == 3, "Only three entries are visible");
            OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,S(100),S(100),-120));
            CaptureTest("preview-three-last.png");
            Require(hits.Any(h=>h.Key=="edit"+Current.Entries.Last().Id), "Scrolling reaches last entry");
            Require(Height==S(76+3*RowHeight+45+NotesHeight), "List height is capped at three rows");
            BeginQuickStep(Current.Entries.Last()); quickStepInput.Text="折叠前草稿";
            ToggleLeft(); ToggleSteps(root);
            int runningBeforeFold=Logic.Flatten(Current.Entries).Count(e=>e.Running);
            ClampWindow(); // Hidden test windows also need an on-screen starting position.
            CaptureTest("preview-before-fold.png");
            RectangleF foldBounds=hits.Single(h=>h.Key=="collapse").Bounds;
            Point foldScreen=PointToScreen(new Point(S(foldBounds.X),S(foldBounds.Y)));
            hits.Single(h=>h.Key=="collapse").Action();
            CaptureTest("preview-one-line-dpi.png");
            RectangleF collapsedButton=hits.Single(h=>h.Key=="collapse").Bounds;
            Require(PointToScreen(new Point(S(collapsedButton.X),S(collapsedButton.Y)))==foldScreen, "Fold button stays at the same screen position: "+foldScreen+" -> "+PointToScreen(new Point(S(collapsedButton.X),S(collapsedButton.Y))));
            Require(Height==S(CollapsedHeight) && Width==S(CenterWidth), "Fold resizes entire window to one line");
            Require(!mainInput.Visible && !childInput.Visible && !quickStepInput.Visible, "Fold hides all inputs");
            Require(!hits.Any(h=>h.Key.StartsWith("edit") || h.Key.StartsWith("add")), "Fold has no hidden list targets");
            Require(Logic.Flatten(Current.Entries).Count(e=>e.Running)==runningBeforeFold, "Fold preserves running timers");
            RectangleF unfoldBounds=hits.Single(h=>h.Key=="collapse").Bounds;
            Point unfoldScreen=PointToScreen(new Point(S(unfoldBounds.X),S(unfoldBounds.Y)));
            hits.Single(h=>h.Key=="collapse").Action();
            CaptureTest("preview-unfolded.png");
            RectangleF expandedButton=hits.Single(h=>h.Key=="collapse").Bounds;
            Require(PointToScreen(new Point(S(expandedButton.X),S(expandedButton.Y)))==unfoldScreen, "Unfold button stays at the same screen position");
            Require(leftOpen && rightOpen && quickStepInput.Text=="折叠前草稿", "Unfold restores panels and draft");
            scale=1; ToggleCollapsed(); CaptureTest("preview-one-line.png"); ToggleCollapsed();
            // Drive actual input key handlers with synthetic KeyEventArgs.
            scale = 1; mainScroll = childScroll = 0;
            session.CreateTask("专注体验"); selected = null; rightOpen = leftOpen = false;
            session.SetEnterStarts(false); session.SetExclusiveFocus(true); RefreshInputHints();
            mainInput.Text = "写完方案";
            typeof(Control).GetMethod("OnKeyDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                .Invoke(mainInput,new object[]{new KeyEventArgs(Keys.Enter)});
            Entry focus = Current.Entries.Last();
            Require(!focus.Running && mainInput.Text == "", "Enter captures without starting");
            mainInput.Text = "整理报价";
            typeof(Control).GetMethod("OnKeyDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                .Invoke(mainInput,new object[]{new KeyEventArgs(Keys.Control|Keys.Enter)});
            Entry immediate = Current.Entries.Last();
            Require(immediate.Running && !focus.Running, "Ctrl Enter captures and starts");
            session.SetNextStep(immediate, "接着补充三家供应商的价格");
            session.Complete(focus); Changed();
            CaptureTest("preview-next-step.png");
            Require(!hits.Any(h=>h.Key=="edit"+focus.Id), "Completed entry is hidden from active list");
            session.Restore(focus); Changed();
            CaptureTest("preview-restored.png");
            Require(hits.Any(h=>h.Key=="edit"+focus.Id), "Restored entry is visible again");
            Require(Width==S(CenterWidth) && !hits.Any(h=>h.Key=="mini"), "Normal list without focus strip");
            Require(appIcon != null && tray.Icon == appIcon && Icon == appIcon, "Custom icon used by tray and window");
            session.SetEnterStarts(false); mainScroll=childScroll=0; UpdateLayout(false);
            CaptureTest("preview-before-quick-step.png");
            hits.Single(h=>h.Key=="quickStep"+immediate.Id).Action();
            Require(!rightOpen && quickStepOwner==immediate, "Quick add stays in the current card");
            quickStepInput.Text="核对付款条件";
            typeof(Control).GetMethod("OnKeyDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                .Invoke(quickStepInput,new object[]{new KeyEventArgs(Keys.Enter)});
            Require(immediate.Children.Last().Title=="核对付款条件" && !immediate.Children.Last().Running
                && quickStepInput.Text=="" && quickStepOwner==immediate, "Quick add supports repeated entries under correct parent");
            quickStepInput.Text="未写完的内容";
            BeginQuickStep(focus); BeginQuickStep(immediate);
            Require(quickStepInput.Text=="未写完的内容", "Switching quick add keeps per-record drafts");
            CaptureTest("preview-quick-step.png");
            Require(hits.Single(h=>h.Key=="quickStep"+immediate.Id).Tip.StartsWith("收起"), "Expanded quick add offers collapse");
            hits.Single(h=>h.Key=="quickStep"+immediate.Id).Action();
            Require(quickStepOwner==null && !quickStepInput.Visible, "Minus closes quick add");
            CaptureTest("preview-quick-step-closed.png");
            Require(hits.Single(h=>h.Key=="quickStep"+immediate.Id).Tip.StartsWith("直接添加"), "Collapsed quick add offers expansion");
            hits.Single(h=>h.Key=="quickStep"+immediate.Id).Action();
            Require(quickStepOwner==immediate && quickStepInput.Text=="未写完的内容", "Plus reopens with preserved draft");
            ToggleSteps(immediate); CaptureTest("preview-steps-panel.png");
            Require(!immediate.Children.Any(c=>hits.Any(h=>h.Key=="quickStep"+c.Id||h.Key=="children"+c.Id)), "Steps panel offers no deeper nesting");
            hits.Single(h=>h.Key=="quickStep"+immediate.Id).Action();
            BeginQuickStep(immediate.Children.Last());
            Rectangle ignored; Require(quickStepOwner==null&&!QuickStepBounds(out ignored), "Quick add ignores step-level records");
            scale=2; BeginQuickStep(immediate); UpdateLayout(false); CaptureTest("preview-quick-step-dpi.png");
            Rectangle qb; bool shown=QuickStepBounds(out qb);
            Require(shown&&quickStepInput.Right<=Width&&quickStepInput.Bottom<=Height, "Quick add fits at 200 percent DPI");
            scale=1; leftOpen=rightOpen=false; quickStepOwner=null; UpdateLayout(false);
            Show(); Application.DoEvents(); int noteClosedHeight=Height;
            ToggleNotes(); noteInput.Text="开发便签\r\n先验证接口，再完善样式。"; noteInput.Modified=true;
            SaveNoteInput();
            Require(db.Notes.Count==1 && !noteInput.Modified,"Manual note saves through session");
            CaptureTest("preview-notes.png");
            Require(Height==noteClosedHeight+S(260) && noteList.Bottom<=notesPanel.Height,"Notes expand downwards and fit");
            noteInput.Text="尚未保存的草稿"; noteInput.Modified=true;
            ToggleNotes(); ToggleNotes();
            Require(noteInput.Text=="尚未保存的草稿","Closing notes preserves editor draft");
            var api=new AgentTasks(session);
            ExecuteAgentCall(api,"add_note",new System.Collections.Generic.Dictionary<string,object>{{"content","模型记录的便签"}});
            Require(noteList.Items.Count==2 && noteInput.Text=="尚未保存的草稿","MCP refreshes notes without overwriting draft");
            ToggleCollapsed();
            Require(!notesPanel.Visible,"Window folding hides notes");
            ToggleCollapsed(); scale=2; UpdateLayout(false);
            CaptureTest("preview-notes-dpi.png");
            Require(notesPanel.Right<=Width && notesPanel.Bottom<=Height,"Notes fit at 200 percent DPI");
            session.PauseAll();
        }
        public void CheckPanelResize()
        {
            scale=1; collapsed=leftOpen=rightOpen=notesOpen=false; quickStepOwner=null;
            ResetPanelSize(); Location=new Point(20,20);
            session.CreateTask("拖拽尺寸测试");
            for(int i=0;i<10;i++) session.Add(Current.Entries,"可调整宽度的长记录标题与下一步说明 " + i);
            UpdateLayout(false); CaptureTest("preview-resize-before.png");
            int initialHeight=MainListHeight;
            Require(ResizeEdgesAt(new PointF(CenterWidth-3,100))==1,"Right edge resizes width");
            Require(ResizeEdgesAt(new PointF(100,MainListHeight+NotesHeight-3))==2,"Bottom edge resizes height");
            Require(ResizeEdgesAt(new PointF(100,20))==0,"Title keeps window dragging");
            OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,S(CenterWidth-12),S(MainListHeight+NotesHeight-12),0));
            Require(resizeEdges==3 && Capture,"Corner starts resize and captures mouse");
            ResizePanelTo(new Point(resizeMouse.X+100,resizeMouse.Y+RowHeight*2));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,0,0,0));
            CaptureTest("preview-resized.png");
            Require(CenterWidth==372 && MainListHeight==initialHeight+156,"Drag changes both dimensions");
            Require(hits.Count(h=>h.Key.StartsWith("edit"))==5,"Taller window shows five records");
            Require(mainInput.Width==S(CenterWidth-78),"Input follows new width");
            Require(db.PanelWidth==372 && db.PanelListHeight==MainListHeight,"Mouse release saves size");
            int keptHeight=Height; Changed();
            Require(Height==keptHeight,"Refresh does not reset custom height");
            ToggleCollapsed(); Require(Height==S(CollapsedHeight),"Custom window folds to one line");
            ToggleCollapsed(); Require(Height==keptHeight && CenterWidth==372,"Unfold restores resized dimensions");
            OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,100,100,-120));
            for(int i=0;i<10;i++) OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,100,100,-120));
            CaptureTest("preview-resized-scroll.png");
            Require(hits.Any(h=>h.Key=="edit"+Current.Entries.Last().Id),"Resize scrolling reaches last record");
            var saved=TaskJson.Decode(TaskJson.Encode(db));
            using(var restored=new MainWindow(new TaskSession(new FloatingTasks.Tests.StaticRepository(saved),new FloatingTasks.Tests.FakeClock()))) {
                Require(restored.CenterWidth==372 && restored.listHeight==listHeight,"Size survives serialization and window recreation");
            }
            mainScroll=0; BeginQuickStep(Current.Entries[0]);
            ToggleNotes(); CaptureTest("preview-resized-notes.png");
            Require(noteInput.Width==S(CenterWidth-44) && noteList.Right<=notesPanel.Width,"Notes expand with panel width");
            Require(quickStepInput.Width==S(CenterWidth-100),"Quick step input expands");
            ToggleSteps(Current.Entries[0]); CaptureTest("preview-resized-steps.png");
            Require(childInput.Width==mainInput.Width && childInput.Right<Width,"Steps panel follows width");
            rightOpen=notesOpen=false; UpdateLayout(false);
            scale=2; UpdateLayout(false); CaptureTest("preview-resized-dpi.png");
            Require(mainInput.Right<=Width && mainInput.Bottom<=Height,"Resized inputs fit at high DPI");
            scale=1; UpdateLayout(false);
            OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,S(CenterWidth-12),S(MainListHeight+NotesHeight-12),0));
            ResizePanelTo(new Point(resizeMouse.X-10000,resizeMouse.Y-10000));
            Capture=false;
            Require(resizeEdges==0 && CenterWidth==272 && listHeight==199,"Minimum size and capture loss handled");
            CaptureTest("preview-resize-minimum.png");
            ResetPanelSize();
            Require(db.PanelWidth==null && db.PanelListHeight==null && MaxVisibleRows==3,"Reset restores automatic compact layout");
            session.PauseAll();
        }
        private static void Require(bool ok, string message) { if(!ok)throw new Exception(message); }
        private void CaptureTest(string name)
        {
            using(var bitmap = new Bitmap(Width,Height))
            {
                DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));
                // Standalone preview: use the actual window region as the alpha mask.
                for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)
                    if(!Region.IsVisible(x,y))bitmap.SetPixel(x,y,Color.Transparent);
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name),System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}
