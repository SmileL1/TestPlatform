using SqlSugar;

namespace TestPlatform.Core.Entities;

[SugarTable("run_logs")]
public class RunLog
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public int StepNumber { get; set; }
    public string ToolName { get; set; } = "";
    [SugarColumn(ColumnDataType = "text")]
    public string Arguments { get; set; } = "";
    [SugarColumn(ColumnDataType = "text")]
    public string Result { get; set; } = "";
    /// <summary>AI 推理思考过程（仅 AI 模式有值），持久化以便历史回看</summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? Thinking { get; set; }
    public bool Success { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
