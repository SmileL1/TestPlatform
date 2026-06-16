# TestPlatform 详细设计文档

> 版本：v2.0 ｜ 更新日期：2026-06-16 ｜ 与当前主干代码同步
>
> 高层架构见 [架构设计文档](architecture.md)，需求见 [需求设计文档](requirements.md)。

---

## 目录

1. [系统架构总览](#1-系统架构总览)
2. [后端设计](#2-后端设计)
   - 2.1 [项目结构](#21-项目结构)
   - 2.2 [数据库设计](#22-数据库设计)
   - 2.3 [REST API 接口](#23-rest-api-接口)
   - 2.4 [SignalR 实时通信](#24-signalr-实时通信)
   - 2.5 [AI 推理执行引擎](#25-ai-推理执行引擎)
   - 2.6 [结构化回放引擎](#26-结构化回放引擎)
   - 2.7 [AI 截图验证](#27-ai-截图验证)
   - 2.8 [录制服务](#28-录制服务)
   - 2.9 [设置与配置](#29-设置与配置)
3. [前端设计](#3-前端设计)
4. [核心流程](#4-核心流程)

---

## 1. 系统架构总览

```
┌──────────────────────────────────────────────────────────────┐
│                    前端 (Vue 3 + Vite)                          │
│  PlanList PlanDetail PlanRunView PlanHistory                   │
│  ScenarioList ScenarioEdit TaskMonitor Recording History Set. │
└───────────────┬──────────────────────────┬────────────────────┘
                │ HTTP REST                 │ WebSocket (SignalR /hubs/test)
┌───────────────▼──────────────────────────▼────────────────────┐
│              ASP.NET Core 9 Web API (net9.0-windows)           │
│  ┌───────────┐  ┌───────────────┐  ┌──────────────────────┐   │
│  │Controllers│  │ Execution     │  │ Recording            │   │
│  │           │  │  RunService   │  │  Recorder/HookHost   │   │
│  └─────┬─────┘  │  StepPlayer   │  └──────────┬───────────┘   │
│        │        └───┬───────┬───┘             │               │
│        │            │       │     ┌───────────▼────────────┐  │
│        │       ┌────▼──┐ ┌──▼───┐ │ Wpf                    │  │
│        │       │ Ai    │ │Vision│ │ WpfDriver/ElementFinder│  │
│        │       │AiAgent│ │Verif.│ │ Screenshot/Input       │  │
│        │       │DeepSeek│└──┬───┘ └───────────┬────────────┘  │
│        │       └───┬───┘    │                 │               │
│  ┌─────▼───────────▼────────▼─────────────────▼────────────┐  │
│  │ AppDbContext (SqlSugar)        SettingsService(密钥加密) │  │
│  └────────────────────┬─────────────────────────────────────┘ │
└───────────────────────┼────────────────────────────────────────┘
                        │ PostgreSQL          │ UIAutomation/Win32  │ HTTPS
        ┌───────────────▼──────┐  ┌───────────▼──────┐  ┌──────────▼─────────┐
        │ scenarios test_runs  │  │ WPF 被测应用      │  │ DeepSeek / 多模态  │
        │ run_logs test_plan*  │  │ (WPF 被测应用)    │  │ (OpenAI 兼容)      │
        └──────────────────────┘  └──────────────────┘  └────────────────────┘
```

**部署说明**：后端必须运行在 Windows 上（依赖 WPF Runtime 与 Windows UIAutomation，并需访问本地桌面会话做钩子/截图），前端可独立部署于任意平台。

---

## 2. 后端设计

### 2.1 项目结构

```
src/
├── TestPlatform.Core/                  # 类库：实体 + DbContext
│   ├── Entities/
│   │   ├── Scenario.cs                  # 测试场景
│   │   ├── TestRun.cs / RunLog.cs       # 单次执行 + 步骤日志
│   │   ├── TestPlan.cs                  # 计划相关 4 个实体
│   │   ├── TestSuite.cs                 # 套件（已纳入 InitDatabase 建表）
│   │   └── AppSetting.cs                # 键值配置（设置页）
│   └── DB/DbContext.cs                  # CreateClient() + InitDatabase()
│
└── TestPlatform.API/                    # Web API 主项目（net9.0-windows）
    ├── Program.cs                       # 建库 + 迁移 + 服务注册 + Hub 映射
    ├── Controllers/                     # 见 2.3
    ├── Hubs/TestHub.cs                  # SignalR Hub
    ├── Ai/
    │   ├── AiAgent.cs                   # WPF 的 LLM 工具调用主循环
    │   ├── WebAiAgent.cs                # Web 的 LLM 工具调用主循环（异步）
    │   ├── DeepSeekClient.cs            # DeepSeek 会话封装（工具集可注入 WPF/Web）
    │   ├── ToolSchemas.cs               # WPF 工具定义（function calling）
    │   ├── BrowserToolSchemas.cs        # Web 工具定义（browser_*）
    │   └── VisionVerifier.cs            # 截图验证（多模态，两端共用）
    ├── Execution/
    │   ├── RunService.cs                # 执行调度（IRunService，按 Type 分发 wpf/web）
    │   ├── StepPlayer.cs                # WPF 结构化回放 + 断言求值
    │   ├── BrowserStepPlayer.cs         # Web 结构化回放
    │   └── Assertion.cs                 # 验证条件模型
    ├── Recording/
    │   ├── Recorder.cs                  # WPF 三路事件合流录制（IRecorder）
    │   ├── BrowserRecorder.cs           # Web 注入脚本录制（IBrowserRecorder）
    │   ├── HookHost.cs                  # 低级鼠标/键盘钩子宿主
    │   └── RecordedStep.cs              # 录制步骤（WPF/Web 共用）
    ├── Wpf/
    │   ├── WpfDriver.cs                 # 桌面语义化操作 API
    │   ├── ElementFinder.cs             # 窗口/控件定位
    │   ├── Input.cs / Screenshot.cs     # 键盘模拟 / 截图
    │   └── ElementInfo.cs / OpResult.cs # 控件信息 / 操作结果
    ├── Web/
    │   ├── BrowserDriver.cs             # 网页语义化操作 API（Playwright，异步）
    │   └── BrowserLauncher.cs           # 浏览器启动：内核→Edge→Chrome 回退
    ├── Settings/
    │   ├── SettingsService.cs           # 配置存取（白名单+回退）
    │   └── SecretProtector.cs           # 密钥加解密
    └── Logging/
        ├── AiLog.cs                     # AI 请求/响应落文件
        └── LogCleanupService.cs         # 过期日志清理（HostedService）
```

启动顺序（`Program.cs`）：`EnsureDatabaseCreated`（连 `postgres` 主库检测并建目标库）→ `MigrateDatabase`（补列/改类型/放宽 nullable）→ `InitDatabase`（CodeFirst 建表）→ 注册 `AppDbContext`/`SettingsService`/`RunService`/`Recorder`/`BrowserRecorder`/`LogCleanupService` → 映射 Controllers 与 `/hubs/test`。

> 服务生命周期：`AppDbContext`、`ISettingsService`、`IRunService`、`IRecorder`、`IBrowserRecorder` 均为**单例**；
> 数据库访问每次经 `AppDbContext.CreateClient()` 取新 `SqlSugarClient`（`IsAutoCloseConnection=true`），不跨请求复用。

### 2.2 数据库设计

所有表由 SqlSugar CodeFirst（`InitTables`）建立，主键统一 `uuid`（非自增）。

#### scenarios — 测试场景
| 列 | 类型 | 说明 |
|----|------|------|
| id | uuid PK | 场景 ID |
| suite_id | uuid NULL | 所属套件（可空） |
| type | varchar(20) | `wpf`（默认，UIAutomation）/ `web`（Playwright 浏览器） |
| name | varchar | 场景名称 |
| window_title | varchar | 目标窗口标题，用于 UIAutomation 定位被测 WPF 窗口 |
| description | text | AI 目标 / 自然语言步骤，支持 `{{参数}}` |
| steps_json | text NULL | 录制步骤（结构化模式使用） |
| parameters_json | text | 参数定义 `[{name,label,defaultValue}]` |
| assertions_json | text | 验证条件（结构化断言数组，或旧版字符串数组） |
| max_steps | int | AI 模式最大步数（默认 60） |
| aiverifyenabled | bool | 是否启用 AI 截图验证 |
| aiverifyprompt | text NULL | AI 验证额外提示（默认用描述） |
| created_at / updated_at | timestamp | 时间戳 |

#### test_runs — 单次执行
| 列 | 类型 | 说明 |
|----|------|------|
| id | uuid PK | 执行 ID |
| scenario_id | uuid | 关联场景 |
| status | varchar | `running/passed/failed/cancelled`（初始 `pending`） |
| input_params_json | text | 运行时参数 JSON |
| total_steps | int | 实际步数 |
| token_used | int | LLM token 用量（AI 模式） |
| error_msg | varchar NULL | 失败原因 |
| started_at / finished_at | timestamp | 起止时间 |

#### run_logs — 步骤日志
| 列 | 类型 | 说明 |
|----|------|------|
| id | uuid PK | 日志 ID |
| run_id | uuid | 关联执行 |
| step_number | int | 步骤序号 |
| tool_name | varchar | 工具名（click/set_text/assert/done/system…） |
| arguments | text | 参数 |
| result | text | 结果文本 |
| thinking | text NULL | AI 思考过程（仅 AI 模式） |
| success | bool | 是否成功 |
| created_at | timestamp | 记录时间 |

#### 计划相关
```
test_plans(id, name, description, created_at, updated_at)
test_plan_scenarios(plan_id, scenario_id, sort_order)         -- 联合主键(plan_id,scenario_id)
test_plan_runs(id, plan_id, status, total_count, passed_count,
               failed_count, running_count, mode, started_at, finished_at)
test_plan_run_items(id, plan_run_id, scenario_id, test_run_id NULL, sort_order, status)
```
`test_run_id` 在场景执行前为 NULL，开始后立即写入，前端据此实时拉日志。

#### app_settings — 配置
`config_key`（PK,varchar100）/ `config_value`（text NULL）。敏感项（两个 ApiKey）以密文存储。

`test_suites` 表已在 `InitDatabase()` 建立，套件接口可用。

### 2.3 REST API 接口

基址 `http://localhost:5000/api`（后端 `launchSettings.json` 默认监听 5000，与前端一致）。

#### 场景 `/api/scenarios`
| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/scenarios` | 全部场景（按创建时间倒序） |
| GET | `/scenarios/{id}` | 单个场景 |
| POST | `/scenarios` | 创建 |
| PUT | `/scenarios/{id}` | 更新（不覆盖 `CreatedAt`；未传 `StepsJson` 时保留原录制步骤） |
| DELETE | `/scenarios/{id}` | 删除 |
| POST | `/scenarios/seed` | 导入示例场景（仅当库为空） |

#### 执行 `/api/tasks`
| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/tasks/run` | 启动执行，返回 `{runId}` |
| POST | `/tasks/{runId}/cancel` | 取消 |
| GET | `/tasks/history?scenarioId=` | 历史（近 100 条） |
| GET | `/tasks/{runId}/logs` | 步骤日志（按 step 升序） |
| GET | `/tasks/{runId}/status` | 执行状态 |

`RunRequest`：
```json
{ "scenarioId": "uuid", "mode": "auto|structured|ai", "inputParams": { "车牌号": "12-34" } }
```

#### 测试计划 `/api/plans`
| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/plans` | 列表（含 `scenarioCount`） |
| GET/POST/PUT/DELETE | `/plans` `/plans/{id}` | 计划 CRUD（删除级联清关联与执行记录） |
| GET | `/plans/{id}/scenarios` | 计划内场景（含详情，按 sort） |
| POST | `/plans/{id}/scenarios` | 批量添加（body：`[uuid]`，去重续排序） |
| DELETE | `/plans/{id}/scenarios/{scenarioId}` | 移除场景 |
| POST | `/plans/{id}/run` | 批量执行，返回 `{planRunId}` |
| POST | `/plans/runs/{runId}/cancel` | 取消整批 |
| GET | `/plans/{id}/runs` | 计划执行历史（近 20） |
| GET | `/plans/{id}/scenario-status` | 计划内各场景「最近一次运行」状态 |
| GET | `/plans/runs/{runId}/items` | 某批次内各场景项状态（含 `testRunId`） |

#### 录制 `/api/recording`
| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/recording/start` | 开始录制，body：`{windowTitle, type}`；`type=web` 时 `windowTitle` 存起始 URL，启动浏览器录制 |
| POST | `/recording/stop` | 停止（按当前录制类型分发），返回 `{steps,count}` |
| GET | `/recording/steps` | 当前步骤 + `isRecording` |
| DELETE | `/recording/steps/{index}` | 删除某步 |
| POST | `/recording/clear` | 清空 |
| POST | `/recording/save` | 另存为新场景（body 可带 `type` 与前端编辑后的 steps） |
| POST | `/recording/save-to/{scenarioId}` | 覆盖已有场景的录制内容（保留名称/断言等） |

> 录制按 `type` 分发到 `Recorder`（wpf）或 `BrowserRecorder`（web），两者共用同一 SignalR `recording` 组与 `RecordedStep` 结构。

#### 套件 `/api/suites`
CRUD + `GET /suites/{id}/scenarios`；删除套件时把其下场景 `suite_id` 置空。

#### 设置 `/api/settings`
| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/settings` | 脱敏配置：`{values, configured}`，**不回传密钥明文** |
| PUT | `/settings` | 保存白名单内键；敏感项加密、留空表示不变 |

#### 系统 `/api/system`
| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/system/windows` | 枚举桌面顶层窗口（供目标窗口选择） |
| GET | `/system/logs/info` | AI 日志目录/文件数/大小 |
| POST | `/system/logs/cleanup?days=14` | 清理过期 AI 日志 |

### 2.4 SignalR 实时通信

**Hub 路径**：`/hubs/test`

客户端 → 服务端（加入/离开组）：`JoinRun(runId)` / `LeaveRun(runId)` / `JoinRecording()` / `LeaveRecording()`。

服务端 → 客户端：

**`StepLog`**（组 `run_{runId}`）：
```json
{ "step":3, "tool":"click", "args":"{...}", "result":"已点击 btn_add",
  "success":true, "thinking":"需要先加一行明细…", "time":"14:23:05", "timestamp":1748909785000 }
```
**`RunFinished`**（组 `run_{runId}`）：
```json
{ "runId":"uuid", "status":"passed|failed|cancelled", "summary":"…", "finishedAt":"2026-06-16T…Z" }
```
**`RecordedStep`**（组 `recording`）：录制到新步骤即推 `RecordedStep`（index/action/target/targetName/value/controlType/gridId/x/y/time）。

### 2.5 AI 推理执行引擎

入口 `AiAgent.RunAsync(testName, windowTitle, goal, assertions, maxSteps, vision?, aiVerifyPrompt?, ct)`：

1. `WpfDriver.Attach(windowTitle)` 定位窗口，找不到直接失败。
2. `DeepSeekClient.StartAsync(systemPrompt, firstMessage)`，首条消息含测试目标与验证条件。
3. 循环（直至 `done` / 超 `maxSteps` / 取消）：解析本轮 `tool_calls` → 逐个 `ExecuteTool` → `Notify`（落库 + SignalR）→ `AddToolResult` → `ContinueAsync` 请求下一轮。
4. 收到 `done(success,summary)`：若开启 AI 截图验证则附加截图判定；**综合判定** = Agent 自报成功 且（未启用 AI 验证 或 AI 验证通过）。
5. 未显式 `done` 而结束时，也对最终界面做一次独立 AI 验证，通过则「翻盘」判成功。

**工具集（`ToolSchemas`，对应 `WpfDriver`）**

| 工具 | 说明 |
|------|------|
| `scan_ui` | 扫描控件树（消耗 token，仅首次/未知/失败时用） |
| `click` | 按 AutomationId 点击 |
| `set_text` | 输入框写文本 |
| `select_item` | 下拉框选项 |
| `read_value` | 读控件值 |
| `press_key` | 特殊键 Enter/Tab/Escape/Backspace/Delete/F1~F12 |
| `wait` | 等待毫秒 |
| `get_row_count` | 明细表格行数（增行前必查） |
| `click_cell` | 表格单元格进入编辑（row + column 关键字） |
| `set_unit_price` | 填指定行単価（点格+写值+Tab） |
| `select_product` | 商品弹窗选商品 |
| `check_dialog` | 检测应用内弹窗及按钮 |
| `assert_text` | 断言控件文本包含 |
| `done` | 报告结果（success + summary） |

`DeepSeekClient` 维护对话历史，按需压缩旧的 `scan_ui` 结果以省 token；`TotalTokensUsed` 计入 `TestRun.TokenUsed`。系统提示中内置被测应用的已知 AutomationId 与操作规则，减少 `scan_ui` 调用。

**Web 版（`WebAiAgent`，对应 `BrowserDriver`）**：结构与 `AiAgent` 对称但全异步。入口 `RunAsync(testName, startUrl, goal, …)` 先 `BrowserDriver.StartAsync(startUrl)`（启动浏览器并导航），再进入同样的 tool-calling 循环。工具集为 `BrowserToolSchemas`：

| 工具 | 说明 |
|------|------|
| `browser_navigate` | 跳转 URL |
| `browser_scan` | 扫描页面可交互元素（selector + 文字） |
| `browser_click` / `browser_click_text` | 按 CSS 选择器 / 按可见文字点击 |
| `browser_fill` | 输入框填写 |
| `browser_select` | 下拉选择（按 value 或可见文本） |
| `browser_get_text` | 读取元素文本 |
| `browser_wait` | 等待毫秒 |
| `assert_text` | 断言元素文本包含 |
| `done` | 报告结果 |

`DeepSeekClient` 的工具集通过构造参数注入（默认 `ToolSchemas.All`，Web 路径传 `BrowserToolSchemas.All`），其余会话逻辑两端复用。

### 2.6 结构化回放引擎

`StepPlayer.RunAsync(windowTitle, steps, assertions?, vision?, goal, aiPrompt?, ct)`：

- **参数替换**：步骤 `Target/Value` 与断言 `Expected` 中 `{{参数}}` 用运行时值替换。
- **逐步重放**：`click/click_cell/set_text/select_item/press_key/wait`；
  `click/click_cell/set_text/select_item` 失败时**延迟 600ms 重试一次**。
- **坐标兜底**：`pos(x,y)` 目标或元素找不到且录有坐标时回退 `ClickPoint`。
- **弹窗处理**：若下一步录制的是 `dialog_btn*` 点击则交给录制（尊重 Yes/No）；否则在点击/选择类操作后**自动关闭弹窗**（最多连 5 层）。
- **提前终止**：连续 5 步失败即停（界面已偏离录制前提）。
- **断言求值**（`Assertion.Op`）：
  - 读值：`equals` / `contains` / `notEmpty`
  - 界面：`exists` / `notExists` / `textVisible` / `textNotVisible`
  - 弹窗：`noDialog` / `dialogContains` / `dialogNotContains`
- **综合判定**：有断言或 AI 验证 → 启用项都通过才算通过；都没有 → 看是否「零步骤失败」。
  返回 `PlayResult`（步数/失败数/断言通过数/AI 结果/失败原因），由 `RunService.BuildSummary` 生成可读 summary。

**Web 版（`BrowserStepPlayer`）**：`RunAsync(startUrl, steps, vision?, goal, aiPrompt?, ct)`，先 `BrowserDriver.StartAsync` 再逐步重放。

- 步骤 `action` 与 WPF 同名：`click` / `set_text`（→ Fill）/ `select_item`（→ Select）/ `press_key` / `wait`；`Target` 为 CSS 选择器。
- **参数替换**：`Target/Value` 中 `{{参数}}` 用运行时值替换。
- **失败重试一次**；`click` 选择器点不到时**回退按可见文字点击**（`TargetName`）。
- 可选 **AI 截图验证**（整页截图 → `VisionVerifier`）。综合判定：开了 AI 验证则二者都通过，否则看步骤是否零失败。

### 2.7 AI 截图验证

`VisionVerifier.VerifyAsync(goal, extraPrompt?, imageBase64, ct)`：

- 走 OpenAI 兼容 `/v1/chat/completions` 多模态消息（text + image_url:data URI）。
- `BuildEndpoint` 自动补全地址：含 `/chat/completions` 直接用；以 `/vN` 结尾补 `/chat/completions`；否则补 `/v1/chat/completions`（适配通义/智谱等）。
- 提示要求模型**首行**输出「`结论：通过` / `结论：不通过`」，据此解析 `Pass`；未配置（无 Key/Model/URL）则 `Skipped=true`，不参与判定。
- 请求/响应写 `AiLog`（不含图片 base64）。

### 2.8 录制服务

**WPF 录制（`Recorder`）** 用三路事件源合成一条步骤流：

1. **鼠标钩子**（`WH_MOUSE_LL`）：**按下时刻**后台解析命中元素（点击常立即弹窗/跳转，必须用按下快照），抬起点与按下点接近则确认为一次点击。表格单元格优先识别为 `click_cell`（保留 gridId/真实行号/列关键字），按钮内文字/图标提升到带 id 的可交互祖先。
2. **键盘钩子**（`WH_KEYBOARD_LL`）：仅当被测进程在前台时记录白名单键（Enter/Tab/Escape/F1~F12，被测系统按钮多绑功能键）。
3. **UIAutomation 属性事件**：`ValuePattern.ValueProperty`（→ `set_text`，ComboBox 则 `select_item`）与 `SelectionItemPattern.IsSelectedProperty`（→ `select_item`）。同元素连续变化**防抖**只留最后值。

**噪声过滤**：只读计算字段、类名/绑定路径式名称、`PART_*` 模板内部件、无 id 又无名称的元素、ComboBox 内部选项的冗余选中事件等一律丢弃。

**防崩溃**：钩子委托用 `GCHandle.Alloc` 固定防 GC 回收；`Stop()` 先卸钩子再移除 UIA 事件。
**与执行互斥**：见 `RunService` 启动前 `Stop()` + 等待。

保存时（`save`/`save-to`）做轻量 `Dedup`（合并相邻同目标输入、丢弃点击行后冗余 select_item），并据 `set_text` 步骤生成参数建议、用步骤描述拼出场景自然语言描述。

**Web 录制（`BrowserRecorder`）**：用 Playwright 打开浏览器，往页面注入监听脚本采集操作。

- 注入用 `Page.AddInitScriptAsync`（每次文档加载重新挂监听，导航后仍生效）+ `ExposeBindingAsync("__recordStep")`（JS → .NET 回传）。注入脚本须用 **IIFE 立即执行**（`AddInitScript` 直接执行脚本文本，非函数）。
- 监听 DOM 的 `click` 与 `change`：点击 → `click`；文本输入 → `set_text`；下拉 → `select_item`（`<select>`/文本框的 `click` 被跳过，避免多余步骤）。
- **选择器**：优先 `#id` → `[name=…]` → 简单 `nth-of-type` 路径。
- **元素名**：取关联 `<label>` / 包裹 label / `placeholder` / `aria-label` / `name`（**绝不取已输入的值**），保证「密码」不会显示成输入内容。
- 与 WPF 录制**共用** `RecordedStep` 结构、SignalR `recording` 组、防抖（同元素连续输入只留最后值）。

### 2.9 设置与配置

`SettingsService` 管理白名单键：`DeepSeek:{ApiKey,Model,BaseUrl}`（操作）、`AiVision:{ApiKey,Model,BaseUrl}`（验证）、`Target:DefaultWindow`、`Logs:RetentionDays`。
- **生效值**（`GetResolvedAsync`）：本表有则用（敏感项解密），否则回退 `appsettings.json`。
- **脱敏视图**（`GetMaskedAsync`）：非敏感项明文 + 敏感项 `configured` 布尔，绝不回传密钥。
- **保存**：仅覆盖提交到的键；敏感项加密、留空表示保持原值。

---

## 3. 前端设计

### 3.1 路由（`router/index.ts`）
| 路径 | 组件 | 说明 |
|------|------|------|
| `/` → `/plans` | — | 重定向到计划 |
| `/plans` | PlanList | 计划列表 |
| `/plans/:id` | PlanDetail | 计划详情 + 场景管理 |
| `/plans/:id/history` | PlanHistoryView | 计划执行历史 |
| `/plans/:planId/run/:runId` | PlanRunView | 计划批量执行监控 |
| `/scenarios` | ScenarioList | 全部场景 |
| `/scenarios/new`、`/scenarios/:id/edit` | ScenarioEdit | 新建/编辑场景 |
| `/monitor/:runId` | TaskMonitor | 单次执行实时监控 |
| `/history` | HistoryView | 全局执行历史 |
| `/recording` | RecordingView | 操作录制 |
| `/settings` | Settings | 设置 |

侧边栏导航（`App.vue`）：测试计划 / 全部场景 / 操作录制 / 执行历史 / 设置。
设计风格为瑞士排版编辑风（暖纸浅底、衬线+等宽混排、左墨条激活、无渐变）。

### 3.2 API 封装（`api/index.ts`）
`planApi / scenarioApi / recordingApi / settingsApi / systemApi / taskApi`，Axios `baseURL = http://localhost:5000/api`，与后端默认端口一致。

### 3.3 SignalR 客户端
```ts
const conn = new signalR.HubConnectionBuilder()
  .withUrl('http://localhost:5000/hubs/test').withAutomaticReconnect().build()
await conn.start(); await conn.invoke('JoinRun', runId)
conn.on('StepLog', d => logs.value.push(d))
conn.on('RunFinished', d => status.value = d.status)
```
录制页改用 `JoinRecording()` + 监听 `RecordedStep`。

---

## 4. 核心流程

> `RunService.ExecuteAsync` 先按 `scenario.Type` 分流：`web` → `ExecuteWebAsync`（见 4.4），其余走下述 WPF 路径。

### 4.1 AI 模式执行
`POST /tasks/run(mode=ai)` → `RunService` 建 `TestRun(running)` → `Task.Run(ExecuteAsync)` → 取生效 AI 配置 → `AiAgent.RunAsync`：`Attach` → `StartAsync(goal)` → 多轮 tool calling（每步 `PushLog` 落库 + SignalR）→ `done` → 可选 AI 截图验证 → 综合判定 → 更新 `TestRun` + 推 `RunFinished`。

### 4.2 录制 → 结构化回放
录制：`/recording/start` 装钩子 + UIA 事件 → 操作被测应用 → 实时推 `RecordedStep` → `/recording/save-to/{id}` 写 `steps_json`。
回放：`POST /tasks/run(mode=structured|auto)` → `StepPlayer.RunAsync` 逐步重放（重试/坐标兜底/自动关弹窗）→ 断言求值 → 可选 AI 截图验证 → `PlayResult` → `RunFinished`。

### 4.3 计划批量执行
`POST /plans/{id}/run` 建 `TestPlanRun` + N 个 `TestPlanRunItem` → 后台顺序：标 item `running` → `RunService.StartRunAsync` 拿 `runId` 立即回写 → 轮询 `TestRun` 终态（最多 ~20 分钟，支持取消）→ 更新 item 与计划统计 → 全部完成标 `completed`。前端轮询 `/plans/runs/{runId}/items` 展示进度。

### 4.4 Web 场景执行（`ExecuteWebAsync`）
起始 URL 取自 `Scenario.WindowTitle`。按「有录制步骤 + 非 ai 模式」决定走结构化回放还是 AI：
- **结构化**：`BrowserStepPlayer.RunAsync(startUrl, steps, …)` → `BrowserDriver.StartAsync`（启动浏览器/回退 Edge）→ 逐步重放（重试、选择器回退文字点击）→ 可选 AI 截图验证 → `PlayResult` → 落库 + `RunFinished`。
- **AI**：`WebAiAgent.RunAsync(startUrl, goal, …)` → 多轮 `browser_*` 工具调用 → `done` → 可选 AI 截图验证 → 落库 + `RunFinished`。
- **录制**：`/recording/start{type:web}` → `BrowserRecorder` 注入脚本采集 → 实时推 `RecordedStep` → 保存为 web 场景（步骤 action 与 WPF 同名，前端 UI 复用）。

---

*本文档依据当前源代码整理，反映真实实现状态；如与代码不符以代码为准并同步更新本文。*
