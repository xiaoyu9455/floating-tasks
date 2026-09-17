using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace FloatingTasks
{
    // Deliberately small stdio subset: initialization, ping, and tools. No resources/prompts advertised.
    public sealed class McpProtocol
    {
        readonly Func<string,Dictionary<string,object>,object> call;
        bool initialized,ready;
        public static JavaScriptSerializer Json() { return new JavaScriptSerializer {MaxJsonLength=16777216,RecursionLimit=256}; }
        public McpProtocol(Func<string,Dictionary<string,object>,object> call) { this.call=call; }
        static object Error(object id,int code,string message) { return new{jsonrpc="2.0",id=id,error=new{code=code,message=message}}; }
        public string Handle(string line)
        {
            var json=Json();
            Dictionary<string,object> request;
            try { request=json.DeserializeObject(line) as Dictionary<string,object>; }
            catch(ArgumentException) { return json.Serialize(Error(null,-32700,"Parse error")); }
            object id=null,value;
            if(request==null || !request.TryGetValue("jsonrpc",out value) || !Object.Equals(value,"2.0") ||
                !request.ContainsKey("method") || !(request["method"] is string))
                return json.Serialize(Error(null,-32600,"Invalid Request"));
            bool hasId=request.TryGetValue("id",out id);
            if(hasId && !(id is string || id is int || id is long || id is decimal))
                return json.Serialize(Error(null,-32600,"Invalid request ID"));
            string method=(string)request["method"];
            if(!hasId) {
                if(method=="notifications/initialized"&&initialized)ready=true;
                return null;
            }
            var args=new Dictionary<string,object>();
            if(request.TryGetValue("params",out value)) {
                args=value as Dictionary<string,object>;
                if(args==null)return json.Serialize(Error(id,-32602,"params must be an object"));
            }
            object result;
            if(method=="initialize") {
                if(initialized)return json.Serialize(Error(id,-32600,"Already initialized"));
                if(!args.ContainsKey("protocolVersion") || !(args["protocolVersion"] is string) ||
                    !args.ContainsKey("capabilities") || !(args["capabilities"] is Dictionary<string,object>) ||
                    !args.ContainsKey("clientInfo") || !(args["clientInfo"] is Dictionary<string,object>))
                    return json.Serialize(Error(id,-32602,"Invalid initialize parameters"));
                string version=(string)args["protocolVersion"];
                if(version!="2024-11-05"&&version!="2025-03-26"&&version!="2025-06-18"&&version!="2025-11-25")version="2025-11-25";
                initialized=true;
                result=new{protocolVersion=version,capabilities=new{tools=new{listChanged=false}},
                    serverInfo=new{name="floating-tasks",version="1.0.0"},
                    instructions="管理浮记的真实任务数据。先 list_tasks 获取稳定 ID。新增记录默认待办，不自动计时。子记录和步骤用 parent_id 关联。操作超时或保存失败后先查询核对，避免重复创建。"};
            }
            else if(method=="ping")result=new{};
            else if(!ready)return json.Serialize(Error(id,-32002,"Initialize and send notifications/initialized first"));
            else if(method=="tools/list")result=new{tools=AgentTasks.Tools()};
            else if(method=="tools/call") {
                if(!args.TryGetValue("name",out value)||!(value is string))
                    return json.Serialize(Error(id,-32602,"Missing tool name"));
                string name=(string)value;
                var toolArgs=new Dictionary<string,object>();
                if(args.TryGetValue("arguments",out value)) {
                    toolArgs=value as Dictionary<string,object>;
                    if(toolArgs==null)return json.Serialize(Error(id,-32602,"arguments must be an object"));
                }
                try {
                    object data=call(name,toolArgs);
                    result=new{content=new[]{new{type="text",text=json.Serialize(data)}},isError=false};
                }
                catch(Exception ex) { result=new{content=new[]{new{type="text",text=ex.Message}},isError=true}; }
            }
            else return json.Serialize(Error(id,-32601,"Method not found"));
            return json.Serialize(new{jsonrpc="2.0",id=id,result=result});
        }
    }
}
