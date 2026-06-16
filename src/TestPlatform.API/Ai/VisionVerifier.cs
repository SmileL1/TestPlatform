using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TestPlatform.API.Logging;

namespace TestPlatform.API.Ai;

/// <summary>AI 视觉验证结果</summary>
public record VisionVerdict(bool Pass, string Answer, bool Skipped = false);

/// <summary>
/// AI 截图验证：把测试结束时的界面截图（base64 PNG）+ 测试目标发给多模态模型，
/// 让它判断测试是否成功。要求模型首行输出"结论：通过/不通过"，据此解析 Pass。
/// 走 OpenAI 兼容的 /v1/chat/completions 多模态消息格式。
/// </summary>
public class VisionVerifier
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _endpoint;

    public bool IsConfigured { get; }

    public VisionVerifier(string? apiKey, string? model, string? baseUrl)
    {
        _model    = model ?? "";
        _endpoint = BuildEndpoint(baseUrl ?? "");
        IsConfigured = !string.IsNullOrWhiteSpace(apiKey)
                       && !string.IsNullOrWhiteSpace(_model)
                       && !string.IsNullOrWhiteSpace(_endpoint);

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    /// <summary>
    /// 兼容各厂商的 OpenAI 风格地址：
    /// 已含 /chat/completions 直接用；以 /vN 结尾补 /chat/completions；否则补 /v1/chat/completions。
    /// 例：通义 .../compatible-mode → +/v1/...；智谱 .../paas/v4 → +/chat/completions。
    /// </summary>
    private static string BuildEndpoint(string baseUrl)
    {
        var b = baseUrl.TrimEnd('/');
        if (string.IsNullOrEmpty(b)) return "";
        if (b.Contains("/chat/completions")) return b;
        if (Regex.IsMatch(b, @"/v\d+$")) return b + "/chat/completions";
        return b + "/v1/chat/completions";
    }

    public async Task<VisionVerdict> VerifyAsync(string goal, string? extraPrompt, string imageBase64, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new VisionVerdict(false, "未配置 AI 视觉模型（AiVision），已跳过 AI 验证", Skipped: true);

        var prompt = $"""
            你是软件自动化测试的验证助手。下面这张图是一次自动化测试**执行完成后**的界面截图。

            ## 测试目标
            {goal}
            {(string.IsNullOrWhiteSpace(extraPrompt) ? "" : $"\n## 额外验证要点\n{extraPrompt}")}

            请结合截图判断本次测试是否成功完成。重点关注：是否有错误/失败提示弹窗、是否跳转到预期的结果界面、关键数据是否正确显示。
            **第一行只输出**：`结论：通过` 或 `结论：不通过`
            然后另起一行简要说明理由（2-4 句）。
            """;

        var body = JsonSerializer.Serialize(new
        {
            model = _model,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = prompt },
                        new { type = "image_url", image_url = new { url = $"data:image/png;base64,{imageBase64}" } }
                    }
                }
            },
            temperature = 0.1,
            max_tokens = 600
        });

        // 记录请求（不含图片 base64，只记目标/要点/模型）
        AiLog.Write("Vision/请求",
            $"model={_model} url={_endpoint} 图片大小={imageBase64.Length}B\n目标：{goal}"
            + (string.IsNullOrWhiteSpace(extraPrompt) ? "" : $"\n额外要点：{extraPrompt}"));

        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(_endpoint, content, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            AiLog.Write("Vision/响应", $"status={(int)resp.StatusCode}\n{AiLog.Trunc(text, 6000)}");

            if (!resp.IsSuccessStatusCode)
                return new VisionVerdict(false, $"AI 验证调用失败（{resp.StatusCode}）：{Trim(text, 200)}", Skipped: true);

            using var doc = JsonDocument.Parse(text);
            var answer = doc.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString() ?? "";

            // 解析首行结论
            var firstLine = answer.Split('\n').FirstOrDefault()?.Trim() ?? "";
            bool pass = firstLine.Contains("通过") && !firstLine.Contains("不通过");
            AiLog.Write("Vision/判定", $"pass={pass}\n{AiLog.Trunc(answer.Trim(), 2000)}");
            return new VisionVerdict(pass, answer.Trim());
        }
        catch (Exception ex)
        {
            AiLog.Write("Vision/异常", ex.ToString());
            return new VisionVerdict(false, $"AI 验证异常：{ex.Message}", Skipped: true);
        }
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
