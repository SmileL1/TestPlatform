using Microsoft.Playwright;
using TestPlatform.API.Wpf;   // 复用 OpResult

namespace TestPlatform.API.Web;

/// <summary>
/// 浏览器自动化驱动（Playwright）。语义化操作 API 与 WpfDriver 对称，但全异步。
/// 供 Web 场景的 AI 推理执行使用：LLM 通过 browser_* 工具调用这里的方法。
/// </summary>
public class BrowserDriver : IAsyncDisposable
{
    private IPlaywright? _pw;
    private IBrowser?    _browser;
    private IPage?       _page;

    public IPage? Page => _page;

    /// <summary>启动有头浏览器、新建页面并导航到起始 URL。</summary>
    public async Task<bool> StartAsync(string startUrl)
    {
        _pw      = await Playwright.CreateAsync();
        _browser = await BrowserLauncher.LaunchAsync(_pw);
        _page    = await _browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 860 }
        });

        if (!string.IsNullOrWhiteSpace(startUrl))
        {
            try
            {
                await _page.GotoAsync(Normalize(startUrl),
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Browser] 起始导航失败: {ex.Message}");
            }
        }
        return true;
    }

    public async Task<OpResult> Navigate(string url)
    {
        await Page!.GotoAsync(Normalize(url),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
        return OpResult.Ok($"已导航到 {url}");
    }

    /// <summary>扫描当前页可交互元素，返回「selector + 标签 + 文字/值」列表（给 LLM 决策）。</summary>
    public async Task<OpResult> ScanAsync()
    {
        var desc = await Page!.EvaluateAsync<string>("""
            () => {
              const els = document.querySelectorAll('a,button,input,select,textarea,[role=button],[onclick]');
              const lines = [], seen = new Set();
              for (const el of els) {
                const r = el.getBoundingClientRect();
                if (r.width === 0 || r.height === 0) continue;       // 跳过不可见
                const tag  = el.tagName.toLowerCase();
                const id   = el.id || '';
                const name = el.getAttribute('name') || '';
                const type = el.getAttribute('type') || '';
                const ph   = el.getAttribute('placeholder') || '';
                const label = (el.innerText || el.value || ph || el.getAttribute('aria-label') || '')
                              .trim().replace(/\s+/g, ' ').slice(0, 40);
                let selector = id ? ('#' + id) : (name ? (tag + '[name="' + name + '"]') : '');
                if (!selector && !label) continue;
                const key = selector + '|' + label;
                if (seen.has(key)) continue; seen.add(key);
                let line = tag + (type ? ('[' + type + ']') : '');
                line += '  selector=' + (selector || '(无id,请用文字点击)');
                if (label) line += '  文字/值="' + label + '"';
                lines.push(line);
                if (lines.length >= 60) break;
              }
              return lines.length ? lines.join('\n') : '页面无可交互元素';
            }
            """);
        return OpResult.Ok("页面可交互元素：\n" + desc);
    }

    public async Task<OpResult> Click(string selector)
    {
        await Page!.ClickAsync(selector, new PageClickOptions { Timeout = 8000 });
        return OpResult.Ok($"已点击 {selector}");
    }

    public async Task<OpResult> ClickText(string text)
    {
        await Page!.GetByText(text, new() { Exact = false }).First
            .ClickAsync(new LocatorClickOptions { Timeout = 8000 });
        return OpResult.Ok($"已按文字点击「{text}」");
    }

    public async Task<OpResult> Fill(string selector, string value)
    {
        await Page!.FillAsync(selector, value, new PageFillOptions { Timeout = 8000 });
        return OpResult.Ok($"已在 {selector} 填写「{value}」");
    }

    public async Task<OpResult> Select(string selector, string value)
    {
        // 先按 value 匹配；不命中再按可见文本（label）匹配
        var matched = await Page!.SelectOptionAsync(selector, new[] { value });
        if (matched.Count == 0)
            matched = await Page.SelectOptionAsync(selector, new SelectOptionValue { Label = value });
        return matched.Count > 0
            ? OpResult.Ok($"已在 {selector} 选择「{value}」")
            : OpResult.Fail($"{selector} 中未找到选项「{value}」");
    }

    public async Task<OpResult> GetText(string selector)
    {
        var t = await Page!.InnerTextAsync(selector, new() { Timeout = 5000 });
        return OpResult.Ok($"{selector} 文本：{t.Trim()}");
    }

    public async Task<OpResult> AssertText(string selector, string expected)
    {
        var t = (await Page!.InnerTextAsync(selector, new() { Timeout = 5000 })).Trim();
        return t.Contains(expected, StringComparison.OrdinalIgnoreCase)
            ? OpResult.Ok($"断言通过：{selector} 包含「{expected}」（实际「{t}」）")
            : OpResult.Fail($"断言失败：{selector} 不含「{expected}」（实际「{t}」）");
    }

    public OpResult Wait(int ms)
    {
        Thread.Sleep(Math.Clamp(ms, 0, 30_000));
        return OpResult.Ok($"已等待 {ms}ms");
    }

    /// <summary>按键（Enter/Tab/Escape 等，作用于当前焦点元素）。</summary>
    public async Task<OpResult> PressKey(string key)
    {
        await Page!.Keyboard.PressAsync(key);
        return OpResult.Ok($"已按键 {key}");
    }

    /// <summary>整页截图 → base64 PNG（供 AI 截图验证复用）。</summary>
    public async Task<string> ScreenshotBase64Async()
    {
        var bytes = await Page!.ScreenshotAsync(new PageScreenshotOptions { FullPage = false });
        return Convert.ToBase64String(bytes);
    }

    private static string Normalize(string url)
        => url.StartsWith("http://") || url.StartsWith("https://") ? url : "http://" + url;

    public async ValueTask DisposeAsync()
    {
        try { if (_browser != null) await _browser.CloseAsync(); } catch { }
        _pw?.Dispose();
    }
}
