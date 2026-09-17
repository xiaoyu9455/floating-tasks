using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace FloatingTasks
{
    public interface IClock { double Seconds { get; } }
    public sealed class MonotonicClock : IClock
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        public double Seconds { get { return watch.Elapsed.TotalSeconds; } }
    }

    // All user mutations pass through this session, independently of the UI.
    public sealed partial class TaskSession
    {
        private readonly ITaskRepository repository;
        private readonly IClock clock;
        private double last;
        private bool dirty;
        private DeletedEntry undo;
        private sealed class DeletedEntry
        {
            public List<Entry> Owner;
            public Entry Entry;
            public int Index;
        }

        public Database Database { get; private set; }
        public TaskItem Current { get { return Database.Tasks[Database.Selected]; } }
        public string Notice { get; private set; }
        public bool CanUndo { get { return undo != null; } }
        public bool HasRunning { get { return Logic.Flatten(Current.Entries).Any(e => e.Running && !e.Completed); } }

        public TaskSession(ITaskRepository repository, IClock clock)
        {
            this.repository = repository;
            this.clock = clock;
            Database = repository.Load();
            foreach (TaskItem task in Database.Tasks)
            {
                dirty |= Logic.FlattenDeepSteps(task);
                dirty |= Logic.Flatten(task.Entries).Any(e => e.Running && !e.Completed);
                Logic.Pause(task);
            }
            last = clock.Seconds;
            Notice = repository.RecoveryNotice;
        }

        public void Advance()
        {
            double now = clock.Seconds;
            double elapsed = Math.Max(0, now - last);
            last = now;
            if (elapsed == 0 || !HasRunning) return;
            Logic.Advance(Current, elapsed);
            dirty = true;
        }

        public void ResumeClock() { last = clock.Seconds; }

        public bool Flush()
        {
            if (!dirty) return true;
            try { repository.Save(Database); dirty = false; Notice = null; return true; }
            catch (Exception ex) { Notice = "保存失败：" + ex.Message; return false; }
        }

        private void Commit() { dirty = true; Flush(); }
        public bool PersistForAgent() { dirty = true; return Flush(); }

        public Entry Add(List<Entry> owner, string title, bool start = true)
        {
            if (String.IsNullOrWhiteSpace(title)) throw new ArgumentException("记录不能为空。");
            if (owner != Current.Entries && !Current.Entries.Any(e => object.ReferenceEquals(e.Children, owner)))
                throw new ArgumentException("细分步骤不能继续拆分，只能在子记录下添加。");
            Advance();
            if (start && Database.ExclusiveFocus) Logic.Pause(Current);
            var entry = new Entry { Title = title.Trim(), Running = start };
            owner.Add(entry);
            if (start) Current.LastEntryId = entry.Id;
            Commit();
            return entry;
        }

        public List<Entry> AddPlannedSteps(TaskItem task, List<Entry> owner, List<string> titles)
        {
            if (!Database.Tasks.Contains(task) || !(owner == task.Entries || task.Entries.Any(e => object.ReferenceEquals(e.Children, owner))))
                throw new ArgumentException("记录列表不属于该任务，或细分步骤不能继续拆分。");
            if (titles == null || titles.Count == 0 || titles.Any(String.IsNullOrWhiteSpace))
                throw new ArgumentException("步骤不能为空。");
            Advance();
            var entries = titles.Select(t => new Entry { Title = t.Trim(), Running = false }).ToList();
            owner.AddRange(entries);
            Commit();
            return entries;
        }

        public void PauseEntry(Entry entry)
        {
            Advance();
            foreach (var item in Logic.Flatten(new[] { entry })) item.Running = false;
            Commit();
        }

        public void Toggle(Entry entry)
        {
            if (entry.Completed) return;
            Advance();
            bool start = !entry.Running;
            if (start && Database.ExclusiveFocus) Logic.Pause(Current);
            if (start) entry.Running = true;
            else
                foreach (Entry item in Logic.Flatten(new[] { entry })) item.Running = false;
            Current.LastEntryId = entry.Id;
            Commit();
        }

        public void Complete(Entry entry, TaskItem task = null)
        {
            task = task ?? Current;
            if (entry.Completed) return;
            Advance();
            string batch = Guid.NewGuid().ToString();
            DateTime now = DateTime.Now;
            foreach (Entry item in Logic.Flatten(new[] { entry }))
            {
                item.Running = false;
                if (item.Completed) continue;
                item.Completed = true; item.CompletedAt = now; item.CompletionBatch = batch;
            }
            task.LastEntryId = entry.Id;
            Commit();
        }

        public void Restore(Entry entry, TaskItem task = null)
        {
            task = task ?? Current;
            Advance();
            string batch = entry.CompletionBatch;
            foreach (Entry item in Logic.Flatten(new[] { entry }))
                if (item == entry || (!String.IsNullOrEmpty(batch) && item.CompletionBatch == batch))
                    Reopen(item);
            // A restored child must remain reachable from the active list.
            Entry parent = Logic.Flatten(task.Entries).FirstOrDefault(e => e.Children.Contains(entry));
            while (parent != null)
            {
                if (parent.Completed) Reopen(parent);
                Entry child = parent;
                parent = Logic.Flatten(task.Entries).FirstOrDefault(e => e.Children.Contains(child));
            }
            task.LastEntryId = entry.Id;
            Commit();
        }

        private static void Reopen(Entry item)
        {
            item.Completed = false; item.CompletedAt = null;
            item.CompletionBatch = ""; item.Running = false;
        }

        public void SetNextStep(Entry entry, string value, TaskItem task = null)
        {
            task = task ?? Current;
            entry.NextStep = (value ?? "").Trim();
            task.LastEntryId = entry.Id;
            Commit();
        }
        public void SetEnterStarts(bool value) { Database.EnterStarts = value; Commit(); }
        public void SetExclusiveFocus(bool value)
        {
            Advance();
            if (value) Logic.Pause(Current);
            Database.ExclusiveFocus = value; Commit();
        }
        public void SetPanelSize(int? width, int? height) { Database.PanelWidth = width; Database.PanelListHeight = height; Commit(); }
        public void SetWindowPosition(int x, int y) { Database.WindowX = x; Database.WindowY = y; Commit(); }
        public void Rename(Entry entry, string title) { entry.Title = title.Trim(); Commit(); }
        public void RenameTask(string title, TaskItem task = null) { (task ?? Current).Name = title.Trim(); Commit(); }

        public void Delete(List<Entry> owner, Entry entry)
        {
            Advance();
            int index = owner.IndexOf(entry);
            if (index < 0) return;
            owner.RemoveAt(index);
            undo = new DeletedEntry { Owner = owner, Entry = entry, Index = index };
            Commit();
        }

        // Reinsert just the removed subtree: later edits and new records survive.
        public void UndoDelete()
        {
            if (undo == null) return;
            Advance();
            foreach (Entry entry in Logic.Flatten(new[] { undo.Entry })) entry.Running = false;
            undo.Owner.Insert(Math.Min(undo.Index, undo.Owner.Count), undo.Entry);
            undo = null;
            Commit();
        }

        public void Switch(int index)
        {
            if (index < 0 || index >= Database.Tasks.Count) throw new ArgumentOutOfRangeException("index");
            if (index == Database.Selected) return;
            Advance(); Logic.Pause(Current);
            Database.Selected = index; undo = null; Commit();
        }

        public void CreateTask(string title, bool select = true)
        {
            Advance(); if (select) Logic.Pause(Current);
            Database.Tasks.Add(new TaskItem { Name = title.Trim() });
            if (select) { Database.Selected = Database.Tasks.Count - 1; undo = null; } Commit();
        }

        public void Import(Database incoming)
        {
            Advance(); Logic.Pause(Current);
            int index = Database.Tasks.Count;
            foreach (TaskItem task in incoming.Tasks)
            {
                task.Id = Guid.NewGuid().ToString();
                foreach (Entry entry in Logic.Flatten(task.Entries))
                {
                    entry.Running = false;
                    string previousId = entry.Id;
                    entry.Id = Guid.NewGuid().ToString();
                    if (task.LastEntryId == previousId) task.LastEntryId = entry.Id;
                }
                Logic.FlattenDeepSteps(task);
                Database.Tasks.Add(task);
            }
            foreach (Note note in incoming.Notes) {
                Database.Notes.Add(new Note { Content = note.Content, Source = note.Source, Created = note.Created, Updated = note.Updated });
            }
            Database.Selected = index; undo = null; Commit();
        }

        public void PauseAll()
        {
            Advance();
            foreach (TaskItem task in Database.Tasks)
            {
                dirty |= Logic.Flatten(task.Entries).Any(e => e.Running && !e.Completed);
                Logic.Pause(task);
            }
            Flush();
        }

        public void SetOpacity(int value) { Database.Transparency = Math.Max(35, Math.Min(100, value)); dirty = true; }
        public void SetPinned(bool value) { Database.Pinned = value; Commit(); }
    }
}
