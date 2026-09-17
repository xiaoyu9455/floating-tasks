using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FloatingTasks.Tests
{
    internal static class McpChecks
    {
        static Dictionary<string,object> Args(params string[] pairs)
        {
            var result=new Dictionary<string,object>();
            for(int i=0;i<pairs.Length;i+=2)result.Add(pairs[i],pairs[i+1]);
            return result;
        }
        static Dictionary<string,object> Map(object value) { return McpProtocol.Json().Deserialize<Dictionary<string,object>>(McpProtocol.Json().Serialize(value)); }
        static bool Reject(Action action) { try { action(); return false; } catch(ArgumentException) { return true; } }
        public static void Run(Action<bool,string> check)
        {
            var repo=new MemoryRepository(); var clock=new FakeClock();
            var session=new TaskSession(repo,clock); var api=new AgentTasks(session);
            var original=session.Current; var running=session.Add(original.Entries,"当前正在做");
            var created=Map(api.Call("create_task",Args("title","AI 计划")));
            string taskId=(string)created["id"];
            var target=session.Database.Tasks.Last();
            check(session.Current==original&&running.Running,"MCP task creation preserves active task and timer");
            var parentResponse=Map(api.Call("add_entry",Args("task_id",taskId,"title","准备上线")));
            var parent=target.Entries.Single();
            var batch=Args("task_id",taskId,"parent_id",parent.Id);
            batch.Add("titles",new[]{"检查","发布"});
            api.Call("add_steps",batch);
            check(parent.Children.Count==2&&!Logic.Flatten(target.Entries).Any(e=>e.Running),"MCP nested batch creates paused todos");
            var child=parent.Children[0];
            check(Reject(delegate{api.Call("add_entry",Args("task_id",taskId,"parent_id",child.Id,"title","检查配置"));})&&child.Children.Count==0,"MCP rejects steps under steps");
            api.Call("update_entry",Args("task_id",taskId,"entry_id",child.Id,"title","检查环境","next_step","先验证配置"));
            check(child.Title=="检查环境"&&child.NextStep=="先验证配置"&&original.LastEntryId==running.Id,"MCP next step edits preserve active context");
            clock.Now=10;
            api.Call("set_entry_status",Args("task_id",taskId,"entry_id",parent.Id,"status","completed"));
            check(Logic.Flatten(target.Entries).All(e=>e.Completed)&&running.Running&&running.Seconds==10,"MCP completion on other task preserves current timing");
            api.Call("set_entry_status",Args("task_id",taskId,"entry_id",child.Id,"status","todo"));
            check(!parent.Completed&&!child.Completed&&parent.Children[1].Completed,"MCP restore reopens ancestors and matching descendants");
            check(Reject(delegate{api.Call("add_entry",Args("task_id",taskId,"parent_id",parent.Children[1].Id,"title","不可添加"));}),"MCP rejects children under completed records");
            int count=parent.Children.Count;
            batch["titles"]=new[]{"合法"," "};
            check(Reject(delegate{api.Call("add_steps",batch);})&&parent.Children.Count==count,"MCP invalid batch makes no partial additions");
            check(Reject(delegate{api.Call("update_entry",Args("task_id",original.Id,"entry_id",child.Id,"title","错误"));})&&child.Title=="检查环境","MCP rejects IDs belonging to another task");
            var todos=Map(api.Call("list_todos",Args("task_id",taskId)));
            check(((IList)todos["todos"]).Count==2,"MCP todo filter excludes completed entries");
            api.Call("set_entry_status",Args("task_id",taskId,"entry_id",child.Id,"status","running"));
            check(session.Current==target&&!running.Running&&child.Running,"MCP explicit running switches task and pauses previous timer");
            api.Call("set_entry_status",Args("task_id",taskId,"entry_id",child.Id,"status","todo"));
            check(!child.Running,"MCP todo pauses active entry");
            var restored=TaskJson.Decode(TaskJson.Encode(session.Database));
            check(restored.Tasks.Last().Id==taskId,"MCP task IDs survive save and reload");
            session.Import(restored);
            check(session.Database.Tasks.Select(t=>t.Id).Distinct().Count()==session.Database.Tasks.Count,"import remaps task IDs");
            var duplicate=TaskJson.Encode(session.Database).Replace(session.Database.Tasks[0].Id,session.Database.Tasks[1].Id);
            bool invalid=false; try{TaskJson.Decode(duplicate);}catch(InvalidDataException){invalid=true;}
            check(invalid,"duplicate task IDs rejected");
            var protocol=new McpProtocol(api.Call);
            check(protocol.Handle("{broken").Contains("-32700"),"MCP malformed JSON returns parse error");
            check(protocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}").Contains("-32002"),"MCP tools require initialization");
            var init="{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"tests\",\"version\":\"1\"}}}";
            check(protocol.Handle(init).Contains("2025-11-25"),"MCP negotiates supported protocol");
            check(protocol.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}")==null,"MCP notification has no response");
            check(protocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\"}").Contains("add_steps"),"MCP discovers task tools");
            check(protocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"unknown\"}").Contains("-32601"),"MCP unknown method returns protocol error");
            check(protocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"unknown\"}}").Contains("\"isError\":true"),"MCP tool errors are reported in content");
            repo.Fail=true;
            check(protocol.Handle("{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"tools/call\",\"params\":{\"name\":\"create_task\",\"arguments\":{\"title\":\"保存失败测试\"}}}").Contains("\"isError\":true"),"MCP does not report failed persistence as success");
        }

        public static void PipeRoundTrip(Action<bool,string> check)
        {
            var session=new TaskSession(new MemoryRepository(),new FakeClock());
            var api=new AgentTasks(session);
            string pipeName="FloatingTasks.Tests."+Guid.NewGuid().ToString("N");
            using(var window=new MainWindow(session)) {
                var handle=window.Handle;
                using(var server=new AgentPipe(delegate(string name,Dictionary<string,object> args) {
                    return window.ExecuteAgentCall(api,name,args);
                },pipeName)) {
                    var work=Task.Factory.StartNew(delegate {
                        using(var client=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut)) {
                            client.Connect(3000);
                            using(var writer=new StreamWriter(client,new UTF8Encoding(false),4096,true){AutoFlush=true})
                            using(var reader=new StreamReader(client,new UTF8Encoding(false),false,4096,true)) {
                                writer.WriteLine("{\"name\":\"add_entry\",\"arguments\":{\"task_id\":\""+session.Current.Id+"\",\"title\":\"管道中文待办\"}}");
                                return reader.ReadLine();
                            }
                        }
                    });
                    var deadline=DateTime.UtcNow.AddSeconds(8);
                    while(!work.IsCompleted&&DateTime.UtcNow<deadline){Application.DoEvents();System.Threading.Thread.Sleep(5);}
                    check(work.IsCompleted&&work.Result.Contains("\"ok\":true")&&session.Current.Entries.Single().Title=="管道中文待办",
                        "MCP named pipe dispatches UTF-8 mutation on UI thread");
                }
            }
        }
    }
}
