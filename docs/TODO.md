# TODO / 路线图

> 更新日期：2026-06-16 ｜ 按优先级与类别整理。带 🔴 为「已确认的现存缺口/隐患」，🟡 为改进项，🟢 为远期规划。

---

## 🔴 需尽快修复的缺口

> 下列 4 项已于 2026-06-16 修复，见文末「已完成」。当前无未处理的 🔴 项。

- [x] ~~前后端端口不一致~~ → 后端 `launchSettings.json` 已统一监听 `5000`，与前端 baseURL/Hub 同源。
- [x] ~~`TestSuite` 表未建~~ → `InitDatabase()` 已加入 `InitTables<TestSuite>()`。
- [x] ~~SignalR Hub 地址需与后端端口对齐~~ → 随端口统一已对齐（前端各视图均 `http://localhost:5000/hubs/test`）。
- [x] ~~`appsettings.json` 含真实密钥~~ → 已置空占位；真实 Key 移至被 `.gitignore` 的 `appsettings.Development.json`。

---

## 🌐 Web 网页端自动化测试（分阶段计划）

> Phase 1（MVP）已于 2026-06-16 落地：Web 场景现在能跑 **AI 推理执行**。Phase 2/3 待办。

### Phase 1 — MVP：AI 模式 Web 自动化 ✅
- [x] 引入 Playwright（`Microsoft.Playwright` 1.60），新增 `Web/BrowserDriver.cs`（异步语义化操作 API：navigate/scan/click/click_text/fill/select/get_text/wait/assert_text/screenshot）。
- [x] `Ai/BrowserToolSchemas.cs`：`browser_*` 工具定义，名称与前端「浏览器测试写法」一致。
- [x] `Ai/WebAiAgent.cs`：异步 LLM 工具循环；`DeepSeekClient` 工具清单参数化（默认仍为 WPF 工具，向后兼容）。
- [x] `RunService.ExecuteAsync` 按 `scenario.Type=="web"` 分发到 `WebAiAgent`+`BrowserDriver`；web 起始 URL 复用 `WindowTitle` 字段（零数据库改动）。
- [x] AI 截图验证零改动复用（`BrowserDriver` 整页截图 → 现有 `VisionVerifier`）。
- [x] 静态 demo 靶子 `samples/web-demo/`（下单表单页，含成功/失败视觉）+ seed 一个 web 示例场景。
- [x] **浏览器内核**：升级到 Playwright 1.60 后，其期望的 chromium 内核（revision 1223）与本机已装版本一致，直接用 Playwright 自带内核；`BrowserLauncher` 的 Edge/Chrome 回退保留为兜底。新环境如缺内核：`pwsh src/TestPlatform.API/bin/Debug/net9.0-windows/playwright.ps1 install chromium`。
- [x] **消除 `NU1903`**：升级 Playwright 1.44 → 1.60，不再传递有漏洞的 `System.Text.Json 6.0.0`，构建已无安全告警。

### Phase 2 — 浏览器录制回放 ✅
- [x] `Recording/BrowserRecorder.cs`：Playwright 注入脚本采集点击/输入/下拉 → 生成选择器化结构化步骤（web 版 `RecordedStep`，复用 SignalR `recording` 组）。
- [x] `Execution/BrowserStepPlayer.cs`：按录制步骤回放（失败重试一次、选择器点不到回退按文字点击、可选 AI 截图验证）；`RunService.ExecuteWebAsync` 按「有步骤+非 ai 模式」走结构化回放。
- [x] `RecordingController.Start` 扩展 `type` 参数（web 时 `WindowTitle` 存起始 URL）；前端 `ScenarioEdit` 放开 web 录制卡片并按类型区分文案；`Web/BrowserLauncher` 共享「内核→Edge→Chrome」回退启动。
- [x] `RecordingView` 独立录制页支持 web（顶部 WPF/Web 切换 + 起始 URL 输入，start/save 传 `type`）。
- [x] 录制元素命名修正：表单控件名取关联 label/placeholder（不取已输入值）；`<select>` 的多余 click 不再记录（避免选择被挤掉）。
- [ ] 抽 `IDriver` 接口统一 WPF/Web（当前 WPF 同步、Web 异步，平行实现）。

### Phase 3 — 完整化
- [ ] CDP 连接已开浏览器（`--remote-debugging-port=9222`，前端已有提示），替代每次新启浏览器。
- [ ] headless 选项、iframe/多标签页、并行执行、浏览器启动参数管理。
- [ ] WPF / Web 混合测试计划串联。

---

## 🟡 功能与体验改进

- [x] ~~场景 `Type=web` 落地或下线~~ → 已落地：Web 场景支持 AI 推理执行（见上「Web 网页端自动化测试」Phase 1）。
- [ ] **套件（Suite）前端管理 UI**
  `/api/suites` 与建表已就绪、`Scenario.SuiteId` 可归类，但前端无套件路由/页面，能力尚未对用户开放。
- [x] **断言编辑器**：`ScenarioEdit` 已有结构化断言可视化编辑（验证方式分组下拉 + AutomationId 选择 + 期望值 + 备注，免手写 JSON）；Web 场景隐藏此卡片（走 AI 截图验证）。Web 结构化断言（CSS 选择器求值）列入 Phase 3。
- [ ] **计划执行从轮询改推送**：`TestPlanController.ExecutePlanAsync` 用 2s 轮询等待场景终态，可改为事件/回调，减少延迟与无效查询。
- [x] **执行历史增强**：状态筛选/统计/错误列/日志/再运行/自动刷新均已具备；本轮补齐**时间范围筛选**（日期下推后端）+ **分页**（上限 100→500，前端分页），失败信息列经异常可读化显示为中文。
- [ ] **录制步骤的拖拽排序与手动插入**：后端 `save` 已支持前端编辑后的步骤，前端补齐拖拽/插入 `wait`/`press_key` 的能力。
- [ ] **目标窗口选择器**：在录制/新建场景处接入 `/api/system/windows` 下拉选择，替代手填窗口标题。
- [ ] **token / 耗时统计可视化**：`TestRun.TokenUsed` 已记录，前端可在历史与监控页展示成本与耗时趋势。

---

## 🟡 健壮性与质量

- [x] **单次执行的全局串行保护**：`RunService` 加全局执行信号量（`SemaphoreSlim(1,1)`），同一时刻只允许一个测试真正执行、其余排队（监控页提示「排队等待」）；执行前自动停止 WPF 与 Web 录制；`IsBusy` 暴露后录制接口在执行期间拒绝开始录制。
- [ ] **回放兜底坐标的有效性**：坐标兜底依赖分辨率/窗口位置不变，跨机器回放易失效，需提示或禁用坐标兜底选项。
- [x] **异常分类与可读化**：`RunService.Friendly()` 把失败信息从原始 `ex.Message` 归类为可读中文（认证/限流/浏览器启动/网页导航/元素超时/窗口未找到/LLM/数据库），已含中文的业务消息原样保留；WPF/Web/异常三条失败路径统一接入，失败时同时作为 summary 推送。
- [ ] **AI 验证地址兼容性测试**：`VisionVerifier.BuildEndpoint` 已适配多厂商，补充对常见厂商（通义/智谱/OpenAI/Azure）的回归用例。

---

## 🟡 工程与交付

- [ ] **补充自动化测试**：当前无单测/集成测试工程，至少为 `StepPlayer` 断言求值、`SettingsService` 加解密、`VisionVerifier` 解析加测试。
- [ ] **CI**：加 GitHub Actions（Windows runner）做 `dotnet build` 与前端 `npm run build` 校验。
- [ ] **配置样例**：提供 `appsettings.Example.json` 与前端 `.env.example`。
- [ ] **README 截图**：补齐各功能页界面截图（占位于 `docs/images/`）。
- [ ] **`docs/design.md` 与代码同步**：已随本次刷新；后续重构类/接口时同步更新。

---

## 🟢 远期规划

- [~] **Web/浏览器自动化引擎**：Playwright AI 模式已落地（Phase 1）；录制回放、CDP 连接、混合场景见上「Web 网页端自动化测试」Phase 2/3。
- [ ] **并行/分布式执行**：多机/多会话并行跑用例，平台侧做编排与结果汇聚。
- [ ] **用例版本管理与对比**：录制步骤/断言的版本化，失败时对比上次成功快照。
- [ ] **自愈式定位**：元素定位失败时由 LLM 结合 `scan_ui` 自动修复并回写步骤。
- [ ] **报告导出**：执行结果导出 HTML/PDF 报告，含截图与步骤明细。
- [ ] **权限与多用户**：登录、角色（测试人员/负责人/管理员）与数据隔离。

---

## 已完成（近期）

- [x] **端口统一**：后端 `launchSettings.json` 监听 `5000`，前端 API/SignalR 开箱即用。
- [x] **`TestSuite` 建表**：纳入 `AppDbContext.InitDatabase()`，套件接口可用。
- [x] **密钥治理**：`appsettings.json` 置空占位；真实 Key 移至 `appsettings.Development.json`（`.gitignore` 忽略），新增 `appsettings.Example.json` 模板与根 `.gitignore`。
- [x] 重构为分层结构（Ai / Execution / Recording / Wpf / Settings / Logging）。
- [x] 结构化回放 + 结构化验证条件（替代「仅看步骤报错」）。
- [x] AI 截图验证（`VisionVerifier`），可与结构化验证叠加判定。
- [x] 在线「设置」页 + API Key 加密存储 + 配置回退。
- [x] 录制三路合流（鼠标/键盘/UIA 事件）+ 噪声过滤 + 前端可编辑步骤。
- [x] 文档体系：README / 架构设计 / 需求设计 / 详细设计 / 本 TODO。
- [x] **Web 自动化 MVP（AI 模式）**：Playwright `BrowserDriver` + `BrowserToolSchemas` + `WebAiAgent`，`RunService` 按 type 分发，截图验证复用，静态 demo 靶子 `samples/web-demo/`。
