using System.Text.RegularExpressions;
using TestPlatform.API.Ai;
using TestPlatform.API.Recording;
using TestPlatform.API.Web;
using TestPlatform.API.Wpf;

namespace TestPlatform.API.Execution;

/// <summary>
/// 网页结构化回放器：按录制步骤顺序在浏览器中重放。
/// 步骤 action 与 WPF 录制同名（click/set_text/select_item/press_key/wait），Target=CSS 选择器。
/// 失败重试一次；click 选择器失败时回退按文字点击。可选 AI 截图验证。
/// </summary>
public class BrowserStepPlayer
{
    private readonly BrowserDriver _driver;
    private readonly Dictionary<string, string> _params;

    public Func<StepCallbackData, Task>? OnStep { get; set; }

    public BrowserStepPlayer(BrowserDriver driver, Dictionary<string, string> inputParams)
    {
        _driver = driver;
        _params = inputParams;
    }

    public async Task<PlayResult> RunAsync(string startUrl, List<RecordedStep> steps,
        VisionVerifier? vision = null, string goal = "", string? aiPrompt = null,
        CancellationToken ct = default)
    {
        await _driver.StartAsync(startUrl);
        await Notify(0, "system", "", $"▶ 网页结构化回放开始，共 {steps.Count} 步", true);

        int stepNum = 0, failed = 0;
        var failures = new List<string>();

        for (int i = 0; i < steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step   = steps[i];
            var target = Substitute(step.Target);
            var value  = Substitute(step.Value);
            stepNum++;

            var result = await Execute(step, target, value);
            if (!result.Success)
            {
                await Task.Delay(500, ct);
                result = await Execute(step, target, value);     // 重试一次
                if (result.Success) result = OpResult.Ok($"{result.Message}（重试后成功）");
            }

            await Notify(stepNum, step.Action, $"target={target} value={value}", result.Message, result.Success);

            if (!result.Success)
            {
                failed++;
                failures.Add($"#{stepNum} {step.Description}: {result.Message}");
            }
            await Task.Delay(120, ct);
        }

        // ── 可选 AI 截图验证 ─────────────────────────────────────
        bool aiChecked = false, aiPass = false; string aiAnswer = "";
        if (vision != null)
        {
            await Notify(++stepNum, "system", "", "▶ AI 截图验证中...", true);
            string img;
            try { img = await _driver.ScreenshotBase64Async(); }
            catch { img = ""; }

            if (img.Length > 0)
            {
                var verdict = await vision.VerifyAsync(goal, aiPrompt, img, ct);
                aiAnswer = verdict.Answer;
                if (!verdict.Skipped) { aiChecked = true; aiPass = verdict.Pass; }
                var aiInput = $"【AI 验证输入】\n测试目标：{goal}"
                            + (string.IsNullOrWhiteSpace(aiPrompt) ? "" : $"\n额外验证要点：{aiPrompt}")
                            + "\n附件：结果页面截图（PNG）";
                await Notify(++stepNum, "ai_verify", aiInput,
                    (verdict.Skipped ? "⊘ AI验证跳过: " : verdict.Pass ? "✓ AI判定通过: " : "✗ AI判定不通过: ") + verdict.Answer,
                    verdict.Skipped || verdict.Pass);
            }
        }

        // ── 综合判定：开了 AI 验证则二者都通过；否则看步骤无失败 ──
        bool success; string reason;
        if (aiChecked)
        {
            success = failed == 0 && aiPass;
            reason  = success ? "" :
                      (failed > 0 ? $"{failed}/{steps.Count} 步失败" : "")
                      + (!aiPass ? $"{(failed > 0 ? "；" : "")}AI 判定不通过：{FirstLine(aiAnswer)}" : "");
        }
        else
        {
            success = failed == 0;
            reason  = failed == 0 ? "" : $"{failed}/{steps.Count} 步失败：{string.Join("；", failures.Take(3))}";
        }

        return new PlayResult
        {
            Success       = success,
            TotalSteps    = steps.Count,
            FailedSteps   = failed,
            AiChecked     = aiChecked,
            AiPass        = aiPass,
            AiAnswer      = aiAnswer,
            FailureReason = reason
        };
    }

    private async Task<OpResult> Execute(RecordedStep step, string target, string value)
    {
        try
        {
            switch (step.Action)
            {
                case "click":
                    var r = await _driver.Click(target);
                    // 选择器点不到时回退按可见文字点击
                    if (!r.Success && !string.IsNullOrWhiteSpace(step.TargetName))
                        return await _driver.ClickText(Substitute(step.TargetName));
                    return r;
                case "set_text":    return await _driver.Fill(target, value);
                case "select_item": return await _driver.Select(target, value);
                case "press_key":   return await _driver.PressKey(string.IsNullOrEmpty(value) ? target : value);
                case "wait":        return _driver.Wait(int.TryParse(value, out var ms) ? ms : 500);
                default:            return OpResult.Fail($"网页回放不支持的动作: {step.Action}");
            }
        }
        catch (Exception ex)
        {
            return OpResult.Fail(ex.Message);
        }
    }

    private static string FirstLine(string s) => s.Split('\n').FirstOrDefault()?.Trim() ?? "";

    private string Substitute(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(text, @"\{\{(\w+)\}\}",
            m => _params.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    private async Task Notify(int step, string tool, string args, string result, bool success)
    {
        Console.WriteLine($"[WebPlay #{step}] {tool} → {result[..Math.Min(result.Length, 80)]}");
        if (OnStep != null)
            await OnStep(new StepCallbackData(step, tool, args, result, success));
    }
}
