using Microsoft.AspNetCore.Mvc;
using TestPlatform.Core.DB;
using TestPlatform.Core.Entities;

namespace TestPlatform.API.Controllers;

[ApiController]
[Route("api/scenarios")]
public class ScenarioController : ControllerBase
{
    private readonly AppDbContext _db;
    public ScenarioController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var db = _db.CreateClient();
        var list = await db.Queryable<Scenario>().OrderByDescending(s => s.CreatedAt).ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var db = _db.CreateClient();
        var item = await db.Queryable<Scenario>().FirstAsync(s => s.Id == id);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Scenario scenario)
    {
        scenario.Id = Guid.NewGuid();
        scenario.CreatedAt = scenario.UpdatedAt = DateTime.UtcNow;
        var db = _db.CreateClient();
        await db.Insertable(scenario).ExecuteCommandAsync();
        return Ok(scenario);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] Scenario scenario)
    {
        scenario.Id = id;
        scenario.UpdatedAt = DateTime.UtcNow;

        // CreatedAt 不允许被覆盖；未提交 StepsJson 时保留数据库中已有的录制步骤
        var ignore = new List<string> { nameof(Scenario.CreatedAt) };
        if (scenario.StepsJson == null)
            ignore.Add(nameof(Scenario.StepsJson));

        var db = _db.CreateClient();
        await db.Updateable(scenario).IgnoreColumns(ignore.ToArray()).ExecuteCommandAsync();
        return Ok(scenario);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var db = _db.CreateClient();
        await db.Deleteable<Scenario>().Where(s => s.Id == id).ExecuteCommandAsync();
        return Ok();
    }

    /// <summary>
    /// 一键导入默认测试场景（首次使用时调用）
    /// </summary>
    [HttpPost("seed")]
    public async Task<IActionResult> Seed()
    {
        var db = _db.CreateClient();
        var count = await db.Queryable<Scenario>().CountAsync();
        if (count > 0)
            return Ok(new { message = $"已有 {count} 个场景，跳过初始化" });

        var scenarios = GetDefaultScenarios();
        await db.Insertable(scenarios).ExecuteCommandAsync();
        return Ok(new { message = $"成功导入 {scenarios.Count} 个默认场景", count = scenarios.Count });
    }

    private static List<Scenario> GetDefaultScenarios() => new()
    {
        new Scenario
        {
            Id          = Guid.NewGuid(),
            Name        = "基本传票发行",
            WindowTitle = "SmartZaiko",
            Description = """
                在伝票作成画面执行以下操作：

                【确认头部信息】
                1. read_value("voucher_branch") 确认ヤード已选择
                2. read_value("voucher_voucherType") 确认伝票区分已选择
                3. read_value("voucher_transportType") 确认取引形態已选择

                【填写車番】
                4. set_text("voucher_vehicleNumber", "{{车牌号}}") 然后 press_key("Enter")
                5. check_dialog 处理弹窗

                【填写明细第一行】
                6. get_row_count 检查行数，为0则 click("btn_add") → check_dialog
                7. click_cell(row=1, column="preWeight") → set_text("detail_1_preWeight", "{{総重}}") → press_key("Tab") → check_dialog
                8. click_cell(row=1, column="afterWeight") → set_text("detail_1_afterWeight", "{{空車重}}") → press_key("Tab") → check_dialog
                9. click_cell(row=1, column="product") → 弹出商品选择窗口后点击第一个商品行
                10. check_dialog → 若弹出単価未設定提示，click("dialog_btn_yes")

                【验证正味】
                11. scan_ui 确认正味 = {{正味}}

                【发行传票】
                12. click("btn_voucherIssue") → check_dialog → click("dialog_btn_yes")
                13. 在発行確認画面 click("btn_issue") → check_dialog 确认成功
                """,
            ParametersJson  = """[{"name":"车牌号","label":"車番","defaultValue":"12-34"},{"name":"総重","label":"総重(kg)","defaultValue":"5000"},{"name":"空車重","label":"空車重(kg)","defaultValue":"2000"},{"name":"正味","label":"正味(kg)","defaultValue":"3000"}]""",
            AssertionsJson  = """["正味应为 {{正味}}","传票发行应成功"]""",
            MaxSteps        = 100,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow
        },
        new Scenario
        {
            Id          = Guid.NewGuid(),
            Name        = "传票保留",
            WindowTitle = "SmartZaiko",
            Description = """
                测试传票保留功能：
                1. 确认基本信息已填写（ヤード、伝票区分等）
                2. set_text("voucher_vehicleNumber", "{{车牌号}}") → press_key("Enter") → check_dialog
                3. get_row_count，为0则 click("btn_add")
                4. click_cell(row=1, column="preWeight") → set_text("detail_1_preWeight", "{{総重}}") → press_key("Tab")
                5. click("btn_pending") または 伝票保留ボタンを探してクリック
                6. check_dialog → click("dialog_btn_yes") 确认保留
                7. 确认保留成功
                """,
            ParametersJson  = """[{"name":"车牌号","label":"車番","defaultValue":"12-34"},{"name":"総重","label":"総重(kg)","defaultValue":"8000"}]""",
            AssertionsJson  = """["传票保留应成功"]""",
            MaxSteps        = 40,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow
        },
        new Scenario
        {
            Id          = Guid.NewGuid(),
            Name        = "新建明细行",
            WindowTitle = "SmartZaiko",
            Description = """
                测试新建明细行功能：
                1. get_row_count 记录初始行数
                2. click("btn_add") → check_dialog
                3. get_row_count 确认行数+1
                4. click_cell(row=1, column="product") → 选择商品 → check_dialog
                5. click("btn_add") 再新增一行 → check_dialog
                6. get_row_count 确认现在有至少2行
                """,
            ParametersJson  = """[]""",
            AssertionsJson  = """["明细表格应有至少2行数据"]""",
            MaxSteps        = 30,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow
        },
        new Scenario
        {
            Id          = Guid.NewGuid(),
            Type        = "web",
            Name        = "Web示例：Demo商城下单",
            WindowTitle = "http://localhost:3000",   // web 场景：此字段存起始 URL
            Description = """
                这是一个下单网页（samples/web-demo，需先本地起在 http://localhost:3000）。请完成一次成功下单：
                1. browser_scan 了解页面元素
                2. browser_fill("#username", "{{用户名}}") 填用户名
                3. browser_fill("#password", "{{密码}}") 填密码
                4. browser_select("#product", "苹果") 选择商品
                5. browser_fill("#qty", "{{数量}}") 填数量
                6. browser_click("#agree") 勾选同意条款
                7. browser_click_text("提交订单") 提交
                8. assert_text("#result", "下单成功") 确认成功横幅出现
                9. done(success=true) 报告结果
                """,
            ParametersJson  = """[{"name":"用户名","label":"用户名","defaultValue":"alice"},{"name":"密码","label":"密码","defaultValue":"123456"},{"name":"数量","label":"数量","defaultValue":"2"}]""",
            AssertionsJson  = """["页面出现「下单成功」","无错误提示"]""",
            MaxSteps        = 40,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow
        }
    };
}
