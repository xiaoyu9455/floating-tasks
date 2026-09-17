using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace FloatingTasks
{
    public partial class MainWindow
    {
        Icon appIcon;
        List<Entry> ActiveEntries(List<Entry> entries) { return entries.Where(e => !e.Completed).ToList(); }
        void RefreshInputHints()
        {
            string hint = db.EnterStarts ? "输入后 Enter 开始" : "先记下 · Ctrl+Enter 开始";
            foreach (TextBox box in new[] { mainInput, childInput })
            {
                if (box == null) continue;
                SendMessage(box.Handle, 0x1501, new IntPtr(1), hint);
                tip.SetToolTip(box, hint); box.AccessibleName = hint;
            }
        }

        void ClampWindow()
        {
            Rectangle area = Screen.FromPoint(Location).WorkingArea;
            Left = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
            Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
        }

        void EditNextStep(Entry entry)
        {
            string value = Prompt("接下来做什么？（留空可清除）", entry.NextStep, true);
            if (value == null) return;
            session.SetNextStep(entry, value); Changed();
        }

        void CompleteEntry(Entry entry)
        {
            session.Complete(entry);
            if (selected != null && selected.Completed) { selected = null; rightOpen = false; }
            Changed();
        }

        void DeleteEntry(Entry entry)
        {
            Entry parent = ParentOf(entry);
            session.Delete(parent == null ? Current.Entries : parent.Children, entry);
            if (selected == entry || (selected != null && Logic.Flatten(entry.Children).Contains(selected)))
            { selected = null; rightOpen = false; }
            Changed();
        }

        void CardMenuOpening(object sender, CancelEventArgs e)
        {
            ContextMenuStrip menu = (ContextMenuStrip)sender;
            while (menu.Items.Count > 0 && (string)menu.Items[0].Tag == "entryAction")
            { ToolStripItem item = menu.Items[0]; menu.Items.RemoveAt(0); item.Dispose(); }
            Point point = PointToClient(Cursor.Position);
            PointF logical = new PointF(point.X / scale, point.Y / scale);
            Entry entry = null;

            {
                Hit hit = hits.LastOrDefault(h => h.Bounds.Contains(logical)
                    && (h.Key.StartsWith("edit") || h.Key.StartsWith("next") || h.Key.StartsWith("toggle")
                        || h.Key.StartsWith("complete") || h.Key.StartsWith("children")));
                if (hit != null)
                    entry = Logic.Flatten(Current.Entries).FirstOrDefault(item => hit.Key.EndsWith(item.Id, StringComparison.Ordinal));
            }
            if (entry == null) return;
            Entry target = entry;
            var delete = new ToolStripMenuItem("删除这条及其子记录（可撤销）") { Tag = "entryAction" };
            delete.Click += delegate { DeleteEntry(target); }; menu.Items.Insert(0, delete);
            var next = new ToolStripMenuItem("留下 / 修改下一步…") { Tag = "entryAction" };
            next.Click += delegate { EditNextStep(target); }; menu.Items.Insert(0, next);
        }

        void ShowCompleted()
        {
            var entries = Logic.Flatten(Current.Entries).Where(e => e.Completed)
                .OrderByDescending(e => e.CompletedAt).ToList();
            using (var dialog = new Form
            {
                Text = Current.Name + " · 已完成", StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                ClientSize = new Size(S(440), S(330)), BackColor = side, ForeColor = ink,
                TopMost = TopMost, Icon = appIcon, AutoScaleMode = AutoScaleMode.None
            })
            {
                var label = new Label { Text = "保留每一次完成。恢复后暂停，等你准备好再继续。", ForeColor = Palette.Muted,
                    Location = new Point(S(16), S(12)), Size = new Size(S(410), S(32)) };
                var list = new ListBox { Location = new Point(S(16), S(48)), Size = new Size(S(408), S(220)),
                    BackColor = surface, ForeColor = ink, BorderStyle = BorderStyle.None,
                    Font = new Font("Microsoft YaHei UI", 10), HorizontalScrollbar = true };
                foreach (Entry entry in entries)
                    list.Items.Add((entry.CompletedAt.HasValue ? entry.CompletedAt.Value.ToString("MM-dd HH:mm") : "已完成")
                        + "  ·  " + entry.Title + "  ·  " + Logic.Time(entry.Seconds));
                if (entries.Count > 0) list.SelectedIndex = 0;
                else label.Text = "还没有已完成事项。做完后，点记录右侧的 ✓。";
                var restore = new Button { Text = "恢复选中事项", Location = new Point(S(16), S(282)),
                    Size = new Size(S(142), S(32)), Enabled = entries.Count > 0, FlatStyle = FlatStyle.Flat,
                    BackColor = Palette.Accent, ForeColor = Palette.OnAccent };
                restore.FlatAppearance.BorderSize = 0;
                restore.FlatAppearance.MouseOverBackColor = Palette.AccentDark;
                restore.Click += delegate
                {
                    if (list.SelectedIndex < 0) return;
                    session.Restore(entries[list.SelectedIndex]); dialog.Close(); Changed();
                };
                var close = new Button { Text = "关闭", Location = new Point(S(334), S(282)),
                    Size = new Size(S(90), S(32)), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat,
                    BackColor = Palette.Bg, ForeColor = Palette.Ink };
                close.FlatAppearance.BorderColor = Palette.InputBorder;
                close.FlatAppearance.MouseOverBackColor = Palette.Hover;
                dialog.Controls.AddRange(new Control[] { label, list, restore, close });
                dialog.CancelButton = close; dialog.ShowDialog(this);
            }
        }
    }
}
