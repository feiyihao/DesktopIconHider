; 安装版（内置多文件版）Inno Setup 脚本
; 编译: "C:\Users\扉英贺\AppData\Local\Programs\Inno Setup 6\ISCC.exe" installer-multi.iss

#define MyAppName "DesktopIconHider"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "DesktopIconHider"
#define MyAppExeName "DesktopIconHider.exe"

[Setup]
AppId={{8F6C2B4D-9E31-4A6E-8B5F-1C2D3E4F5A6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=dist\安装-多文件版
OutputBaseFilename=DesktopIconHider-Setup-多文件版
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
; 多文件版：安装整个发布目录
Source: "dist\免安装-多文件版\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent
