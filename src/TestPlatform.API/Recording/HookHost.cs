using System.Runtime.InteropServices;

namespace TestPlatform.API.Recording;

/// <summary>
/// 全局鼠标/键盘钩子宿主。
/// 关键约束：低级钩子回调只在「安装钩子的线程」泵消息时才会触发——
/// ASP.NET 线程池线程没有消息循环，装上去回调永远不执行。
/// 因此钩子必须安装在本类的专用 STA 线程上，由 GetMessage 循环驱动。
/// </summary>
public sealed class HookHost : IDisposable
{
    /// <summary>左键按下（屏幕坐标）</summary>
    public event Action<int, int>? LeftDown;
    /// <summary>左键抬起（屏幕坐标）</summary>
    public event Action<int, int>? LeftUp;
    /// <summary>键盘按下（虚拟键码）</summary>
    public event Action<uint>? KeyDown;

    private Thread?  _thread;
    private uint     _threadId;
    private IntPtr   _mouseHook;
    private IntPtr   _kbHook;
    private LowLevelProc? _mouseProc;   // 持有引用防 GC
    private LowLevelProc? _kbProc;
    private GCHandle _mouseHandle;
    private GCHandle _kbHandle;

    public bool IsRunning => _thread is { IsAlive: true };

    public void Start()
    {
        if (IsRunning) return;

        _mouseProc = MouseCallback;
        _kbProc    = KeyboardCallback;
        _mouseHandle = GCHandle.Alloc(_mouseProc);
        _kbHandle    = GCHandle.Alloc(_kbProc);

        _thread = new Thread(ThreadProc) { IsBackground = true, Name = "RecordingHooks" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Stop()
    {
        if (_thread != null)
        {
            if (_threadId != 0)
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(1000);
            _thread   = null;
            _threadId = 0;
        }
        Thread.Sleep(100); // 等待可能挂起的回调收尾
        if (_mouseHandle.IsAllocated) _mouseHandle.Free();
        if (_kbHandle.IsAllocated)    _kbHandle.Free();
    }

    public void Dispose() => Stop();

    // ── 钩子线程：安装 → 泵消息 → WM_QUIT 后卸载 ─────────────────
    private void ThreadProc()
    {
        _threadId = GetCurrentThreadId();
        try
        {
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            using var mod  = proc.MainModule!;
            var hMod = GetModuleHandle(mod.ModuleName);

            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc!, hMod, 0);
            _kbHook    = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc!, hMod, 0);
            Console.WriteLine($"[Hook] 钩子已安装 mouse={_mouseHook != IntPtr.Zero} keyboard={_kbHook != IntPtr.Zero}");

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally
        {
            if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
            if (_kbHook != IntPtr.Zero)    { UnhookWindowsHookEx(_kbHook);    _kbHook    = IntPtr.Zero; }
            Console.WriteLine("[Hook] 钩子已卸载");
        }
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            if (msg is WM_LBUTTONDOWN or WM_LBUTTONUP)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (msg == WM_LBUTTONDOWN) LeftDown?.Invoke(data.pt.X, data.pt.Y);
                else                       LeftUp?.Invoke(data.pt.X, data.pt.Y);
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == WM_KEYDOWN)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            KeyDown?.Invoke(data.vkCode);
        }
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    // ── P/Invoke ─────────────────────────────────────────────────
    private const int  WH_MOUSE_LL    = 14;
    private const int  WH_KEYBOARD_LL = 13;
    private const int  WM_LBUTTONDOWN = 0x0201;
    private const int  WM_LBUTTONUP   = 0x0202;
    private const int  WM_KEYDOWN     = 0x0100;
    private const uint WM_QUIT        = 0x0012;

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public System.Drawing.Point pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMsg { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public System.Drawing.Point pt; }

    [DllImport("user32.dll")]   private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")]   private static extern bool   UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]   private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]   private static extern int    GetMessage(out NativeMsg lpMsg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")]   private static extern bool   TranslateMessage(ref NativeMsg lpMsg);
    [DllImport("user32.dll")]   private static extern IntPtr DispatchMessage(ref NativeMsg lpMsg);
    [DllImport("user32.dll")]   private static extern bool   PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint   GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string lpModuleName);
}
