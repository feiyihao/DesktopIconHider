using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopIconHider;

/// <summary>
/// 全局低级鼠标钩子，通过两次“鼠标左键按下”的时间与位置间隔来判定双击。
/// 相比依赖 WM_LBUTTONDBLCLK，此方式在低级钩子中更稳定可靠；
/// 同时跟踪拖动（按下后移动超过系统拖动阈值），“拖动 + 单击”不会被误判为双击。
///
/// 注意：回调必须尽快返回。低级鼠标钩子会阻塞整个系统的输入处理，
/// 在回调中做耗时操作（如跨进程调用 / SendMessage）会导致鼠标指针卡住不动，
/// 因此这里只做轻量判断，耗时操作由订阅方在后台线程处理。
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private const int WhMouseLl = 14;         // WH_MOUSE_LL
    private const int WmMouseMove = 0x0200;   // WM_MOUSEMOVE
    private const int WmLButtonDown = 0x0201; // WM_LBUTTONDOWN
    private const int WmLButtonUp = 0x0202;   // WM_LBUTTONUP
    private const int SmCxDoubleClk = 36;     // 双击 X 轴容差
    private const int SmCyDoubleClk = 37;     // 双击 Y 轴容差
    private const int SmCxDrag = 68;          // 拖动判定 X 轴阈值
    private const int SmCyDrag = 69;          // 拖动判定 Y 轴阈值

    private readonly LowLevelMouseProc _proc;
    private IntPtr _hookId;

    // 上一次“干净的单击”（按下到抬起期间未拖动），用于配对判双击
    private long _lastClickDownTicks;
    private int _lastClickX;
    private int _lastClickY;
    private bool _hasLastClick;

    // 当前按压状态（用于拖动判定）
    private bool _isButtonDown;
    private bool _isDragging;
    private long _pressStartTicks;
    private int _pressX;
    private int _pressY;

    public event EventHandler<MouseHookEventArgs>? MouseDoubleClick;

    public MouseHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero)
        {
            return;
        }

        using var module = Process.GetCurrentProcess().MainModule
            ?? throw new InvalidOperationException("无法获取当前进程模块。");

        _hookId = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(module.ModuleName), 0);
        if (_hookId == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "安装鼠标钩子失败。");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // 回调必须轻量、快速返回，否则会阻塞系统输入（鼠标卡顿）。
        if (nCode >= 0)
        {
            try
            {
                // MSLLHOOKSTRUCT 前 8 字节为 POINT{X, Y}，直接读取避免装箱/空值警告。
                switch ((int)wParam)
                {
                    case WmMouseMove:
                        if (_isButtonDown && !_isDragging)
                        {
                            _isDragging = IsBeyondDrag(
                                Marshal.ReadInt32(lParam, 0),
                                Marshal.ReadInt32(lParam, 4));
                        }
                        break;

                    case WmLButtonDown:
                        HandleButtonDown(
                            Marshal.ReadInt32(lParam, 0),
                            Marshal.ReadInt32(lParam, 4));
                        break;

                    case WmLButtonUp:
                        _isButtonDown = false;
                        if (_isDragging)
                        {
                            // 本次按压是一次拖动（如拖动滑块 / 复选框），不计为单击，
                            // 因此也不会与紧随其后的单击凑成双击。
                            _isDragging = false;
                            _hasLastClick = false;
                        }
                        else
                        {
                            // 记录这次干净的单击，等待与下一次按下配对。
                            _hasLastClick = true;
                            _lastClickDownTicks = _pressStartTicks;
                            _lastClickX = _pressX;
                            _lastClickY = _pressY;
                        }
                        break;
                }
            }
            catch
            {
                // 忽略回调中的异常，避免钩子因异常失效。
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void HandleButtonDown(int x, int y)
    {
        _isButtonDown = true;
        _isDragging = false;
        _pressX = x;
        _pressY = y;
        _pressStartTicks = Stopwatch.GetTimestamp();

        // 与上一次“干净的单击”配对：时间间隔按两次按下的时刻计算（同 Windows 系统行为）。
        var isDoubleClick = _hasLastClick
            && WithinDoubleClickTime(_pressStartTicks)
            && WithinDoubleClickRange(x, y);

        _hasLastClick = false;

        if (isDoubleClick)
        {
            var hWnd = WindowFromPoint(new POINT { X = x, Y = y });
            MouseDoubleClick?.Invoke(this, new MouseHookEventArgs(x, y, hWnd));
        }
    }

    private bool WithinDoubleClickTime(long nowTicks)
    {
        var elapsedMs = (nowTicks - _lastClickDownTicks) * 1000.0 / Stopwatch.Frequency;
        return elapsedMs <= GetDoubleClickTime();
    }

    private bool WithinDoubleClickRange(int x, int y)
    {
        return Math.Abs(x - _lastClickX) <= GetSystemMetrics(SmCxDoubleClk)
            && Math.Abs(y - _lastClickY) <= GetSystemMetrics(SmCyDoubleClk);
    }

    private bool IsBeyondDrag(int x, int y)
    {
        return Math.Abs(x - _pressX) > GetSystemMetrics(SmCxDrag)
            || Math.Abs(y - _pressY) > GetSystemMetrics(SmCyDrag);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT pt);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}

internal sealed class MouseHookEventArgs : EventArgs
{
    public int ScreenX { get; }
    public int ScreenY { get; }
    public IntPtr WindowUnderCursor { get; }

    public MouseHookEventArgs(int screenX, int screenY, IntPtr windowUnderCursor)
    {
        ScreenX = screenX;
        ScreenY = screenY;
        WindowUnderCursor = windowUnderCursor;
    }
}