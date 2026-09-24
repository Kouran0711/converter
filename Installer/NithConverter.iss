; Build with scripts/Publish.ps1 -Installer, using Inno Setup 6.3 or later.
#ifndef AppVersion
  #define AppVersion "1.7.0"
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
#define DependenciesDir "Dependencies"

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
OutputBaseFilename=NITH.Converter
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
Source: "{#DependenciesDir}\prerequisites\VC_redist.x64.exe"; DestDir: "{tmp}"; DestName: "VC_redist.x64.exe"; Flags: deleteafterinstall

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#BrandIcon}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#BrandIcon}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\VC_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Preparando componentes nativos do Windows..."; Flags: waituntilterminated runhidden
; Corrige instalações anteriores que ocultavam a pasta inteira. Primeiro restaura a visibilidade
; de todos os arquivos e diretórios; depois oculta SOMENTE o lixo técnico no diretório raiz.
; Pastas, desinstalador, executável principal e arquivos de suporte importantes permanecem visíveis.
Filename: "{cmd}"; Parameters: "/C attrib -h ""{app}\*"" /S /D >nul 2>&1 & exit /b 0"; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/C attrib +h ""{app}\*.dll"" >nul 2>&1 & attrib +h ""{app}\*.json"" >nul 2>&1 & attrib +h ""{app}\*.pri"" >nul 2>&1 & attrib +h ""{app}\*.pdb"" >nul 2>&1 & attrib +h ""{app}\*.xml"" >nul 2>&1 & attrib +h ""{app}\*.winmd"" >nul 2>&1 & attrib +h ""{app}\RestartAgent.exe"" >nul 2>&1 & attrib +h ""{app}\{#BrandIcon}"" >nul 2>&1 & attrib -h ""{app}\{#AppExeName}"" >nul 2>&1 & attrib -h ""{app}\unins*.exe"" >nul 2>&1 & attrib -h ""{app}\unins*.dat"" >nul 2>&1 & exit /b 0"; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExeName}"; Description: "Abrir {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

; Settings, logs, histórico e arquivos do usuário ficam fora da pasta do programa.
