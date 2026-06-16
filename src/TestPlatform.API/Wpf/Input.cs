using System.Runtime.InteropServices;

namespace TestPlatform.API.Wpf;

/// <summary>可模拟的特殊按键。F1~F12 必须保持连续，按偏移量计算 VK 码。</summary>
public enum SpecialKey
{
    Enter, Tab, Escape, Backspace, Delete,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12
}

/// <summary>鼠标 / 键盘模拟（全局注入，被测应用须在前台）</summary>
public static class Input
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern short VkKeyScan(char ch);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP   = 0x0004;
    private const uint MOUSEEVENTF_WHEEL    = 0x0800;
    private const uint KEYEVENTF_KEYDOWN    = 0x0000;
    private const uint KEYEVENTF_KEYUP      = 0x0002;

    private const byte VK_CONTROL = 0x11;
    private const byte VK_SHIFT   = 0x10;

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_LEFTDOWN, x, y, 0, IntPtr.Zero);
        Thread.Sleep(30);
        mouse_event(MOUSEEVENTF_LEFTUP, x, y, 0, IntPtr.Zero);
        Thread.Sleep(50);
    }

    public static void DoubleClick(int x, int y)
    {
        Click(x, y);
        Thread.Sleep(80);
        Click(x, y);
    }

    public static void Scroll(int x, int y, int notches)
    {
        SetCursorPos(x, y);
        Thread.Sleep(50);
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)(notches * 120), IntPtr.Zero);
    }

    /// <summary>逐字符模拟键盘输入（仅 ASCII；中文/日文请用 ValuePattern 写值）</summary>
    public static void TypeText(string text)
    {
        foreach (char c in text)
        {
            short vk = VkKeyScan(c);
            byte keyCode = (byte)(vk & 0xFF);
            bool needShift = (vk & 0x100) != 0;

            if (needShift) keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
            keybd_event(keyCode, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
            Thread.Sleep(10);
            keybd_event(keyCode, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            if (needShift) keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
            Thread.Sleep(20);
        }
    }

    /// <summary>Ctrl+A 全选（清空输入框前用）</summary>
    public static void SelectAll()
    {
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
        keybd_event((byte)'A', 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
        Thread.Sleep(10);
        keybd_event((byte)'A', 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        Thread.Sleep(30);
    }

    public static void Press(SpecialKey key)
    {
        byte vk = key switch
        {
            SpecialKey.Enter     => 0x0D,
            SpecialKey.Tab       => 0x09,
            SpecialKey.Escape    => 0x1B,
            SpecialKey.Backspace => 0x08,
            SpecialKey.Delete    => 0x2E,
            >= SpecialKey.F1 and <= SpecialKey.F12 => (byte)(0x70 + (key - SpecialKey.F1)),
            _ => 0
        };
        if (vk == 0) return;

        keybd_event(vk, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
        Thread.Sleep(10);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        Thread.Sleep(30);
    }
}
