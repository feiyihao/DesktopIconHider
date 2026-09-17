using System.Runtime.InteropServices;
using System.Text;

namespace DesktopIconHider;

/// <summary>
/// 通过向资源管理器发送命令来切换桌面图标的显示 / 隐藏。
/// </summary>
internal static class DesktopIconToggler
{
    private const int WmCommand = 0x0111;
    private const int ToggleDesktop = 0x7402; // “显示桌面图标”切换命令

    private const int LvmHitTest = 0x1000 + 18;     // LVM_HITTEST
    private const int LvmGetItemCount = 0x1000 + 4; // LVM_GETITEMCOUNT
    private const uint LvmhtOnItem = 0x000E;        // LVMHT_ONITEMICON|ONITEMLABEL|ONITEMSTATEICON

    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;

    private static IntPtr _cachedDefView;
    private static readonly object Sync = new();

    // 跨进程命中测试复用的进程句柄与远程缓冲区。
    // 复用可避免每次双击都执行 OpenProcess/VirtualAllocEx（首次调用与长时间闲置后尤其慢）。
    private static readonly object HitTestSync = new();
    private static IntPtr _remoteProcess;
    private static IntPtr _remoteBuffer;
    private static uint _remotePid;

    private static int _busy; // 防止连续双击时重入

    /// <summary>
    /// 启动时在后台线程预先执行窗口查找与命中测试，
    /// 提前加载相关模块与完成跨进程初始化，避免首次操作卡顿。
    /// </summary>
    public static void WarmUp()
    {
        Task.Run(() =>
        {
            try
            {
                var defView = GetDefView();
                var listView = defView == IntPtr.Zero ? IntPtr.Zero : FindListView(defView);
                if (listView != IntPtr.Zero)
                {
                    // 完整执行一次命中测试：提前加载模块、建立进程句柄与远程缓冲区缓存。
                    // 结果无关紧要。
                    ListViewHitTest(listView, new POINT { X = 0, Y = 0 });
                }

                // 预热双击处理路径（含 Task.Run 与后续代码的 JIT），
                // 避免首次双击时在钩子回调中产生额外延迟。传入空句柄不会有任何副作用。
                HandleDesktopDoubleClick(-1, -1, IntPtr.Zero);
            }
            catch
            {
                // 预热失败不影响后续按需初始化。
            }
        });
    }

    /// <summary>释放缓存的进程句柄与远程内存。</summary>
    public static void Shutdown()
    {
        lock (HitTestSync)
        {
            ResetRemoteBuffer();
        }
    }

    /// <summary>切换桌面图标的显示 / 隐藏。</summary>
    public static void Toggle()
    {
        var defView = GetDefView();
        if (defView != IntPtr.Zero)
        {
            SendMessage(defView, WmCommand, (IntPtr)ToggleDesktop, IntPtr.Zero);
        }
    }

    /// <summary>
    /// 在后台线程切换桌面图标，避免阻塞调用线程（托盘菜单 / 鼠标钩子回调）。
    /// </summary>
    public static void ToggleAsync()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return; // 上一次切换尚未完成，忽略本次
        }

        Task.Run(() =>
        {
            try
            {
                Toggle();
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    /// <summary>
    /// 处理一次桌面双击。
    /// 所有耗时操作（跨进程命中测试、切换图标）都在后台线程执行，
    /// 保证全局鼠标钩子回调能立即返回，避免鼠标指针卡顿。
    /// </summary>
    public static void HandleDesktopDoubleClick(int screenX, int screenY, IntPtr hWndUnderCursor)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return; // 上一次操作尚未完成，忽略本次
        }

        Task.Run(() =>
        {
            try
            {
                if (ShouldToggle(screenX, screenY, hWndUnderCursor))
                {
                    Toggle();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    /// <summary>
    /// 判断一次双击是否应触发桌面图标切换：
    /// 1. 鼠标必须位于桌面窗口体系内（句柄比对，排除文件资源管理器等其他窗口）；
    /// 2. 图标已隐藏时，任意桌面位置双击都恢复图标；
    /// 3. 图标可见时，仅空白处（未命中图标、复选框等部位）触发。
    /// </summary>
    private static bool ShouldToggle(int screenX, int screenY, IntPtr hWndUnderCursor)
    {
        if (!IsDesktopWindow(hWndUnderCursor))
        {
            return false;
        }

        var defView = GetDefView();
        if (defView == IntPtr.Zero)
        {
            return false;
        }

        var listView = FindListView(defView);
        // 找不到列表视图时保守处理：不触发，避免误隐藏。
        if (listView == IntPtr.Zero)
        {
            return false;
        }

        // 图标已隐藏（列表视图不可见或没有任何项目）：双击桌面任意位置恢复图标。
        if (!IsWindowVisible(listView) || GetItemCount(listView) == 0)
        {
            return true;
        }

        // 屏幕坐标 → ListView 客户区坐标。
        var pt = new POINT { X = screenX, Y = screenY };
        if (!ScreenToClient(listView, ref pt))
        {
            return false;
        }

        // 返回 -1 表示未命中任何图标部位，即空白区域。
        return ListViewHitTest(listView, pt) == -1;
    }

    /// <summary>
    /// 判断窗口是否属于桌面窗口体系（句柄比对，而非类名比对）。
    /// 文件资源管理器等窗口内部也有 SHELLDLL_DefView/SysListView32，
    /// 但句柄与桌面不同，因此不会被误判。
    /// </summary>
    private static bool IsDesktopWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        var defView = GetDefView();
        if (defView == IntPtr.Zero)
        {
            return false;
        }

        // 鼠标窗口或其父链等于桌面视图（如桌面 SysListView32 → SHELLDLL_DefView）。
        var current = hWnd;
        while (current != IntPtr.Zero)
        {
            if (current == defView)
            {
                return true;
            }
            current = GetParent(current);
        }

        // 鼠标窗口是桌面视图的祖先（Progman / WorkerW，图标隐藏时鼠标落在这些窗口上）。
        current = defView;
        while (current != IntPtr.Zero)
        {
            if (current == hWnd)
            {
                return true;
            }
            current = GetParent(current);
        }

        return false;
    }

    /// <summary>查询列表视图中的项目数量。</summary>
    private static int GetItemCount(IntPtr hListView)
    {
        return (int)SendMessage(hListView, LvmGetItemCount, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>获取缓存的桌面视图句柄，失效时重新查找。</summary>
    private static IntPtr GetDefView()
    {
        if (_cachedDefView != IntPtr.Zero && IsWindow(_cachedDefView))
        {
            return _cachedDefView;
        }

        lock (Sync)
        {
            _cachedDefView = FindDesktopDefView();
            return _cachedDefView;
        }
    }

    /// <summary>在桌面视图下查找图标列表视图。</summary>
    private static IntPtr FindListView(IntPtr defView)
    {
        var listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
        return listView != IntPtr.Zero
            ? listView
            : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    private static IntPtr FindDesktopDefView()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView != IntPtr.Zero)
        {
            return defView;
        }

        // 某些系统（如多显示器）下桌面视图位于 WorkerW 窗口中。
        var workerW = FindWorkerWWithDefView();
        return workerW == IntPtr.Zero
            ? IntPtr.Zero
            : FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
    }

    private static IntPtr FindWorkerWWithDefView()
    {
        var result = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (GetClassName(hWnd) == "WorkerW" &&
                FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                result = hWnd;
                return false; // 停止枚举
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static string GetClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// 跨进程执行 ListView 命中测试：返回命中的项目索引，
    /// -1 表示空白区域，-2 表示无法判定。
    /// 复用了进程句柄与远程缓冲区，避免每次调用都重新分配（这是卡顿的主要来源之一）。
    /// </summary>
    private static int ListViewHitTest(IntPtr hListView, POINT clientPt)
    {
        GetWindowThreadProcessId(hListView, out uint pid);
        if (pid == 0)
        {
            return -2;
        }

        lock (HitTestSync)
        {
            var size = Marshal.SizeOf<LVHITTESTINFO>();
            var info = new LVHITTESTINFO { pt = clientPt };

            if (!WriteHitTestInfo(pid, ref info, size))
            {
                // 缓存的句柄可能已失效（如资源管理器重启），重建后重试一次。
                ResetRemoteBuffer();
                if (!WriteHitTestInfo(pid, ref info, size))
                {
                    return -2;
                }
            }

            SendMessage(hListView, LvmHitTest, IntPtr.Zero, _remoteBuffer);

            LVHITTESTINFO result = default;
            if (!ReadProcessMemory(_remoteProcess, _remoteBuffer, ref result, (IntPtr)size, out _))
            {
                return -2;
            }

            // flags 指示命中的部位：命中图标 / 标签 / 复选框等状态图标均视为“非空白”。
            if ((result.flags & LvmhtOnItem) != 0 || result.iItem >= 0)
            {
                return Math.Max(result.iItem, 0);
            }

            return -1;
        }
    }

    private static bool WriteHitTestInfo(uint pid, ref LVHITTESTINFO info, int size)
    {
        return EnsureRemoteBuffer(pid)
            && WriteProcessMemory(_remoteProcess, _remoteBuffer, ref info, (IntPtr)size, out _);
    }

    /// <summary>确保缓存的进程句柄与远程缓冲区可用于指定进程。</summary>
    private static bool EnsureRemoteBuffer(uint pid)
    {
        if (_remoteProcess != IntPtr.Zero)
        {
            if (_remotePid == pid)
            {
                return true;
            }

            ResetRemoteBuffer();
        }

        var process = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite, false, pid);
        if (process == IntPtr.Zero)
        {
            return false;
        }

        var remote = VirtualAllocEx(
            process,
            IntPtr.Zero,
            (IntPtr)Marshal.SizeOf<LVHITTESTINFO>(),
            MemCommit | MemReserve,
            PageReadWrite);
        if (remote == IntPtr.Zero)
        {
            CloseHandle(process);
            return false;
        }

        _remoteProcess = process;
        _remotePid = pid;
        _remoteBuffer = remote;
        return true;
    }

    private static void ResetRemoteBuffer()
    {
        if (_remoteBuffer != IntPtr.Zero && _remoteProcess != IntPtr.Zero)
        {
            VirtualFreeEx(_remoteProcess, _remoteBuffer, IntPtr.Zero, MemRelease);
        }

        if (_remoteProcess != IntPtr.Zero)
        {
            CloseHandle(_remoteProcess);
        }

        _remoteProcess = IntPtr.Zero;
        _remoteBuffer = IntPtr.Zero;
        _remotePid = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LVHITTESTINFO
    {
        public POINT pt;
        public uint flags;
        public int iItem;
        public int iSubItem;
        public int iGroup;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, ref LVHITTESTINFO lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, ref LVHITTESTINFO lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesRead);
}
