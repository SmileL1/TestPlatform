using System.Text.RegularExpressions;
using TestPlatform.API.Ai;
using TestPlatform.API.Recording;
using TestPlatform.API.Wpf;

namespace TestPlatform.API.Execution;

/// <summary>每步回放结果回调数据</summary>
public record StepCallbackData(int StepNumber, string ToolName, string Arguments, string Result, bool Success, string Thinking = "");

/// <summary>回放整体结果</summary>
public class PlayResult
{
    public bool   Success         { get; set; }
    public int    TotalSteps      { get; set; }
    public int    FailedSteps     { get; set; }
    public int    AssertPassed    { get; set; }
    public int    AssertTotal     { get; set; }
    public bool   AiChecked       { get; set; }   // AI 验证是否真正参与了判定
    public bool   AiPass          { get; set; }
    public string AiAnswer        { get; set; } = "";
    public string FailureReason   { get; set; } = "";
}

/// <summary>
/// 结构化回放器：忠实重放录制步骤。
/// 策略：失败自动重试一次；连续 5 步失败提前终止（界面已偏离录制前提）；
/// 弹窗按"录制了就按录制来，没录制才自动关"处理。
/// </summary>
public class StepPlayer
{
    /// <summary>主明细表格（click_cell 未录到表格 id 时的回退，兼容旧场景）</summary>
    private const string DefaultGridId = "DgVoucherInItem";

    private readonly WpfDriver _driver;
    private readonly Dictionary<string, string> _params;

    public Func<StepCallbackData, Task>? OnStep { get; set; }

    public StepPlayer(WpfDriver driver, Dictionary<string, string> inputParams)
    {
        _driver = driver;
        _params = inputParams;
    }

    public async Task<PlayResult> RunAsync(string windowTitle, List<RecordedStep> steps,
        List<Assertion>? assertions = null, VisionVerifier? vision = null,
        string goal = "", string? aiPrompt = null, CancellationToken ct = default)
    {
        assertions ??= new();
        if (!_driver.Attach(windowTitle))
            return new PlayResult { Success = false, FailureReason = $"无法找到窗口: {windowTitle}" };

        await Notify(0, "system", "", $"▶ 结构化回放开始，共 {steps.Count} 步", true);

        int stepNum = 0, failed = 0, consecutive = 0;
        var failures = new List<string>();

        for (int i = 0; i < steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = steps[i];
            var next = i + 1 < steps.Count ? steps[i + 1] : null;
            stepNum++;

            var target = Substitute(step.Target);
            var value  = Substitute(step.Value);

            var result = Execute(step, target, value);

            // 失败重试一次（UI 渲染时序导致的偶发失败）
            if (!result.Success && step.Action is "click" or "click_cell" or "set_text" or "select_item")
            {
                await Task.Delay(600, ct);
                result = Execute(step, target, value);
                if (result.Success) result = OpResult.Ok($"{result.Message}（重试后成功）");
            }

            await Notify(stepNum, step.Action, $"target={target} value={value}", result.Message, result.Success);

            if (!result.Success)
            {
                failed++;
                consecutive++;
                failures.Add($"#{stepNum} {step.Description}: {result.Message}");
                if (consecutive >= 5)
                {
                    await Notify(stepNum, "system", "", "⛔ 连续 5 步失败，提前终止回放", false);
                    break;
                }
            }
            else consecutive = 0;

            // 弹窗处理：录制里下一步就是弹窗按钮点击时交给录制步骤（尊重录的是 Yes 还是 No）
            bool nextHandlesDialog = next is { Action: "click" }
                && next.Target.StartsWith("dialog_btn", StringComparison.OrdinalIgnoreCase);
            if (!nextHandlesDialog && step.Action is "click" or "click_cell" or "select_item" or "press_key")
                await AutoCloseDialogs(stepNum, ct);

            await Task.Delay(100, ct);
        }

        // ── 验证条件（趁结果界面还在、数据未变时读取）──────────
        int assertPass = 0;
        var assertFails = new List<string>();
        if (assertions.Count > 0)
            await Notify(++stepNum, "system", "", $"▶ 开始验证 {assertions.Count} 条条件", true);

        foreach (var a in assertions)
        {
            ct.ThrowIfCancellationRequested();
            var (ok, label, detail) = EvalAssertion(a);

            if (ok) assertPass++;
            else    assertFails.Add($"{label}（{detail}）");

            await Notify(++stepNum, "assert", $"{a.Op} {Substitute(a.ElementId)} {Substitute(a.Expected)}",
                ok ? $"✓ 验证通过: {label}（{detail}）" : $"✗ 验证失败: {label}（{detail}）",
                ok);
        }

        // ── AI 截图验证（在结果界面上做；验证后不再自动返回主界面，便于计划测试串联）──
        bool aiChecked = false, aiPass = false;
        string aiAnswer = "";
        if (vision != null)
        {
            await Notify(++stepNum, "system", "", "▶ AI 截图验证中...", true);

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
            aiAnswer = verdict.Answer;
            if (!verdict.Skipped) { aiChecked = true; aiPass = verdict.Pass; }

            // 记录 AI 输入（目标 + 额外要点 + 已附截图），便于核对判断依据
            var aiInput = $"【AI 验证输入】\n测试目标：{goal}"
                        + (string.IsNullOrWhiteSpace(aiPrompt) ? "" : $"\n额外验证要点：{aiPrompt}")
                        + "\n附件：结果界面截图（PNG）";
            await Notify(++stepNum, "ai_verify", aiInput,
                (verdict.Skipped ? "⊘ AI验证跳过: " : verdict.Pass ? "✓ AI判定通过: " : "✗ AI判定不通过: ") + verdict.Answer,
                verdict.Skipped || verdict.Pass);
        }

        // ── 综合判定：有验证条件或 AI 验证时，二者(启用的)都通过才算通过；都没有则看步骤 ──
        bool success;
        string reason;
        bool hasAssert = assertions.Count > 0;
        if (hasAssert || aiChecked)
        {
            bool assertOk = !hasAssert || assertPass == assertions.Count;
            bool aiOk     = !aiChecked || aiPass;
            success = assertOk && aiOk;

            var parts = new List<string>();
            if (hasAssert && !assertOk)
                parts.Add($"{assertFails.Count}/{assertions.Count} 条验证未通过：{string.Join("；", assertFails.Take(3))}");
            if (aiChecked && !aiPass)
                parts.Add($"AI 判定不通过：{FirstLine(aiAnswer)}");
            reason = string.Join("；", parts);
        }
        else
        {
            success = failed == 0;
            reason  = failed == 0 ? ""
                : $"{failed}/{steps.Count} 步失败：{string.Join("；", failures.Take(3))}"
                  + (failures.Count > 3 ? $"……等 {failures.Count} 处" : "");
        }

        return new PlayResult
        {
            Success       = success,
            TotalSteps    = steps.Count,
            FailedSteps   = failed,
            AssertPassed  = assertPass,
            AssertTotal   = assertions.Count,
            AiChecked     = aiChecked,
            AiPass        = aiPass,
            AiAnswer      = aiAnswer,
            FailureReason = reason
        };
    }

    private static string FirstLine(string s)
        => s.Split('\n').FirstOrDefault()?.Trim() ?? "";

    /// <summary>执行一条断言，返回 (是否通过, 展示名, 实际情况描述)</summary>
    private (bool ok, string label, string detail) EvalAssertion(Assertion a)
    {
        var id       = Substitute(a.ElementId);
        var expected = Substitute(a.Expected);
        string label = a.Label;

        switch (a.Op)
        {
            // ── 界面状态 ──────────────────────────────────────────
            case "exists":
            {
                bool ok = _driver.ElementExists(id);
                return (ok, Fallback(label, $"控件 {id} 存在"), ok ? "已出现" : "未找到");
            }
            case "notExists":
            {
                bool ok = !_driver.ElementExists(id);
                return (ok, Fallback(label, $"控件 {id} 不存在"), ok ? "确实不存在" : "仍然存在");
            }
            case "textVisible":
            {
                bool ok = _driver.TextVisible(expected);
                return (ok, Fallback(label, $"界面出现「{expected}」"), ok ? "已出现" : "界面上未找到该文本");
            }
            case "textNotVisible":
            {
                bool ok = !_driver.TextVisible(expected);
                return (ok, Fallback(label, $"界面无「{expected}」"), ok ? "确实未出现" : "界面上仍能找到该文本");
            }

            // ── 弹窗检查 ──────────────────────────────────────────
            case "noDialog":
            {
                var d = _driver.TryGetDialog();
                return (d == null, Fallback(label, "无错误弹窗"),
                        d == null ? "无弹窗" : $"出现弹窗「{d.Message}」");
            }
            case "dialogNotContains":
            {
                var d = _driver.TryGetDialog();
                bool ok = d == null || !d.Message.Contains(expected, StringComparison.OrdinalIgnoreCase);
                return (ok, Fallback(label, $"无「{expected}」提示"),
                        ok ? (d == null ? "无弹窗" : $"弹窗不含该文本（{d.Message}）") : $"弹窗含该文本（{d!.Message}）");
            }
            case "dialogContains":
            {
                var d = _driver.TryGetDialog();
                bool ok = d != null && d.Message.Contains(expected, StringComparison.OrdinalIgnoreCase);
                return (ok, Fallback(label, $"出现「{expected}」提示"),
                        ok ? $"弹窗含该文本（{d!.Message}）" : (d == null ? "无弹窗" : $"弹窗不含该文本（{d.Message}）"));
            }

            // ── 读控件值 ──────────────────────────────────────────
            default:
            {
                _driver.ReadValue(id, out var actual);
                actual = actual?.Trim() ?? "";
                bool ok = a.Op switch
                {
                    "contains" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
                    "notEmpty" => !string.IsNullOrWhiteSpace(actual),
                    _          => string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase)
                };
                var want = a.Op switch
                {
                    "contains" => $"期望包含「{expected}」",
                    "notEmpty" => "期望非空",
                    _          => $"期望等于「{expected}」"
                };
                return (ok, Fallback(label, id), $"{want}，实际「{actual}」");
            }
        }
    }

    private static string Fallback(string label, string auto)
        => string.IsNullOrWhiteSpace(label) ? auto : label;

    // ── 单步分发 ─────────────────────────────────────────────────

    private OpResult Execute(RecordedStep step, string target, string value)
    {
        try
        {
            return step.Action switch
            {
                "click"       => ExecuteClick(target, step),
                "click_cell"  => ExecuteClickCell(step, value),
                "set_text"    => ExecuteSetText(step, target, value),
                "select_item" => _driver.SelectItem(target, value),
                "press_key"   => _driver.PressKey(value),
                "wait"        => ExecuteWait(value),
                _             => OpResult.Fail($"未知操作: {step.Action}")
            };
        }
        catch (Exception ex)
        {
            return OpResult.Fail(ex.Message);
        }
    }

    private OpResult ExecuteClick(string target, RecordedStep step)
    {
        // 录制的弹窗按钮点击：弹窗可能已被自动处理或本次未出现，跳过而不算失败
        if (target.StartsWith("dialog_btn", StringComparison.OrdinalIgnoreCase)
            && _driver.TryGetDialog() == null)
            return OpResult.Ok("弹窗未出现或已关闭，跳过该点击");

        var r = _driver.Click(target);

        // 元素找不到时退回录制坐标（布局未变的场景下仍可用）
        if (!r.Success && step.X > 0 && step.Y > 0 && target.StartsWith("pos("))
            return _driver.ClickPoint(step.X, step.Y);
        return r;
    }

    private OpResult ExecuteClickCell(RecordedStep step, string value)
    {
        if (!int.TryParse(value, out var row))
        {
            // 缺行号：退回坐标
            if (step.X > 0 && step.Y > 0) return _driver.ClickPoint(step.X, step.Y);
            return OpResult.Fail("click_cell 缺少行号");
        }
        var gridId = string.IsNullOrEmpty(step.GridId) ? DefaultGridId : step.GridId;
        var r = _driver.ClickCell(gridId, row, step.TargetName);

        // 表格/单元格找不到（弹窗未开等）退回坐标点击
        if (!r.Success && step.X > 0 && step.Y > 0)
            return _driver.ClickPoint(step.X, step.Y);
        return r;
    }

    private OpResult ExecuteSetText(RecordedStep step, string target, string value)
    {
        // 录制时来自 ComboBox 的文本变化 → 用选择而不是写值
        if (step.ControlType == "ComboBox")
            return _driver.SelectItem(target, value);

        // detail_{N}_{col} 格子：先点击单元格进入编辑模式
        var parts = target.Split('_');
        if (target.StartsWith("detail_") && parts.Length >= 3 && int.TryParse(parts[1], out var row))
        {
            var col = string.Join("_", parts.Skip(2));
            _driver.ClickCell(string.IsNullOrEmpty(step.GridId) ? DefaultGridId : step.GridId, row, col);
            Thread.Sleep(400);
        }

        var r = _driver.SetText(target, value);
        Thread.Sleep(150);
        return r;
    }

    private static OpResult ExecuteWait(string ms)
    {
        if (int.TryParse(ms, out var milliseconds))
            Thread.Sleep(Math.Clamp(milliseconds, 0, 30_000));
        return OpResult.Ok($"已等待 {ms}ms");
    }

    // ── 弹窗自动处理（最多连续 5 层）──────────────────────────────

    private async Task AutoCloseDialogs(int stepNum, CancellationToken ct)
    {
        for (int i = 0; i < 5; i++)
        {
            var dialog = _driver.TryGetDialog();
            if (dialog == null) break;

            await Notify(stepNum, "check_dialog", "", dialog.ToString(), true);
            var close = _driver.CloseDialog();
            await Notify(stepNum, "auto_close_dialog", "", close.Message, close.Success);
            if (!close.Success) break;
            await Task.Delay(400, ct);
        }
    }

    // ── 工具 ─────────────────────────────────────────────────────

    private string Substitute(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(text, @"\{\{(\w+)\}\}",
            m => _params.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    private async Task Notify(int step, string tool, string args, string result, bool success)
    {
        Console.WriteLine($"[Player #{step}] {tool} → {result[..Math.Min(result.Length, 80)]}");
        if (OnStep != null)
            await OnStep(new StepCallbackData(step, tool, args, result, success));
    }
}
