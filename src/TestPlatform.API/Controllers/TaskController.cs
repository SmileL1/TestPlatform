using Microsoft.AspNetCore.Mvc;
using TestPlatform.API.Execution;
using TestPlatform.Core.DB;
using TestPlatform.Core.Entities;

namespace TestPlatform.API.Controllers;

[ApiController]
[Route("api/tasks")]
public class TaskController : ControllerBase
{
    private readonly IRunService _runService;
    private readonly AppDbContext _db;

    public TaskController(IRunService runService, AppDbContext db)
    {
        _runService = runService;
        _db = db;
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] RunRequest req)
    {
        var runId = await _runService.StartRunAsync(req.ScenarioId, req.InputParams ?? new(), req.Mode);
        return Ok(new { runId });
    }

    [HttpPost("{runId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid runId)
    {
        await _runService.CancelRunAsync(runId);
        return Ok();
    }

    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] Guid? scenarioId)
    {
        var db = _db.CreateClient();
        var query = db.Queryable<TestRun>();
        if (scenarioId.HasValue)
            query = query.Where(r => r.ScenarioId == scenarioId.Value);
        var list = await query.OrderByDescending(r => r.StartedAt).Take(100).ToListAsync();
        return Ok(list);
    }

    [HttpGet("{runId:guid}/logs")]
    public async Task<IActionResult> Logs(Guid runId)
    {
        var db = _db.CreateClient();
        var logs = await db.Queryable<RunLog>()
            .Where(l => l.RunId == runId)
            .OrderBy(l => l.StepNumber)
            .ToListAsync();
        return Ok(logs);
    }

    [HttpGet("{runId:guid}/status")]
    public async Task<IActionResult> Status(Guid runId)
    {
        var db = _db.CreateClient();
        var run = await db.Queryable<TestRun>().FirstAsync(r => r.Id == runId);
        return run == null ? NotFound() : Ok(run);
    }
}

public class RunRequest
{
    public Guid ScenarioId { get; set; }
    public Dictionary<string, string>? InputParams { get; set; }
    /// <summary>执行模式：auto / structured / ai</summary>
    public string Mode { get; set; } = "auto";
}
