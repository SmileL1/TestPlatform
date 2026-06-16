using TestPlatform.API.Execution;
using TestPlatform.API.Wpf;

namespace TestPlatform.API.Ai;

public class AgentResult
{
    public bool   Success       { get; set; }
    public string Summary       { get; set; } = "";
    public string FailureReason { get; set; } = "";
    public int    TotalSteps    { get; set; }
    public int    TokenUsed     { get; set; }
}

/// <summary>
/// AI 推理执行：LLM 通过 tool-calling 逐步操作界面。
/// 一轮可能返回多个 tool_call，须先执行完并逐个回传结果，再请求下一轮。
/// </summary>
public class AiAgent
{
    private readonly DeepSeekClient _llm;
    private readonly WpfDriver _driver;

    public Func<StepCallbackData, Task>? OnStep { get; set; }

    public AiAgent(DeepSeekClient llm, WpfDriver driver)
    {
        _llm    = llm;
        _driver = driver;
    }

    public async Task<AgentResult> RunAsync(string testName, string windowTitle, string goal,
        List<string> assertions, int maxSteps,
        VisionVerifier? vision = null, string? aiVerifyPrompt = null,
        CancellationToken ct = default)
    {
        if (!_driver.Attach(windowTitle))
            return new AgentResult { Success = false, FailureReason = $"无法找到窗口: {windowTitle}" };

        _llm.Reset();

        var firstMessage = $"""
            ## 测试目标
            {goal}

            ## 验证条件
            {string.Join("\n", assertions.Select(a => $"- {a}"))}

            请开始执行测试步骤。
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

                    // AI 截图验证（在结果界面上做；不再自动返回主界面，便于计划测试串联）
                    var (aiChecked, aiPass, aiAnswer) =
                        await RunVisionVerifyAsync(vision, goal, aiVerifyPrompt, step + 1, ct);

                    // 综合判定：Agent 自报成功 + AI 截图验证（启用时）都通过才算通过
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

                var result = ExecuteTool(call, testName, step);
                await Notify(step, call.Name, call.RawArgs, result.Message, result.Success, response.Text);
                _llm.AddToolResult(call.Id, result.Message);
            }

            response = await _llm.ContinueAsync(SystemPrompt);
        }

        ct.ThrowIfCancellationRequested();

        var reason = step >= maxSteps ? "超过最大步数"
                   : !string.IsNullOrEmpty(lastError) ? $"LLM 调用失败: {lastError}"
                   : "LLM 未能完成测试";

        // Agent 未显式完成时，也对最终界面做一次独立的 AI 截图验证（可据此翻盘判通过）
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

    private static string FirstLine(string s)
        => s.Split('\n').FirstOrDefault()?.Trim() ?? "";

    /// <summary>对当前被测窗口截图并交多模态模型验证；vision 为 null 时直接跳过（不发日志）。</summary>
    private async Task<(bool Checked, bool Pass, string Answer)> RunVisionVerifyAsync(
        VisionVerifier? vision, string goal, string? aiPrompt, int stepNo, CancellationToken ct)
    {
        if (vision == null) return (false, false, "");

        await Notify(stepNo, "system", "", "▶ AI 截图验证中...", true);

        string img;
        try
        {
            var r = _driver.Finder.Window!.Current.BoundingRectangle;
            img = Screenshot.CaptureRegionBase64((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        }
        catch
        {
            img = Screenshot.CaptureRegionBase64(0, 0, 0, 0); // 回退全屏
        }

        var verdict = await vision.VerifyAsync(goal, aiPrompt, img, ct);
        var aiInput = $"【AI 验证输入】\n测试目标：{goal}"
                    + (string.IsNullOrWhiteSpace(aiPrompt) ? "" : $"\n额外验证要点：{aiPrompt}")
                    + "\n附件：结果界面截图（PNG）";
        await Notify(stepNo, "ai_verify", aiInput,
            (verdict.Skipped ? "⊘ AI验证跳过: " : verdict.Pass ? "✓ AI判定通过: " : "✗ AI判定不通过: ") + verdict.Answer,
            verdict.Skipped || verdict.Pass);

        return verdict.Skipped ? (false, false, verdict.Answer) : (true, verdict.Pass, verdict.Answer);
    }

    private OpResult ExecuteTool(ToolCall call, string testName, int step)
    {
        var a = call.Args();
        try
        {
            switch (call.Name)
            {
                case "scan_ui":       return OpResult.Ok(_driver.ScanDescription());
                case "click":         return _driver.Click(a.GetValueOrDefault("element_id", ""));
                case "set_text":      return _driver.SetText(a.GetValueOrDefault("element_id", ""), a.GetValueOrDefault("text", ""));
                case "select_item":   return _driver.SelectItem(a.GetValueOrDefault("element_id", ""), a.GetValueOrDefault("item_text", ""));
                case "read_value":    return _driver.ReadValue(a.GetValueOrDefault("element_id", ""), out _);
                case "press_key":     return _driver.PressKey(a.GetValueOrDefault("key", ""));
                case "wait":          return Wait(a.GetValueOrDefault("milliseconds", "500"));
                case "get_row_count": { var r = _driver.GetRowCount(out _); return r; }
                case "click_cell":    return _driver.ClickCell("DgVoucherInItem", ParseInt(a, "row", 1), a.GetValueOrDefault("column", "preWeight"));
                case "set_unit_price":return _driver.SetUnitPrice(ParseInt(a, "row", 1), a.GetValueOrDefault("unit_price", "0"));
                case "select_product":return _driver.SelectProduct(a.GetValueOrDefault("product_code", ""));
                case "check_dialog":  { var d = _driver.TryGetDialog(); return OpResult.Ok(d?.ToString() ?? "无弹窗"); }
                case "assert_text":   return Assert(a);
                default:              return OpResult.Fail($"未知工具 {call.Name}");
            }
        }
        catch (Exception ex)
        {
            return OpResult.Fail($"执行 {call.Name} 异常 - {ex.Message}");
        }
    }

    private OpResult Assert(Dictionary<string, string> a)
    {
        var id = a.GetValueOrDefault("element_id", "");
        var expected = a.GetValueOrDefault("expected_text", "");
        _driver.ReadValue(id, out var actual);
        return actual.Contains(expected, StringComparison.OrdinalIgnoreCase)
            ? OpResult.Ok($"断言通过: {id} 包含 \"{expected}\"（实际: \"{actual}\"）")
            : OpResult.Fail($"断言失败: {id} 不包含 \"{expected}\"（实际: \"{actual}\"）");
    }

    private static OpResult Wait(string ms)
    {
        if (int.TryParse(ms, out var v)) Thread.Sleep(Math.Clamp(v, 0, 30_000));
        return OpResult.Ok($"已等待 {ms}ms");
    }

    private static int ParseInt(Dictionary<string, string> a, string key, int fallback)
        => int.TryParse(a.GetValueOrDefault(key, ""), out var v) ? v : fallback;

    private async Task Notify(int step, string tool, string args, string result, bool success, string thinking = "")
    {
        Console.WriteLine($"[AI #{step}] {tool} → {result[..Math.Min(result.Length, 100)]}");
        if (OnStep != null)
            await OnStep(new StepCallbackData(step, tool, args, result, success, thinking));
    }

    private const string SystemPrompt = """
        你是一个专业的 WPF 自动化测试 Agent，被测系统是日语仓库管理系统 SmartZaiko。

        ## 工具
        - scan_ui        : 谨慎使用，仅首次/未知界面/操作失败时
        - click          : element_id 用 AutomationId
        - set_text       : element_id + text
        - select_item    : 下拉框选择
        - click_cell     : 表格单元格 row + column
        - set_unit_price : 填単価 row + unit_price（商品选完后调用）
        - select_product : 商品弹窗中选商品
        - get_row_count  : 操作表格前先获取行数
        - check_dialog   : 每次点击后检查弹窗
        - read_value / assert_text / press_key / wait
        - done           : 报告结果 success + summary

        ## 已知 AutomationId
        voucher_branch, voucher_voucherType, voucher_issuedType, voucher_transportType,
        voucher_vehicleNumber, voucher_customer,
        btn_add, btn_voucherIssue, btn_delete, btn_pending, btn_continue, btn_measurement,
        detail_{N}_preWeight, detail_{N}_afterWeight, detail_{N}_product, detail_{N}_memo, detail_{N}_grossWeight, detail_{N}_unitPrice,
        btn_issue, btn_issueCancel,
        issue_taxType, issue_issuedUser, issue_paymentType, issue_paymentContent, issue_paymentMethod,
        dialog_message, dialog_btn_yes, dialog_btn_no, dialog_btn_cancel,
        product_search, product_list, unitprice_settingPrice, unitprice_list

        ## 规则
        1. 已知 AutomationId 直接用，不必 scan_ui
        2. 表格：get_row_count → click_cell 激活 → set_text 填值
        3. 点击后必须 check_dialog 处理弹窗
        4. 商品选完后调用 set_unit_price 填単価
        5. 完成调用 done(success=true)，失败 done(success=false)
        """;
}
