# 浮记 MCP 接入

浮记提供本地 stdio MCP 服务。Agent 可以直接创建任务、添加子记录、批量拆分步骤、设置下一步、查询待办、完成或恢复记录；结果写入浮记现有数据，并在桌面界面刷新。

## 接入

将以下两个文件放在同一目录：

- 浮记.exe：桌面程序，负责真实数据、界面和计时。
- FloatingTasks.Mcp.exe：Agent 启动的 stdio MCP 入口，不要用桌面程序充当 MCP 命令。

在支持本地 stdio MCP 的 Agent 中新增服务器：

- 名称：floating-tasks
- 类型：stdio
- 命令：C:\Apps\FloatingTasks\FloatingTasks.Mcp.exe
- 参数：留空

上述命令是示例路径，请替换为实际安装路径。采用 mcpServers JSON 配置的客户端可参考同目录的 mcp.config.example.json；保留客户端原有服务器。目录移动后更新 command 的绝对路径。不同 Agent 的配置文件位置不同，本项目不自动改写任何 Agent 的全局设置。

首次工具调用会连接正在运行的浮记；没有运行时自动打开同目录桌面程序。若旧版浮记正在运行，先从托盘选择“退出并暂停”，再打开新版。仅握手和发现工具不会启动桌面程序。

服务面向同一台 Windows 电脑、同一用户及登录会话，不提供远程 HTTP 地址。Agent 必须支持启动本地可执行文件。

## 可调用工具

| 工具 | 功能 |
| --- | --- |
| list_tasks | 列出任务名称、稳定 ID、当前任务和累计用时 |
| get_task | 读取指定任务的完整记录树 |
| create_task | 创建任务，保留当前任务与计时 |
| rename_task | 修改指定任务名称 |
| add_entry | 添加顶层记录或子记录 |
| add_steps | 一次添加 1 至 100 个待办步骤 |
| update_entry | 修改标题、设置或清空下一步提示 |
| set_entry_status | 设置 todo、running、completed |
| list_todos | 查询全部或指定任务中未完成且未运行的待办 |

先 list_tasks 获得 task_id；创建任务也会返回 ID。记录和步骤用 entry_id 标识，添加下级时传 parent_id。不要把名称或列表序号当 ID。

新增记录默认 todo，不开始计时。层级固定为 任务 → 子记录 → 细分步骤：parent_id 只能指向顶层子记录，细分步骤不能再拆分。名称最多 250 字符，下一步提示最多 2000 字符。

set_entry_status 的具体行为：

- todo：恢复已完成记录及同批完成的后代，重开已完成祖先，暂停所选子树。
- completed：完成整棵子树，保留此前独立完成记录的批次。
- running：切换到该任务、暂停原任务，然后开始所选记录，遵循浮记的单项专注偏好；已完成记录需要先恢复。

“待办”是未运行且未完成的记录；正在进行的事项可从 get_task 查看。累计时间不代表今天用时。

## 示例

可直接对已接入的 Agent 说：

> 在浮记创建“网站上线”任务，添加“准备内容”和“发布验收”两条记录；在“发布验收”下面拆出“检查配置”“测试访问”“正式发布”三个待办步骤。先不计时，把“检查配置”的下一步设置为“确认环境变量”。

典型调用顺序：

1. create_task，title 为“网站上线”，保存返回的 id。
2. add_entry，传 task_id 和 title“发布验收”，保存返回 entries 中的记录 id。
3. add_steps，传 task_id、parent_id 和 titles 数组。
4. update_entry，传 task_id、entry_id、next_step。

每次调用超时或返回保存失败，都先查询核对已有记录，再决定重试；失败时内存操作可能已经发生，重复创建可能造成重复事项。服务不会自动重试写操作。

## 数据与验证

MCP 通过当前用户受限的命名管道请求桌面程序，在 UI 线程调用 TaskSession；它不会作为第二个进程直接改写 tasks.json。正式数据仍在 %LOCALAPPDATA%\FloatingTasks，旧数据兼容，任务 ID 在首次保存后稳定，导入时重新分配 ID。

构建：

```powershell
.\build.ps1
.\build.ps1 -Test
.\tests\McpStdio.ps1
```

回归覆盖原有计时/撤销/持久化、任务 ID、跨任务操作、多层步骤、无效批次不部分添加、完成/恢复、协议握手、保存失败，以及隔离管道到 UI 线程的实际调用。stdio 检查验证正式入口的工具发现、中文编码、标准输出和 EOF 退出；测试不创建真实用户任务。

协议实现是本地工具所需的 MCP 子集，兼容 2024-11-05、2025-03-26、2025-06-18、2025-11-25 握手版本；不声明 resources、prompts 或远程传输能力。参考 [MCP 生命周期规范](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle)。

## 用对话记录便签
便签跨任务共享，无需 task_id。连接新版 MCP 后可说：
> 把这句存到便签中：先验证接口，再完善样式。
> 把刚才讨论的方案总结成一条便签。

- add_note(content)：新增便签，返回 Id、Content、Source、Created、Updated。
- list_notes()：查询已保存便签及稳定 Id。
- update_note(note_id, content)：修改指定便签。
每条 1 至 20000 字符。保存原文时完整保留换行与空格；总结由调用方模型完成。
桌面便签列表同步刷新，不覆盖手动输入草稿，不改变任务和计时。
需要支持 MCP 的模型客户端连接本项目服务；仅说这句话而未接入服务不会自动写入。
更新后请正常退出旧版浮记、启动新版，并让模型客户端重新连接 MCP 以刷新工具列表。
