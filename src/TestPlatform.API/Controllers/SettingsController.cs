using Microsoft.AspNetCore.Mvc;
using TestPlatform.API.Settings;

namespace TestPlatform.API.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settings;

    public SettingsController(ISettingsService settings) => _settings = settings;

    /// <summary>返回脱敏配置：非敏感项明文 + 敏感项(ApiKey)是否已配置；绝不回传密钥明文</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var masked = await _settings.GetMaskedAsync();
        return Ok(new { values = masked.Values, configured = masked.Configured });
    }

    /// <summary>保存配置（仅白名单内的键）</summary>
    [HttpPut]
    public async Task<IActionResult> Save([FromBody] Dictionary<string, string?> values)
    {
        if (values == null) return BadRequest("空请求");
        await _settings.SaveAsync(values);
        return Ok(new { saved = true });
    }
}
