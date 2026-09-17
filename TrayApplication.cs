using System.Drawing;
using Microsoft.Win32;
using System.Windows.Forms;

namespace DesktopIconHider;

/// <summary>
/// 系统托盘应用：双击桌面空白处隐藏 / 显示桌面图标，单击托盘图标也可手动切换。
/// </summary>
internal sealed class TrayApplication : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly MouseHook _mouseHook;

    public TrayApplication()
    {
        // exe 已通过 csproj 嵌入图标，直接提取用于托盘显示。
        _icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!)!;

        // 仅首次运行自动开启开机自启，之后尊重用户的手动设置。
        AutoStart.EnsureInitialSetup();

        // 后台预热桌面窗口查找与命中测试，避免首次操作卡顿。
        DesktopIconToggler.WarmUp();

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("立即隐藏 / 显示桌面图标", null, (_, _) => DesktopIconToggler.ToggleAsync()));

        var autoStartItem = new ToolStripMenuItem("开机自启")
        {
            CheckOnClick = true,
            Checked = AutoStart.IsEnabled()
        };
        autoStartItem.Click += (_, _) => AutoStart.SetEnabled(autoStartItem.Checked);
        menu.Items.Add(autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => Exit()));

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "桌面图标隐藏工具：双击桌面切换图标",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                DesktopIconToggler.ToggleAsync();
            }
        };

        _mouseHook = new MouseHook();
        // 钩子回调中不能做耗时操作，这里只转交参数，实际判断与切换在后台线程完成。
        _mouseHook.MouseDoubleClick += (_, e) =>
            DesktopIconToggler.HandleDesktopDoubleClick(e.ScreenX, e.ScreenY, e.WindowUnderCursor);
        _mouseHook.Install();
    }

    private void Exit()
    {
        _notifyIcon.Visible = false;
        Application.Exit();
    }

    public void Dispose()
    {
        _mouseHook.Dispose();
        DesktopIconToggler.Shutdown();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}

/// <summary>通过注册表 HKCU\...\Run 控制开机自启。</summary>
internal static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppKeyPath = @"Software\DesktopIconHider";
    private const string ValueName = "DesktopIconHider";
    private const string InitializedValue = "Initialized";

    /// <summary>
    /// 仅首次运行时自动开启开机自启并记录标记；
    /// 之后（包括用户手动关闭后）不再自动修改自启设置。
    /// </summary>
    public static void EnsureInitialSetup()
    {
        using var key = Registry.CurrentUser.CreateSubKey(AppKeyPath, writable: true);
        if (key?.GetValue(InitializedValue) is null)
        {
            SetEnabled(true);
            key?.SetValue(InitializedValue, 1, RegistryValueKind.DWord);
        }
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                key.SetValue(ValueName, $"\"{path}\"");
            }
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
