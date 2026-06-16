namespace TestPlatform.API.Recording;

/// <summary>
/// 一条录制/回放步骤。JSON 字段名与历史数据兼容（action/target/targetName/value/...）。
/// click_cell 约定：Value=行号、TargetName=列关键字、GridId=所属表格（为空回退默认表格）。
/// </summary>
public class RecordedStep
{
    public int    Index       { get; set; }
    /// <summary>click / click_cell / set_text / select_item / press_key / wait</summary>
    public string Action      { get; set; } = "";
    /// <summary>AutomationId（或 press_key 的键名）</summary>
    public string Target      { get; set; } = "";
    /// <summary>可读名称（click_cell 时为列关键字）</summary>
    public string TargetName  { get; set; } = "";
    /// <summary>输入值 / 选项值 / 行号 / 毫秒数</summary>
    public string Value       { get; set; } = "";
    public string ControlType { get; set; } = "";
    /// <summary>click_cell 所属表格的 AutomationId（弹窗内表格回放必需）</summary>
    public string GridId      { get; set; } = "";
    public int    X           { get; set; }
    public int    Y           { get; set; }
    public string Time        { get; set; } = DateTime.Now.ToString("HH:mm:ss");

    /// <summary>人可读描述（前端展示用，随 JSON 序列化输出）</summary>
    public string Description => Action switch
    {
        "click"       => $"点击「{TargetName}」",
        "click_cell"  => $"点击表格第{Value}行「{TargetName}」列",
        "set_text"    => $"在「{TargetName}」输入「{Value}」",
        "select_item" => $"在「{TargetName}」选择「{Value}」",
        "press_key"   => $"按键 {Value}",
        "wait"        => $"等待 {Value}ms",
        _             => $"{Action} → {Target}"
    };
}
