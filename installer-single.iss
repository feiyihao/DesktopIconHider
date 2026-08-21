; 安装版（内置单文件版）Inno Setup 脚本
; 编译: "C:\Users\扉英贺\AppData\Local\Programs\Inno Setup 6\ISCC.exe" installer-single.iss

#define MyAppName "DesktopIconHider"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "DesktopIconHider"
#define MyAppExeName "DesktopIconHider.exe"

[Setup]
AppId={{8F6C2B4D-9E31-4A6E-8B5F-1C2D3E4F5A6B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=dist\安装-单文件版
OutputBaseFilename=DesktopIconHider-Setup-单文件版
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
; 单文件版：只安装单个 exe
Source: "dist\免安装-单文件版\DesktopIconHider.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent
