namespace TestPlatform.API.Execution;

/// <summary>
/// 结构化验证条件，回放结束后执行。Expected 支持 {{参数}} 占位符。
///
/// Op 取值（按用途分三组）：
///   读控件值（需 ElementId）：equals / contains / notEmpty
///   界面状态：exists（控件存在=跳转成功）/ notExists / textVisible（界面出现文本）/ textNotVisible
///   弹窗检查：noDialog（无任何弹窗）/ dialogNotContains（无含指定文本的弹窗，如"失败"）/ dialogContains
/// </summary>
public class Assertion
{
    /// <summary>控件 AutomationId（读值类、exists/notExists 用）</summary>
    public string ElementId { get; set; } = "";
    /// <summary>比较/检查方式</summary>
    public string Op { get; set; } = "equals";
    /// <summary>期望值 / 要匹配的文本（视 Op 而定）</summary>
    public string Expected { get; set; } = "";
    /// <summary>展示用备注</summary>
    public string Label { get; set; } = "";
}
