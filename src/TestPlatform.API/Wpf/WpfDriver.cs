using System.Windows.Automation;

namespace TestPlatform.API.Wpf;

/// <summary>检测到的应用内弹窗</summary>
public record DialogInfo(string Message, List<string> Buttons)
{
    public override string ToString() =>
        $"[弹窗] 消息: \"{Message}\" | 可用按钮: {string.Join(", ", Buttons)}";
}

/// <summary>
/// WPF 操作驱动：录制回放与 AI 模式共用的全部界面操作。
/// 元素查找默认带 1.5 秒等待，操作结果用 OpResult 类型表达成败。
/// </summary>
public class WpfDriver
{
    private const int DefaultWaitMs = 1500;

    public ElementFinder Finder { get; } = new();

    public bool Attach(string windowTitle) => Finder.Attach(windowTitle);

    // ── 基本操作 ─────────────────────────────────────────────────

    public OpResult Click(string elementId)
    {
        var el = Finder.Resolve(elementId) ?? Finder.ById(elementId, DefaultWaitMs);
        if (el == null) return OpResult.Fail($"未找到元素 {elementId}");

        try
        {
            if (el.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
            {
                ((InvokePattern)invoke).Invoke();
                return OpResult.Ok($"点击了 {elementId}");
            }
            var r = el.Current.BoundingRectangle;
            Input.Click((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
            return OpResult.Ok($"鼠标点击了 {elementId}");
        }
        catch (Exception ex)
        {
            return OpResult.Fail($"点击 {elementId} 失败 - {ex.Message}");
        }
    }

    public OpResult ClickPoint(int x, int y)
    {
        Input.Click(x, y);
        return OpResult.Ok($"点击坐标 ({x},{y})");
    }

    public OpResult SetText(string elementId, string text)
    {
        var el = Finder.Resolve(elementId) ?? Finder.ById(elementId, DefaultWaitMs);
        if (el == null) return OpResult.Fail($"未找到元素 {elementId}");

        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
            {
                ((ValuePattern)vp).SetValue(text);
                return OpResult.Ok($"在 {elementId} 中输入了 \"{text}\"");
            }
            el.SetFocus();
            Thread.Sleep(100);
            Input.SelectAll();
            Input.TypeText(text);
            return OpResult.Ok($"通过键盘在 {elementId} 中输入了 \"{text}\"");
        }
        catch (Exception ex)
        {
            return OpResult.Fail($"输入文本失败 - {ex.Message}");
        }
    }

    public OpResult ReadValue(string elementId, out string value)
    {
        value = "";
        var el = Finder.Resolve(elementId) ?? Finder.ById(elementId, DefaultWaitMs);
        if (el == null) return OpResult.Fail($"未找到元素 {elementId}");
        try
        {
            value = ElementFinder.ReadText(el);
            return OpResult.Ok(value);
        }
        catch (Exception ex)
        {
            return OpResult.Fail($"读取值失败 - {ex.Message}");
        }
    }

    public OpResult PressKey(string keyName)
    {
        if (!Enum.TryParse<SpecialKey>(keyName, true, out var key))
            return OpResult.Fail($"不支持的按键 {keyName}");
        Input.Press(key);
        return OpResult.Ok($"已按下 {keyName} 键");
    }

    // ── 下拉框 / 列表项选择 ──────────────────────────────────────

    public OpResult SelectItem(string elementId, string itemText)
    {
        var el = Finder.Resolve(elementId) ?? Finder.ById(elementId, DefaultWaitMs);
        if (el == null) return OpResult.Fail($"未找到元素 {elementId}");

        try
        {
            // ComboBox：展开 → 匹配列表项 → 选中 → 收起
            if (el.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var ecObj))
            {
                var ec = (ExpandCollapsePattern)ecObj;
                if (ec.Current.ExpandCollapseState != ExpandCollapseState.Expanded)
                    ec.Expand();

                AutomationElementCollection? items = null;
                for (int i = 0; i < 8; i++)
                {
                    Thread.Sleep(200);
                    items = el.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
                    if (items.Count > 0) break;
                }

                if (items == null || items.Count == 0)
                {
                    TryCollapse(ec);
                    return OpResult.Fail($"{elementId} 下拉列表为空（等待超时）");
                }

                // 先精确匹配（"Cash" 不应误中 "CashAndCard"），再退回包含匹配
                var matched = items.Cast<AutomationElement>()
                        .FirstOrDefault(i => string.Equals(i.Current.Name, itemText, StringComparison.OrdinalIgnoreCase))
                    ?? items.Cast<AutomationElement>()
                        .FirstOrDefault(i => i.Current.Name.Contains(itemText, StringComparison.OrdinalIgnoreCase));

                if (matched != null)
                {
                    if (matched.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var si))
                        ((SelectionItemPattern)si).Select();
                    else
                    {
                        var r = matched.Current.BoundingRectangle;
                        Input.Click((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
                    }
                    Thread.Sleep(100);
                    TryCollapse(ec);
                    return OpResult.Ok($"在 {elementId} 中选择了 \"{itemText}\"");
                }

                // 列表中无匹配：可编辑下拉框直接写值
                if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vpObj)
                    && !((ValuePattern)vpObj).Current.IsReadOnly)
                {
                    TryCollapse(ec);
                    ((ValuePattern)vpObj).SetValue(itemText);
                    return OpResult.Ok($"已向 {elementId} 直接写入 \"{itemText}\"（列表中无匹配项）");
                }

                var available = string.Join(", ", items.Cast<AutomationElement>()
                    .Select(i => $"\"{i.Current.Name}\"").Take(10));
                TryCollapse(ec);
                return OpResult.Fail($"未找到 \"{itemText}\"，可选项: [{available}]");
            }

            // 元素本身就是列表项/行（如弹窗中的 product_row_xxx）：直接选中
            if (el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var siSelf))
            {
                ((SelectionItemPattern)siSelf).Select();
                Thread.Sleep(100);
                return OpResult.Ok($"已选中 {elementId}");
            }

            return Click(elementId);
        }
        catch (Exception ex)
        {
            return OpResult.Fail($"选择项目失败 - {ex.Message}");
        }
    }

    private static void TryCollapse(ExpandCollapsePattern ec)
    {
        try
        {
            if (ec.Current.ExpandCollapseState == ExpandCollapseState.Expanded)
                ec.Collapse();
        }
        catch { }
    }

    // ── 表格操作 ─────────────────────────────────────────────────

    /// <summary>
    /// 点击表格单元格使其进入编辑模式。
    /// 定位顺序：单元格内 detail_ 类 id → 单元格 id/文本含列关键字 → SmartZaiko 已知列序。
    /// </summary>
    public OpResult ClickCell(string gridId, int row, string columnKeyword)
    {
        var grid = Finder.ById(gridId, DefaultWaitMs);
        if (grid == null) return OpResult.Fail($"未找到表格 {gridId}");

        try
        {
            var rows = grid.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));
            if (rows.Count == 0)  return OpResult.Fail($"表格 {gridId} 中没有数据行");
            if (row > rows.Count) return OpResult.Fail($"第 {row} 行不存在（共 {rows.Count} 行）");

            var targetRow = rows[row - 1];
            var cells = targetRow.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Custom))
                .Cast<AutomationElement>()
                .OrderBy(c => c.Current.BoundingRectangle.X)
                .ToList();

            AutomationElement? target = null;

            // 1) 单元格自身或其内容的 id/名称 含列关键字
            foreach (var cell in cells)
            {
                var key = cell.Current.AutomationId + "|" + cell.Current.Name;
                if (key.Contains(columnKeyword, StringComparison.OrdinalIgnoreCase)) { target = cell; break; }

                var inner = cell.FindFirst(TreeScope.Descendants, Condition.TrueCondition);
                if (inner != null &&
                    (inner.Current.AutomationId ?? "").Contains(columnKeyword, StringComparison.OrdinalIgnoreCase))
                { target = cell; break; }
            }

            // 2) SmartZaiko 已知可见列序（No | 総重 | 空車重 | 商品名 | 備考 | 計量重）
            if (target == null)
            {
                var colIndex = columnKeyword.ToLower() switch
                {
                    "preweight"   or "総重"  => 1,
                    "afterweight" or "空車重" => 2,
                    "product"     or "商品"   => 3,
                    "memo"        or "備考"   => 4,
                    "grossweight" or "計量重" => 5,
                    _ => -1
                };
                if (colIndex >= 0 && colIndex < cells.Count) target = cells[colIndex];
            }

            // 3) 找不到精确列时点行中央（至少让行获得焦点）
            if (target == null && cells.Count == 0)
            {
                var rr = targetRow.Current.BoundingRectangle;
                Input.Click((int)(rr.X + rr.Width / 2), (int)(rr.Y + rr.Height / 2));
                Thread.Sleep(200);
                return OpResult.Ok($"已点击第 {row} 行（未能精确定位列 {columnKeyword}）");
            }
            if (target == null)
                return OpResult.Fail($"找不到第 {row} 行的 [{columnKeyword}] 列");

            var b = target.Current.BoundingRectangle;
            Input.Click((int)(b.X + b.Width / 2), (int)(b.Y + b.Height / 2));
            Thread.Sleep(300);
            return OpResult.Ok($"已点击第 {row} 行 [{columnKeyword}] 列");
        }
        catch (Exception ex)
        {
            return OpResult.Fail($"click_cell 失败 - {ex.Message}");
        }
    }

    /// <summary>明细表格行数（依赖 row_N_exists 标记约定）</summary>
    public OpResult GetRowCount(out int count)
    {
        count = 0;
        if (!Finder.IsAttached) return OpResult.Fail("未连接到目标窗口");
        for (int i = 1; i <= 50; i++)
        {
            if (Finder.ById($"row_{i}_exists") == null) break;
            count = i;
        }
        return OpResult.Ok(count == 0 ? "表格行数: 0（无数据行）" : $"表格行数: {count}");
    }

    // ── 弹窗 ─────────────────────────────────────────────────────

    /// <summary>控件是否存在且可见（用于验证"跳转到含该控件的页面"）</summary>
    public bool ElementExists(string elementId, int timeoutMs = 800)
    {
        var el = Finder.ById(elementId, timeoutMs);
        if (el == null) return false;
        try { return !el.Current.IsOffscreen; } catch { return false; }
    }

    /// <summary>界面上是否存在包含指定文本的控件（用于验证页面标题/状态文字）</summary>
    public bool TextVisible(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            foreach (var e in Finder.Scan(10))
                if ((e.Text + "|" + e.Name).Contains(text, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        catch { }
        return false;
    }

    /// <summary>检测应用内弹窗（依赖 dialog_message / dialog_btn_* 约定），无弹窗返回 null</summary>
    public DialogInfo? TryGetDialog()
    {
        var msgEl = Finder.ById("dialog_message");
        if (msgEl == null) return null;

        var message = ElementFinder.ReadText(msgEl);
        var buttons = new List<string>();
        foreach (var aid in new[] { "dialog_btn_yes", "dialog_btn_no", "dialog_btn_cancel" })
        {
            var btn = Finder.ById(aid);
            if (btn != null && !btn.Current.IsOffscreen) buttons.Add(aid);
        }
        return new DialogInfo(message, buttons);
    }

    /// <summary>关闭当前弹窗：优先 Yes/OK，没有则 Cancel</summary>
    public OpResult CloseDialog()
    {
        var dialog = TryGetDialog();
        if (dialog == null) return OpResult.Ok("无弹窗");

        var btn = dialog.Buttons.Contains("dialog_btn_yes") ? "dialog_btn_yes"
                : dialog.Buttons.FirstOrDefault();
        if (btn == null) return OpResult.Fail($"弹窗无可用按钮: {dialog.Message}");

        var r = Click(btn);
        return r.Success ? OpResult.Ok($"已关闭弹窗: {dialog.Message}") : r;
    }

    // ── SmartZaiko 业务辅助（AI 模式工具）─────────────────────────

    /// <summary>填写指定行単価：点击单元格 → 写值 → Tab 确认</summary>
    public OpResult SetUnitPrice(int row, string unitPrice)
    {
        var click = ClickCell("DgVoucherInItem", row, "unitPrice");
        if (!click.Success) return click;
        Thread.Sleep(300);

        var cell = Finder.ById($"detail_{row}_unitPrice", 1000);
        if (cell != null && cell.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
        {
            ((ValuePattern)vp).SetValue(unitPrice);
            Thread.Sleep(100);
            Input.Press(SpecialKey.Tab);
            return OpResult.Ok($"第{row}行単価设为 {unitPrice}");
        }

        // 退路：键盘输入（焦点应已在格内）
        Input.SelectAll();
        Input.TypeText(unitPrice);
        Thread.Sleep(100);
        Input.Press(SpecialKey.Tab);
        return OpResult.Ok($"已通过键盘输入第{row}行単価 {unitPrice}");
    }

    /// <summary>商品格已进入编辑状态时输入商品代码并确认</summary>
    public OpResult SelectProductInCell(int row, string productCode)
    {
        var cell = Finder.ById($"detail_{row}_product", 2000);
        if (cell == null) return OpResult.Fail("商品格未进入编辑状态");

        if (!string.IsNullOrWhiteSpace(productCode))
        {
            if (cell.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
                ((ValuePattern)vp).SetValue(productCode);
            else
            {
                cell.SetFocus();
                Thread.Sleep(100);
                Input.SelectAll();
                Input.TypeText(productCode);
            }
            Thread.Sleep(200);
            Input.Press(SpecialKey.Enter);
            Thread.Sleep(600);
            return OpResult.Ok($"已输入商品代码 {productCode} 并确认");
        }
        return SelectProduct("");
    }

    /// <summary>在已打开的商品选择弹窗中选择商品（keyword 为空选第一个）</summary>
    public OpResult SelectProduct(string keyword)
    {
        var list = Finder.ById("product_list", 3000);
        if (list == null || list.Current.IsOffscreen)
            return OpResult.Fail("商品选择弹窗未出现（等待超时）");

        Thread.Sleep(300);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var byId = Finder.ById($"product_row_{keyword}");
            if (byId != null) return InvokeRow(byId, keyword);
        }

        var rows = list.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));
        if (rows.Count == 0) return OpResult.Fail("商品列表为空");

        foreach (AutomationElement row in rows)
        {
            var key = (row.Current.Name ?? "") + "|" + (row.Current.AutomationId ?? "");
            if (string.IsNullOrWhiteSpace(keyword) || key.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return InvokeRow(row, string.IsNullOrWhiteSpace(keyword) ? "第一个" : keyword);
        }

        var available = string.Join(", ", rows.Cast<AutomationElement>().Take(5).Select(r => $"\"{r.Current.Name}\""));
        return OpResult.Fail($"未找到商品 \"{keyword}\"，列表前5项: [{available}]");
    }

    private static OpResult InvokeRow(AutomationElement row, string label)
    {
        if (row.TryGetCurrentPattern(InvokePattern.Pattern, out var inv))
        {
            ((InvokePattern)inv).Invoke();
            Thread.Sleep(300);
            return OpResult.Ok($"选择了 \"{label}\"");
        }
        if (row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sel))
            ((SelectionItemPattern)sel).Select();

        var r = row.Current.BoundingRectangle;
        Input.DoubleClick((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
        Thread.Sleep(300);
        return OpResult.Ok($"双击选择了 \"{label}\"");
    }

    /// <summary>回到主页（btn_home 约定），为下一次测试恢复初始状态</summary>
    public OpResult GoHome()
    {
        var r = Click("btn_home");
        if (r.Success) Thread.Sleep(800);
        return r.Success ? OpResult.Ok("已回到主页") : OpResult.Fail("未找到首页按钮 btn_home");
    }

    /// <summary>生成界面控件树文本（AI 模式阅读界面）</summary>
    public string ScanDescription()
    {
        var elements = Finder.Scan();
        var lines = new List<string>
        {
            "=== 当前界面控件树 ===",
            $"窗口: {Finder.Window?.Current.Name}",
            $"扫描到 {elements.Count} 个可交互/有意义的控件:",
            ""
        };
        lines.AddRange(elements.Select(e => e.ToDescription()));
        return string.Join("\n", lines);
    }
}
