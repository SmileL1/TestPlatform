using System.IO;
using System.Linq;

namespace TestPlatform.API.Logging;

/// <summary>
/// 轻量 AI 调用文件日志：把 DeepSeek / Vision 的请求与响应写到 logs/ai-yyyyMMdd.log，
/// 便于事后排查（不依赖第三方日志库）。线程安全、失败静默不影响主流程。
/// </summary>
public static class AiLog
{
    private static readonly object _lock = new();
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "logs");

    public static void Write(string category, string message)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var file = Path.Combine(Dir, $"ai-{DateTime.Now:yyyyMMdd}.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {message}";
            lock (_lock) File.AppendAllText(file, line + Environment.NewLine);
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    /// <summary>截断超长文本，避免日志过大（base64 图片等不应进来）</summary>
    public static string Trunc(string? s, int max = 4000)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        return s.Length <= max ? s : s[..max] + $"…(共 {s.Length} 字, 已截断)";
    }

    /// <summary>日志目录用量统计：(目录, 文件数, 总字节)</summary>
    public static (string Dir, int Count, long Bytes) Info()
    {
        try
        {
            if (!Directory.Exists(Dir)) return (Dir, 0, 0);
            var files = Directory.GetFiles(Dir, "ai-*.log");
            long bytes = files.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
            return (Dir, files.Length, bytes);
        }
        catch { return (Dir, 0, 0); }
    }

    /// <summary>删除超过保留天数的日志文件，返回删除个数。retentionDays&lt;=0 不清理。</summary>
    public static int Cleanup(int retentionDays)
    {
        if (retentionDays <= 0) return 0;
        int deleted = 0;
        try
        {
            if (!Directory.Exists(Dir)) return 0;
            var cutoff = DateTime.Now.AddDays(-retentionDays);
            foreach (var f in Directory.GetFiles(Dir, "ai-*.log"))
            {
                try { if (File.GetLastWriteTime(f) < cutoff) { File.Delete(f); deleted++; } }
                catch { /* 文件占用等，跳过 */ }
            }
        }
        catch { }
        return deleted;
    }
}
