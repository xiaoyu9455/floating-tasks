using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace FloatingTasks
{
    // Executed exclusively on the desktop UI thread.
    public sealed class AgentTasks
    {
        private readonly TaskSession session;
        private bool persistedIds;
        public AgentTasks(TaskSession session) { this.session = session; }

        public static object[] Tools()
        {
            return new object[] {
                Tool("add_note", "保存便签，独立于任务且不影响计时。用户说把这句存到便签中时，传入明确指向的原文；只有要求总结时才整理。成功返回后才告知已保存，失败先 list_notes 核对。", Props("content", new {type="string",minLength=1,maxLength=20000}), new[]{"content"}, false),
                Tool("list_notes", "读取所有便签，返回稳定 ID、原文和记录时间。", Props(), new string[0], true),
                Tool("update_note", "根据稳定 ID 修改便签内容。", Props("note_id", Text(), "content", new {type="string",minLength=1,maxLength=20000}), new[]{"note_id","content"}, false),
                Tool("list_tasks", "列出任务及稳定 ID，不切换当前任务。", Props(), new string[0], true),
                Tool("get_task", "读取任务的完整子记录树、状态和下一步。", Props("task_id", Text()), new[]{"task_id"}, true),
                Tool("create_task", "创建任务，返回稳定 ID；保留当前任务和计时。", Props("title", Text()), new[]{"title"}, false),
                Tool("rename_task", "修改指定任务名称。", Props("task_id", Text(), "title", Text()), new[]{"task_id","title"}, false),
                Tool("add_entry", "创建子记录或细分步骤，默认待办且不计时。parent_id 仅可指向未完成的顶层子记录，细分步骤不能再拆分。", Props("task_id", Text(), "title", Text(), "parent_id", Text()), new[]{"task_id","title"}, false),
                Tool("add_steps", "一次添加 1 至 100 个待办步骤；parent_id 省略则添加顶层记录。先验证全部参数再保存。", Props("task_id", Text(), "parent_id", Text(), "titles", new {type="array",items=Text(),minItems=1,maxItems=100}), new[]{"task_id","titles"}, false),
                Tool("update_entry", "修改记录标题或下一步提示；next_step 空字符串可清空提示。", Props("task_id",Text(),"entry_id",Text(),"title",Text(),"next_step",new{type="string",maxLength=2000}), new[]{"task_id","entry_id"}, false),
                Tool("set_entry_status", "todo 恢复完成并暂停子树；completed 完成整棵子树；running 切换到该任务并开始该记录，遵循单项专注偏好。", Props("task_id",Text(),"entry_id",Text(),"status",new{type="string",@enum=new[]{"todo","running","completed"}}), new[]{"task_id","entry_id","status"}, false),
                Tool("list_todos", "列出未完成且未运行的待办，可按 task_id 过滤；返回父记录 ID。", Props("task_id",Text()), new string[0], true)
            };
        }

        static object Text() { return new {type="string",minLength=1,maxLength=250}; }
        static Dictionary<string,object> Props(params object[] pairs)
        {
            var d=new Dictionary<string,object>();
            for(int i=0;i<pairs.Length;i+=2)d.Add((string)pairs[i],pairs[i+1]);
            return d;
        }
        static object Tool(string name,string description,object properties,string[] required,bool read)
        {
            return new {name=name,description=description,inputSchema=new {type="object",properties=properties,required=required,additionalProperties=false},
                annotations=new {readOnlyHint=read,destructiveHint=false,openWorldHint=false}};
        }
        static string Get(Dictionary<string,object> args,string key,bool required=true,int max=250,bool empty=false)
        {
            object value;
            if(!args.TryGetValue(key,out value)) { if(!required)return null; throw new ArgumentException("缺少参数："+key); }
            var text=value as string;
            if(text==null || text.Length>max || (!empty && String.IsNullOrWhiteSpace(text)))
                throw new ArgumentException("参数无效："+key);
            return text.Trim();
        }
        TaskItem Task(string id)
        {
            var task=session.Database.Tasks.FirstOrDefault(t=>t.Id==id);
            if(task==null)throw new ArgumentException("任务不存在，请重新 list_tasks。");
            return task;
        }
        static Entry Find(TaskItem task,string id)
        {
            var entry=Logic.Flatten(task.Entries).FirstOrDefault(e=>e.Id==id);
            if(entry==null)throw new ArgumentException("记录不属于指定任务或已不存在。");
            return entry;
        }
        static object View(Entry e)
        {
            return new {id=e.Id,title=e.Title,status=e.Completed?"completed":e.Running?"running":"todo",
                next_step=e.NextStep,seconds=e.Seconds,created=e.Created.ToString("o"),
                completed_at=e.CompletedAt.HasValue?e.CompletedAt.Value.ToString("o"):null,
                children=e.Children.Select(View).ToArray()};
        }
        static int Depth(List<Entry> entries,Entry target,int level)
        {
            foreach(var entry in entries) { if(entry==target)return level; int found=Depth(entry.Children,target,level+1); if(found>=0)return found; }
            return -1;
        }
        List<Entry> Owner(TaskItem task,string parentId)
        {
            if(parentId==null)return task.Entries;
            var parent=Find(task,parentId);
            if(parent.Completed)throw new ArgumentException("父记录已完成，请先恢复为 todo。");
            if(Depth(task.Entries,parent,0)>0)throw new ArgumentException("细分步骤不能继续拆分，parent_id 请指向顶层子记录。");
            return parent.Children;
        }
        public object Call(string name,Dictionary<string,object> args)
        {
            var allowed=new Dictionary<string,string[]> {
                {"add_note",new[]{"content"}}, {"list_notes",new string[0]}, {"update_note",new[]{"note_id","content"}}, {"list_tasks",new string[0]}, {"get_task",new[]{"task_id"}}, {"create_task",new[]{"title"}},
                {"rename_task",new[]{"task_id","title"}}, {"add_entry",new[]{"task_id","title","parent_id"}},
                {"add_steps",new[]{"task_id","parent_id","titles"}}, {"update_entry",new[]{"task_id","entry_id","title","next_step"}},
                {"set_entry_status",new[]{"task_id","entry_id","status"}}, {"list_todos",new[]{"task_id"}}
            };
            if(!allowed.ContainsKey(name))throw new ArgumentException("未知工具："+name);
            if(args.Keys.Any(k=>!allowed[name].Contains(k)))throw new ArgumentException("包含未知参数。");
            session.Advance();
            object result;
            if(name=="list_notes") result=new {notes=session.Database.Notes.ToArray()};
            else if(name=="add_note" || name=="update_note") {
                Get(args,"content",true,20000);
                result=session.SaveNote((string)args["content"],name=="update_note"?Get(args,"note_id"):null,"agent");
            }
            else if(name=="list_tasks")
                result=new {tasks=session.Database.Tasks.Select(t=>new{id=t.Id,title=t.Name,selected=t==session.Current,seconds=t.Seconds}).ToArray()};
            else if(name=="create_task") {
                session.CreateTask(Get(args,"title"),false);
                var created=session.Database.Tasks.Last();
                result=new {id=created.Id,title=created.Name};
            }
            else if(name=="list_todos") {
                string filter=Get(args,"task_id",false);
                var tasks=filter==null?session.Database.Tasks:new List<TaskItem>{Task(filter)};
                result=new {todos=tasks.SelectMany(t=>Logic.Flatten(t.Entries).Where(e=>!e.Completed&&!e.Running).Select(e=>new {
                    task_id=t.Id,entry_id=e.Id,title=e.Title,next_step=e.NextStep,
                    parent_id=Logic.Flatten(t.Entries).Where(p=>p.Children.Contains(e)).Select(p=>p.Id).FirstOrDefault()
                })).ToArray()};
            }
            else {
                var task=Task(Get(args,"task_id"));
                if(name=="get_task")result=new{id=task.Id,title=task.Name,seconds=task.Seconds,entries=task.Entries.Select(View).ToArray()};
                else if(name=="rename_task") { session.RenameTask(Get(args,"title"),task); result=new{id=task.Id,title=task.Name}; }
                else if(name=="add_entry" || name=="add_steps") {
                    var owner=Owner(task,Get(args,"parent_id",false));
                    var titles=new List<string>();
                    if(name=="add_entry")titles.Add(Get(args,"title"));
                    else {
                        object raw;
                        if(!args.TryGetValue("titles",out raw) || !(raw is IList))throw new ArgumentException("titles 必须是数组。");
                        foreach(var value in (IList)raw) {
                            var title=value as string;
                            if(String.IsNullOrWhiteSpace(title)||title.Length>250)throw new ArgumentException("步骤名称须为 1 至 250 字符。");
                            titles.Add(title.Trim());
                        }
                        if(titles.Count<1||titles.Count>100)throw new ArgumentException("每次添加 1 至 100 个步骤。");
                    }
                    var entries=session.AddPlannedSteps(task,owner,titles);
                    result=new{task_id=task.Id,entries=entries.Select(View).ToArray()};
                }
                else {
                    var entry=Find(task,Get(args,"entry_id"));
                    if(name=="update_entry") {
                        string title=Get(args,"title",false),next=Get(args,"next_step",false,2000,true);
                        if(title==null&&next==null)throw new ArgumentException("至少提供 title 或 next_step。");
                        if(title!=null)session.Rename(entry,title);
                        if(next!=null)session.SetNextStep(entry,next,task);
                    }
                    else {
                        string status=Get(args,"status");
                        if(status=="completed")session.Complete(entry,task);
                        else if(status=="todo") {
                            if(entry.Completed)session.Restore(entry,task);
                            session.PauseEntry(entry);
                        }
                        else if(status=="running") {
                            if(entry.Completed)throw new ArgumentException("记录已完成，请先恢复为 todo。");
                            session.Switch(session.Database.Tasks.IndexOf(task));
                            if(!entry.Running)session.Toggle(entry);
                        }
                        else throw new ArgumentException("status 必须是 todo、running 或 completed。");
                    }
                    result=View(entry);
                }
            }
            // Also persists IDs generated when reading old files. Never report a failed save as success.
            if(!(persistedIds ? session.Flush() : session.PersistForAgent()))throw new InvalidOperationException(session.Notice+"；内存操作可能已生效，请先查询核对，不要盲目重复创建。");
            persistedIds=true;
            return result;
        }
    }
}
