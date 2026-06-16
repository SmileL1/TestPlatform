using SqlSugar;
using TestPlatform.Core.Entities;

namespace TestPlatform.Core.DB;

public class AppDbContext
{
    private readonly string _connectionString;

    public AppDbContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    public ISqlSugarClient CreateClient()
    {
        return new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connectionString,
            DbType = DbType.PostgreSQL,
            IsAutoCloseConnection = true,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (property, column) =>
                {
                    if (column.IsPrimarykey && column.PropertyInfo.PropertyType == typeof(Guid))
                        column.IsIdentity = false;
                }
            }
        });
    }

    public void InitDatabase()
    {
        var db = CreateClient();
        db.CodeFirst.InitTables<TestPlan>();
        db.CodeFirst.InitTables<TestPlanScenario>();
        db.CodeFirst.InitTables<TestPlanRun>();
        db.CodeFirst.InitTables<TestPlanRunItem>();
        db.CodeFirst.InitTables<TestSuite>();
        db.CodeFirst.InitTables<Scenario>();
        db.CodeFirst.InitTables<TestRun>();
        db.CodeFirst.InitTables<RunLog>();
        db.CodeFirst.InitTables<AppSetting>();
    }
}
