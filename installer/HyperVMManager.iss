#define AppName "VENOM VM-WARE"
#define AppVersion "1.1.8"
#define AppPublisher "VENOMDRMSUPPORT"
#define AppExeName "HyperVMManager.exe"
#define BuildOutput "..\\HyperVMManager\\bin\\Release\\net8.0-windows"
#define AppIcon "..\\HyperVMManager\\Assets\\AppIcon.ico"

[Setup]
AppId={{7BA11AF8-5582-4473-B64A-3F67BFC4A53D}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=HyperVMManager-Setup-{#AppVersion}
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\\{#AppExeName}
UninstallDisplayName={#AppName}
SetupIconFile={#AppIcon}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#BuildOutput}\\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\\{#AppName}"; Filename: "{app}\\{#AppExeName}"; IconFilename: "{app}\\{#AppExeName}"
Name: "{autodesktop}\\{#AppName}"; Filename: "{app}\\{#AppExeName}"; IconFilename: "{app}\\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent runascurrentuser
