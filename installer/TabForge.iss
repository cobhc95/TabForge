; TabForge installer (Inno Setup 7). Built by tools\Package-Release.ps1, which passes the version and
; the publish folder:  ISCC.exe /DAppVersion=0.5 /DFileVersion=0.5.0.0 /DSourceDir=..\build\TabForge installer\TabForge.iss
#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef SourceDir
  #define SourceDir "..\build\TabForge"
#endif
; File version resource: passed in as FileVersion (0.5.0.0); otherwise the numeric part of AppVersion ("0.2.0-beta.4" -> "0.2.0").
#ifdef FileVersion
  #define NumericVersion FileVersion
#elif Pos("-", AppVersion) > 0
  #define NumericVersion Copy(AppVersion, 1, Pos("-", AppVersion) - 1)
#else
  #define NumericVersion AppVersion
#endif

[Setup]
AppId={{6B7F1D3E-4C2A-4E9B-9F3D-7A1C5E2B8D40}
AppName=TabForge
AppVersion={#AppVersion}
AppVerName=TabForge {#AppVersion}
AppPublisher=TabForge contributors
AppPublisherURL=https://github.com/cobhc95/TabForge
AppSupportURL=https://github.com/cobhc95/TabForge/issues
AppUpdatesURL=https://github.com/cobhc95/TabForge/releases
VersionInfoVersion={#NumericVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoProductName=TabForge
DefaultDirName={autopf}\TabForge
DefaultGroupName=TabForge
; Per-user by default (no admin prompt); the user may choose an all-users install instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\TabForge\Assets\TabForge.ico
UninstallDisplayIcon={app}\TabForge.exe
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=TabForge-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nIt is recommended that you close all other applications before continuing.%n%nTabForge is an independent project. VST and ASIO are trademarks of Steinberg Media Technologies GmbH. Other product and company names are trademarks of their owners. TabForge is not affiliated with, sponsored or endorsed by any of them.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "associate"; Description: "Open .gp, .gp5, .gp4, .gp3, .gpx and TabForge (.tforge) files with TabForge"; GroupDescription: "Windows integration:"

[Files]
; The whole publish folder: TabForge.exe, the loose SoundTouch.Net.dll (LGPL, replaceable), licenses\ (licence texts), Assets\, Resources\.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD_PARTY.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\TabForge"; Filename: "{app}\TabForge.exe"
Name: "{group}\{cm:UninstallProgram,TabForge}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\TabForge"; Filename: "{app}\TabForge.exe"; Tasks: desktopicon

[Registry]
; The same per-user entries TabForge's own Settings > Windows integration switch writes and removes.
Root: HKCU; Subkey: "Software\Classes\TabForge.gp"; ValueType: string; ValueName: ""; ValueData: "Score file (.gp)"; Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.gp\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\TabForge.exe"",0"; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.gp\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\TabForge.exe"" ""%1"""; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\.gp\OpenWithProgids"; ValueType: none; ValueName: "TabForge.gp"; Flags: uninsdeletevalue; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.gp5"; ValueType: string; ValueName: ""; ValueData: "Score file (.gp5)"; Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.gp5\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\TabForge.exe"",0"; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.gp5\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\TabForge.exe"" ""%1"""; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\.gp5\OpenWithProgids"; ValueType: none; ValueName: "TabForge.gp5"; Flags: uninsdeletevalue; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.gp4"; ValueType: string; ValueName: ""; ValueData: "Score file (.gp4)"; Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.gp4\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\TabForge.exe"",0"; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.gp4\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\TabForge.exe"" ""%1"""; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\.gp4\OpenWithProgids"; ValueType: none; ValueName: "TabForge.gp4"; Flags: uninsdeletevalue; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.gp3"; ValueType: string; ValueName: ""; ValueData: "Score file (.gp3)"; Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.gp3\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\TabForge.exe"",0"; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.gp3\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\TabForge.exe"" ""%1"""; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\.gp3\OpenWithProgids"; ValueType: none; ValueName: "TabForge.gp3"; Flags: uninsdeletevalue; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.gpx"; ValueType: string; ValueName: ""; ValueData: "Score file (.gpx)"; Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.gpx\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\TabForge.exe"",0"; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.gpx\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\TabForge.exe"" ""%1"""; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\.gpx\OpenWithProgids"; ValueType: none; ValueName: "TabForge.gpx"; Flags: uninsdeletevalue; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.tforge"; ValueType: string; ValueName: ""; ValueData: "TabForge project"; Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.tforge\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\TabForge.exe"",0"; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\TabForge.tforge\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\TabForge.exe"" ""%1"""; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\.tforge\OpenWithProgids"; ValueType: none; ValueName: "TabForge.tforge"; Flags: uninsdeletevalue; Tasks: associate

[Run]
Filename: "{app}\TabForge.exe"; Description: "{cm:LaunchProgram,TabForge}"; Flags: nowait postinstall skipifsilent
