using SqlSugar;

namespace TestPlatform.Core.Entities;

/// <summary>
/// 应用级键值配置（如各 AI 接口的 ApiKey/Model/BaseUrl）。
/// 在「设置」页可改，运行时优先读本表，缺省回退到 appsettings.json。
/// </summary>
[SugarTable("app_settings")]
public class AppSetting
{
    [SugarColumn(ColumnName = "config_key", IsPrimaryKey = true, IsIdentity = false, Length = 100)]
    public string Key { get; set; } = "";

    [SugarColumn(ColumnName = "config_value", ColumnDataType = "text", IsNullable = true)]
    public string? Value { get; set; }
}
