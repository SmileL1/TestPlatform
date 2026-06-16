using System.Net.Http;
using System.Text;
using System.Text.Json;
using TestPlatform.API.Logging;

namespace TestPlatform.API.Ai;

public class ToolCall
{
    public string Id       { get; set; } = "";
    public string Name     { get; set; } = "";
    public string RawArgs  { get; set; } = "{}";

    public Dictionary<string, string> Args()
    {
        try
        {
            using var doc = JsonDocument.Parse(RawArgs);
            var dict = new Dictionary<string, string>();
            foreach (var p in doc.RootElement.EnumerateObject())
                dict[p.Name] = p.Value.ValueKind == JsonValueKind.String
                    ? p.Value.GetString() ?? ""
                    : p.Value.GetRawText();
            return dict;
        }
        catch { return new(); }
    }
}

public class LlmResponse
{
    public bool   Success      { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string Text         { get; set; } = "";
    public List<ToolCall> ToolCalls { get; set; } = new();
    public bool HasToolCalls => ToolCalls.Count > 0;
}

/// <summary>
/// DeepSeek（OpenAI 兼容）tool-calling 客户端。
/// 维护对话历史，统计真实 token 用量，网络异常自动重试一次。
/// </summary>
public class DeepSeekClient
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _baseUrl;
    private readonly List<object> _tools;
    private readonly List<object> _history = new();

    public int TotalTokensUsed { get; private set; }

    /// <param name="tools">本次会话暴露给 LLM 的工具集；为 null 时默认用 WPF 工具（<see cref="ToolSchemas.All"/>）。</param>
    public DeepSeekClient(string apiKey, string model, string baseUrl, List<object>? tools = null)
    {
        _model   = model;
        _baseUrl = baseUrl;
        _tools   = tools ?? ToolSchemas.All;
        _http    = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public void Reset()
    {
        _history.Clear();
        TotalTokensUsed = 0;
    }

    /// <summary>首轮：发送系统提示 + 用户目标</summary>
    public Task<LlmResponse> StartAsync(string systemPrompt, string userMessage)
    {
        _history.Add(new { role = "user", content = userMessage });
        return SendAsync(systemPrompt);
    }

    /// <summary>追加一条工具结果（多个 tool_call 须先全部追加再 ContinueAsync）</summary>
    public void AddToolResult(string toolCallId, string result)
        => _history.Add(new { role = "tool", content = result, tool_call_id = toolCallId });

    /// <summary>基于当前对话历史请求下一轮决策</summary>
    public Task<LlmResponse> ContinueAsync(string systemPrompt) => SendAsync(systemPrompt);

    private async Task<LlmResponse> SendAsync(string systemPrompt)
    {
        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        messages.AddRange(_history);

        var body = JsonSerializer.Serialize(new
        {
            model       = _model,
            messages,
            tools       = _tools,
            tool_choice = "auto",
            temperature = 0.1,
            max_tokens  = 4096
        });

        AiLog.Write("DeepSeek/请求", $"model={_model} url={_baseUrl}/v1/chat/completions\n{AiLog.Trunc(body, 8000)}");

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                var resp = await _http.PostAsync($"{_baseUrl}/v1/chat/completions", content);
                var text = await resp.Content.ReadAsStringAsync();

                AiLog.Write("DeepSeek/响应", $"status={(int)resp.StatusCode}\n{AiLog.Trunc(text, 8000)}");

                if (!resp.IsSuccessStatusCode)
                    return new LlmResponse { Success = false, ErrorMessage = $"API 错误 ({resp.StatusCode}): {text}" };

                return Parse(text);
            }
            catch (Exception ex) when (attempt == 1 && ex is HttpRequestException or TaskCanceledException)
            {
                Console.WriteLine($"[DeepSeek] 网络异常，2 秒后重试: {ex.Message}");
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                return new LlmResponse { Success = false, ErrorMessage = $"请求异常: {ex.Message}" };
            }
        }
    }

    private LlmResponse Parse(string responseText)
    {
        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;

        if (root.TryGetProperty("usage", out var usage))
        {
            int prompt = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
            int completion = usage.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : 0;
            TotalTokensUsed += prompt + completion;
        }

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return new LlmResponse { Success = false, ErrorMessage = $"无效的 API 响应: {responseText}" };

        var message = choices[0].GetProperty("message");
        var result = new LlmResponse
        {
            Success = true,
            Text    = message.TryGetProperty("content", out var ct) && ct.ValueKind == JsonValueKind.String
                      ? ct.GetString() ?? "" : ""
        };

        if (message.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in tcs.EnumerateArray())
            {
                result.ToolCalls.Add(new ToolCall
                {
                    Id      = tc.GetProperty("id").GetString() ?? "",
                    Name    = tc.GetProperty("function").GetProperty("name").GetString() ?? "",
                    RawArgs = tc.GetProperty("function").GetProperty("arguments").GetString() ?? "{}"
                });
            }
        }

        // 把 assistant 消息原样存回历史（保留 tool_calls 结构）
        _history.Add(JsonSerializer.Deserialize<object>(message.GetRawText())!);
        return result;
    }
}
