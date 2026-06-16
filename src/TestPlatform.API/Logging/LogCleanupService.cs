using Microsoft.Extensions.Hosting;
using TestPlatform.API.Settings;

namespace TestPlatform.API.Logging;

/// <summary>
/// 后台日志清理：启动后稍等即清一次，之后每 24 小时按「设置 → 日志保留天数」清理过期 AI 日志。
/// </summary>
public class LogCleanupService : BackgroundService
{
    private readonly ISettingsService _settings;

    public LogCleanupService(ISettingsService settings) => _settings = settings;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var cfg = await _settings.GetResolvedAsync();
                int days = int.TryParse(cfg.GetValueOrDefault("Logs:RetentionDays"), out var d) && d > 0 ? d : 14;
                var n = AiLog.Cleanup(days);
                if (n > 0) AiLog.Write("Cleanup", $"清理过期日志 {n} 个（保留 {days} 天）");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LogCleanup] {ex.Message}");
            }

            try { await Task.Delay(TimeSpan.FromHours(24), ct); } catch { break; }
        }
    }
}
