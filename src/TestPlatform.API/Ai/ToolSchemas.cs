namespace TestPlatform.API.Ai;

/// <summary>DeepSeek tool-calling 的工具定义（WPF 自动化）</summary>
public static class ToolSchemas
{
    public static readonly List<object> All = new()
    {
        Tool("scan_ui", "扫描当前窗口的所有可交互控件，返回控件树。仅在首次/界面未知/操作失败时使用（消耗较多 token）。"),
        Tool("click", "点击元素", new { element_id = Str("元素的 AutomationId") }),
        Tool("set_text", "在输入框中输入文本", new { element_id = Str("AutomationId"), text = Str("要输入的文本") }),
        Tool("select_item", "从下拉框选择选项", new { element_id = Str("AutomationId"), item_text = Str("选项文本") }),
        Tool("read_value", "读取控件当前值", new { element_id = Str("AutomationId") }),
        Tool("press_key", "按特殊键", new { key = Str("Enter/Tab/Escape/Backspace/Delete/F1~F12") }),
        Tool("wait", "等待若干毫秒", new { milliseconds = Int("毫秒数") }),
        Tool("get_row_count", "获取明细表格当前行数。判断是否需要新增行前必须先调用。"),
        Tool("click_cell", "点击表格单元格进入编辑模式",
            new { row = Int("行号，从1开始"), column = Str("列关键字: preWeight/afterWeight/product/memo/grossWeight/unitPrice") }),
        Tool("set_unit_price", "填写指定行単価（自动点单元格+写值+Tab）",
            new { row = Int("行号"), unit_price = Str("単価数值") }),
        Tool("select_product", "在已打开的商品选择弹窗中选商品",
            new { product_code = Str("商品代码，为空选第一个") }),
        Tool("check_dialog", "检测当前是否有应用内弹窗，返回消息与可用按钮。点击后如有弹窗须先处理。"),
        Tool("assert_text", "断言控件文本包含指定内容",
            new { element_id = Str("AutomationId"), expected_text = Str("期望包含的文本") }),
        Tool("done", "测试完成，报告结果",
            new { success = Bool("是否通过"), summary = Str("结果总结") }),
    };

    private static object Str(string desc)  => new { type = "string",  description = desc };
    private static object Int(string desc)  => new { type = "integer", description = desc };
    private static object Bool(string desc) => new { type = "boolean", description = desc };

    private static object Tool(string name, string description, object? parameters = null)
    {
        var props = new Dictionary<string, object>();
        var required = new List<string>();
        if (parameters != null)
        {
            foreach (var p in parameters.GetType().GetProperties())
            {
                props[p.Name] = p.GetValue(parameters)!;
                required.Add(p.Name);
            }
        }
        return new
        {
            type = "function",
            function = new
            {
                name,
                description,
                parameters = new { type = "object", properties = props, required }
            }
        };
    }
}
