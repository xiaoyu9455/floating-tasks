using System;
using System.Collections.Generic;
using System.Linq;

namespace FloatingTasks {
 public class Entry {
  public string Id = Guid.NewGuid().ToString();
  public string Title = "";
  public DateTime Created = DateTime.Now;
  public double Seconds;
  public bool Running = true;
  public bool Completed;
  public DateTime? CompletedAt;
  public string CompletionBatch = "";
  public string NextStep = "";
  public bool Expanded = true;
  public List<Entry> Children = new List<Entry>();
 }
 public class TaskItem {
  public string Id = Guid.NewGuid().ToString();
  public string Name = "新任务";
  public string LastEntryId = "";
  public double Seconds;
  public List<Entry> Entries = new List<Entry>();
 }
 public class Note {
  public string Id = Guid.NewGuid().ToString();
  public string Content = "";
  public string Source = "manual";
  public DateTime Created = DateTime.Now;
  public DateTime Updated = DateTime.Now;
  public override string ToString() { return Created.ToString("MM-dd HH:mm") + " · " + Content.Replace("\r", " ").Replace("\n", " "); }
 }
 public class Database {
  public List<Note> Notes = new List<Note>();
  public int Version = 1;
  public int Selected;
  public int Transparency = 94;
  public bool Pinned = true;
  public bool EnterStarts = true;
  public bool ExclusiveFocus;
  public int? WindowX;
  public int? WindowY;
  public int? PanelWidth;
  public int? PanelListHeight;
  public List<TaskItem> Tasks = new List<TaskItem>();
 }
 public static class Logic {
  public static IEnumerable<Entry> Flatten(IEnumerable<Entry> entries) {
   foreach (Entry e in entries) { yield return e; foreach (Entry child in Flatten(e.Children)) yield return child; }
  }
  public static void Advance(TaskItem task, double seconds) {
   if (seconds < 0) return;
   var active = Flatten(task.Entries).Where(e => e.Running && !e.Completed).ToList();
   foreach (Entry e in active) e.Seconds += seconds;
   if (active.Count > 0) task.Seconds += seconds;
  }
  public static void Pause(TaskItem task) { foreach (Entry e in Flatten(task.Entries)) e.Running = false; }
  // 结构约定：任务 → 子记录 → 细分步骤，最多两级。旧数据里更深层级在加载时上提为同级步骤。
  public static bool FlattenDeepSteps(TaskItem task) {
   bool changed = false;
   foreach (Entry entry in task.Entries)
    if (entry.Children.Any(child => child.Children.Count > 0)) {
     List<Entry> flat = Flatten(entry.Children).ToList();
     foreach (Entry item in flat) item.Children = new List<Entry>();
     entry.Children = flat; changed = true;
    }
   return changed;
  }
  public static string Time(double seconds) {
   long n = (long)Math.Max(0, seconds);
   return String.Format("{0:00}:{1:00}:{2:00}", n / 3600, n / 60 % 60, n % 60);
  }
 }
}
