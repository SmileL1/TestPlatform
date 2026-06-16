using SqlSugar;

namespace TestPlatform.Core.Entities;

[SugarTable("test_suites")]
public class TestSuite
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public string Name        { get; set; } = "";
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? Description { get; set; }
    public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt  { get; set; } = DateTime.UtcNow;
}
