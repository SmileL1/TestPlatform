using System.Diagnostics;
using System.Windows.Automation;
using Microsoft.AspNetCore.Mvc;
using TestPlatform.API.Logging;

namespace TestPlatform.API.Controllers;

public record WindowInfo(string Title, string ProcessName, int Pid);

[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
    /// <summary>
    /// 列出当前桌面上所有有标题的顶层窗口（供「目标窗口」选择）。
    /// 与 ElementFinder.Attach 同源：按 ControlType.Window 枚举 RootElement 的直接子级。
    /// </summary>
    [HttpGet("windows")]
    public IActionResult Windows()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<WindowInfo>();

        try
        {
            var wins = AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));

            foreach (AutomationElement w in wins)
            {
                string title;
                int pid;
                try
                {
                    title = w.Current.Name?.Trim() ?? "";
                    pid   = w.Current.ProcessId;
                }
                catch { continue; }   // 窗口正在关闭/切换

                if (string.IsNullOrWhiteSpace(title) || !seen.Add(title)) continue;

                string proc = "";
                try { proc = Process.GetProcessById(pid).ProcessName; } catch { }

                list.Add(new WindowInfo(title, proc, pid));
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"枚举窗口失败：{ex.Message}");
        }

        return Ok(list.OrderBy(x => x.ProcessName).ThenBy(x => x.Title));
    }

    /// <summary>AI 日志用量（目录 / 文件数 / 大小）</summary>
    [HttpGet("logs/info")]
    public IActionResult LogsInfo()
    {
        var (dir, count, bytes) = AiLog.Info();
        return Ok(new { dir, count, sizeKb = bytes / 1024 });
    }

    /// <summary>立即清理超过 days 天的 AI 日志</summary>
    [HttpPost("logs/cleanup")]
    public IActionResult LogsCleanup([FromQuery] int days = 14)
    {
        var deleted = AiLog.Cleanup(days);
        AiLog.Write("Cleanup", $"手动清理过期日志 {deleted} 个（保留 {days} 天）");
        return Ok(new { deleted });
    }
}
