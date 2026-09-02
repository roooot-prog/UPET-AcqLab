; UPET AcqLab — Inno Setup script (optional if ISCC is installed)
; Compile: ISCC.exe installer\UPETAcqLab.iss

#define MyAppName "UPET AcqLab"
#define MyAppVersion "3.3.63"
#define MyAppPublisher "Universitatea din Petrosani"
#define MyAppExeName "UPETAcqLab.exe"

[Setup]
AppId={{8F3C2A11-9B7E-4AC0-8AB0-001234567890}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf32}\UPET AcqLab
DefaultGroupName=UPET AcqLab
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=UPET-AcqLab-Setup-3.3.63
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x86 x64compatible
ArchitecturesInstallIn64BitMode=
WizardStyle=modern
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a Desktop icon"; GroupDescription: "Additional icons:"
Name: "drivers"; Description: "Install USB/COM drivers (FTDI, CH340, CP210x, PL2303, VC++)"; GroupDescription: "Drivers:"; Flags: checkedonce

[Files]
Source: "..\publish-v2\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "drivers\*"; DestDir: "{app}\installer-drivers"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Install-UsbSerialDrivers.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Uninstall-UPETAcqLab.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Uninstall-UPETAcqLab.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "Diagnose-Spider8Usb.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Diagnose-Spider8Usb.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "CITESTE-MA-USB-SPIDER8.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{group}\Diagnostic Spider8 USB"; Filename: "{app}\Diagnose-Spider8Usb.cmd"; IconFilename: "{app}\app.ico"
Name: "{group}\Readme USB Spider8"; Filename: "{app}\CITESTE-MA-USB-SPIDER8.txt"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Install-UsbSerialDrivers.ps1"" -DriversRoot ""{app}\installer-drivers"""; StatusMsg: "Installing USB/COM drivers..."; Flags: runhidden waituntilterminated; Tasks: drivers
Filename: "{app}\{#MyAppExeName}"; Description: "Launch UPET AcqLab"; Flags: nowait postinstall skipifsilent
