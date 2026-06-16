using SqlSugar;

namespace TestPlatform.Core.Entities;

/// <summary>测试计划：一组测试场景的集合，支持批量运行</summary>
[SugarTable("test_plans")]
public class TestPlan
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid    Id          { get; set; } = Guid.NewGuid();
    public string  Name        { get; set; } = "";
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? Description { get; set; }
    public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt  { get; set; } = DateTime.UtcNow;
}

/// <summary>计划与场景的多对多关联</summary>
[SugarTable("test_plan_scenarios")]
public class TestPlanScenario
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid PlanId     { get; set; }
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid ScenarioId { get; set; }
    public int  SortOrder  { get; set; }  // 执行顺序
}

/// <summary>计划的一次批量执行记录</summary>
[SugarTable("test_plan_runs")]
public class TestPlanRun
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid     Id           { get; set; } = Guid.NewGuid();
    public Guid     PlanId       { get; set; }
    public string   Status       { get; set; } = "running"; // running/completed
    public int      TotalCount   { get; set; }
    public int      PassedCount  { get; set; }
    public int      FailedCount  { get; set; }
    public int      RunningCount { get; set; }
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string?  Mode         { get; set; } = "auto";
    public DateTime StartedAt    { get; set; } = DateTime.UtcNow;
    [SugarColumn(IsNullable = true)]
    public DateTime? FinishedAt  { get; set; }
}

/// <summary>计划执行中，每个场景的运行记录关联</summary>
[SugarTable("test_plan_run_items")]
public class TestPlanRunItem
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public Guid   PlanRunId   { get; set; }
    public Guid   ScenarioId  { get; set; }
    [SugarColumn(IsNullable = true)]
    public Guid?  TestRunId   { get; set; }  // 对应的 TestRun，执行前为 null
    public int    SortOrder   { get; set; }
    public string Status      { get; set; } = "pending"; // pending/running/passed/failed
}
