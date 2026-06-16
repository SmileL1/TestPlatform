using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using TestPlatform.API.Execution;
using TestPlatform.Core.DB;
using TestPlatform.Core.Entities;

namespace TestPlatform.API.Controllers;

[ApiController]
[Route("api/plans")]
public class TestPlanController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IRunService  _runService;
    // 记录每个计划运行的 CancellationTokenSource，用于取消
    private static readonly Dictionary<Guid, CancellationTokenSource> _runningPlans = new();
    private static readonly object _planLock = new();

    public TestPlanController(AppDbContext db, IRunService runService)
    {
        _db         = db;
        _runService = runService;
    }

    // ── 计划 CRUD ────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var db   = _db.CreateClient();
        var list = await db.Queryable<TestPlan>()
            .OrderByDescending(p => p.CreatedAt).ToListAsync();

        var counts = await db.Queryable<TestPlanScenario>()
            .GroupBy(ps => ps.PlanId)
            .Select(ps => new { PlanId = ps.PlanId, Count = SqlFunc.AggregateCount(ps.ScenarioId) })
            .ToListAsync();

        var result = list.Select(p => new
        {
            p.Id, p.Name, p.Description, p.CreatedAt, p.UpdatedAt,
            ScenarioCount = counts.FirstOrDefault(c => c.PlanId == p.Id)?.Count ?? 0
        });
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var db = _db.CreateClient();
        var plan = await db.Queryable<TestPlan>().FirstAsync(p => p.Id == id);
        return plan == null ? NotFound() : Ok(plan);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TestPlan plan)
    {
        plan.Id = Guid.NewGuid();
        plan.CreatedAt = plan.UpdatedAt = DateTime.UtcNow;
        var db = _db.CreateClient();
        await db.Insertable(plan).ExecuteCommandAsync();
        return Ok(plan);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] TestPlan plan)
    {
        plan.Id = id; plan.UpdatedAt = DateTime.UtcNow;
        var db = _db.CreateClient();
        await db.Updateable(plan).ExecuteCommandAsync();
        return Ok(plan);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var db = _db.CreateClient();
        await db.Deleteable<TestPlanScenario>().Where(ps => ps.PlanId == id).ExecuteCommandAsync();
        await db.Deleteable<TestPlanRun>().Where(r => r.PlanId == id).ExecuteCommandAsync();
        await db.Deleteable<TestPlan>().Where(p => p.Id == id).ExecuteCommandAsync();
        return Ok();
    }

    // ── 计划内场景管理 ───────────────────────────────────────────

    /// <summary>获取计划内所有场景（含场景详情）</summary>
    [HttpGet("{id:guid}/scenarios")]
    public async Task<IActionResult> GetScenarios(Guid id)
    {
        var db = _db.CreateClient();
        var items = await db.Queryable<TestPlanScenario, Scenario>(
                (ps, s) => new JoinQueryInfos(JoinType.Inner, ps.ScenarioId == s.Id))
            .Where((ps, s) => ps.PlanId == id)
            .OrderBy((ps, s) => ps.SortOrder)
            .Select((ps, s) => new
            {
                s.Id, s.Name, s.Type, s.WindowTitle, s.Description,
                s.MaxSteps, s.ParametersJson, s.StepsJson,
                ps.SortOrder
            })
            .ToListAsync();
        return Ok(items);
    }

    /// <summary>向计划中添加场景（批量）</summary>
    [HttpPost("{id:guid}/scenarios")]
    public async Task<IActionResult> AddScenarios(Guid id, [FromBody] List<Guid> scenarioIds)
    {
        var db = _db.CreateClient();
        // 获取当前最大排序
        var maxOrder = await db.Queryable<TestPlanScenario>()
            .Where(ps => ps.PlanId == id)
            .MaxAsync(ps => ps.SortOrder);

        var toAdd = new List<TestPlanScenario>();
        foreach (var sid in scenarioIds)
        {
            var exists = await db.Queryable<TestPlanScenario>()
                .AnyAsync(ps => ps.PlanId == id && ps.ScenarioId == sid);
            if (!exists)
                toAdd.Add(new TestPlanScenario { PlanId = id, ScenarioId = sid, SortOrder = ++maxOrder });
        }
        if (toAdd.Any())
            await db.Insertable(toAdd).ExecuteCommandAsync();
        return Ok(new { added = toAdd.Count });
    }

    /// <summary>从计划中移除场景</summary>
    [HttpDelete("{id:guid}/scenarios/{scenarioId:guid}")]
    public async Task<IActionResult> RemoveScenario(Guid id, Guid scenarioId)
    {
        var db = _db.CreateClient();
        await db.Deleteable<TestPlanScenario>()
            .Where(ps => ps.PlanId == id && ps.ScenarioId == scenarioId)
            .ExecuteCommandAsync();
        return Ok();
    }

    // ── 批量运行 ─────────────────────────────────────────────────

    [HttpPost("{id:guid}/run")]
    public async Task<IActionResult> RunPlan(Guid id, [FromBody] PlanRunRequest req)
    {
        var db = _db.CreateClient();
        var plan = await db.Queryable<TestPlan>().FirstAsync(p => p.Id == id);
        if (plan == null) return NotFound();

        var scenarios = await db.Queryable<TestPlanScenario, Scenario>(
                (ps, s) => new JoinQueryInfos(JoinType.Inner, ps.ScenarioId == s.Id))
            .Where((ps, s) => ps.PlanId == id)
            .OrderBy((ps, s) => ps.SortOrder)
            .Select((ps, s) => new { s.Id, ps.SortOrder })
            .ToListAsync();

        if (!scenarios.Any())
            return BadRequest(new { error = "计划中没有场景" });

        // 创建计划执行记录
        var planRun = new TestPlanRun
        {
            PlanId       = id,
            Status       = "running",
            TotalCount   = scenarios.Count,
            RunningCount = 0,
            Mode         = req.Mode ?? "auto",
            StartedAt    = DateTime.UtcNow
        };
        await db.Insertable(planRun).ExecuteCommandAsync();

        // 创建每个场景的执行项
        var items = scenarios.Select(s => new TestPlanRunItem
        {
            PlanRunId  = planRun.Id,
            ScenarioId = s.Id,
            SortOrder  = s.SortOrder,
            Status     = "pending"
        }).ToList();
        await db.Insertable(items).ExecuteCommandAsync();

        // 异步逐个运行（顺序执行）
        var cts = new CancellationTokenSource();
        lock (_planLock) _runningPlans[planRun.Id] = cts;

        _ = Task.Run(() => ExecutePlanAsync(planRun.Id, items, req, cts.Token));

        return Ok(new { planRunId = planRun.Id });
    }

    /// <summary>取消计划运行</summary>
    [HttpPost("runs/{runId:guid}/cancel")]
    public async Task<IActionResult> CancelPlanRun(Guid runId)
    {
        lock (_planLock)
        {
            if (_runningPlans.TryGetValue(runId, out var cts))
            {
                cts.Cancel();
                _runningPlans.Remove(runId);
            }
        }
        var db = _db.CreateClient();
        await db.Updateable<TestPlanRun>()
            .SetColumns(r => new TestPlanRun { Status = "cancelled", FinishedAt = DateTime.UtcNow })
            .Where(r => r.Id == runId).ExecuteCommandAsync();
        await db.Updateable<TestPlanRunItem>()
            .SetColumns(i => new TestPlanRunItem { Status = "cancelled" })
            .Where(i => i.PlanRunId == runId && i.Status == "pending")
            .ExecuteCommandAsync();
        return Ok(new { message = "已取消" });
    }

    private async Task ExecutePlanAsync(Guid planRunId, List<TestPlanRunItem> items, PlanRunRequest req, CancellationToken ct = default)
    {
        var db = _db.CreateClient();
        foreach (var item in items.OrderBy(i => i.SortOrder))
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                // 标记为 running
                await db.Updateable<TestPlanRunItem>()
                    .SetColumns(i => new TestPlanRunItem { Status = "running" })
                    .Where(i => i.Id == item.Id).ExecuteCommandAsync();

                // 运行场景
                var runId = await _runService.StartRunAsync(
                    item.ScenarioId,
                    req.InputParams ?? new(),
                    req.Mode ?? "auto");

                // ★ 立即把 TestRunId 写入，前端就能实时拉日志了
                await db.Updateable<TestPlanRunItem>()
                    .SetColumns(i => new TestPlanRunItem { TestRunId = runId })
                    .Where(i => i.Id == item.Id).ExecuteCommandAsync();

                // 等待完成（轮询，支持取消）
                string finalStatus = "failed";
                for (int i = 0; i < 240; i++) // 最多等20分钟
                {
                    try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
                    if (ct.IsCancellationRequested) break;
                    var run = await db.Queryable<TestRun>().FirstAsync(r => r.Id == runId);
                    if (run?.Status is "passed" or "failed" or "cancelled")
                    {
                        finalStatus = run.Status;
                        break;
                    }
                }
                if (ct.IsCancellationRequested) { finalStatus = "cancelled"; }

                // 更新 item 最终状态
                await db.Updateable<TestPlanRunItem>()
                    .SetColumns(i => new TestPlanRunItem { Status = finalStatus })
                    .Where(i => i.Id == item.Id).ExecuteCommandAsync();

                // 更新计划运行统计
                await UpdatePlanRunStats(planRunId);
            }
            catch (Exception ex)
            {
                await db.Updateable<TestPlanRunItem>()
                    .SetColumns(i => new TestPlanRunItem { Status = "failed" })
                    .Where(i => i.Id == item.Id).ExecuteCommandAsync();
                Console.WriteLine($"[PlanRun] 场景执行异常: {ex.Message}");
            }
        }

        lock (_planLock) _runningPlans.Remove(planRunId);

        // 全部完成
        var planFinalStatus = ct.IsCancellationRequested ? "cancelled" : "completed";
        await db.Updateable<TestPlanRun>()
            .SetColumns(r => new TestPlanRun { Status = planFinalStatus, FinishedAt = DateTime.UtcNow })
            .Where(r => r.Id == planRunId).ExecuteCommandAsync();
    }

    private async Task UpdatePlanRunStats(Guid planRunId)
    {
        var db    = _db.CreateClient();
        var items = await db.Queryable<TestPlanRunItem>()
            .Where(i => i.PlanRunId == planRunId).ToListAsync();

        await db.Updateable<TestPlanRun>()
            .SetColumns(r => new TestPlanRun
            {
                PassedCount  = items.Count(i => i.Status == "passed"),
                FailedCount  = items.Count(i => i.Status == "failed"),
                RunningCount = items.Count(i => i.Status == "running")
            })
            .Where(r => r.Id == planRunId).ExecuteCommandAsync();
    }

    // ── 计划执行历史 ─────────────────────────────────────────────

    [HttpGet("{id:guid}/runs")]
    public async Task<IActionResult> GetRuns(Guid id)
    {
        var db   = _db.CreateClient();
        var runs = await db.Queryable<TestPlanRun>()
            .Where(r => r.PlanId == id)
            .OrderByDescending(r => r.StartedAt)
            .Take(20).ToListAsync();
        return Ok(runs);
    }

    /// <summary>计划内每个场景的「最近一次运行」状态（执行历史按计划维度查看用）</summary>
    [HttpGet("{id:guid}/scenario-status")]
    public async Task<IActionResult> ScenarioStatus(Guid id)
    {
        var db = _db.CreateClient();
        var scenarios = await db.Queryable<TestPlanScenario, Scenario>(
                (ps, s) => new JoinQueryInfos(JoinType.Inner, ps.ScenarioId == s.Id))
            .Where((ps, s) => ps.PlanId == id)
            .OrderBy((ps, s) => ps.SortOrder)
            .Select((ps, s) => new { s.Id, s.Name, s.Type, s.StepsJson, ps.SortOrder })
            .ToListAsync();

        var ids = scenarios.Select(x => x.Id).ToList();
        var runs = ids.Count == 0
            ? new List<TestRun>()
            : await db.Queryable<TestRun>().Where(r => ids.Contains(r.ScenarioId))
                .OrderByDescending(r => r.StartedAt).ToListAsync();

        var latest = new Dictionary<Guid, TestRun>();
        foreach (var r in runs)
            if (!latest.ContainsKey(r.ScenarioId)) latest[r.ScenarioId] = r;

        var result = scenarios.Select(s =>
        {
            latest.TryGetValue(s.Id, out var run);
            return new
            {
                scenarioId     = s.Id,
                name           = s.Name,
                type           = s.Type,
                sortOrder      = s.SortOrder,
                stepCount      = StepCount(s.StepsJson),
                lastRunId      = run?.Id,
                lastStatus     = run?.Status,
                lastStartedAt  = run?.StartedAt,
                lastFinishedAt = run?.FinishedAt,
                lastSteps      = run?.TotalSteps
            };
        });
        return Ok(result);
    }

    private static int StepCount(string? stepsJson)
    {
        if (string.IsNullOrWhiteSpace(stepsJson) || stepsJson == "[]") return 0;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(stepsJson);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                ? doc.RootElement.GetArrayLength() : 0;
        }
        catch { return 0; }
    }

    [HttpGet("runs/{runId:guid}/items")]
    public async Task<IActionResult> GetRunItems(Guid runId)
    {
        var db = _db.CreateClient();
        var items = await db.Queryable<TestPlanRunItem, Scenario>(
                (i, s) => new JoinQueryInfos(JoinType.Inner, i.ScenarioId == s.Id))
            .Where((i, s) => i.PlanRunId == runId)
            .OrderBy((i, s) => i.SortOrder)
            .Select((i, s) => new
            {
                i.Id, i.Status, i.TestRunId, i.SortOrder,
                ScenarioId   = i.ScenarioId,
                ScenarioName = s.Name, ScenarioType = s.Type
            })
            .ToListAsync();
        return Ok(items);
    }
}

public class PlanRunRequest
{
    public string? Mode { get; set; } = "auto";
    public Dictionary<string, string>? InputParams { get; set; }
}
