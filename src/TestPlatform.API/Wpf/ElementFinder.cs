using System.Windows.Automation;

namespace TestPlatform.API.Wpf;

/// <summary>
/// 元素定位：窗口连接、按 AutomationId 查找（内建等待）、坐标命中（含空容器层下钻与祖先提升）。
/// 这是录制与回放共用的定位基础设施。
/// </summary>
public class ElementFinder
{
    public AutomationElement? Window { get; private set; }

    public bool IsAttached => Window != null;

    /// <summary>按标题（部分匹配）连接顶层窗口</summary>
    public bool Attach(string windowTitle)
    {
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));

        foreach (AutomationElement w in windows)
        {
            if (w.Current.Name.Contains(windowTitle, StringComparison.OrdinalIgnoreCase))
            {
                Window = w;
                Console.WriteLine($"[Wpf] 已连接窗口: {w.Current.Name}");
                return true;
            }
        }
        Console.WriteLine($"[Wpf] 未找到窗口: {windowTitle}");
        return false;
    }

    /// <summary>
    /// 按 AutomationId 查找元素。timeoutMs > 0 时轮询等待元素出现，
    /// 替代以前散落各处的固定 Sleep。
    /// </summary>
    public AutomationElement? ById(string automationId, int timeoutMs = 0)
    {
        if (Window == null || string.IsNullOrEmpty(automationId)) return null;

        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            try
            {
                var el = Window.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
                if (el != null) return el;
            }
            catch { /* 窗口切换中，重试 */ }

            if (Environment.TickCount64 >= deadline) return null;
            Thread.Sleep(150);
        }
    }

    /// <summary>
    /// 坐标命中元素。处理两个 WPF 实际问题：
    /// 1. FromPoint 可能只返回覆盖全窗口的空 Window/Pane 层（自定义窗体/悬浮层），需要按坐标手动下钻；
    /// 2. 命中的常是按钮内部的文字/图标，需要向上提升到带 AutomationId 的交互祖先。
    /// </summary>
    public AutomationElement? FromPoint(int x, int y)
    {
        try
        {
            var el = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (el == null) return null;

            var cur = el.Current;
            bool emptyContainer = string.IsNullOrEmpty(cur.AutomationId)
                                  && string.IsNullOrEmpty(cur.Name)
                                  && (cur.ControlType == ControlType.Window || cur.ControlType == ControlType.Pane);
            if (emptyContainer && Window != null)
            {
                var deeper = DescendToPoint(el, x, y) ?? DescendToPoint(Window, x, y);
                if (deeper != null) return deeper;
            }
            return el;
        }
        catch { return null; }
    }

    /// <summary>元素是否在已连接窗口的（原始视图）树内</summary>
    public bool IsInWindow(AutomationElement el)
    {
        if (Window == null) return false;
        try
        {
            var walker = TreeWalker.RawViewWalker;
            var cur = el;
            int guard = 0;
            while (cur != null && guard++ < 50)
            {
                if (cur.Equals(Window)) return true;
                cur = walker.GetParent(cur);
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// 从容器按坐标逐层下钻：每层挑“包含该点、面积最小”的子元素，钻到最深的真实控件。
    /// 跳过空 Window 子层（悬浮层壳），避免再次掉进覆盖层。
    /// </summary>
    public static AutomationElement? DescendToPoint(AutomationElement root, int x, int y)
    {
        try
        {
            var point  = new System.Windows.Point(x, y);
            var walker = TreeWalker.ControlViewWalker;
            var cur    = root;
            bool moved = false;

            for (int depth = 0; depth < 25; depth++)
            {
                AutomationElement? best = null;
                double bestArea = double.MaxValue;

                var child = walker.GetFirstChild(cur);
                while (child != null)
                {
                    try
                    {
                        var cc = child.Current;
                        bool isOverlayShell = cc.ControlType == ControlType.Window
                                              && string.IsNullOrEmpty(cc.Name)
                                              && string.IsNullOrEmpty(cc.AutomationId);
                        var r = cc.BoundingRectangle;
                        if (!isOverlayShell && !r.IsEmpty && r.Contains(point))
                        {
                            var area = r.Width * r.Height;
                            if (area < bestArea) { bestArea = area; best = child; }
                        }
                    }
                    catch { /* 个别子元素读属性失败，跳过 */ }
                    child = walker.GetNextSibling(child);
                }

                if (best == null) break;
                cur   = best;
                moved = true;
            }
            return moved ? cur : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// 向上提升到最近带 AutomationId 的交互祖先（最多 4 层）。
    /// 点击按钮时命中的常是内部 TextBlock，提升后才能拿到 btn_xxx 这类 id。
    /// 无 id 的 Button 也返回（至少能记下按钮名而非裸坐标）。
    /// </summary>
    public static AutomationElement? LiftToInteractive(AutomationElement el)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var cur = walker.GetParent(el);
            for (int i = 0; i < 4 && cur != null; i++)
            {
                var c = cur.Current;
                if (c.ControlType == ControlType.Window) return null;
                if (!string.IsNullOrEmpty(c.AutomationId)) return cur;
                if (c.ControlType == ControlType.Button) return cur;
                cur = walker.GetParent(cur);
            }
        }
        catch { }
        return null;
    }

    // ── 界面扫描（AI 模式用）────────────────────────────────────

    private readonly Dictionary<string, AutomationElement> _scanCache = new();
    private int _scanCounter;

    /// <summary>扫描窗口控件树，返回可交互/有意义的控件列表</summary>
    public List<ElementInfo> Scan(int maxDepth = 8)
    {
        if (Window == null) throw new InvalidOperationException("未连接到目标窗口");
        _scanCache.Clear();
        _scanCounter = 0;
        var results = new List<ElementInfo>();
        ScanElement(Window, results, 0, maxDepth);
        return results;
    }

    /// <summary>解析元素引用：扫描编号（e_N）或 AutomationId</summary>
    public AutomationElement? Resolve(string elementId)
    {
        if (_scanCache.TryGetValue(elementId, out var cached)) return cached;
        return ById(elementId);
    }

    private void ScanElement(AutomationElement element, List<ElementInfo> results, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        try
        {
            var c = element.Current;
            if (c.IsOffscreen && depth > 1) return;

            var info = new ElementInfo
            {
                Id           = $"e_{_scanCounter++}",
                ControlType  = c.ControlType.ProgrammaticName.Replace("ControlType.", ""),
                Text         = ReadText(element),
                AutomationId = c.AutomationId ?? "",
                Name         = c.Name ?? "",
                IsEnabled    = c.IsEnabled,
                Depth        = depth,
                Bounds = new ElementRect
                {
                    X = (int)c.BoundingRectangle.X,     Y = (int)c.BoundingRectangle.Y,
                    Width = (int)c.BoundingRectangle.Width, Height = (int)c.BoundingRectangle.Height
                }
            };

            _scanCache[info.Id] = element;
            if (IsMeaningful(info)) results.Add(info);

            var children = element.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement child in children)
                ScanElement(child, results, depth + 1, maxDepth);
        }
        catch (ElementNotAvailableException) { }
    }

    public static string ReadText(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
            {
                var val = ((ValuePattern)vp).Current.Value;
                if (!string.IsNullOrEmpty(val)) return val;
            }
            if (!string.IsNullOrEmpty(element.Current.Name)) return element.Current.Name;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var tp))
                return ((TextPattern)tp).DocumentRange.GetText(200);
        }
        catch { }
        return "";
    }

    private static readonly HashSet<string> InteractiveTypes = new()
    {
        "Button", "Edit", "ComboBox", "CheckBox", "RadioButton",
        "ListItem", "MenuItem", "TabItem", "DataItem", "TreeItem",
        "Slider", "Spinner", "DatePicker", "Calendar", "Hyperlink"
    };

    private static bool IsMeaningful(ElementInfo info)
    {
        if (InteractiveTypes.Contains(info.ControlType)) return true;
        if (info.ControlType == "Text" && !string.IsNullOrEmpty(info.Text)) return true;
        if (info.ControlType is "DataGrid" or "Table" or "List") return true;
        return false;
    }
}
