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

## 🟡 功能与体验改进

- [ ] **场景 `Type=web` 落地或下线**
  当前 `web` 仅为占位、无浏览器驱动。要么补齐浏览器自动化（见远期），要么在 UI/文档中暂时隐藏该选项避免误导。
- [ ] **套件（Suite）前端管理 UI**
  `/api/suites` 与建表已就绪、`Scenario.SuiteId` 可归类，但前端无套件路由/页面，能力尚未对用户开放。
- [ ] **断言编辑器**：在 `ScenarioEdit` 提供结构化断言（Op/ElementId/Expected/Label）的可视化编辑，降低手写 JSON 成本。
- [ ] **计划执行从轮询改推送**：`TestPlanController.ExecutePlanAsync` 用 2s 轮询等待场景终态，可改为事件/回调，减少延迟与无效查询。
- [ ] **执行历史增强**：支持按状态/时间筛选、分页（当前固定取近 100 条），并展示 summary/错误原因列。
- [ ] **录制步骤的拖拽排序与手动插入**：后端 `save` 已支持前端编辑后的步骤，前端补齐拖拽/插入 `wait`/`press_key` 的能力。
- [ ] **目标窗口选择器**：在录制/新建场景处接入 `/api/system/windows` 下拉选择，替代手填窗口标题。
- [ ] **token / 耗时统计可视化**：`TestRun.TokenUsed` 已记录，前端可在历史与监控页展示成本与耗时趋势。

---

## 🟡 健壮性与质量

- [ ] **单次执行的全局串行保护**：目前无显式互斥，若并发触发两次执行会争抢前台焦点/钩子。建议加运行锁或队列。
- [ ] **回放兜底坐标的有效性**：坐标兜底依赖分辨率/窗口位置不变，跨机器回放易失效，需提示或禁用坐标兜底选项。
- [ ] **异常分类与可读化**：`RunService` 的异常 summary 直接透传 `ex.Message`，建议归类（连接/窗口未找到/LLM 失败等）。
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

- [ ] **Web/浏览器自动化引擎**：引入 Playwright，使 `Type=web` 真正可用，并支持 WPF/Web 混合场景。
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
