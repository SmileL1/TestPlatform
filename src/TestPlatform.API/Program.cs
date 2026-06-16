using TestPlatform.API.Execution;
using TestPlatform.API.Logging;
using TestPlatform.API.Recording;
using TestPlatform.API.Settings;
using TestPlatform.Core.DB;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddCors(opt => opt.AddPolicy("All", p =>
    p.WithOrigins("http://localhost:5173", "http://localhost:5174", "http://localhost:3000")
     .AllowAnyMethod()
     .AllowAnyHeader()
     .AllowCredentials()));   // SignalR WebSocket 必须 AllowCredentials，不能用 AllowAnyOrigin

var connStr = builder.Configuration.GetConnectionString("Default")!;

// ── 自动创建数据库（若不存在）──────────────────────────────────
EnsureDatabaseCreated(connStr);

var dbCtx = new AppDbContext(connStr);
MigrateDatabase(connStr);  // 先做列迁移（处理已有数据的 nullable 列）
dbCtx.InitDatabase();       // 再建表/更新结构
builder.Services.AddSingleton(dbCtx);
builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.AddSingleton<IRunService, RunService>();
builder.Services.AddSingleton<IRecorder, Recorder>();
builder.Services.AddSingleton<IBrowserRecorder, BrowserRecorder>();
builder.Services.AddHostedService<LogCleanupService>();

var app = builder.Build();
app.UseCors("All");
app.MapControllers();
app.MapHub<TestPlatform.API.Hubs.TestHub>("/hubs/test");
app.Run();

// ── 连接 postgres 主库，检测并创建目标数据库 ───────────────────
static void EnsureDatabaseCreated(string connStr)
{
    // 用 SqlSugar 自带的 Npgsql 连接（不需要额外引用）
    // 手动把 Database= 替换为 postgres
    var targetDb = ExtractDbName(connStr);
    var masterConn = System.Text.RegularExpressions.Regex.Replace(
        connStr,
        @"Database\s*=\s*[^;]+",
        "Database=postgres",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    using var db = new SqlSugar.SqlSugarClient(new SqlSugar.ConnectionConfig
    {
        ConnectionString  = masterConn,
        DbType            = SqlSugar.DbType.PostgreSQL,
        IsAutoCloseConnection = true
    });

    var exists = db.Ado.GetScalar($"SELECT 1 FROM pg_database WHERE datname = '{targetDb}'") != null;
    if (!exists)
    {
        db.Ado.ExecuteCommand($"CREATE DATABASE \"{targetDb}\"");
        Console.WriteLine($"[DB] 数据库 '{targetDb}' 创建成功");
    }
    else
    {
        Console.WriteLine($"[DB] 数据库 '{targetDb}' 已存在");
    }
}

// ── 迁移：给已有表添加新列（保证旧数据不报 null 错）──────────────
static void MigrateDatabase(string connStr)
{
    try
    {
        using var db = new SqlSugar.SqlSugarClient(new SqlSugar.ConnectionConfig
        {
            ConnectionString      = connStr,
            DbType                = SqlSugar.DbType.PostgreSQL,
            IsAutoCloseConnection = true
        });

        // scenarios 表：补 type 列（默认 wpf）
        db.Ado.ExecuteCommand(@"
            DO $$ BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name='scenarios' AND column_name='type'
                ) THEN
                    ALTER TABLE scenarios ADD COLUMN type varchar(20) DEFAULT 'wpf' NOT NULL;
                END IF;
            END $$;
        ");

        // scenarios 表：补 suite_id 列（可空）
        db.Ado.ExecuteCommand(@"
            DO $$ BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name='scenarios' AND column_name='suite_id'
                ) THEN
                    ALTER TABLE scenarios ADD COLUMN suite_id uuid NULL;
                END IF;
            END $$;
        ");

        // 已有行的 type 若为 null，填 wpf
        db.Ado.ExecuteCommand("UPDATE scenarios SET type = 'wpf' WHERE type IS NULL OR type = ''");

        // scenarios 表：补 AI 截图验证相关列
        db.Ado.ExecuteCommand(@"
            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_name='scenarios' AND column_name='aiverifyenabled') THEN
                    ALTER TABLE scenarios ADD COLUMN aiverifyenabled boolean DEFAULT false NOT NULL;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_name='scenarios' AND column_name='aiverifyprompt') THEN
                    ALTER TABLE scenarios ADD COLUMN aiverifyprompt text NULL;
                END IF;
            END $$;
        ");

        // run_logs 表：补 thinking 列（AI 推理过程持久化）
        db.Ado.ExecuteCommand(@"
            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_name='run_logs' AND column_name='thinking') THEN
                    ALTER TABLE run_logs ADD COLUMN thinking text NULL;
                END IF;
            END $$;
        ");

        // run_logs.arguments / result 由 varchar(255) 升为 text（AI 输入/输出超 255 会报 22001）
        db.Ado.ExecuteCommand(@"
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_name='run_logs' AND column_name='arguments'
                    AND data_type='character varying') THEN
                    ALTER TABLE run_logs ALTER COLUMN arguments TYPE text;
                END IF;
                IF EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_name='run_logs' AND column_name='result'
                    AND data_type='character varying') THEN
                    ALTER TABLE run_logs ALTER COLUMN result TYPE text;
                END IF;
            END $$;
        ");

        // test_plan_run_items.testrunid 改为允许 NULL
        db.Ado.ExecuteCommand(@"
            DO $$ BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name='test_plan_run_items' AND column_name='testrunid'
                    AND is_nullable='NO'
                ) THEN
                    ALTER TABLE test_plan_run_items ALTER COLUMN testrunid DROP NOT NULL;
                END IF;
            END $$;
        ");

        Console.WriteLine("[DB] 迁移完成");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB] 迁移跳过（可能表不存在）: {ex.Message}");
    }
}

static string ExtractDbName(string connStr)
{
    var m = System.Text.RegularExpressions.Regex.Match(
        connStr, @"Database\s*=\s*([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    return m.Success ? m.Groups[1].Value.Trim() : "test_platform";
}
