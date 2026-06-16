namespace TestPlatform.API.Ai;

/// <summary>DeepSeek tool-calling 的工具定义（Web 浏览器自动化）。工具名与前端「浏览器测试写法」说明一致。</summary>
public static class BrowserToolSchemas
{
    public static readonly List<object> All = new()
    {
        Tool("browser_connect", "确保浏览器已就绪。起始页面通常已自动打开，一般无需手动调用。"),
        Tool("browser_navigate", "跳转到指定网址", new { url = Str("目标 URL") }),
        Tool("browser_scan", "扫描当前页面的可交互元素，返回每个元素的 selector 与文字。界面未知或操作失败时使用。"),
        Tool("browser_click", "按 CSS selector 点击元素（如 #submit、button.login）",
            new { selector = Str("CSS 选择器") }),
        Tool("browser_click_text", "按可见文字点击元素（找不到 id 时用，如「提交订单」）",
            new { text = Str("元素上的可见文字") }),
        Tool("browser_fill", "在输入框中填写文本",
            new { selector = Str("CSS 选择器"), text = Str("要填写的文本") }),
        Tool("browser_select", "在下拉框中选择选项",
            new { selector = Str("CSS 选择器"), value = Str("选项的值或可见文本") }),
        Tool("browser_get_text", "读取元素的文本内容", new { selector = Str("CSS 选择器") }),
        Tool("browser_wait", "等待若干毫秒", new { milliseconds = Int("毫秒数") }),
        Tool("assert_text", "断言元素文本包含指定内容",
            new { selector = Str("CSS 选择器"), expected_text = Str("期望包含的文本") }),
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
