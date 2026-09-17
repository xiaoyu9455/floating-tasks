# 架构与维护

这是一款本地 Windows 小工具，继续使用系统已有的 .NET Framework / WinForms。无需引入网页运行时、服务端或依赖注入框架。

## 职责与依赖

```text
Program（组装依赖与单实例）
  └─ MainWindow（显示与交互）
       └─ TaskSession（任务操作、计时、撤销、保存时机）
            ├─ IClock → MonotonicClock（单调时钟）
            └─ ITaskRepository → JsonTaskRepository（本地原子存储）
```

- `src/Domain/TaskModel.cs`：任务、记录及累计规则，不依赖窗口。
- `src/Application/TaskSession.cs`：新增、删除、撤销、切换、暂停和导入。所有界面写操作经过同一个会话。
- `src/Persistence/JsonTaskRepository.cs`：JSON 校验、兼容版本 1、备份恢复与原子替换。路径由构造函数传入，不使用可变的全局存储路径。
- `src/UI/`：生命周期与布局、绘制、交互、对话框分别维护；Palette 统一颜色。使用部分类型拆分同一窗口的显示职责，不引入额外窗口间状态同步。
- `tests/`：可替换时钟和仓储的行为测试，以及窗口渲染检查。测试代码不编译进正式程序。

## 时间与状态约定

同一时刻多个记录可以运行。每条独立累计；任务累计只加一份实际运行时间。时间推进使用 Stopwatch，不受系统时间被校准或改动影响。

暂停单条记录时递归暂停整棵子树，先统一结算到操作时刻；继续仅启动所选记录，不自动启动下级。

操作前结算计时。退出、睡眠、锁屏会暂停；加载时所有记录暂停。UI 每秒刷新正在计时的显示，运行中每两秒检查保存；没有数据变化时不写磁盘。

撤销保存的是删除的子树、原列表和索引，而不是整个任务快照。因此删除之后新增、改名、继续累计的数据不会被撤销覆盖。切换任务后清除撤销。

## 数据与故障处理

正式数据固定保存在 `%LOCALAPPDATA%\FloatingTasks`，不属于项目清理范围。保留旧版 JSON 字段兼容。

先写临时文件并 flush，再原子替换主文件。主文件损坏时读备份；恢复后首次写入保留有效备份，并另存损坏原件。保存失败保持 dirty，后续继续重试，界面显示错误；用户主动退出时若保存仍失败，会提示是否继续退出。

导入校验格式、空对象、重复标识、负时间和层级上限。导入到现有工作区时重新分配记录标识，防止重复导入造成冲突。

## 层级约定（2026-09）

结构固定为三级：任务 → 子记录 → 细分步骤，细分步骤不能再拆分。引擎（Flatten/暂停/完成/恢复）保持深度无关，兼容旧数据；创建路径全部加限制：TaskSession.Add 与 AddPlannedSteps 只接受顶层列表或顶层子记录的 Children，MCP Owner 校验 parent 必须是顶层记录，UI 右栏不再提供加步骤与展开按钮。加载和导入时 Logic.FlattenDeepSteps 把旧数据里更深层级上提为同级步骤，每条记录的用时与状态原样保留。

## 窗口与资源

Region 决定实际窗口形状与面板间的空白区域，不使用紫色透明键。原生系统事件、托盘、定时器和缓存字体随窗口 Dispose 释放。

当前透明度仍作用于整张窗口与文字。窗口显示测试覆盖 100% / 200% 绘制和滚动布局；实际关机、睡眠通知仍需人工实机验证。自绘图标暂未提供完整屏幕阅读器支持。

## 构建与检查

```powershell
.\build.ps1
.\build.ps1 -Test
```

正式产物是根目录 `浮记.exe` 与 `FloatingTasks.Mcp.exe`。测试结果、预览和临时数据库全部进入 `artifacts/`，可随时删除，下次检查会重新生成。

## 完成与专注（2026-09）

Entry 新增 Completed / CompletedAt / CompletionBatch / NextStep；TaskItem 的 LastEntryId 用于恢复上下文。
保留 JSON Version 1 读取兼容；旧文件缺少偏好字段时沿用 Enter 立即开始、并行计时，新数据库默认 Enter 先记下、单项专注。
完成子树使用独立批次；恢复仅撤回同批完成，不重开先前独立完成的子项。恢复子项同时重开已完成祖先，不改变仍在运行的未完成祖先。
没有增加按日计时字段，累计用时不可当作今日用时。今日完成数依据 CompletedAt，恢复完成后会从统计中移除。
MainWindow.Focus.cs 维护下一步编辑、已完成列表、窗口位置恢复和记录右键菜单。
图标 assets/floating-tasks.ico 同时由 /win32icon 和嵌入资源打包，主窗口和 NotifyIcon 共用受管理的 Icon，随窗口释放。

默认悬浮列表最多同时显示三项，手动拉高后按可用高度显示更多，其余通过滚轮查看。顶部折叠按钮把整个窗口收成单行标题与累计计时，展开后恢复面板和输入草稿；折叠不改变计时状态。折叠状态仅在当前进程内保存，旧数据 MiniMode 字段仍忽略。

## MCP 接入

新增 src/Mcp/AgentTasks.cs（工具与参数校验）、McpProtocol.cs（stdio 协议）、AgentPipe.cs（桌面管道与控制台入口）。Program 组装管道，MainWindow.Mcp.cs 把请求串行调度到 UI 线程并刷新界面。

Agent → FloatingTasks.Mcp.exe（stdio）→ 当前用户/会话命名管道 → MainWindow → TaskSession → JsonTaskRepository。只有桌面进程拥有真实数据库的写入口。管道 ACL 仅允许当前用户；连接和响应等待有超时，写调用不自动重试。

TaskItem 新增持久化 Id；旧数据自动生成，MCP 首次成功调用时确保保存，导入时重新分配。普通规划修改不改变 Selected 或暂停当前计时，只有明确 running 操作才切换任务。正式构建现在同时产出根目录 浮记.exe 和 FloatingTasks.Mcp.exe。接入步骤见 MCP接入说明.md，配置示例见 mcp.config.example.json。

## 菜单生命周期与恢复快照

临时菜单在 Closed 后通过 BeginInvoke 延迟 Dispose，避免 WinForms 点击关闭流程继续访问已释放句柄。窗口拥有菜单容器负责兜底清理。测试同时覆盖菜单项点击与反复打开。

JsonTaskRepository 每次实例首次覆盖已有数据前，将有效旧数据复制到数据目录 history 的唯一文件，保留跨会话快照；从备份恢复时保存有效备份的快照。滚动 tasks.json.bak 仍保留。


## 便签
Database.Notes 为全局独立便签列表，旧数据默认空列表。TaskSession.Notes.cs 统一写入、删除和撤销；
导入重新分配便签 ID。MainWindow.Notes.cs 实现下拉面板、多行编辑和历史列表；折叠保留内存草稿。
AgentTasks 暴露 add_note/list_notes/update_note，经原有 UI 线程与原子存储链路写入。

## 可拖拽尺寸（2026-09）
MainWindow.Resize.cs 负责主面板右侧、底部和右下角的鼠标捕获、尺寸限制、松手保存及恢复默认。PanelWidth / PanelListHeight 以逻辑像素存储，旧数据缺字段时继续使用 272 宽、最多三行的自动高度。用户调整高度后按实际可用高度计算可见行数，绘制、滚动、快捷步骤与输入区域共用尺寸。右侧步骤栏使用主面板宽度；便签保留可滚动编辑区。UpdateLayout 同步输入字体以支持 DPI 变化。WindowChecks 覆盖拖拽、尺寸持久化、折叠恢复、滚动末项、便签布局、200% DPI 和最小尺寸。
