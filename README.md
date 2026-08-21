# DesktopIconHider

Windows 11 系统托盘工具：**双击桌面**隐藏 / 显示桌面图标，无 GUI 窗口，仅在系统托盘驻留。首次运行自动开启**开机自启**。

## 功能说明

| 操作 | 效果 |
| --- | --- |
| 双击桌面空白区域 | 隐藏 / 显示桌面图标（功能始终开启，无需额外开启） |
| 单击托盘图标（左键） | 手动切换桌面图标（作为双击的备用方式） |
| 右键托盘图标 → 立即隐藏/显示桌面图标 | 手动切换桌面图标 |
| 右键托盘图标 → 开机自启 | 勾选 / 取消开机自启（默认开启） |
| 右键托盘图标 → 退出 | 退出程序 |

## 工作原理

1. **系统托盘**：使用 WinForms `NotifyIcon`，通过 `Application.Run()` 消息循环驱动。
2. **双击检测**：安装全局低级鼠标钩子 `WH_MOUSE_LL`，通过两次 `WM_LBUTTONUP` 的时间间隔（< 系统双击时间）与位置间隔判定双击，再通过 `WindowFromPoint` 判断鼠标是否位于桌面区域。此方式比依赖 `WM_LBUTTONDBLCLK` 更稳定。
3. **隐藏 / 显示图标**：找到资源管理器的 `SHELLDLL_DefView` 窗口，向其发送 `WM_COMMAND(0x7402)`（“显示桌面图标”切换命令）。
4. **开机自启**：向注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 写入当前程序路径。

## 环境要求

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/)（或安装 .NET 10 桌面运行时）

## 构建

```powershell
cd D:\Documents\VScode项目\DesktopIconHider
dotnet build -c Release
```

生成文件位于 `bin\Release\net10.0-windows\DesktopIconHider.exe`。

## 发布为单文件可执行程序

```powershell
# 完全自包含单文件（推荐，无需安装 .NET，可直接拷贝单个 exe 到任意电脑运行）
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false

# 框架依赖单文件（体积小，但目标电脑需安装 .NET 10 桌面运行时）
dotnet publish -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false
```

发布结果位于 `bin\Release\net10.0-windows\win-x64\publish\`，其中 `DesktopIconHider.exe` 即为独立可执行文件（自包含模式约 49 MB，自带图标，无需其它文件）。

## 项目结构

```
DesktopIconHider/
├── DesktopIconHider.csproj   # 项目配置（WinExe、嵌入图标 app.ico）
├── Program.cs                # 入口：消息循环
├── TrayApplication.cs        # 托盘图标 + 菜单 + 开机自启
├── MouseHook.cs              # 全局鼠标钩子（双击检测）
├── DesktopIconToggler.cs     # 桌面图标切换 + 空白区域命中测试
├── app.manifest              # 应用清单（asInvoker）
├── app.ico                   # 程序图标
└── README.md
```
