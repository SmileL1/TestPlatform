using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Playwright;
using TestPlatform.API.Hubs;
using TestPlatform.API.Web;

namespace TestPlatform.API.Recording;

public interface IBrowserRecorder
{
    bool IsRecording { get; }
    Task StartAsync(string url);
    Task<List<RecordedStep>> StopAsync();
    List<RecordedStep> GetSteps();
    void DeleteStep(int index);
    void Clear();
}

/// <summary>
/// 网页操作录制器。用 Playwright 打开一个浏览器，往页面注入监听脚本，
/// 把用户的点击 / 输入 / 下拉选择实时采集成步骤（与 WPF 录制共用 RecordedStep 结构与 SignalR 通道）。
/// 元素定位用 CSS 选择器：优先 #id → [name] → 简单 nth-of-type 路径。
/// </summary>
public class BrowserRecorder : IBrowserRecorder, IAsyncDisposable
{
    private readonly IHubContext<TestHub> _hub;
    private readonly List<RecordedStep> _steps = new();
    private readonly object _lock = new();

    private IPlaywright? _pw;
    private IBrowser?    _browser;
    private IPage?       _page;
    private bool         _isRecording;

    public bool IsRecording => _isRecording;

    public BrowserRecorder(IHubContext<TestHub> hub) => _hub = hub;

    public async Task StartAsync(string url)
    {
        if (_isRecording) return;

        lock (_lock) _steps.Clear();

        _pw      = await Playwright.CreateAsync();
        _browser = await BrowserLauncher.LaunchAsync(_pw);
        _page    = await _browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 860 }
        });

        // 注入脚本调用此绑定回传一条操作（JSON）
        await _page.ExposeBindingAsync("__recordStep",
            (Microsoft.Playwright.BindingSource _, string json) => { OnJsStep(json); return Task.CompletedTask; });

        // 每次文档加载都重新挂监听（导航后仍生效）
        await _page.AddInitScriptAsync(InjectScript);

        _isRecording = true;
        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                await _page.GotoAsync(Normalize(url),
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
            }
            catch (Exception ex) { Console.WriteLine($"[BrowserRec] 导航失败: {ex.Message}"); }
        }
        Console.WriteLine($"[BrowserRec] 开始录制 → {url}");
    }

    public async Task<List<RecordedStep>> StopAsync()
    {
        _isRecording = false;
        try { if (_browser != null) await _browser.CloseAsync(); } catch { }
        _pw?.Dispose();
        _browser = null; _page = null; _pw = null;
        Console.WriteLine($"[BrowserRec] 停止录制，共 {_steps.Count} 步");
        return GetSteps();
    }

    public List<RecordedStep> GetSteps()
    {
        lock (_lock) return _steps.ToList();
    }

    public void DeleteStep(int index)
    {
        lock (_lock)
        {
            var s = _steps.FirstOrDefault(x => x.Index == index);
            if (s != null) _steps.Remove(s);
        }
    }

    public void Clear()
    {
        lock (_lock) _steps.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_browser != null) await _browser.CloseAsync(); } catch { }
        _pw?.Dispose();
    }

    // ── JS → .NET：一条操作 ───────────────────────────────────────

    private record JsStep(string action, string? selector, string? value, string? text);

    private void OnJsStep(string json)
    {
        if (!_isRecording) return;
        JsStep? e;
        try { e = JsonSerializer.Deserialize<JsStep>(json); } catch { return; }
        if (e == null || string.IsNullOrEmpty(e.selector)) return;

        // action 复用 WPF 命名，前端表格与回放可直接通用
        var action = e.action switch
        {
            "fill"   => "set_text",
            "select" => "select_item",
            _         => "click"
        };
        var value = e.value ?? "";
        var name  = string.IsNullOrWhiteSpace(e.text) ? e.selector! : e.text!;

        lock (_lock)
        {
            // 防抖：同一元素连续输入/选择只保留最后一次值
            var last = _steps.LastOrDefault();
            if (last != null && last.Action == action && last.Target == e.selector
                && action is "set_text" or "select_item")
            {
                last.Value = value;
                PushStep(last);
                return;
            }
            // 点击文本框紧接着的输入：丢弃多余的点击（保留输入步骤）
            if (last is { Action: "click" } && action == "set_text" && last.Target == e.selector)
                _steps.Remove(last);

            var step = new RecordedStep
            {
                Index       = _steps.Count,
                Action      = action,
                Target      = e.selector!,
                TargetName  = name,
                Value       = value,
                ControlType = "web",
                Time        = DateTime.Now.ToString("HH:mm:ss")
            };
            _steps.Add(step);
            ReindexNoLock();
            PushStep(step);
            Console.WriteLine($"[BrowserRec] #{step.Index} {step.Description}");
        }
    }

    private void ReindexNoLock()
    {
        for (int i = 0; i < _steps.Count; i++) _steps[i].Index = i;
    }

    private void PushStep(RecordedStep step)
        => _ = _hub.Clients.Group("recording").SendAsync("RecordedStep", step);

    private static string Normalize(string url)
        => url.StartsWith("http://") || url.StartsWith("https://") ? url : "http://" + url;

    // ── 注入页面的监听脚本（AddInitScript 直接执行脚本文本，故用 IIFE 立即运行）──
    private const string InjectScript = """
        (() => {
          if (window.__tpRecHooked) return; window.__tpRecHooked = true;
          function sel(el) {
            if (!el || el.nodeType !== 1) return '';
            if (el.id) return '#' + el.id;
            var nm = el.getAttribute && el.getAttribute('name');
            if (nm) return el.tagName.toLowerCase() + '[name="' + nm + '"]';
            var path = [], e = el;
            while (e && e.nodeType === 1 && e !== document.body) {
              var s = e.tagName.toLowerCase();
              var p = e.parentNode;
              if (p) {
                var sib = Array.prototype.filter.call(p.children, function(c){ return c.tagName === e.tagName; });
                if (sib.length > 1) s += ':nth-of-type(' + (sib.indexOf(e) + 1) + ')';
              }
              path.unshift(s); e = p;
            }
            return path.join(' > ');
          }
          // 表单控件的“名字”：关联 label → 包裹 label → placeholder/aria-label/name（绝不用已输入的值）
          function fieldName(el) {
            if (el.id) {
              var lb = document.querySelector('label[for="' + el.id + '"]');
              if (lb) { var t = (lb.innerText || '').trim(); if (t) return t.slice(0, 40); }
            }
            var wrap = el.closest && el.closest('label');
            if (wrap) { var t2 = (wrap.innerText || '').trim(); if (t2) return t2.slice(0, 40); }
            var a = el.getAttribute && (el.getAttribute('placeholder') || el.getAttribute('aria-label') || el.getAttribute('name'));
            if (a) return ('' + a).trim().slice(0, 40);
            return el.id || el.tagName.toLowerCase();
          }
          // 可点击元素的名字：优先按钮/链接文字，没有再退回字段名（如 checkbox 用其 label）
          function clickName(el) {
            var t = ((el.innerText || '') + '').trim().replace(/\s+/g, ' ');
            return t ? t.slice(0, 40) : fieldName(el);
          }
          document.addEventListener('click', function(ev) {
            var el = ev.target.closest('a,button,input,select,textarea,[role=button],label') || ev.target;
            var t = (el.getAttribute && el.getAttribute('type')) || '';
            // 文本类输入框 / 文本域 / 下拉框的点击都不记：文本框等 change 记成 set_text，下拉框等 change 记成 select
            if (el.tagName === 'INPUT' && ['text','password','number','email','search','tel'].indexOf(t) >= 0) return;
            if (el.tagName === 'TEXTAREA' || el.tagName === 'SELECT') return;
            window.__recordStep(JSON.stringify({ action: 'click', selector: sel(el), text: clickName(el) }));
          }, true);
          document.addEventListener('change', function(ev) {
            var el = ev.target;
            if (el.tagName === 'SELECT')
              window.__recordStep(JSON.stringify({ action: 'select', selector: sel(el), value: el.value, text: fieldName(el) }));
            else if (el.type === 'checkbox' || el.type === 'radio')
              { /* 由 click 记录 */ }
            else
              window.__recordStep(JSON.stringify({ action: 'fill', selector: sel(el), value: el.value, text: fieldName(el) }));
          }, true);
        })();
        """;
}
