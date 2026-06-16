using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using TestPlatform.Core.DB;
using TestPlatform.Core.Entities;

namespace TestPlatform.API.Controllers;

[ApiController]
[Route("api/suites")]
public class SuiteController : ControllerBase
{
    private readonly AppDbContext _db;
    public SuiteController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var db   = _db.CreateClient();
        var list = await db.Queryable<TestSuite>()
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        // 附带每个套件的场景数量
        var scenarioCounts = await db.Queryable<Scenario>()
            .Where(s => s.SuiteId != null)
            .GroupBy(s => s.SuiteId)
            .Select(s => new { SuiteId = s.SuiteId, Count = SqlFunc.AggregateCount(s.Id) })
            .ToListAsync();

        var result = list.Select(suite => new
        {
            suite.Id, suite.Name, suite.Description,
            suite.CreatedAt, suite.UpdatedAt,
            ScenarioCount = scenarioCounts
                .FirstOrDefault(c => c.SuiteId == suite.Id)?.Count ?? 0
        });

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var db   = _db.CreateClient();
        var item = await db.Queryable<TestSuite>().FirstAsync(s => s.Id == id);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TestSuite suite)
    {
        suite.Id        = Guid.NewGuid();
        suite.CreatedAt = suite.UpdatedAt = DateTime.UtcNow;
        var db = _db.CreateClient();
        await db.Insertable(suite).ExecuteCommandAsync();
        return Ok(suite);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] TestSuite suite)
    {
        suite.Id        = id;
        suite.UpdatedAt = DateTime.UtcNow;
        var db = _db.CreateClient();
        await db.Updateable(suite).ExecuteCommandAsync();
        return Ok(suite);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var db = _db.CreateClient();
        // 套件下的场景改为未归属
        await db.Updateable<Scenario>()
            .SetColumns(s => new Scenario { SuiteId = null })
            .Where(s => s.SuiteId == id)
            .ExecuteCommandAsync();
        await db.Deleteable<TestSuite>().Where(s => s.Id == id).ExecuteCommandAsync();
        return Ok();
    }

    /// <summary>获取某个套件下的所有场景</summary>
    [HttpGet("{id:guid}/scenarios")]
    public async Task<IActionResult> GetScenarios(Guid id)
    {
        var db   = _db.CreateClient();
        var list = await db.Queryable<Scenario>()
            .Where(s => s.SuiteId == id)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
        return Ok(list);
    }
}
