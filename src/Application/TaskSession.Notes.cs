using System;
using System.Linq;
namespace FloatingTasks {
 public sealed partial class TaskSession {
  Note deletedNote;
  int deletedNoteIndex;
  public bool CanUndoNote { get { return deletedNote != null; } }
  public Note SaveNote(string content, string id = null, string source = "manual") {
   if (String.IsNullOrWhiteSpace(content) || content.Length > 20000)
    throw new ArgumentException("便签须为 1 至 20000 字符。");
   Note note = id == null ? new Note { Source = source } : Database.Notes.FirstOrDefault(n => n.Id == id);
   if (note == null) throw new ArgumentException("便签已不存在，请重新读取。");
   note.Content = content; note.Updated = DateTime.Now;
   if (id == null) Database.Notes.Insert(0, note);
   Commit(); return note;
  }
  public void DeleteNote(string id) {
   int index = Database.Notes.FindIndex(n => n.Id == id);
   if (index < 0) throw new ArgumentException("便签已不存在。");
   deletedNote = Database.Notes[index]; deletedNoteIndex = index;
   Database.Notes.RemoveAt(index); Commit();
  }
  public void UndoNoteDelete() {
   if (deletedNote == null) return;
   Database.Notes.Insert(Math.Min(deletedNoteIndex, Database.Notes.Count), deletedNote);
   deletedNote = null; Commit();
  }
 }
}
