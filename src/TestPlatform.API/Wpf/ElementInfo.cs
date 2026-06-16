namespace TestPlatform.API.Wpf;

/// <summary>扫描得到的控件信息（供 AI 模式阅读界面）</summary>
public class ElementInfo
{
    public string Id           { get; set; } = "";
    public string ControlType  { get; set; } = "";
    public string Text         { get; set; } = "";
    public string AutomationId { get; set; } = "";
    public string Name         { get; set; } = "";
    public bool   IsEnabled    { get; set; }
    public int    Depth        { get; set; }
    public ElementRect Bounds  { get; set; } = new();

    public string ToDescription()
    {
        var parts = new List<string> { $"[{Id}]", $"Type={ControlType}" };
        if (!string.IsNullOrEmpty(Text))         parts.Add($"Text=\"{Text}\"");
        if (!string.IsNullOrEmpty(Name) && Name != Text) parts.Add($"Name=\"{Name}\"");
        if (!string.IsNullOrEmpty(AutomationId)) parts.Add($"AutomationId=\"{AutomationId}\"");
        parts.Add($"Pos=({Bounds.X},{Bounds.Y},{Bounds.Width},{Bounds.Height})");
        if (!IsEnabled) parts.Add("Disabled");
        return new string(' ', Depth * 2) + string.Join(" | ", parts);
    }
}

public class ElementRect
{
    public int X      { get; set; }
    public int Y      { get; set; }
    public int Width  { get; set; }
    public int Height { get; set; }
}
