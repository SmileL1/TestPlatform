# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

TestPlatform 是一个 WPF/Web 双模式自动化测试平台，面向日语仓库管理系统 SmartZaiko。后端为 ASP.NET Core 9 Web API，前端为 Vue 3 + Element Plus，通过 SignalR 实时推送测试进度。

## 构建与运行

### 后端（Windows 专属）

```powershell
# 构建
dotnet build src/TestPlatform.API/TestPlatform.API.csproj

# 运行（目标框架必须是 net9.0-windows，因为依赖 WPF/UIAutomation）
dotnet run --project src/TestPlatform.API/TestPlatform.API.csproj --framework net9.0-windows

# 发布
dotnet publish src/TestPlatform.API/TestPlatform.API.csproj -f net9.0-windows -c Release -o src/TestPlatform.API/publish
```

### 前端

```powershell
cd web/testplatform-web
npm install
npm run dev        # 开发服务器 (Vite, 默认 5173)
npm run build      # 类型检查 + 生产构建
```

## 配置

后端配置文件：`src/TestPlatform.API/appsettings.json`

- **数据库**：PostgreSQL，连接字符串在 `ConnectionStrings:Default`
- **AI 引擎**：DeepSeek API，配置在 `DeepSeek:ApiKey / Model / BaseUrl`
- **前端 CORS**：允许 `localhost:5173`、`5174`、`3000`

启动时自动创建数据库（若不存在）并执行 `MigrateDatabase` + `InitDatabase`，无需手动建表。

## 架构

### 后端项目结构

```
TestPlatform.sln
└── src/
    ├── TestPlatform.Core/          # 实体 + DbContext（SqlSugar + PostgreSQL）
    │   ├── Entities/               # Scenario, TestRun, RunLog, TestPlan, TestSuite 等
    │   └── DB/DbContext.cs         # AppDbContext：CreateClient() + InitDatabase()
    └── TestPlatform.API/           # ASP.NET Core Web API（net9.0-windows）
        ├── Program.cs              # 启动：注册服务、DB 初始化、SignalR Hub
        ├── Controllers/            # REST API 端点
        ├── Hubs/TestHub.cs         # SignalR：客户端按 run_{runId} 分组订阅
        ├── Services/
        │   ├── AgentService.cs     # 核心调度：AI 模式 vs 结构化模式
        │   ├── StructuredExecutor.cs # 回放录制步骤
        │   └── Recording/          # 鼠标/键盘录制服务
        └── AgentCore/
            ├── Agent.cs            # AI Agent 循环（LLM tool-calling 驱动）
            ├── DeepSeekClient.cs   # DeepSeek API 客户端
            ├── UIAutomationTools.cs# Windows UIAutomation 操作（WPF 控件）
            ├── BrowserTools.cs     # Playwright 浏览器操作
            ├── InputSimulator.cs   # 鼠标/键盘模拟
            └── ScreenCapture.cs    # 截图
```

### 执行模式

`AgentService.StartRunAsync` 支持三种模式：

- **`auto`**（默认）：场景有 `StepsJson` 时走结构化，否则走 AI
- **`structured`**：强制回放录制的步骤（`StructuredExecutor`）
- **`ai`**：强制调用 DeepSeek LLM 逐步推理

### 数据模型

- `Scenario`：测试场景，含 `Type`（wpf/web）、`StepsJson`（录制步骤）、`AssertionsJson`、`ParametersJson`
- `TestRun`：一次执行记录，状态：`running / passed / failed / cancelled`
- `RunLog`：每步详细日志，关联 `RunId`
- `TestPlan` / `TestPlanScenario` / `TestPlanRun` / `TestPlanRunItem`：测试计划与批量执行

### 前端路由（Vue Router）

| 路径 | 视图 | 说明 |
|------|------|------|
| `/` | ScenarioList | 场景列表 |
| `/scenario/:id/edit` | ScenarioEdit | 编辑场景 |
| `/run/:runId` | TaskMonitor | 实时监控（SignalR） |
| `/history` | HistoryView | 执行历史 |
| `/recording` | RecordingView | 录制操作步骤 |
| `/plans` | PlanList | 测试计划 |

### SignalR 事件

客户端加入 `run_{runId}` 组后接收：
- `StepLog`：每步执行结果（step/tool/args/result/success/thinking/timestamp）
- `RunFinished`：运行结束（runId/status/summary）

## 关键约束

- 后端**必须在 Windows 上构建和运行**（依赖 WPF、Windows UIAutomation、System.Drawing.Common）
- 录制时会卸载鼠标钩子，因此执行前若正在录制会自动停止录制（`AgentService` 第 56-60 行）
- `AppDbContext.CreateClient()` 每次返回新的 `SqlSugarClient` 实例（`IsAutoCloseConnection = true`），不可跨请求复用
- 场景 `Type` 字段区分 WPF（`wpf`）和浏览器（`web`）测试，AI Agent 可混用 `UIAutomationTools` 和 `BrowserTools`
