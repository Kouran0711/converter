; Build with scripts/Publish.ps1 -Installer, using Inno Setup 6.3 or later.
#ifndef AppVersion
  #define AppVersion "1.3.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#define AppName "NITH Converter"
#define AppPublisher "Nith Digital"
#define AppPublisherURL "https://nithdigital.com.br"
#define AppUpdatesURL "https://github.com/Kouran0711/converter/releases"
#define AppExeName "NITHConverter.exe"
#define BrandIcon "NITH Converter " + AppVersion + ".ico"

[Setup]
AppId={{5A22F590-A9F0-4B54-A732-F1E83C00A277}
AppName={#AppName}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppPublisherURL}
AppSupportURL={#AppPublisherURL}
AppUpdatesURL={#AppUpdatesURL}
AppCopyright=Nith Digital - nithdigital.com.br
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\NITH Converter
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts\installer
OutputBaseFilename=NITH Converter
SetupIconFile={#PublishDir}\Assets\Brand\NithConverter.ico
UninstallDisplayIcon={app}\{#BrandIcon}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=yes
UsePreviousAppDir=yes
UninstallDisplayName={#AppName}
VersionInfoVersion={#AppVersion}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar um atalho na área de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\Assets\Brand\NithConverter.ico"; DestDir: "{app}"; DestName: "{#BrandIcon}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#BrandIcon}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#BrandIcon}"; Tasks: desktopicon

[Run]
; Mantém a pasta de instalação visualmente limpa: arquivos de runtime continuam presentes,
; porém ocultos no Explorer padrão. O executável principal permanece visível.
Filename: "{cmd}"; Parameters: "/C attrib +h ""{app}\*"" /S /D & attrib -h ""{app}\{#AppExeName}"""; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExeName}"; Description: "Abrir {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

; Settings, logs, histórico e arquivos do usuário ficam fora da pasta do programa.
