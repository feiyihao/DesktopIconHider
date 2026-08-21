using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopIconHider;

/// <summary>
/// 全局低级鼠标钩子，通过两次“鼠标左键抬起”事件的时间与位置间隔来判定双击。
/// 相比依赖 WM_LBUTTONDBLCLK，此方式在低级钩子中更稳定可靠。
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private const int WhMouseLl = 14;        // WH_MOUSE_LL
    private const int WmLButtonUp = 0x0202;  // WM_LBUTTONUP
    private const int SmCxDoubleClk = 36;    // 双击 X 轴容差
    private const int SmCyDoubleClk = 37;    // 双击 Y 轴容差

    private readonly LowLevelMouseProc _proc;
    private IntPtr _hookId;

    private long _lastUpTicks;
    private int _lastUpX;
    private int _lastUpY;
    private bool _hasLastUp;

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
        if (nCode >= 0 && wParam == (IntPtr)WmLButtonUp)
        {
            try
            {
                // MSLLHOOKSTRUCT 前 8 字节为 POINT{X, Y}，直接读取避免装箱/空值警告。
                var x = Marshal.ReadInt32(lParam, 0);
                var y = Marshal.ReadInt32(lParam, 4);
                var now = Stopwatch.GetTimestamp();

                if (_hasLastUp && IsDoubleClick(now, x, y))
                {
                    _hasLastUp = false;
                    var hWnd = WindowFromPoint(new POINT { X = x, Y = y });
                    MouseDoubleClick?.Invoke(this, new MouseHookEventArgs(x, y, hWnd));
                }
                else
                {
                    _hasLastUp = true;
                    _lastUpTicks = now;
                    _lastUpX = x;
                    _lastUpY = y;
                }
            }
            catch
            {
                // 忽略回调中的异常，避免钩子因异常失效。
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool IsDoubleClick(long nowTicks, int x, int y)
    {
        var elapsedMs = (nowTicks - _lastUpTicks) * 1000.0 / Stopwatch.Frequency;
        if (elapsedMs > GetDoubleClickTime())
        {
            return false;
        }

        var toleranceX = GetSystemMetrics(SmCxDoubleClk);
        var toleranceY = GetSystemMetrics(SmCyDoubleClk);
        return Math.Abs(x - _lastUpX) <= toleranceX && Math.Abs(y - _lastUpY) <= toleranceY;
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