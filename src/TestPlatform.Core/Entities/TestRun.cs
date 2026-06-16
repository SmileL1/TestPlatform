using SqlSugar;

namespace TestPlatform.Core.Entities;

[SugarTable("test_runs")]
public class TestRun
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScenarioId { get; set; }
    public string Status { get; set; } = "pending";
    [SugarColumn(ColumnDataType = "text")]
    public string InputParamsJson { get; set; } = "{}";
    public int TotalSteps { get; set; }
    public int TokenUsed { get; set; }
    [SugarColumn(IsNullable = true)]
    public string? ErrorMsg { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    [SugarColumn(IsNullable = true)]
    public DateTime? FinishedAt { get; set; }
}
