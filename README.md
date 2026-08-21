# DesktopIconHider

Windows 11 系统托盘工具：**双击桌面空白处**隐藏 / 显示桌面图标，无 GUI 窗口，仅在系统托盘驻留。首次运行自动开启**开机自启**。

## 功能说明

| 操作 | 效果 |
| --- | --- |
| 双击桌面空白区域 | 隐藏 / 显示桌面图标（功能始终开启，无需额外开启） |
| 双击桌面图标（图标隐藏时任意位置） | 恢复显示桌面图标 |
| 单击托盘图标（左键） | 手动切换桌面图标（作为双击的备用方式） |
| 右键托盘图标 → 立即隐藏/显示桌面图标 | 手动切换桌面图标 |
| 右键托盘图标 → 开机自启 | 勾选 / 取消开机自启（默认开启，取消后重启不再自动开启） |
| 右键托盘图标 → 退出 | 退出程序 |

> 在其他软件 / 文件资源管理器窗口中双击**不会**触发，仅桌面空白处有效。

## 工作原理

1. **系统托盘**：使用 WinForms `NotifyIcon`，通过 `Application.Run()` 消息循环驱动。
2. **双击检测**：安装全局低级鼠标钩子 `WH_MOUSE_LL`，通过两次 `WM_LBUTTONUP` 的时间间隔（< 系统双击时间）与位置间隔判定双击，再通过 `WindowFromPoint` 验证鼠标位于桌面窗口体系（句柄比对，排除文件资源管理器）。
3. **空白判断**：通过 ListView 命中测试 `LVM_HITTEST` 判断是否落在图标上，双击桌面图标不会触发。
4. **隐藏 / 显示图标**：向资源管理器的 `SHELLDLL_DefView` 窗口发送 `WM_COMMAND(0x7402)`（“显示桌面图标”切换命令）。
5. **开机自启**：向注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 写入程序路径（仅首次运行自动开启）。

## 下载（Release）

从 [Releases](https://github.com/feiyihao/DesktopIconHider/releases) 页面下载，共 4 个版本：

| 版本 | 说明 | 适用场景 |
| --- | --- | --- |
| `免安装-单文件版.zip` | 单个 exe，自包含，无需安装 .NET | 便携使用，拷贝即用 |
| `免安装-多文件版.zip` | 多个文件，自包含，无需安装 .NET | 便携使用，启动略快 |
| `安装-单文件版.exe` | Inno Setup 安装程序（内置单文件版） | 正式安装，含开始菜单/卸载 |
| `安装-多文件版.exe` | Inno Setup 安装程序（内置多文件版） | 正式安装，含开始菜单/卸载 |

## 编译环境

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/)（`dotnet --version` 验证）
- 可选：[Inno Setup 6](https://jrsoftware.org/isinfo.php)（仅构建安装版时需要）

## 构建

```powershell
cd D:\Documents\VScode项目\DesktopIconHider

# 普通构建（Debug 调试用）
dotnet build -c Release

# 免安装-单文件版（自包含，约 49 MB，单个 exe）
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false `
  -o dist/免安装-单文件版

# 免安装-多文件版（自包含，多个文件）
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:DebugType=None -p:DebugSymbols=false `
  -o dist/免安装-多文件版

# 安装版（先执行上面任一发布，再用 Inno Setup 编译）
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer-single.iss
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer-multi.iss
```

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
├── installer-single.iss      # 安装版（单文件）Inno Setup 脚本
├── installer-multi.iss       # 安装版（多文件）Inno Setup 脚本
└── README.md
```
