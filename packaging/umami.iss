; Windows installer for one published folder (Inno Setup 6.3+), built by packaging/package.sh:
;   ISCC /DVersion=2.3.12 /DArch=x64 /DAppFiles=publish\win-x64 /DOutputDir=dist /DOutputName=... umami.iss
; Installs for the current user only (no admin prompt): the editor never writes next to itself, settings and the user
; library live in %AppData%\UmamiThemeEditor.

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif

[Setup]
AppId={{6F0B5C59-3E0A-4C0E-9C2B-0D5A1A8B3D71}
AppName=Umami 3DS Theme Editor
AppVersion={#Version}
AppVerName=Umami 3DS Theme Editor {#Version}
AppPublisher=BakuretsuBaka
AppPublisherURL=https://github.com/BakuretsuBaka/Umami-3DS-Theme-Editor
AppSupportURL=https://github.com/BakuretsuBaka/Umami-3DS-Theme-Editor/issues
AppUpdatesURL=https://github.com/BakuretsuBaka/Umami-3DS-Theme-Editor/releases/latest
DefaultDirName={autopf}\Umami 3DS Theme Editor
DefaultGroupName=Umami 3DS Theme Editor
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0
SetupIconFile=..\src\ThemeEditor.App\Assets\umami.ico
UninstallDisplayIcon={app}\UmamiThemeEditor.exe
UninstallDisplayName=Umami 3DS Theme Editor
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
; The languages the editor speaks, where this Inno Setup has them (it has no Chinese)
#if FileExists(CompilerPath + "Languages\German.isl")
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
#endif
#if FileExists(CompilerPath + "Languages\Spanish.isl")
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
#endif
#if FileExists(CompilerPath + "Languages\French.isl")
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
#endif
#if FileExists(CompilerPath + "Languages\Italian.isl")
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
#endif
#if FileExists(CompilerPath + "Languages\Japanese.isl")
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"
#endif
#if FileExists(CompilerPath + "Languages\Korean.isl")
Name: "ko"; MessagesFile: "compiler:Languages\Korean.isl"
#endif
#if FileExists(CompilerPath + "Languages\Dutch.isl")
Name: "nl"; MessagesFile: "compiler:Languages\Dutch.isl"
#endif
#if FileExists(CompilerPath + "Languages\Polish.isl")
Name: "pl"; MessagesFile: "compiler:Languages\Polish.isl"
#endif
#if FileExists(CompilerPath + "Languages\BrazilianPortuguese.isl")
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
#endif
#if FileExists(CompilerPath + "Languages\Russian.isl")
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
#endif
#if FileExists(CompilerPath + "Languages\Swedish.isl")
Name: "sv"; MessagesFile: "compiler:Languages\Swedish.isl"
#endif
#if FileExists(CompilerPath + "Languages\Turkish.isl")
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppFiles}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Umami 3DS Theme Editor"; Filename: "{app}\UmamiThemeEditor.exe"
Name: "{autodesktop}\Umami 3DS Theme Editor"; Filename: "{app}\UmamiThemeEditor.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\UmamiThemeEditor.exe"; Description: "{cm:LaunchProgram,Umami 3DS Theme Editor}"; Flags: nowait postinstall skipifsilent
