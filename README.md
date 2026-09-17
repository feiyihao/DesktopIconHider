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
2. **双击检测**：安装全局低级鼠标钩子 `WH_MOUSE_LL`，通过两次 `WM_LBUTTONDOWN` 的时间间隔（< 系统双击时间）与位置间隔判定双击，并通过 `WindowFromPoint` 验证鼠标位于桌面窗口体系（**句柄比对**，排除文件资源管理器等 shell 窗口）。
3. **拖动排除**：跟踪按压过程，按下后移动超过系统拖动阈值（`SM_CXDRAG`/`SM_CYDRAG`）即判定为拖动。拖动产生的抬起不计为单击，因此「拖动 + 紧接着单击」不会被误判为双击。
4. **空白判断**：通过 ListView 命中测试 `LVM_HITTEST` 判断是否落在图标上，命中图标 / 标签 / 复选框等状态图标（`LVMHT_ONITEM`）均视为非空白，不会触发。
5. **隐藏 / 显示图标**：向资源管理器的 `SHELLDLL_DefView` 窗口发送 `WM_COMMAND(0x7402)`（“显示桌面图标”切换命令）。
6. **开机自启**：向注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 写入程序路径（仅首次运行自动开启，之后尊重用户手动设置）。

### 性能设计（避免鼠标卡顿）

低级鼠标钩子 `WH_MOUSE_LL` 的回调会**阻塞整个系统的输入处理**，因此：

- 钩子回调内**只做轻量判断**（读取坐标、时间/位置比对、`WindowFromPoint`），不执行任何耗时操作；
- 跨进程命中测试与图标切换全部在**后台线程**（`Task.Run`）执行，并加防重入保护；
- **缓存进程句柄与远程缓冲区**，避免每次双击都执行 `OpenProcess`/`VirtualAllocEx`（这是首次调用和长时间闲置后卡顿的主要来源）；
- 启动时**预热**：提前完成模块加载、JIT 编译与跨进程缓存建立，首次双击也走热路径；
- 图标可见状态**实时查询**（`IsWindowVisible` + `LVM_GETITEMCOUNT`），与资源管理器自身切换保持同步。

## 下载（Release）

从 [Releases](https://github.com/feiyihao/DesktopIconHider/releases) 页面下载，共 4 个版本：

| 资产名 | 版本 | 说明 |
| --- | --- | --- |
| `DesktopIconHider-Portable-SingleFile.zip` | 免安装-单文件版 | 单个 exe，自包含，无需安装 .NET，拷贝即用 |
| `DesktopIconHider-Portable-MultiFile.zip` | 免安装-多文件版 | 多文件，自包含，无需安装 .NET，启动略快 |
| `DesktopIconHider-Setup-SingleFile.exe` | 安装-单文件版 | Inno Setup 安装程序（内置单文件版），含开始菜单/卸载 |
| `DesktopIconHider-Setup-MultiFile.exe` | 安装-多文件版 | Inno Setup 安装程序（内置多文件版），含开始菜单/卸载 |

## 更新日志

### v1.0.1

修复两个已记录的问题：

**1. 首次隐藏图标卡顿（鼠标指针冻结，完成后才跳到新位置）**

- **原因**：`WH_MOUSE_LL` 钩子回调会阻塞系统输入管线，而回调中同步执行了 `OpenProcess` + `VirtualAllocEx` + 跨进程 `SendMessage(LVM_HITTEST)` + `SendMessage(WM_COMMAND)` 等耗时操作。首次运行需加载 .NET 模块与 JIT，最慢；长时间闲置后内存被压缩，缓存重建又变慢。
- **修复**：钩子回调改为只做轻量判断，耗时操作移交后台线程；缓存进程句柄与远程缓冲区；启动时预热；加防重入保护。

**2. 拖动复选框后立即单击被误判为双击**

- **原因**：原逻辑只比较两次「左键抬起」的时间与位置，拖动释放的那次抬起与紧随其后的单击抬起正好凑成一对。
- **修复**：改为跟踪完整按压过程（按下 → 移动 → 抬起），按下后移动超过系统拖动阈值即判定为拖动，拖动不计为单击，无法与后续单击配对；同时用 `LVMHT_ONITEM` 标志位把复选框等状态图标也视为非空白。

**其他改进**

- 图标可见状态改为实时查询，与资源管理器自身「查看 → 显示桌面图标」操作保持同步，不再出现状态错乱；
- 托盘菜单点击改为异步执行，不再阻塞界面；
- 程序退出时释放缓存的跨进程句柄与内存。

### v1.0.0

首个版本：系统托盘常驻，双击桌面空白处隐藏 / 显示桌面图标，支持开机自启。

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
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"   # 或 C:\Program Files (x86)\Inno Setup 6\ISCC.exe
& $iscc installer-single.iss
& $iscc installer-multi.iss
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
