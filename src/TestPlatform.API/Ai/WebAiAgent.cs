using TestPlatform.API.Execution;
using TestPlatform.API.Web;
using TestPlatform.API.Wpf;   // OpResult

namespace TestPlatform.API.Ai;

/// <summary>
/// Web 版 AI 推理执行：LLM 通过 browser_* 工具驱动 Playwright 浏览器逐步完成网页测试。
/// 结构与 <see cref="AiAgent"/> 对称，但工具执行全异步。复用 DeepSeekClient / VisionVerifier / 综合判定逻辑。
/// </summary>
public class WebAiAgent
{
    private readonly DeepSeekClient _llm;
    private readonly BrowserDriver  _driver;

    public Func<StepCallbackData, Task>? OnStep { get; set; }

    public WebAiAgent(DeepSeekClient llm, BrowserDriver driver)
    {
        _llm    = llm;
        _driver = driver;
    }

    public async Task<AgentResult> RunAsync(string testName, string startUrl, string goal,
        List<string> assertions, int maxSteps,
        VisionVerifier? vision = null, string? aiVerifyPrompt = null,
        CancellationToken ct = default)
    {
        await _driver.StartAsync(startUrl);
        _llm.Reset();

        var firstMessage = $"""
            ## 测试目标
            {goal}

            ## 起始页面
            {startUrl}

            ## 验证条件
            {string.Join("\n", assertions.Select(a => $"- {a}"))}

            起始页面已自动打开。请先 browser_scan 了解页面，再开始执行测试步骤。
            """;

        var response = await _llm.StartAsync(SystemPrompt, firstMessage);
        int step = 0;
        string lastError = "";

        while (step < maxSteps && !ct.IsCancellationRequested)
        {
            step++;

            if (!response.Success)
            {
                lastError = response.ErrorMessage;
                await Notify(step, "error", "", $"LLM 调用失败: {response.ErrorMessage}", false);
                break;
            }
            if (!response.HasToolCalls)
            {
                await Notify(step, "system", "", "LLM 未返回工具调用，结束", false);
                break;
            }

            foreach (var call in response.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();

                if (call.Name == "done")
                {
                    var args = call.Args();
                    bool ok = args.GetValueOrDefault("success", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
                    var summary = args.GetValueOrDefault("summary", "");
                    await Notify(step, "done", call.RawArgs,
                        ok ? $"✅ 测试通过: {summary}" : $"❌ 测试失败: {summary}", ok, response.Text);

                    var (aiChecked, aiPass, aiAnswer) =
                        await RunVisionVerifyAsync(vision, goal, aiVerifyPrompt, step + 1, ct);

                    bool finalOk = ok && (!aiChecked || aiPass);
                    var finalSummary = aiChecked
                        ? $"{summary}；AI 截图验证{(aiPass ? "通过" : "不通过")}（{FirstLine(aiAnswer)}）"
                        : summary;
                    var failReason = finalOk ? ""
                        : aiChecked && !aiPass ? $"AI 截图验证不通过：{FirstLine(aiAnswer)}"
                        : $"AI 判定测试未完成：{summary}";

                    return new AgentResult
                    {
                        Success = finalOk, Summary = finalSummary, FailureReason = failReason,
                        TotalSteps = step, TokenUsed = _llm.TotalTokensUsed
                    };
                }

                var result = await ExecuteToolAsync(call);
                await Notify(step, call.Name, call.RawArgs, result.Message, result.Success, response.Text);
                _llm.AddToolResult(call.Id, result.Message);
            }

            response = await _llm.ContinueAsync(SystemPrompt);
        }

        ct.ThrowIfCancellationRequested();

        var reason = step >= maxSteps ? "超过最大步数"
                   : !string.IsNullOrEmpty(lastError) ? $"LLM 调用失败: {lastError}"
                   : "LLM 未能完成测试";

        // Agent 未显式完成时，也对最终页面做一次独立 AI 截图验证（可据此翻盘判通过）
        var (endChecked, endPass, endAnswer) =
            await RunVisionVerifyAsync(vision, goal, aiVerifyPrompt, step + 1, ct);

        if (endChecked && endPass)
            return new AgentResult
            {
                Success = true,
                Summary = $"AI 截图验证通过（{FirstLine(endAnswer)}）（Agent 未显式完成，按结果界面判定）",
                TotalSteps = step, TokenUsed = _llm.TotalTokensUsed
            };

        return new AgentResult
        {
            Success = false,
            FailureReason = endChecked ? $"{reason}；AI 截图验证不通过：{FirstLine(endAnswer)}" : reason,
            TotalSteps = step, TokenUsed = _llm.TotalTokensUsed
        };
    }

    private static string FirstLine(string s) => s.Split('\n').FirstOrDefault()?.Trim() ?? "";

    /// <summary>对当前页面截图并交多模态模型验证；vision 为 null 时直接跳过。</summary>
    private async Task<(bool Checked, bool Pass, string Answer)> RunVisionVerifyAsync(
        VisionVerifier? vision, string goal, string? aiPrompt, int stepNo, CancellationToken ct)
    {
        if (vision == null) return (false, false, "");

        await Notify(stepNo, "system", "", "▶ AI 截图验证中...", true);

        string img;
        try { img = await _driver.ScreenshotBase64Async(); }
        catch { return (false, false, "截图失败，跳过 AI 验证"); }

        var verdict = await vision.VerifyAsync(goal, aiPrompt, img, ct);
        var aiInput = $"【AI 验证输入】\n测试目标：{goal}"
                    + (string.IsNullOrWhiteSpace(aiPrompt) ? "" : $"\n额外验证要点：{aiPrompt}")
                    + "\n附件：结果页面截图（PNG）";
        await Notify(stepNo, "ai_verify", aiInput,
            (verdict.Skipped ? "⊘ AI验证跳过: " : verdict.Pass ? "✓ AI判定通过: " : "✗ AI判定不通过: ") + verdict.Answer,
            verdict.Skipped || verdict.Pass);

        return verdict.Skipped ? (false, false, verdict.Answer) : (true, verdict.Pass, verdict.Answer);
    }

    private async Task<OpResult> ExecuteToolAsync(ToolCall call)
    {
        var a = call.Args();
        try
        {
            switch (call.Name)
            {
                case "browser_connect":    return OpResult.Ok("浏览器已就绪");
                case "browser_navigate":   return await _driver.Navigate(a.GetValueOrDefault("url", ""));
                case "browser_scan":        return await _driver.ScanAsync();
                case "browser_click":       return await _driver.Click(a.GetValueOrDefault("selector", ""));
                case "browser_click_text":  return await _driver.ClickText(a.GetValueOrDefault("text", ""));
                case "browser_fill":        return await _driver.Fill(a.GetValueOrDefault("selector", ""), a.GetValueOrDefault("text", ""));
                case "browser_select":      return await _driver.Select(a.GetValueOrDefault("selector", ""), a.GetValueOrDefault("value", ""));
                case "browser_get_text":    return await _driver.GetText(a.GetValueOrDefault("selector", ""));
                case "browser_wait":        return _driver.Wait(ParseInt(a, "milliseconds", 500));
                case "assert_text":         return await _driver.AssertText(a.GetValueOrDefault("selector", ""), a.GetValueOrDefault("expected_text", ""));
                default:                    return OpResult.Fail($"未知工具 {call.Name}");
            }
        }
        catch (Exception ex)
        {
            return OpResult.Fail($"执行 {call.Name} 异常 - {ex.Message}");
        }
    }

    private static int ParseInt(Dictionary<string, string> a, string key, int fallback)
        => int.TryParse(a.GetValueOrDefault(key, ""), out var v) ? v : fallback;

    private async Task Notify(int step, string tool, string args, string result, bool success, string thinking = "")
    {
        Console.WriteLine($"[WebAI #{step}] {tool} → {result[..Math.Min(result.Length, 100)]}");
        if (OnStep != null)
            await OnStep(new StepCallbackData(step, tool, args, result, success, thinking));
    }

    private const string SystemPrompt = """
        你是一个专业的 Web 网页自动化测试 Agent，通过浏览器工具逐步操作页面完成测试目标。

        ## 工具
        - browser_connect   : 确保浏览器就绪（一般无需手动调用）
        - browser_navigate  : 跳转到 URL
        - browser_scan      : 扫描当前页面可交互元素（返回 selector 与文字）。界面未知/操作失败时用
        - browser_click     : 按 CSS selector 点击（如 #submit）
        - browser_click_text: 按可见文字点击（如 提交订单）
        - browser_fill      : 输入框填写文本（selector + text）
        - browser_select    : 下拉框选择（selector + value）
        - browser_get_text  : 读取元素文本
        - browser_wait      : 等待毫秒
        - assert_text       : 断言元素文本包含
        - done              : 报告结果 success + summary

        ## 规则
        1. 起始页面已自动打开。先 browser_scan 了解元素，再操作。
        2. 选择器优先用 #id；没有 id 时用 browser_click_text 按可见文字点击。
        3. 复选框/单选框用 browser_click 点击其 selector。
        4. 操作后若要确认结果，用 browser_scan 或 assert_text/browser_get_text 读取页面状态。
        5. 完成调用 done(success=true)；失败调用 done(success=false) 并在 summary 说明原因。
        """;
}
