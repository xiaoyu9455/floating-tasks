using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace FloatingTasks
{
    public interface ITaskRepository
    {
        Database Load();
        void Save(Database database);
        string RecoveryNotice { get; }
    }

    // Version 1 stays readable, including files produced by earlier releases.
    public static class TaskJson
    {
        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 16777216, RecursionLimit = 256 };
        }

        public static string Encode(Database database) { return Serializer().Serialize(database); }

        public static Database Decode(string json)
        {
            Database database = Serializer().Deserialize<Database>(json);
            if (database == null || database.Version != 1 || database.Tasks == null)
                throw new InvalidDataException("不支持的任务数据格式。");
            if (database.Notes == null) throw new InvalidDataException("便签列表损坏。");
            var noteIds = new HashSet<string>();
            foreach (Note note in database.Notes)
                if (note == null || String.IsNullOrWhiteSpace(note.Id) || !noteIds.Add(note.Id)
                    || String.IsNullOrWhiteSpace(note.Content) || note.Content.Length > 20000)
                    throw new InvalidDataException("便签内容或标识无效。");
            var ids = new HashSet<string>();
            var taskIds = new HashSet<string>();
            foreach (TaskItem task in database.Tasks)
            {
                if (task == null || task.Entries == null || String.IsNullOrWhiteSpace(task.Name))
                    throw new InvalidDataException("任务缺少名称或记录列表。");
                if (String.IsNullOrWhiteSpace(task.Id) || !taskIds.Add(task.Id))
                    throw new InvalidDataException("任务标识为空或重复。");
                ValidateTime(task.Seconds);
                ValidateEntries(task.Entries, ids, 0);
            }
            if (database.Tasks.Count == 0) database.Tasks.Add(new TaskItem { Name = "任务1" });
            database.Selected = Math.Max(0, Math.Min(database.Selected, database.Tasks.Count - 1));
            database.Transparency = Math.Max(35, Math.Min(100, database.Transparency));
            return database;
        }

        private static void ValidateTime(double value)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value) || value < 0)
                throw new InvalidDataException("记录时间无效。");
        }

        private static void ValidateEntries(List<Entry> entries, HashSet<string> ids, int depth)
        {
            if (depth > 64) throw new InvalidDataException("子记录层级超过 64 层。");
            foreach (Entry entry in entries)
            {
                if (entry == null || entry.Children == null || String.IsNullOrWhiteSpace(entry.Title)
                    || String.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id))
                    throw new InvalidDataException("记录为空、标识重复或结构损坏。");
                ValidateTime(entry.Seconds);
                entry.NextStep = entry.NextStep ?? "";
                if (entry.Completed) entry.Running = false;
                ValidateEntries(entry.Children, ids, depth + 1);
            }
        }
    }

    public sealed class JsonTaskRepository : ITaskRepository
    {
        public static string DefaultDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatingTasks"); }
        }
        public string FilePath { get; private set; }
        public string RecoveryNotice { get; private set; }
        private bool preserveBackup;
        private bool snapshotSaved;

        public JsonTaskRepository(string directory) { FilePath = Path.Combine(directory, "tasks.json"); }

        public Database Load()
        {
            if (!File.Exists(FilePath))
            {
                if (File.Exists(FilePath + ".bak")) return LoadBackup();
                var fresh = new Database { Transparency = 94, EnterStarts = false, ExclusiveFocus = true };
                fresh.Tasks.Add(new TaskItem { Name = "任务1" });
                return fresh;
            }
            try { return TaskJson.Decode(File.ReadAllText(FilePath, Encoding.UTF8)); }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is ArgumentException || ex is InvalidOperationException)) throw;
                return LoadBackup();
            }
        }

        private Database LoadBackup()
        {
            try
            {
                Database result = TaskJson.Decode(File.ReadAllText(FilePath + ".bak", Encoding.UTF8));
                preserveBackup = true;
                RecoveryNotice = "已从备份恢复";
                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("无法读取任务数据与备份；原文件未改动。数据目录：" + Path.GetDirectoryName(FilePath), ex);
            }
        }

        private void PreserveSessionSnapshot()
        {
            if (snapshotSaved) return;
            string source = preserveBackup ? FilePath + ".bak" : FilePath;
            if (!File.Exists(source)) return;
            // Validate before archiving; after recovery preserve the good backup.
            TaskJson.Decode(File.ReadAllText(source, Encoding.UTF8));
            string history = Path.Combine(Path.GetDirectoryName(FilePath), "history");
            Directory.CreateDirectory(history);
            string target = Path.Combine(history, "tasks-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
            File.Copy(source, target, false);
            snapshotSaved = true;
        }

        public void Save(Database database)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            PreserveSessionSnapshot();
            string temporary = FilePath + ".tmp";
            byte[] bytes = Encoding.UTF8.GetBytes(TaskJson.Encode(database));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(FilePath))
            {
                if (preserveBackup)
                    File.Copy(FilePath, FilePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
                File.Replace(temporary, FilePath, preserveBackup ? null : FilePath + ".bak");
            }
            else File.Move(temporary, FilePath);
            preserveBackup = false;
        }
    }
}
