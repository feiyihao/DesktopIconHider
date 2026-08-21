namespace DesktopIconHider;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // 使用 WinForms 消息循环驱动托盘图标与全局鼠标钩子。
        using var app = new TrayApplication();
        Application.Run();
    }
}
