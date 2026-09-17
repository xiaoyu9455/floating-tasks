using System;
using System.Windows.Forms;

namespace FloatingTasks
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (var mutex = new System.Threading.Mutex(true, "Local\\FloatingTasks.Desktop.v1", out created))
            {
                if (!created)
                {
                    MessageBox.Show("浮记已经运行，请双击系统托盘图标打开。", "浮记");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    var repository = new JsonTaskRepository(JsonTaskRepository.DefaultDirectory);
                    var session = new TaskSession(repository, new MonotonicClock());
                    using (var window = new MainWindow(session))
                    {
                        var api = new AgentTasks(session);
                        // Create the handle before the pipe can dispatch work to the UI thread.
                        var handle = window.Handle;
                        using (var pipe = new AgentPipe(delegate(string name, System.Collections.Generic.Dictionary<string, object> args) {
                            return window.ExecuteAgentCall(api, name, args);
                        })) Application.Run(window);
                    }
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "浮记无法启动"); }
            }
        }
    }
}
