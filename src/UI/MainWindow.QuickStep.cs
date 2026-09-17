using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FloatingTasks
{
    public partial class MainWindow
    {
        TextBox quickStepInput;
        Entry quickStepOwner;
        readonly Dictionary<string,string> stepDrafts = new Dictionary<string,string>();

        void InitializeQuickStep()
        {
            quickStepInput = MakeInput("小步骤 · Enter 添加", delegate(bool startNow) { SubmitQuickStep(startNow); });
            quickStepInput.Visible = false;
            quickStepInput.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if(e.KeyCode == Keys.Escape)
                {
                    if(quickStepOwner != null) stepDrafts.Remove(quickStepOwner.Id);
                    quickStepOwner = null; quickStepInput.Visible = false; Invalidate();
                }
            };
            Controls.Add(quickStepInput);
        }

        void ToggleQuickStep(Entry entry)
        {
            if(quickStepOwner != entry) { BeginQuickStep(entry); return; }
            stepDrafts[entry.Id] = quickStepInput.Text;
            quickStepOwner = null;
            quickStepInput.Visible = false;
            Invalidate();
        }

        void BeginQuickStep(Entry entry)
        {
            if(!Current.Entries.Contains(entry)) return;
            if(quickStepOwner != null) stepDrafts[quickStepOwner.Id] = quickStepInput.Text;
            quickStepOwner = entry;
            string draft;
            quickStepInput.Text = stepDrafts.TryGetValue(entry.Id, out draft) ? draft : "";
            SendMessage(quickStepInput.Handle,0x1501,new IntPtr(1),"小步骤 · Enter 添加");
            LayoutQuickStep(); Invalidate(); quickStepInput.Focus();
            quickStepInput.SelectionStart = quickStepInput.TextLength;
        }

        void SubmitQuickStep(bool startNow)
        {
            if(quickStepOwner == null || quickStepOwner.Completed
                || !Logic.Flatten(Current.Entries).Contains(quickStepOwner)) return;
            string title = quickStepInput.Text.Trim();
            if(title.Length == 0) return;
            session.Add(quickStepOwner.Children,title,startNow || db.EnterStarts);
            stepDrafts.Remove(quickStepOwner.Id); quickStepInput.Clear();
            Changed();
            SendMessage(quickStepInput.Handle,0x1501,new IntPtr(1),"已添加 · 继续输入");
            quickStepInput.Focus();
        }

        bool QuickStepBounds(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if(collapsed || quickStepOwner == null || quickStepOwner.Completed) return false;
            var roots = ActiveEntries(Current.Entries);
            int index = roots.IndexOf(quickStepOwner);
            int x = centerX, offset = mainScroll;
            if(index < 0 && rightOpen && selected != null)
            {
                index = ActiveEntries(selected.Children).IndexOf(quickStepOwner);
                x = centerX + CenterWidth + Gap; offset = childScroll;
            }
            if(index < offset || index >= offset + MaxVisibleRows) return false;
            bounds = new Rectangle(S(x+50),S(66+(index-offset)*RowHeight+30),S(CenterWidth-100),S(19));
            return true;
        }

        void LayoutQuickStep()
        {
            if(quickStepInput == null) return;
            Rectangle bounds;
            bool shown = QuickStepBounds(out bounds);
            if(shown && quickStepInput.Bounds != bounds) quickStepInput.Bounds = bounds;
            quickStepInput.Visible = shown;
            if(shown) quickStepInput.BringToFront();
        }
    }
}

