; Inno Setup script for WindowsAI — a classic, modern Next-Next-Finish installer for the
; unpackaged (self-contained) build. Build the publish output first (see build.ps1), which
; passes /DMyAppSrc and /DMyAppVersion to this script.
;
;   iscc /DMyAppSrc="...\publish" /DMyAppVersion="1.0.0" installer\WindowsAI.iss
;
; The in-app first-run wizard handles all configuration; this installer only places files,
; shortcuts, an optional run-at-startup entry, and an uninstaller that preserves user data.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef MyAppSrc
  #define MyAppSrc "publish\win-x64"
#endif

#define MyAppName "WindowsAI"
#define MyAppPublisher "DrVanHelsing"
#define MyAppExeName "WindowsAI.App.exe"
#define MyAppId "{{6B3EA526-5D24-42CF-9C93-C7E7534B0420}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir=output
OutputBaseFilename=WindowsAI-Setup-{#MyAppVersion}
SetupIconFile={#MyAppSrc}\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
; Code signing (recommended to avoid SmartScreen) is configured by the build script via
; SignTool when a certificate is available — not required to produce the installer.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startup"; Description: "Start {#MyAppName} when I sign in (recommended — it lives in the tray)"; GroupDescription: "Startup:"

[Files]
; The entire self-contained publish output (app + .NET + WindowsAppSDK runtime + assets).
Source: "{#MyAppSrc}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Opt-in run-at-startup (HKCU Run). Removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Leave %LOCALAPPDATA%\WindowsAI (conversations, settings, encrypted keys) intact on uninstall.
Type: filesandordirs; Name: "{app}\logs"

[Code]
// Warn (don't block) if the WebView2 Evergreen runtime is missing — rich/math chat rendering needs it.
function WebView2Installed(): Boolean;
var
  Value: string;
begin
  Result :=
    RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Value) or
    RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Value) or
    RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Value);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not WebView2Installed() then
    MsgBox('WindowsAI uses the Microsoft Edge WebView2 runtime for rich chat (math, code, tables). ' +
           'It was not detected. Install it free from https://developer.microsoft.com/microsoft-edge/webview2/ ' +
           'for the best experience.', mbInformation, MB_OK);
end;
