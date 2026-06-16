using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using TestPlatform.API.Ai;
using TestPlatform.API.Hubs;
using TestPlatform.API.Logging;
using TestPlatform.API.Recording;
using TestPlatform.API.Settings;
using TestPlatform.API.Web;
using TestPlatform.API.Wpf;
using TestPlatform.Core.DB;
using TestPlatform.Core.Entities;

namespace TestPlatform.API.Execution;

public interface IRunService
{
    Task<Guid> StartRunAsync(Guid scenarioId, Dictionary<string, string> inputParams, string mode = "auto");
    Task CancelRunAsync(Guid runId);
    bool IsRunning(Guid runId);
}

/// <summary>
/// 测试运行调度：模式选择（auto/structured/ai）、运行记录落库、SignalR 实时推送。
/// </summary>
public class RunService : IRunService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<TestHub> _hub;
    private readonly ISettingsService _settings;
    private readonly IRecorder _recorder;
    private readonly Dictionary<Guid, CancellationTokenSource> _running = new();
    private readonly object _lock = new();

    public RunService(AppDbContext db, IHubContext<TestHub> hub, ISettingsService settings, IRecorder recorder)
    {
        _db       = db;
        _hub      = hub;
        _settings = settings;
        _recorder = recorder;
    }

    public async Task<Guid> StartRunAsync(Guid scenarioId, Dictionary<string, string> inputParams, string mode = "auto")
    {
        var db = _db.CreateClient();
        var scenario = await db.Queryable<Scenario>().FirstAsync(s => s.Id == scenarioId)
            ?? throw new Exception($"场景 {scenarioId} 不存在");

        var run = new TestRun
        {
            ScenarioId      = scenarioId,
            Status          = "running",
            InputParamsJson = JsonSerializer.Serialize(inputParams),
            StartedAt       = DateTime.UtcNow
        };
        await db.Insertable(run).ExecuteCommandAsync();

        // 录制与回放共用全局钩子和鼠标，运行前必须停止录制
        if (_recorder.IsRecording)
        {
            _recorder.Stop();
            await Task.Delay(300);
        }

        var cts = new CancellationTokenSource();
        lock (_lock) _running[run.Id] = cts;

        _ = Task.Run(() => ExecuteAsync(run.Id, scenario, inputParams, mode, cts.Token));
        return run.Id;
    }

    public Task CancelRunAsync(Guid runId)
    {
        lock (_lock)
        {
            if (_running.TryGetValue(runId, out var cts))
            {
                cts.Cancel();
                _running.Remove(runId);
            }
        }
        return Task.CompletedTask;
    }

    public bool IsRunning(Guid runId)
    {
        lock (_lock) return _running.ContainsKey(runId);
    }

    // ── 执行 ─────────────────────────────────────────────────────

    private async Task ExecuteAsync(Guid runId, Scenario scenario,
        Dictionary<string, string> inputParams, string mode, CancellationToken ct)
    {
        var db = _db.CreateClient();
        // AI 配置：本表（设置页）优先，回退 appsettings.json
        var cfg = await _settings.GetResolvedAsync();
        try
        {
            // Web 场景：浏览器自动化，仅 AI 推理执行（暂无结构化回放）
            if (scenario.Type == "web")
            {
                await ExecuteWebAsync(runId, scenario, inputParams, mode, cfg, ct);
                return;
            }

            bool hasSteps = !string.IsNullOrWhiteSpace(scenario.StepsJson) && scenario.StepsJson != "[]";
            bool structured = mode switch
            {
                "structured" => true,
                "ai"         => false,
                _            => hasSteps   // auto：有录制步骤走结构化
            };

            await PushLog(runId, 0, "system", "",
                $"▶ 开始执行：{scenario.Name}  [{(structured ? "结构化回放" : "AI 推理执行")}]", true);

            string status; int totalSteps; int tokenUsed = 0;
            string summary; string errorMsg;

            if (structured)
            {
                var steps = JsonSerializer.Deserialize<List<RecordedStep>>(scenario.StepsJson!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                var assertions = ParseAssertions(scenario.AssertionsJson);

                // 诊断：明确告知本次以什么判定成败，便于核对验证条件是否生效
                var judgeBy = assertions.Count > 0
                    ? $"将以 {assertions.Count} 条验证条件判定成败（步骤失败不直接判失败）"
                    : (scenario.AiVerifyEnabled ? "将以 AI 截图验证判定成败"
                                                : "未检测到有效验证条件，按“所有步骤无失败”判定");
                await PushLog(runId, 0, "system", "", $"判定方式：{judgeBy}", true);

                // AI 截图验证（场景开启时）：用 AiVision 配置构造视觉验证器
                VisionVerifier? vision = null;
                if (scenario.AiVerifyEnabled)
                    vision = new VisionVerifier(cfg["AiVision:ApiKey"], cfg["AiVision:Model"], cfg["AiVision:BaseUrl"]);

                // AI 验证用的目标描述（替换 {{参数}}）
                var aiGoal = scenario.Description;
                foreach (var (k, v) in inputParams) aiGoal = aiGoal.Replace($"{{{{{k}}}}}", v);

                var player = new StepPlayer(new WpfDriver(), inputParams)
                {
                    OnStep = d => PushLog(runId, d.StepNumber, d.ToolName, d.Arguments, d.Result, d.Success)
                };
                var r = await player.RunAsync(scenario.WindowTitle, steps, assertions,
                    vision, aiGoal, scenario.AiVerifyPrompt, ct);

                status     = r.Success ? "passed" : "failed";
                totalSteps = r.TotalSteps;
                errorMsg   = r.FailureReason;
                summary    = BuildSummary(r);
            }
            else
            {
                var apiKey  = cfg["DeepSeek:ApiKey"]  ?? throw new Exception("未配置 DeepSeek:ApiKey（请在「设置」页填写操作 API Key）");
                var model   = cfg["DeepSeek:Model"]   ?? "deepseek-chat";
                var baseUrl = cfg["DeepSeek:BaseUrl"] ?? "https://api.deepseek.com";

                // 目标描述中替换 {{参数}}
                var goal = scenario.Description;
                foreach (var (k, v) in inputParams)
                    goal = goal.Replace($"{{{{{k}}}}}", v);

                // 记录发给 AI 的目标（输入），便于核对 AI 决策依据
                await PushLog(runId, 0, "system", $"模型：{model}", $"【AI 目标】\n{goal}", true);

                var assertions = new List<string>();
                try { assertions = JsonSerializer.Deserialize<List<string>>(scenario.AssertionsJson) ?? new(); }
                catch { }

                // AI 截图验证（场景开启时）：AI 推理执行结束后，对结果界面截图交多模态模型独立判定
                VisionVerifier? vision = null;
                if (scenario.AiVerifyEnabled)
                {
                    vision = new VisionVerifier(cfg["AiVision:ApiKey"], cfg["AiVision:Model"], cfg["AiVision:BaseUrl"]);
                    await PushLog(runId, 0, "system", "", "判定方式：AI 推理执行 + AI 截图验证", true);
                }

                var agent = new AiAgent(new DeepSeekClient(apiKey, model, baseUrl), new WpfDriver())
                {
                    OnStep = d => PushLog(runId, d.StepNumber, d.ToolName, d.Arguments, d.Result, d.Success, d.Thinking)
                };
                var r = await agent.RunAsync(scenario.Name, scenario.WindowTitle, goal, assertions,
                    scenario.MaxSteps, vision, scenario.AiVerifyPrompt, ct);

                status     = r.Success ? "passed" : "failed";
                totalSteps = r.TotalSteps;
                tokenUsed  = r.TokenUsed;
                summary    = r.Summary;
                errorMsg   = r.FailureReason;
            }

            await db.Updateable<TestRun>()
                .SetColumns(r => new TestRun
                {
                    Status     = status,
                    FinishedAt = DateTime.UtcNow,
                    TotalSteps = totalSteps,
                    TokenUsed  = tokenUsed,
                    ErrorMsg   = errorMsg
                })
                .Where(r => r.Id == runId).ExecuteCommandAsync();

            await _hub.Clients.Group($"run_{runId}").SendAsync("RunFinished", new
            {
                runId, status, summary,
                finishedAt = DateTime.UtcNow.ToString("o")
            });
        }
        catch (OperationCanceledException)
        {
            await db.Updateable<TestRun>()
                .SetColumns(r => new TestRun { Status = "cancelled", FinishedAt = DateTime.UtcNow })
                .Where(r => r.Id == runId).ExecuteCommandAsync();
            await _hub.Clients.Group($"run_{runId}")
                .SendAsync("RunFinished", new { runId, status = "cancelled" });
        }
        catch (Exception ex)
        {
            await db.Updateable<TestRun>()
                .SetColumns(r => new TestRun { Status = "failed", FinishedAt = DateTime.UtcNow, ErrorMsg = ex.Message })
                .Where(r => r.Id == runId).ExecuteCommandAsync();
            await _hub.Clients.Group($"run_{runId}")
                .SendAsync("RunFinished", new { runId, status = "failed", error = ex.Message });
        }
        finally
        {
            lock (_lock) _running.Remove(runId);
        }
    }

    /// <summary>Web 场景执行：有录制步骤且非 ai 模式走结构化回放，否则走 LLM 工具调用；落库/推送同 WPF 路径。</summary>
    private async Task ExecuteWebAsync(Guid runId, Scenario scenario,
        Dictionary<string, string> inputParams, string mode, Dictionary<string, string?> cfg, CancellationToken ct)
    {
        var db = _db.CreateClient();

        bool hasSteps = !string.IsNullOrWhiteSpace(scenario.StepsJson) && scenario.StepsJson != "[]";
        bool structured = mode switch
        {
            "structured" => true,
            "ai"         => false,
            _            => hasSteps   // auto：有录制步骤走结构化
        };

        var startUrl = scenario.WindowTitle;   // web 场景：该字段存起始 URL
        var goal = scenario.Description;
        foreach (var (k, v) in inputParams) goal = goal.Replace($"{{{{{k}}}}}", v);

        await PushLog(runId, 0, "system", "",
            $"▶ 开始执行：{scenario.Name}  [Web · {(structured ? "结构化回放" : "AI 推理执行")}]　起始URL：{startUrl}", true);

        VisionVerifier? vision = null;
        if (scenario.AiVerifyEnabled)
        {
            vision = new VisionVerifier(cfg["AiVision:ApiKey"], cfg["AiVision:Model"], cfg["AiVision:BaseUrl"]);
            await PushLog(runId, 0, "system", "", "判定方式：执行 + AI 截图验证", true);
        }

        string status; int totalSteps; int tokenUsed = 0; string summary; string errorMsg;

        await using var driver = new BrowserDriver();

        if (structured)
        {
            var steps = JsonSerializer.Deserialize<List<RecordedStep>>(scenario.StepsJson!,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            var player = new BrowserStepPlayer(driver, inputParams)
            {
                OnStep = d => PushLog(runId, d.StepNumber, d.ToolName, d.Arguments, d.Result, d.Success)
            };
            var r = await player.RunAsync(startUrl, steps, vision, goal, scenario.AiVerifyPrompt, ct);

            status     = r.Success ? "passed" : "failed";
            totalSteps = r.TotalSteps;
            summary    = r.Success
                ? (r.AiChecked ? $"测试通过：{r.TotalSteps} 步回放成功；AI 判定通过" : $"测试通过：{r.TotalSteps} 步回放成功")
                : r.FailureReason;
            errorMsg   = r.FailureReason;
        }
        else
        {
            var apiKey  = cfg["DeepSeek:ApiKey"]  ?? throw new Exception("未配置 DeepSeek:ApiKey（请在「设置」页填写操作 API Key）");
            var model   = cfg["DeepSeek:Model"]   ?? "deepseek-chat";
            var baseUrl = cfg["DeepSeek:BaseUrl"] ?? "https://api.deepseek.com";

            await PushLog(runId, 0, "system", $"模型：{model}", $"【AI 目标】\n{goal}", true);

            var assertions = new List<string>();
            try { assertions = JsonSerializer.Deserialize<List<string>>(scenario.AssertionsJson) ?? new(); }
            catch { }

            var agent = new WebAiAgent(new DeepSeekClient(apiKey, model, baseUrl, BrowserToolSchemas.All), driver)
            {
                OnStep = d => PushLog(runId, d.StepNumber, d.ToolName, d.Arguments, d.Result, d.Success, d.Thinking)
            };
            var r = await agent.RunAsync(scenario.Name, startUrl, goal, assertions,
                scenario.MaxSteps, vision, scenario.AiVerifyPrompt, ct);

            status     = r.Success ? "passed" : "failed";
            totalSteps = r.TotalSteps;
            tokenUsed  = r.TokenUsed;
            summary    = r.Success ? r.Summary : r.FailureReason;
            errorMsg   = r.FailureReason;
        }

        await db.Updateable<TestRun>()
            .SetColumns(t => new TestRun
            {
                Status     = status,
                FinishedAt = DateTime.UtcNow,
                TotalSteps = totalSteps,
                TokenUsed  = tokenUsed,
                ErrorMsg   = errorMsg
            })
            .Where(t => t.Id == runId).ExecuteCommandAsync();

        await _hub.Clients.Group($"run_{runId}").SendAsync("RunFinished", new
        {
            runId, status, summary,
            finishedAt = DateTime.UtcNow.ToString("o")
        });
    }

    private static string BuildSummary(PlayResult r)
    {
        if (!r.Success) return r.FailureReason;
        var parts = new List<string>();
        if (r.AssertTotal > 0) parts.Add($"{r.AssertPassed}/{r.AssertTotal} 条验证通过");
        if (r.AiChecked)       parts.Add($"AI 判定通过（{r.AiAnswer.Split('\n').FirstOrDefault()?.Trim()}）");
        if (parts.Count == 0)  parts.Add($"{r.TotalSteps} 步无失败（未设验证条件）");
        return "测试通过：" + string.Join("；", parts);
    }

    /// <summary>
    /// 解析验证条件。新格式为结构化断言数组；旧格式（自由文本字符串数组）无法用于结构化判定，忽略。
    /// </summary>
    private static List<Assertion> ParseAssertions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return new();
        try
        {
            var list = JsonSerializer.Deserialize<List<Assertion>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            return list.Where(IsValidAssertion).ToList();
        }
        catch
        {
            return new(); // 旧的字符串数组格式
        }
    }

    /// <summary>一条断言是否有效（按验证方式判断需要哪个字段）。弹窗/文本类不需要 ElementId。</summary>
    private static bool IsValidAssertion(Assertion a) => a.Op switch
    {
        "noDialog"                                                  => true,
        "textVisible" or "textNotVisible"
            or "dialogContains" or "dialogNotContains"              => !string.IsNullOrWhiteSpace(a.Expected),
        // equals / contains / notEmpty / exists / notExists
        _                                                           => !string.IsNullOrWhiteSpace(a.ElementId)
    };

    private async Task PushLog(Guid runId, int step, string tool, string args, string result, bool success, string thinking = "")
    {
        // 入库与实时推送解耦：入库失败（如旧库字段过短）也绝不能吞掉实时日志
        try
        {
            var db = _db.CreateClient();
            await db.Insertable(new RunLog
            {
                RunId      = runId,
                StepNumber = step,
                ToolName   = tool,
                Arguments  = args,
                Result     = result,
                Thinking   = string.IsNullOrWhiteSpace(thinking) ? null : thinking,
                Success    = success,
                CreatedAt  = DateTime.UtcNow
            }).ExecuteCommandAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PushLog] 入库失败（不影响实时日志）: {ex.Message}");
            AiLog.Write("PushLog", $"入库失败 step={step} tool={tool}: {ex.Message}");
        }

        try
        {
            await _hub.Clients.Group($"run_{runId}").SendAsync("StepLog", new
            {
                step, tool, args, result, success, thinking,
                time      = DateTime.Now.ToString("HH:mm:ss"),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PushLog] 推送失败: {ex.Message}");
        }
    }
}
