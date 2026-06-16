using SqlSugar;

namespace TestPlatform.Core.Entities;

[SugarTable("scenarios")]
public class Scenario
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid   Id          { get; set; } = Guid.NewGuid();
    /// <summary>所属套件 ID（可为空，表示未归属套件）</summary>
    [SugarColumn(IsNullable = true)]
    public Guid?  SuiteId     { get; set; }
    /// <summary>测试类型：wpf / web</summary>
    [SugarColumn(DefaultValue = "wpf")]
    public string Type        { get; set; } = "wpf";
    public string Name        { get; set; } = "";
    public string WindowTitle { get; set; } = "SmartZaiko";
    [SugarColumn(ColumnDataType = "text")]
    public string Description { get; set; } = "";
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? StepsJson { get; set; }
    [SugarColumn(ColumnDataType = "text")]
    public string ParametersJson { get; set; } = "[]";
    [SugarColumn(ColumnDataType = "text")]
    public string AssertionsJson { get; set; } = "[]";
    public int MaxSteps { get; set; } = 60;
    /// <summary>是否启用 AI 截图验证（回放结束后截图交多模态模型判断）</summary>
    [SugarColumn(DefaultValue = "false")]
    public bool AiVerifyEnabled { get; set; }
    /// <summary>AI 验证的额外提示（可空，默认用场景描述）</summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? AiVerifyPrompt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
