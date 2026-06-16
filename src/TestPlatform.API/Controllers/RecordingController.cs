using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using TestPlatform.API.Recording;
using TestPlatform.Core.DB;
using TestPlatform.Core.Entities;

namespace TestPlatform.API.Controllers;

[ApiController]
[Route("api/recording")]
public class RecordingController : ControllerBase
{
    private readonly IRecorder _recorder;
    private readonly AppDbContext _db;

    public RecordingController(IRecorder recorder, AppDbContext db)
    {
        _recorder = recorder;
        _db = db;
    }

    [HttpPost("start")]
    public IActionResult Start([FromBody] StartRecordingRequest req)
    {
        try
        {
            _recorder.Start(req.WindowTitle ?? "SmartZaiko");
            return Ok(new { message = "录制已开始", windowTitle = req.WindowTitle });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("stop")]
    public IActionResult Stop()
    {
        var steps = _recorder.Stop();
        return Ok(new { steps, count = steps.Count });
    }

    [HttpGet("steps")]
    public IActionResult GetSteps()
        => Ok(new { steps = _recorder.GetSteps(), isRecording = _recorder.IsRecording });

    [HttpDelete("steps/{index:int}")]
    public IActionResult DeleteStep(int index)
    {
        _recorder.DeleteStep(index);
        return Ok();
    }

    [HttpPost("clear")]
    public IActionResult Clear()
    {
        _recorder.Clear();
        return Ok();
    }

    /// <summary>将录制结果保存为测试场景</summary>
    [HttpPost("save")]
    public async Task<IActionResult> SaveAsScenario([FromBody] SaveScenarioRequest req)
    {
        // 优先用前端编辑后的步骤（删除/排序/改值/手动插入），否则用服务端原始录制
        var steps = req.Steps is { Count: > 0 } ? req.Steps : _recorder.GetSteps();
        if (steps.Count == 0)
            return BadRequest(new { error = "没有录制到任何步骤" });

        steps = Dedup(steps);
        for (int i = 0; i < steps.Count; i++) steps[i].Index = i;

        var description = string.Join("\n", steps.Select((s, i) => $"{i + 1}. {s.Description}"));

        var stepsJson = JsonSerializer.Serialize(steps.Select(s => new
        {
            action      = s.Action,
            target      = s.Target,
            targetName  = s.TargetName,
            value       = s.Value,
            controlType = s.ControlType,
            gridId      = s.GridId,
            x = s.X, y = s.Y
        }));

        var paramSuggestions = steps
            .Where(s => s.Action == "set_text" && !string.IsNullOrEmpty(s.Value))
            .Select(s => new { name = SuggestParamName(s.TargetName), label = s.TargetName, defaultValue = s.Value })
            .DistinctBy(p => p.name)
            .ToList();

        var scenario = new Scenario
        {
            Id             = Guid.NewGuid(),
            Name           = req.Name ?? $"录制场景_{DateTime.Now:MMdd_HHmm}",
            WindowTitle    = req.WindowTitle ?? "SmartZaiko",
            Description    = description,
            StepsJson      = stepsJson,
            ParametersJson = JsonSerializer.Serialize(paramSuggestions),
            AssertionsJson = "[]",
            MaxSteps       = 100,
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow
        };

        var db = _db.CreateClient();
        await db.Insertable(scenario).ExecuteCommandAsync();
        return Ok(new { scenario, message = $"场景「{scenario.Name}」保存成功，共 {steps.Count} 步" });
    }

    /// <summary>用当前录制（或前端编辑后的步骤）覆盖已有场景的录制内容（重新录制整个场景）</summary>
    [HttpPost("save-to/{scenarioId:guid}")]
    public async Task<IActionResult> SaveToScenario(Guid scenarioId, [FromBody] SaveScenarioRequest req)
    {
        var steps = req.Steps is { Count: > 0 } ? req.Steps : _recorder.GetSteps();
        if (steps.Count == 0)
            return BadRequest(new { error = "没有录制到任何步骤" });

        var db = _db.CreateClient();
        var scenario = await db.Queryable<Scenario>().FirstAsync(s => s.Id == scenarioId);
        if (scenario == null) return NotFound(new { error = "场景不存在" });

        steps = Dedup(steps);
        for (int i = 0; i < steps.Count; i++) steps[i].Index = i;

        // 覆盖步骤、自然语言描述、参数建议；保留名称/窗口/断言/AI验证等用户配置
        scenario.StepsJson      = JsonSerializer.Serialize(steps.Select(s => new
        {
            action = s.Action, target = s.Target, targetName = s.TargetName,
            value = s.Value, controlType = s.ControlType, gridId = s.GridId, x = s.X, y = s.Y
        }));
        scenario.Description    = string.Join("\n", steps.Select((s, i) => $"{i + 1}. {s.Description}"));
        scenario.ParametersJson = JsonSerializer.Serialize(steps
            .Where(s => s.Action == "set_text" && !string.IsNullOrEmpty(s.Value))
            .Select(s => new { name = SuggestParamName(s.TargetName), label = s.TargetName, defaultValue = s.Value })
            .DistinctBy(p => p.name));
        scenario.UpdatedAt      = DateTime.UtcNow;

        await db.Updateable(scenario)
            .UpdateColumns(s => new { s.StepsJson, s.Description, s.ParametersJson, s.UpdatedAt })
            .ExecuteCommandAsync();

        return Ok(new { scenario, message = $"场景「{scenario.Name}」已重新录制，共 {steps.Count} 步" });
    }

    /// <summary>
    /// 仅合并相邻的同目标输入类步骤（录制防抖兜底），保留故意的重复点击；
    /// 点击列表行后紧随的同目标 select_item 事件视为重复，丢弃。
    /// </summary>
    private static List<RecordedStep> Dedup(List<RecordedStep> steps)
    {
        var result = new List<RecordedStep>();
        foreach (var s in steps)
        {
            var last = result.LastOrDefault();
            if (last != null && last.Action == s.Action && last.Target == s.Target
                && s.Action is "set_text" or "select_item")
            {
                last.Value = s.Value;
                continue;
            }
            if (last is { Action: "click" } && s.Action == "select_item" && last.Target == s.Target)
                continue;
            result.Add(s);
        }
        return result;
    }

    private static string SuggestParamName(string label)
        => label.Replace(" ", "_").Replace("「", "").Replace("」", "").ToLower();
}

public class StartRecordingRequest
{
    public string? WindowTitle { get; set; }
}

public class SaveScenarioRequest
{
    public string? Name        { get; set; }
    public string? WindowTitle { get; set; }
    /// <summary>前端编辑后的步骤列表（为空则用服务端录制的原始步骤）</summary>
    public List<RecordedStep>? Steps { get; set; }
}
