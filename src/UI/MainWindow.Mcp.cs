using System;
using System.Collections.Generic;
using System.Linq;

namespace FloatingTasks
{
    public partial class MainWindow
    {
        public object ExecuteAgentCall(AgentTasks api,string name,Dictionary<string,object> args)
        {
            if(IsDisposed || Disposing)throw new InvalidOperationException("浮记正在退出。");
            if(InvokeRequired)return Invoke(new Func<object>(delegate {return ExecuteAgentCall(api,name,args);}));
            try { return api.Call(name,args); }
            finally {
                if(selected!=null && (selected.Completed || !Logic.Flatten(Current.Entries).Contains(selected))) {
                    selected=null;rightOpen=false;
                }
                RefreshNotes(); UpdateLayout(true);
            }
        }
    }
}
