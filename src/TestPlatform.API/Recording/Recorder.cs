using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.AspNetCore.SignalR;
using TestPlatform.API.Hubs;
using TestPlatform.API.Wpf;

namespace TestPlatform.API.Recording;

public interface IRecorder
{
    bool IsRecording { get; }
    void Start(string windowTitle);
    List<RecordedStep> Stop();
    List<RecordedStep> GetSteps();
    void DeleteStep(int index);
    void Clear();
}

/// <summary>
/// 操作录制器。三个事件源汇成一条步骤流：
///   1. 鼠标钩子 → click / click_cell（按下时刻解析元素，不受点击后弹窗/跳转影响）
///   2. 键盘钩子 → press_key（Enter/Tab/Escape/F1~F12，仅被测进程在前台时）
///   3. UIAutomation 属性事件 → set_text / select_item（带防抖与噪声过滤）
/// </summary>
public class Recorder : IRecorder, IDisposable
{
    private readonly IHubContext<TestHub> _hub;
    private readonly ElementFinder _finder = new();
    private readonly HookHost _hooks = new();
    private readonly List<RecordedStep> _steps = new();
    private readonly object _lock = new();

    private bool     _isRecording;
    private DateTime _ignoreBefore;     // 录制开始后短暂忽略初始化事件
    private int      _targetPid;
    private AutomationPropertyChangedEventHandler? _propHandler;

    // 按下时刻的点击解析任务：点击按钮常立刻弹窗/跳转，必须用按下时的界面快照
    private (int X, int Y, Task<RecordedStep?> Resolve)? _pendingDown;

    public bool IsRecording => _isRecording;

    public Recorder(IHubContext<TestHub> hub)
    {
        _hub = hub;
        _hooks.LeftDown += OnLeftDown;
        _hooks.LeftUp   += OnLeftUp;
        _hooks.KeyDown  += OnKeyDown;
    }

    // ── 生命周期 ─────────────────────────────────────────────────

    public void Start(string windowTitle)
    {
        if (_isRecording) return;
        if (!_finder.Attach(windowTitle))
            throw new Exception($"未找到窗口: {windowTitle}");

        lock (_lock)
        {
            _steps.Clear();
            _pendingDown = null;
        }
        _targetPid    = _finder.Window!.Current.ProcessId;
        _ignoreBefore = DateTime.Now.AddSeconds(2);
        _isRecording  = true;

        _propHandler = OnPropertyChanged;
        Automation.AddAutomationPropertyChangedEventHandler(
            _finder.Window, TreeScope.Descendants, _propHandler,
            ValuePattern.ValueProperty,
            SelectionItemPattern.IsSelectedProperty);

        _hooks.Start();
        Console.WriteLine($"[Recorder] 开始录制 → {_finder.Window.Current.Name}");
    }

    public List<RecordedStep> Stop()
    {
        if (!_isRecording) return GetSteps();
        _isRecording = false;

        _hooks.Stop();
        if (_finder.Window != null && _propHandler != null)
        {
            try { Automation.RemoveAutomationPropertyChangedEventHandler(_finder.Window, _propHandler); }
            catch { }
        }

        Console.WriteLine($"[Recorder] 停止录制，共 {_steps.Count} 步");
        return GetSteps();
    }

    public List<RecordedStep> GetSteps()
    {
        lock (_lock) return _steps.ToList();
    }

    public void DeleteStep(int index)
    {
        lock (_lock)
        {
            var step = _steps.FirstOrDefault(s => s.Index == index);
            if (step != null) _steps.Remove(step);
        }
    }

    public void Clear()
    {
        lock (_lock) _steps.Clear();
    }

    public void Dispose()
    {
        Stop();
        _hooks.Dispose();
    }

    // ── 鼠标：按下解析 + 抬起确认 ────────────────────────────────

    private void OnLeftDown(int x, int y)
    {
        if (!_isRecording) return;
        // 同步登记（钩子回调里只做轻量操作），解析在后台进行
        var resolve = Task.Run(() => BuildClickStep(x, y));
        lock (_lock) _pendingDown = (x, y, resolve);
    }

    private void OnLeftUp(int x, int y)
    {
        if (!_isRecording) return;
        Task.Run(() =>
        {
            (int X, int Y, Task<RecordedStep?> Resolve)? down;
            lock (_lock) { down = _pendingDown; _pendingDown = null; }

            // 抬起点与按下点接近 → 一次点击，等待按下时刻的解析结果
            if (down != null && Math.Abs(down.Value.X - x) <= 8 && Math.Abs(down.Value.Y - y) <= 8)
            {
                try
                {
                    if (down.Value.Resolve.Wait(1500) && down.Value.Resolve.Result != null)
                    {
                        AddStep(down.Value.Resolve.Result);
                        return;
                    }
                }
                catch { /* 解析异常走退路 */ }
            }

            // 退路：按抬起位置再解析一次（拖动或按下解析失败）
            var step = BuildClickStep(x, y);
            if (step != null) AddStep(step);
        });
    }

    /// <summary>解析坐标处的元素，构造点击步骤；无法识别返回 null（原因打日志）</summary>
    private RecordedStep? BuildClickStep(int x, int y)
    {
        try
        {
            var el = _finder.FromPoint(x, y);   // 内含空容器层下钻
            if (el == null)
            {
                Console.WriteLine($"[Recorder] 点击({x},{y})：未命中元素，跳过");
                return null;
            }
            if (!_finder.IsInWindow(el))
            {
                Console.WriteLine($"[Recorder] 点击({x},{y})：不在目标窗口内（{el.Current.Name}），跳过");
                return null;
            }

            // 表格单元格优先（保留表格 id / 行号 / 列关键字，弹窗内表格也能回放）
            var cellStep = TryBuildCellStep(el, x, y);
            if (cellStep != null) return cellStep;

            var aid  = el.Current.AutomationId ?? "";
            var name = el.Current.Name ?? "";
            var ctrl = CtrlName(el);

            // 命中按钮内部文字/图标时提升到带 id 的祖先（btn_voucherIssue 等）
            if (string.IsNullOrEmpty(aid))
            {
                var lifted = ElementFinder.LiftToInteractive(el);
                if (lifted != null)
                {
                    el = lifted;
                    aid  = el.Current.AutomationId ?? "";
                    name = el.Current.Name ?? "";
                    ctrl = CtrlName(el);
                }
            }

            // 没有 id 的纯容器没有回放价值
            if (string.IsNullOrEmpty(aid) && ctrl is "Pane" or "Group" or "Window" or "ScrollViewer")
            {
                Console.WriteLine($"[Recorder] 点击({x},{y})：容器元素 {ctrl}「{name}」无 AutomationId，跳过");
                return null;
            }

            return new RecordedStep
            {
                Action      = "click",
                Target      = string.IsNullOrEmpty(aid) ? $"pos({x},{y})" : aid,
                TargetName  = string.IsNullOrEmpty(name) ? aid : name,
                ControlType = ctrl,
                X = x, Y = y
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Recorder] 点击({x},{y})：解析异常 {ex.Message}");
            return null;
        }
    }

    /// <summary>识别表格单元格点击：向上找 DataItem 行与所属表格，提取真实行号和列关键字</summary>
    private RecordedStep? TryBuildCellStep(AutomationElement el, int x, int y)
    {
        try
        {
            var walker = TreeWalker.RawViewWalker;

            // 1) 向上找 DataItem（行）
            AutomationElement? cell = null, rowEl = null;
            var cur = el;
            for (int i = 0; i < 6 && cur != null; i++)
            {
                if (cur.Current.ControlType == ControlType.DataItem) { rowEl = cur; break; }
                cell = cur;
                cur = walker.GetParent(cur);
            }
            if (rowEl == null) return null;

            // 2) 继续向上找表格本体，拿 AutomationId
            string gridId = "";
            var gridEl = walker.GetParent(rowEl);
            for (int i = 0; i < 4 && gridEl != null; i++)
            {
                var ct = gridEl.Current.ControlType;
                if (ct == ControlType.DataGrid || ct == ControlType.Table || ct == ControlType.List)
                {
                    gridId = gridEl.Current.AutomationId ?? "";
                    break;
                }
                gridEl = walker.GetParent(gridEl);
            }

            // 3) 真实行号 = 该行前面的 DataItem 兄弟数 + 1
            int rowIndex = 1;
            var sibling = walker.GetPreviousSibling(rowEl);
            int guard = 0;
            while (sibling != null && guard++ < 100)
            {
                if (sibling.Current.ControlType == ControlType.DataItem) rowIndex++;
                sibling = walker.GetPreviousSibling(sibling);
            }

            // 4) 列关键字：detail_{n}_{col} 类 id 直接取列名，否则用单元格文本
            var cellAid  = (cell ?? el).Current.AutomationId ?? "";
            var elAid    = el.Current.AutomationId ?? "";
            var column   = ExtractColumnKey(elAid) ?? ExtractColumnKey(cellAid)
                           ?? (cell ?? el).Current.Name ?? "";

            return new RecordedStep
            {
                Action      = "click_cell",
                Target      = string.IsNullOrEmpty(elAid) ? cellAid : elAid,
                TargetName  = column,
                Value       = rowIndex.ToString(),
                ControlType = "DataItem",
                GridId      = gridId,
                X = x, Y = y
            };
        }
        catch { return null; }
    }

    /// <summary>从 "detail_1_preWeight" 提取列关键字 "preWeight"</summary>
    private static string? ExtractColumnKey(string aid)
    {
        if (string.IsNullOrEmpty(aid)) return null;
        var parts = aid.Split('_');
        if (parts.Length >= 3 && int.TryParse(parts[1], out _))
            return string.Join("_", parts.Skip(2));
        return null;
    }

    // ── 键盘：白名单按键 ─────────────────────────────────────────

    // 被测系统的按钮都绑定了功能键（行追加F1、伝票発行F10 等），F1~F12 必须录
    private static readonly Dictionary<uint, string> RecordableKeys = new()
    {
        { 0x0D, "Enter" }, { 0x09, "Tab" }, { 0x1B, "Escape" },
        { 0x70, "F1" }, { 0x71, "F2" }, { 0x72, "F3" },  { 0x73, "F4" },
        { 0x74, "F5" }, { 0x75, "F6" }, { 0x76, "F7" },  { 0x77, "F8" },
        { 0x78, "F9" }, { 0x79, "F10" }, { 0x7A, "F11" }, { 0x7B, "F12" }
    };

    private void OnKeyDown(uint vkCode)
    {
        if (!_isRecording || DateTime.Now < _ignoreBefore) return;
        if (!RecordableKeys.TryGetValue(vkCode, out var keyName)) return;

        Task.Run(() =>
        {
            try
            {
                // 仅当被测应用（含其弹窗）在前台时记录
                GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
                if (pid != (uint)_targetPid) return;

                AddStep(new RecordedStep
                {
                    Action = "press_key", Target = keyName, TargetName = keyName, Value = keyName
                });
            }
            catch { }
        });
    }

    // ── UIAutomation 属性事件：文本输入与选项选择 ─────────────────

    private void OnPropertyChanged(object sender, AutomationPropertyChangedEventArgs e)
    {
        if (!_isRecording || DateTime.Now < _ignoreBefore) return;
        try
        {
            var el   = (AutomationElement)sender;
            var aid  = el.Current.AutomationId ?? "";
            var name = el.Current.Name ?? "";
            var ctrl = CtrlName(el);

            if (IsNoise(aid, name, ctrl, el)) return;

            if (e.Property == ValuePattern.ValueProperty)
            {
                var newVal = e.NewValue?.ToString() ?? "";
                if (string.IsNullOrEmpty(newVal)) return;

                var target  = string.IsNullOrEmpty(aid) ? name : aid;
                var display = CleanName(string.IsNullOrEmpty(name) ? aid : name);
                var action  = ctrl == "ComboBox" ? "select_item" : "set_text";

                // 防抖：同一元素连续变化只保留最后一次
                lock (_lock)
                {
                    var last = _steps.LastOrDefault();
                    if (last != null && last.Target == target && last.Action == action)
                    {
                        last.Value = newVal;
                        PushStep(last);
                        return;
                    }
                }
                AddStep(new RecordedStep
                {
                    Action = action, Target = target, TargetName = display,
                    Value = newVal, ControlType = ctrl
                });
            }
            else if (e.Property == SelectionItemPattern.IsSelectedProperty && (bool)(e.NewValue ?? false))
            {
                // ComboBox 内的选项选择由 ComboBox 自身的 ValueProperty 事件捕获（target=控件 id，可回放）。
                // 这里 sender 是被选中的选项（id 为空、name=选项文本），若不过滤会产生
                // target=选项文本 的冗余步骤，回放时按"选项文本"找控件必然失败（如截图 #20/#22）。
                if (IsInsideComboBox(el)) return;

                // 没有 AutomationId 的选项无法回放定位，跳过
                if (string.IsNullOrEmpty(aid)) return;

                AddStep(new RecordedStep
                {
                    Action = "select_item", Target = aid,
                    TargetName = CleanName(string.IsNullOrEmpty(name) ? aid : name),
                    Value = name, ControlType = ctrl
                });
            }
        }
        catch { }
    }

    /// <summary>过滤无法回放或没有意义的属性事件</summary>
    private static bool IsNoise(string aid, string name, string ctrl, AutomationElement el)
    {
        // 只读控件（合計、正味等自动计算字段）
        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp)
                && ((ValuePattern)vp).Current.IsReadOnly) return true;
        }
        catch { return true; }

        // 类名/绑定路径式的名称（BridgeVision.xxx.Model、項目: ... 等）
        if (IsClassName(name) || IsClassName(aid)) return true;
        if (name.Contains("インデックス") || name.Contains("項目:") || name.Contains("BridgeVision")) return true;

        // 既无 id 又无名称，回放时无法定位
        if (string.IsNullOrEmpty(aid) && string.IsNullOrEmpty(name)) return true;

        // 控件模板内部件
        if (aid is "PART_TextBox" or "PART_Popup" or "PART_ScrollViewer") return true;

        // 表格内无 id 的 Edit（自动计算格）
        if (string.IsNullOrEmpty(aid) && ctrl == "Edit") return true;

        return false;
    }

    /// <summary>元素是否位于 ComboBox 内（向上最多找 4 层）</summary>
    private static bool IsInsideComboBox(AutomationElement el)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var cur = walker.GetParent(el);
            for (int i = 0; i < 4 && cur != null; i++)
            {
                if (cur.Current.ControlType == ControlType.ComboBox) return true;
                cur = walker.GetParent(cur);
            }
        }
        catch { }
        return false;
    }

    private static bool IsClassName(string s) =>
        !string.IsNullOrEmpty(s) && s.Contains('.') && s.Contains("Model");

    private static string CleanName(string name)
    {
        if (!IsClassName(name)) return name;
        return name.Split('.').Last().Replace("Model", "").Replace("BM", "");
    }

    private static string CtrlName(AutomationElement el) =>
        el.Current.ControlType.ProgrammaticName.Replace("ControlType.", "");

    // ── 步骤入列 + SignalR 推送 ──────────────────────────────────

    private void AddStep(RecordedStep step)
    {
        lock (_lock)
        {
            step.Index = _steps.Count;
            step.Time  = DateTime.Now.ToString("HH:mm:ss");
            _steps.Add(step);
        }
        PushStep(step);
        Console.WriteLine($"[Recorder] #{step.Index} {step.Description}");
    }

    private void PushStep(RecordedStep step)
    {
        _ = _hub.Clients.Group("recording").SendAsync("RecordedStep", step);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
}
