using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace FloatingTasks
{
    public sealed class AgentPipe : IDisposable
    {
        public static string Name { get { return "FloatingTasks.Agent.v1."+WindowsIdentity.GetCurrent().User.Value+"."+Process.GetCurrentProcess().SessionId; } }
        readonly Func<string,Dictionary<string,object>,object> call;
        readonly object gate=new object();
        readonly string pipeName;
        NamedPipeServerStream active;
        bool stopped;
        public AgentPipe(Func<string,Dictionary<string,object>,object> call, string pipeName = null)
        {
            this.call=call; this.pipeName=pipeName ?? Name;
            var worker=new Thread(Run) {IsBackground=true,Name="FloatingTasks MCP bridge"};
            worker.Start();
        }
        void Run()
        {
            while(true) {
                try {
                    var security=new PipeSecurity();
                    security.SetAccessRuleProtection(true,false);
                    security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
                    using(var pipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security)) {
                        lock(gate) { if(stopped)return; active=pipe; }
                        pipe.WaitForConnection();
                        using(var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true))
                        using(var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true) {AutoFlush=true}) {
                            // Bound idle clients so they cannot occupy the only connection indefinitely.
                            var read=reader.ReadLineAsync();
                            if(!read.Wait(10000))continue;
                            string line=read.Result;
                            if(line==null)continue;
                            var json=McpProtocol.Json();
                            object response;
                            try {
                                var message=json.Deserialize<Dictionary<string,object>>(line);
                                var args=message["arguments"] as Dictionary<string,object>;
                                var name=message["name"] as string;
                                if(args==null||name==null)throw new ArgumentException("无效工具调用。");
                                response=new{ok=true,data=call(name,args)};
                            }
                            catch(Exception ex) { response=new{ok=false,error=ex.GetBaseException().Message}; }
                            var write=writer.WriteLineAsync(json.Serialize(response));
                            if(!write.Wait(10000))continue;
                        }
                    }
                }
                catch(Exception ex) {
                    lock(gate) { if(stopped)return; }
                    Debug.WriteLine("MCP pipe: "+ex.Message);
                    Thread.Sleep(100);
                }
                finally { lock(gate) { active=null; } }
            }
        }
        public void Dispose()
        {
            lock(gate) { stopped=true; if(active!=null)active.Dispose(); }
        }
    }

    public static class McpProgram
    {
        public static void Main()
        {
            Console.InputEncoding=new UTF8Encoding(false);
            Console.OutputEncoding=new UTF8Encoding(false);
            var protocol=new McpProtocol(CallDesktop);
            string line;
            while((line=Console.ReadLine())!=null) {
                try { string reply=protocol.Handle(line); if(reply!=null)Console.WriteLine(reply); }
                catch(Exception ex) { Console.Error.WriteLine(ex.Message); }
            }
        }
        static object CallDesktop(string name,Dictionary<string,object> args)
        {
            using(var pipe=new NamedPipeClientStream(".",AgentPipe.Name,PipeDirection.InOut,PipeOptions.Asynchronous)) {
                try { pipe.Connect(500); }
                catch(TimeoutException) {
                    string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"浮记.exe");
                    if(!File.Exists(exe))throw new FileNotFoundException("请将 FloatingTasks.Mcp.exe 与新版浮记.exe 放在同一目录。");
                    bool desktopRunning;
                    try { using(var mutex=Mutex.OpenExisting("Local\\FloatingTasks.Desktop.v1")) desktopRunning=true; }
                    catch(WaitHandleCannotBeOpenedException) { desktopRunning=false; }
                    if(!desktopRunning) Process.Start(new ProcessStartInfo(exe) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory});
                    try { pipe.Connect(10000); }
                    catch(TimeoutException) { throw new IOException("无法连接浮记。若旧版正在运行，请退出后打开新版浮记.exe；否则检查程序能否正常启动。"); }
                }
                using(var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true) {AutoFlush=true})
                using(var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true)) {
                    var json=McpProtocol.Json();
                    writer.WriteLine(json.Serialize(new{name=name,arguments=args}));
                    var read=reader.ReadLineAsync();
                    if(!read.Wait(15000))throw new IOException("浮记响应超时；操作可能已执行，请先查询核对，勿直接重试创建。");
                    if(read.Result==null)throw new IOException("浮记连接已关闭；请查询核对操作结果。");
                    var response=json.Deserialize<Dictionary<string,object>>(read.Result);
                    if(!Object.Equals(response["ok"],true))throw new InvalidOperationException((string)response["error"]);
                    return response["data"];
                }
            }
        }
    }
}
