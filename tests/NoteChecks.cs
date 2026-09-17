using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace FloatingTasks.Tests {
 static class NoteChecks {
  public static void Run(Action<bool,string> check) {
   var repo=new MemoryRepository(); var session=new TaskSession(repo,new FakeClock()); var api=new AgentTasks(session);
   var entry=session.Add(session.Current.Entries,"继续开发");
   string content="  保留原文\r\n第二行：先验证接口  ";
   var note=(Note)api.Call("add_note",new Dictionary<string,object>{{"content",content}});
   check(note.Content==content && note.Source=="agent" && entry.Running,"Agent note preserves original whitespace and active timer");
   api.Call("update_note",new Dictionary<string,object>{{"note_id",note.Id},{"content","修改后"}});
   check(session.Database.Notes.Count==1 && note.Content=="修改后","Agent updates the same note by stable ID");
   bool invalid=false;
   try {api.Call("add_note",new Dictionary<string,object>{{"content",new string('x',20001)}});}catch(ArgumentException){invalid=true;}
   check(invalid && session.Database.Notes.Count==1,"Oversized note rejected without mutation");
   var decoded=TaskJson.Decode(TaskJson.Encode(session.Database));
   check(decoded.Notes.Single().Id==note.Id && decoded.Notes.Single().Content=="修改后","Notes round trip through JSON");
   check(TaskJson.Decode("{\"Version\":1,\"Tasks\":[]}").Notes.Count==0,"Legacy database loads with empty notes");
   session.DeleteNote(note.Id); var later=session.SaveNote("删除后新增");
   session.UndoNoteDelete();
   check(session.Database.Notes.Contains(note) && session.Database.Notes.Contains(later),"Undo note deletion preserves later additions");
   session.Import(decoded);
   check(session.Database.Notes.Select(n=>n.Id).Distinct().Count()==3,"Import preserves notes and remaps IDs");
   string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"note-test-"+Guid.NewGuid().ToString("N"));
   var disk=new JsonTaskRepository(path); disk.Save(session.Database);
   check(new JsonTaskRepository(path).Load().Notes.Count==3,"Notes survive actual repository restart");
   repo.Fail=true; bool failed=false;
   try {api.Call("add_note",new Dictionary<string,object>{{"content","磁盘失败"}});}catch(InvalidOperationException){failed=true;}
   check(failed,"Agent note reports persistence failure");
   repo.Fail=false; check(session.Flush(),"Failed note save remains retryable");
  }
 }
}
