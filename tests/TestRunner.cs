using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;

namespace FloatingTasks.Tests
{
    internal sealed class FakeClock : IClock
    {
        public double Now;
        public double Seconds { get { return Now; } }
    }
    internal sealed class MemoryRepository : ITaskRepository
    {
        public int Writes;
        public bool Fail;
        public string RecoveryNotice { get { return null; } }
        public Database Load() { var d=new Database(); d.Tasks.Add(new TaskItem { Name="测试" }); return d; }
        public void Save(Database d) { if(Fail)throw new IOException("模拟磁盘写入失败"); Writes++; }
    }
    internal sealed class StaticRepository : ITaskRepository
    {
        private readonly Database database;
        public StaticRepository(Database database) { this.database = database; }
        public string RecoveryNotice { get { return null; } }
        public Database Load() { return database; }
        public void Save(Database d) { }
    }

    internal static class TestRunner
    {
        private static readonly List<string> passed = new List<string>();
        private static void Check(bool value, string name)
        {
            if (!value) throw new Exception("FAIL: " + name);
            passed.Add("PASS: " + name);
        }
        [STAThread]
        public static void Main()
        {
            try
            {
                SessionChecks(); SubtreePauseChecks(); CompletionChecks(); DepthChecks(); PersistenceChecks(); SnapshotChecks(); McpChecks.Run(Check); NoteChecks.Run(Check);
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                McpChecks.PipeRoundTrip(Check);
                var session=new TaskSession(new MemoryRepository(),new FakeClock());
                using(var window=new MainWindow(session)) { window.CheckMenuLifetime(); window.CheckInterface(); window.CheckPanelResize(); }
                passed.Add("PASS: task and more menu actions finish before deferred disposal; repeated reopen has no stale menu");
                passed.Add("PASS: drag resize, dynamic rows, notes reflow, collapse restore, JSON size persistence and resize limits");
                passed.Add("PASS: borderless drawers, stable anchor, click-through gaps, no chroma key, 200% DPI and overflow");
            }
            catch(Exception ex) { passed.Add(ex.ToString()); Environment.ExitCode=1; }
            File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),passed.ToArray());
        }

        private static void SnapshotChecks()
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "snapshot-test-" + Guid.NewGuid().ToString("N"));
            var repo = new JsonTaskRepository(folder);
            var original = repo.Load();
            original.Tasks[0].Name = "原有记录";
            repo.Save(original);
            var sessionRepo = new JsonTaskRepository(folder);
            var loaded = sessionRepo.Load();
            loaded.Tasks[0].Name = "修改之后";
            sessionRepo.Save(loaded);
            loaded.Tasks[0].Name = "再次修改";
            sessionRepo.Save(loaded);
            string[] history = Directory.GetFiles(Path.Combine(folder, "history"), "*.json");
            Check(history.Length == 1 && TaskJson.Decode(File.ReadAllText(history[0])).Tasks[0].Name == "原有记录",
                "independent startup snapshot survives repeated saves and rolling backup replacement");
            var restarted = new JsonTaskRepository(folder);
            var current = restarted.Load();
            current.Tasks[0].Name = "下次启动修改";
            restarted.Save(current);
            Check(Directory.GetFiles(Path.Combine(folder,"history"),"*.json").Length == 2,
                "each new repository session preserves another immutable snapshot");
        }

        private static void SessionChecks()
        {
            var repository=new MemoryRepository(); var clock=new FakeClock();
            var session=new TaskSession(repository,clock);
            session.Advance(); session.Flush();
            Check(repository.Writes==0,"idle session does not write");
            Entry parent=session.Add(session.Current.Entries,"父项");
            Entry child=session.Add(parent.Children,"子项");
            clock.Now=600; session.Advance();
            Check(parent.Seconds==600&&child.Seconds==600&&session.Current.Seconds==600,"concurrent parent/child without double-counting total");
            session.Toggle(child);clock.Now=660;session.Advance();
            Check(child.Seconds==600&&parent.Seconds==660,"independent pause");
            session.Delete(session.Current.Entries,parent);
            Entry newer=session.Add(session.Current.Entries,"删除后新增");
            session.Rename(newer,"新增后又改名");
            clock.Now=700;session.Advance();session.UndoDelete();
            Check(session.Current.Entries.Contains(newer)&&newer.Title=="新增后又改名"&&newer.Seconds==40,"undo preserves later additions, edits and elapsed time");
            Check(!parent.Running&&!child.Running&&parent.Children.Contains(child),"undo restores paused subtree");
            session.CreateTask("另一个任务");
            Check(!newer.Running&&session.Current.Seconds==0,"task switch pauses previous task");
            session.Switch(0);session.Toggle(newer);session.PauseAll();
            Check(!newer.Running,"immediate pause after start is persisted");
            int writes=repository.Writes;clock.Now=100000;session.Advance();session.Flush();
            Check(repository.Writes==writes&&newer.Seconds==40,"paused/offline interval does not count or write");
            repository.Fail=true;session.SetPinned(false);
            Check(session.Notice!=null,"save failure is exposed");
            repository.Fail=false;session.Flush();
            Check(session.Notice==null&&repository.Writes==writes+1,"failed saves remain dirty and retry");
            Database imported=TaskJson.Decode(TaskJson.Encode(session.Database));
            var originalIds=Logic.Flatten(session.Current.Entries).Select(e=>e.Id).ToArray();
            session.Import(imported);
            Check(!Logic.Flatten(session.Current.Entries).Any(e=>originalIds.Contains(e.Id)),"imports use fresh identifiers");
            Check(Logic.Time(90061)=="25:01:01","durations over 24 hours");
        }

        private static void SubtreePauseChecks()
        {
            var repository = new MemoryRepository(); var clock = new FakeClock();
            var session = new TaskSession(repository, clock);
            Entry parent = session.Add(session.Current.Entries, "线路");
            Entry child = session.Add(parent.Children, "细分步骤");
            // 深层嵌套只可能来自旧数据：直接构造，验证引擎对既有深层子树的递归行为。
            Entry grandchild = new Entry { Title = "更细步骤" }; child.Children.Add(grandchild);
            Entry paused = session.Add(parent.Children, "尚未开始", false);
            Entry completed = session.Add(parent.Children, "已完成", false);
            session.Complete(completed);
            Entry other = session.Add(session.Current.Entries, "另一线路");
            clock.Now = 10;
            int writes = repository.Writes;
            session.Toggle(parent);
            Check(parent.Seconds == 10 && child.Seconds == 10 && grandchild.Seconds == 10,
                "parent pause settles every descendant at the same instant");
            Check(!Logic.Flatten(new[] { parent }).Any(e => e.Running) && other.Running
                && repository.Writes == writes + 1, "parent pause persists entire subtree without pausing another route");
            clock.Now = 100; session.Advance();
            Check(parent.Seconds == 10 && child.Seconds == 10 && grandchild.Seconds == 10
                && other.Seconds == 100 && session.Current.Seconds == 100,
                "paused descendants stay frozen while unrelated route keeps timing");
            Database saved = TaskJson.Decode(TaskJson.Encode(session.Database));
            Check(!Logic.Flatten(new[] { saved.Tasks[0].Entries[0] }).Any(e => e.Running),
                "subtree pause flags survive serialization");
            session.Toggle(parent); clock.Now = 110; session.Advance();
            Check(parent.Seconds == 20 && !child.Running && !grandchild.Running
                && !paused.Running && completed.Completed && !completed.Running,
                "resuming parent does not restart descendants or count paused interval");
            session.Toggle(child); session.Toggle(grandchild);
            clock.Now = 120; session.Toggle(child);
            clock.Now = 130; session.Advance();
            Check(parent.Running && other.Running && !child.Running && !grandchild.Running
                && child.Seconds == 20 && grandchild.Seconds == 20 && parent.Seconds == 40,
                "pausing nested step freezes its descendants without affecting ancestors");
            session.Toggle(parent); session.Toggle(other);
            clock.Now = 1000; session.Advance();
            Check(!session.HasRunning && session.Current.Seconds == 130,
                "no hidden descendant timer keeps task total running after all routes pause");
        }
        private static void CompletionChecks()
        {
            var clock = new FakeClock();
            var session = new TaskSession(new MemoryRepository(), clock);
            Entry parent = session.Add(session.Current.Entries, "项目", false);
            Entry earlier = session.Add(parent.Children, "已完成的小步", false);
            session.Complete(earlier);
            Entry child = session.Add(parent.Children, "当前小步", true);
            session.SetNextStep(child, "补上最后一段");
            clock.Now = 30;
            session.Complete(parent);
            Check(parent.Completed && child.Completed && earlier.Completed && !session.HasRunning
                && child.Seconds == 30, "completion settles time and archives unfinished subtree");
            session.Restore(parent);
            Check(!parent.Completed && !child.Completed && earlier.Completed && !session.HasRunning,
                "restore reopens only its completion batch and remains paused");
            session.Toggle(parent);
            session.Restore(earlier);
            Check(parent.Running && !earlier.Completed && !earlier.Running, "restoring a child preserves unrelated active parent");
            session.PauseAll();
            session.SetExclusiveFocus(true);
            session.Toggle(parent); clock.Now = 40; session.Toggle(child);
            Check(!parent.Running && child.Running && parent.Seconds == 10,
                "exclusive focus switches without overlapping timers");
            session.SetExclusiveFocus(false); session.Toggle(parent);
            Check(parent.Running && child.Running, "parallel timing remains available");
            session.Complete(parent); session.Restore(child);
            Check(!parent.Completed && !child.Completed && !parent.Running && !child.Running,
                "restoring archived child also reopens ancestors");
            session.SetEnterStarts(false);
            Database saved = TaskJson.Decode(TaskJson.Encode(session.Database).Insert(1, "\"MiniMode\":true,"));
            Entry savedChild = saved.Tasks[0].Entries[0].Children[1];
            Check(savedChild.NextStep == "补上最后一段" && !saved.EnterStarts
                && saved.Tasks[0].LastEntryId == savedChild.Id, "next step and preferences survive serialization");
            session.Import(saved);
            Check(Logic.Flatten(session.Current.Entries).Any(e => e.Id == session.Current.LastEntryId),
                "import remaps last active record");
            Database legacy = TaskJson.Decode("{\"Version\":1,\"Tasks\":[{\"Name\":\"旧任务\",\"Entries\":[{\"Id\":\"old\",\"Title\":\"旧记录\",\"Seconds\":42,\"Children\":[]}]}]}");
            Check(legacy.EnterStarts && !legacy.ExclusiveFocus && !legacy.Tasks[0].Entries[0].Completed
                && legacy.Tasks[0].Entries[0].Seconds == 42, "legacy data retains durations and input behavior");
        }
        private static void DepthChecks()
        {
            var session = new TaskSession(new MemoryRepository(), new FakeClock());
            Entry root = session.Add(session.Current.Entries, "子记录");
            Entry step = session.Add(root.Children, "细分步骤");
            bool rejected = false;
            try { session.Add(step.Children, "更细步骤"); } catch (ArgumentException) { rejected = true; }
            Check(rejected && step.Children.Count == 0, "session rejects steps under steps");
            rejected = false;
            try { session.AddPlannedSteps(session.Current, step.Children, new List<string> { "更细步骤" }); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected && step.Children.Count == 0, "planned steps reject deep owners");
            Entry grandchild = new Entry { Title = "旧更细", Running = false };
            Entry legacyStep = new Entry { Title = "旧步骤", Running = false };
            Entry legacyRoot = new Entry { Title = "旧子记录", Running = false };
            legacyStep.Children.Add(grandchild); legacyRoot.Children.Add(legacyStep);
            var legacyDb = new Database();
            legacyDb.Tasks.Add(new TaskItem { Name = "旧任务", Entries = { legacyRoot } });
            var loaded = new TaskSession(new StaticRepository(legacyDb), new FakeClock());
            Check(legacyRoot.Children.Count == 2 && legacyRoot.Children[0] == legacyStep
                && legacyRoot.Children[1] == grandchild && legacyStep.Children.Count == 0,
                "legacy deep steps flatten on load");
            var importDeep = TaskJson.Decode("{\"Version\":1,\"Tasks\":[{\"Name\":\"导入\",\"Entries\":[{\"Title\":\"子记录\",\"Seconds\":1,\"Children\":[{\"Title\":\"步骤\",\"Seconds\":2,\"Children\":[{\"Title\":\"更细\",\"Seconds\":3,\"Children\":[]}]}]}]}]}");
            session.Import(importDeep);
            TaskItem imported = session.Current;
            Check(imported.Entries.All(e => e.Children.All(c => c.Children.Count == 0))
                && imported.Entries[0].Children.Count == 2, "import flattens deep steps");
            Check(Logic.Flatten(imported.Entries).Sum(e => e.Seconds) == 6, "flatten preserves every step's elapsed time");
        }
        private static void PersistenceChecks()
        {
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var repo=new JsonTaskRepository(path);Database db=repo.Load();
            db.Tasks[0].Entries.Add(new Entry {Title="保留的数据"});repo.Save(db);
            db.Tasks[0].Seconds=10;repo.Save(db);
            Check(File.Exists(repo.FilePath+".bak"),"atomic save keeps previous backup");
            File.WriteAllText(repo.FilePath,"{broken");
            var recovered=new JsonTaskRepository(path);Database restored=recovered.Load();
            Check(restored.Tasks[0].Entries[0].Title=="保留的数据","corrupt primary recovers from backup");
            recovered.Save(restored);
            Check(TaskJson.Decode(File.ReadAllText(repo.FilePath+".bak")).Tasks[0].Entries.Count==1,"first recovery save preserves valid backup");
            Check(Directory.GetFiles(path,"*.corrupt-*").Length==1,"corrupt original retained for diagnosis");
            Entry original=restored.Tasks[0].Entries[0];original.Running=true;repo.Save(restored);
            var reopened=new TaskSession(new JsonTaskRepository(path),new FakeClock {Now=999999});
            Check(!reopened.HasRunning,"restart never resumes persisted running flag automatically");
            original.Children.Add(new Entry {Id=original.Id,Title="重复标识"});
            bool rejected=false;try{TaskJson.Decode(TaskJson.Encode(restored));}catch(InvalidDataException){rejected=true;}
            Check(rejected,"duplicate identifiers rejected before use");
            original.Children.Clear();original.Seconds=-1;
            rejected=false;try{TaskJson.Decode(TaskJson.Encode(restored));}catch(InvalidDataException){rejected=true;}
            Check(rejected,"negative elapsed values rejected");
        }
    }
}
