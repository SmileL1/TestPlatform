using Microsoft.Playwright;

namespace TestPlatform.API.Web;

/// <summary>
/// 浏览器启动器：依次尝试 ① Playwright 自带 chromium 内核 → ② 系统 Edge（Windows 自带）→ ③ 系统 Chrome。
/// 解决「Playwright 包版本与本机已装内核版本不匹配」时找不到浏览器的问题，且无需额外下载。
/// 执行（BrowserDriver）与录制（BrowserRecorder）共用此逻辑。
/// </summary>
public static class BrowserLauncher
{
    public static async Task<IBrowser> LaunchAsync(IPlaywright pw, bool headless = false)
    {
        var opts = new BrowserTypeLaunchOptions { Headless = headless };
        var errors = new List<string>();

        foreach (var channel in new string?[] { null, "msedge", "chrome" })
        {
            try
            {
                opts.Channel = channel;   // null = 用 Playwright 自带内核
                var b = await pw.Chromium.LaunchAsync(opts);
                Console.WriteLine($"[Browser] 已启动：{(channel ?? "Playwright 内核")}");
                return b;
            }
            catch (Exception ex)
            {
                errors.Add($"{channel ?? "内核"}: {ex.Message.Split('\n')[0]}");
            }
        }
        throw new Exception("无法启动任何浏览器（已尝试 Playwright 内核 / Edge / Chrome）：" + string.Join(" | ", errors));
    }
}
