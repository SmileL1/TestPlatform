using TestPlatform.Core.DB;
using TestPlatform.Core.Entities;

namespace TestPlatform.API.Settings;

/// <summary>脱敏后的设置：values 含非敏感项明文；configured 标记各敏感项是否已配置（不回传密钥本身）</summary>
public record MaskedSettings(Dictionary<string, string?> Values, Dictionary<string, bool> Configured);

public interface ISettingsService
{
    /// <summary>生效值（密钥已解密为明文）：本表有则用本表，否则回退 appsettings.json。仅供后端执行使用</summary>
    Task<Dictionary<string, string?>> GetResolvedAsync();
    /// <summary>脱敏视图（给前端）：非敏感项明文 + 敏感项是否已配置，绝不回传密钥明文</summary>
    Task<MaskedSettings> GetMaskedAsync();
    /// <summary>保存（仅覆盖白名单内的键；敏感项加密落库；敏感项传空表示保持不变）</summary>
    Task SaveAsync(Dictionary<string, string?> values);
}

/// <summary>
/// 应用配置存取：AI 接口的 ApiKey/Model/BaseUrl 两套——
/// 操作（DeepSeek，纯文本推理）与 验证（AiVision，多模态识图）。
/// 键名与 appsettings.json 保持一致，便于回退。两个 ApiKey 为敏感项：加密落库、不回传明文。
/// </summary>
public class SettingsService : ISettingsService
{
    /// <summary>可被「设置」页管理的键白名单</summary>
    public static readonly string[] Keys =
    {
        "DeepSeek:ApiKey", "DeepSeek:Model", "DeepSeek:BaseUrl",   // 操作（无需识图）
        "AiVision:ApiKey", "AiVision:Model", "AiVision:BaseUrl",   // 验证（需识图）
        "Target:DefaultWindow",                                    // 默认测试目标窗口（新建场景/录制预填）
        "Logs:RetentionDays"                                       // AI 日志保留天数（过期自动清理）
    };

    /// <summary>敏感项：加密存储、不回传明文</summary>
    public static readonly HashSet<string> SecretKeys = new()
    {
        "DeepSeek:ApiKey", "AiVision:ApiKey"
    };

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public SettingsService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    /// <summary>本表原始存储值（敏感项为密文）</summary>
    private async Task<Dictionary<string, string?>> GetStoredAsync()
    {
        var c = _db.CreateClient();
        var rows = await c.Queryable<AppSetting>().ToListAsync();
        var map = rows.ToDictionary(r => r.Key, r => r.Value);
        return Keys.ToDictionary(k => k, k => map.TryGetValue(k, out var v) ? v : null);
    }

    public async Task<Dictionary<string, string?>> GetResolvedAsync()
    {
        var stored = await GetStoredAsync();
        return Keys.ToDictionary(k => k, k =>
        {
            var s = stored[k];
            if (string.IsNullOrWhiteSpace(s)) return _config[k];          // 回退 appsettings.json（明文）
            return SecretKeys.Contains(k) ? SecretProtector.Decrypt(s) : s;
        });
    }

    public async Task<MaskedSettings> GetMaskedAsync()
    {
        var stored = await GetStoredAsync();
        var values     = new Dictionary<string, string?>();
        var configured = new Dictionary<string, bool>();

        foreach (var k in Keys)
        {
            if (SecretKeys.Contains(k))
            {
                // 已配置 = 本表有密文 或 appsettings 有回退值；不回传任何密钥内容
                configured[k] = !string.IsNullOrWhiteSpace(stored[k])
                                || !string.IsNullOrWhiteSpace(_config[k]);
            }
            else
            {
                values[k] = string.IsNullOrWhiteSpace(stored[k]) ? _config[k] : stored[k];
            }
        }
        return new MaskedSettings(values, configured);
    }

    public async Task SaveAsync(Dictionary<string, string?> values)
    {
        var c = _db.CreateClient();
        foreach (var k in Keys)
        {
            if (!values.TryGetValue(k, out var v)) continue;             // 未提交的键不动

            if (SecretKeys.Contains(k))
            {
                if (string.IsNullOrWhiteSpace(v)) continue;              // 敏感项留空 = 保持原值不变
                v = SecretProtector.Encrypt(v.Trim());                   // 加密落库
            }

            var existing = await c.Queryable<AppSetting>().FirstAsync(x => x.Key == k);
            if (existing == null)
                await c.Insertable(new AppSetting { Key = k, Value = v }).ExecuteCommandAsync();
            else
            {
                existing.Value = v;
                await c.Updateable(existing).ExecuteCommandAsync();
            }
        }
    }
}
