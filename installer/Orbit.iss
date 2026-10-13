; Inno Setup script for Orbit (https://jrsoftware.org/isinfo.php).
; Built by .github/workflows/release.yml; locally:
;   iscc /DAppVersion=0.2.0 /DArch=x64 /DSourceDir=..\publish\Orbit-win-x64 installer\Orbit.iss
; Produces Orbit-<version>-win-<arch>-setup.exe in ..\dist.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef NumericVersion
  #define NumericVersion AppVersion
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\Orbit-win-" + Arch
#endif

[Setup]
AppId={{6F3B2C1E-8D4A-4E7B-9C5D-0A1B2C3D4E5F}
AppName=Orbit
AppVersion={#AppVersion}
AppVerName=Orbit {#AppVersion}
AppPublisher=Nalinish Ranjan
AppPublisherURL=https://github.com/NRanjan-17/Orbit
AppSupportURL=https://github.com/NRanjan-17/Orbit/issues
AppUpdatesURL=https://github.com/NRanjan-17/Orbit/releases
; Windows file versions must be numeric (0.2.0, not 0.2.0-beta).
VersionInfoVersion={#NumericVersion}
LicenseFile=..\LICENSE

; Install location: the user can change it on the "Select Destination Location" page.
DefaultDirName={autopf}\Orbit
DisableDirPage=no
DefaultGroupName=Orbit
DisableProgramGroupPage=yes
UsePreviousAppDir=yes

; Per-user by default (no admin prompt); the user may choose "all users" on the first page instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.19041

OutputDir=..\dist
OutputBaseFilename=Orbit-{#AppVersion}-win-{#Arch}-setup
SetupIconFile=..\src\Orbit\Assets\Orbit.ico
UninstallDisplayIcon={app}\Orbit.exe
UninstallDisplayName=Orbit
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; Close a running Orbit (it lives in the tray) before replacing its files.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start Orbit when Windows starts (in the tray)"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Orbit"; Filename: "{app}\Orbit.exe"
Name: "{autodesktop}\Orbit"; Filename: "{app}\Orbit.exe"; Tasks: desktopicon

[Registry]
; Same per-user Run entry Orbit's own "Start with Windows" setting writes (StartupRegistration.cs).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Orbit"; ValueData: """{app}\Orbit.exe"" --tray"; Tasks: startup
; Removed on uninstall even if Orbit created them itself (ValueType none = don't create, only clean up).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Orbit"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\Orbit.App"; ValueType: none; Flags: uninsdeletekey

[Run]
Filename: "{app}\Orbit.exe"; Description: "{cm:LaunchProgram,Orbit}"; Flags: nowait postinstall skipifsilent

; Settings, logs and the exact-battery key in %LOCALAPPDATA%\Orbit are kept on uninstall (reinstalling keeps
; your setup). Delete that folder to remove them.
