; NITH Converter - instalador online.
; Requer Inno Setup 6.7.2+ porque usa download + extractarchive em tempo de instalação.
#ifndef AppVersion
  #define AppVersion "1.7.3"
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

; ImageMagick e FFmpeg são baixados NO COMPUTADOR DO USUÁRIO durante a instalação.
; Ghostscript é app-local via Ghostscript.NativeAssets/NuGet para evitar o instalador NSIS silencioso,
; que pode bloquear em versões recentes. O GitHub Actions apenas restaura esse pacote como parte do build.
#define ImageMagickUrl "https://download.imagemagick.org/archive/binaries/ImageMagick-7.1.2-31-portable-Q16-HDRI-x64.7z"
#define ImageMagickHash "a6a83a77a5284a2cae5ca4a81d95e5fad21ecd56cdb647ee99f970e233504fff"
#define FFmpegUrl "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-09-22-13-18/ffmpeg-n9.0.2-3-ga5923073bf-win64-lgpl-shared-9.0.zip"
#define VCRedistUrl "https://aka.ms/vc14/vc_redist.x64.exe"
#define LibreOfficeUrl "https://download.documentfoundation.org/libreoffice/stable/26.8.0/win/x86_64/LibreOffice_26.8.0_Win_x86-64.msi"
#define LibreOfficeHash "4aa6c6e1895f4055104effcb556bd3362d20c6ad707c149543304f395ef9db95"

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
ArchiveExtraction=full
WizardStyle=modern
WizardResizable=yes
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
; ImageMagick: pacote portátil privado do app. Download e extração são feitos pelo Setup.
Source: "{#ImageMagickUrl}"; DestDir: "{app}\bin\ImageMagick"; DestName: "ImageMagick-7.1.2-31-portable-Q16-HDRI-x64.7z"; ExternalSize: 12_000_000; Hash: "{#ImageMagickHash}"; Flags: external download extractarchive ignoreversion; Check: NeedImageMagick

; FFmpeg/FFprobe: build LGPL compartilhado, menor que o pacote estático. O ZIP possui pasta raiz própria;
; o aplicativo faz descoberta recursiva dentro de bin.
Source: "{#FFmpegUrl}"; DestDir: "{app}\bin\FFmpeg"; DestName: "ffmpeg-n9.0.2-3-ga5923073bf-win64-lgpl-shared-9.0.zip"; ExternalSize: 77_000_000; Flags: external download extractarchive ignoreversion; Check: NeedFFmpeg

; Pré-requisito de sistema baixado para a pasta temporária e executado em modo oculto.
Source: "{#VCRedistUrl}"; DestDir: "{tmp}"; DestName: "VC_redist.x64.exe"; ExternalSize: 32_000_000; Flags: external download ignoreversion; Check: NeedVCRuntime

; LibreOffice: instalado silenciosamente apenas quando ausente. Dá suporte a DOC/DOCX/XLS/XLSX/PPT/PPTX/ODT/ODS/ODP/RTF.
Source: "{#LibreOfficeUrl}"; DestDir: "{tmp}"; DestName: "LibreOffice_26.8.0_Win_x86-64.msi"; ExternalSize: 375_000_000; Hash: "{#LibreOfficeHash}"; Flags: external download ignoreversion; Check: NeedLibreOffice

; Aplicativo propriamente dito. Ghostscript.NativeAssets vem junto do publish; ImageMagick/FFmpeg são externos.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\Assets\Brand\NithConverter.ico"; DestDir: "{app}"; DestName: "{#BrandIcon}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#BrandIcon}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#BrandIcon}"; Tasks: desktopicon

[Run]
; Nenhum terminal é mostrado. Se já estiver instalado, o Check pula completamente a etapa.
Filename: "{tmp}\VC_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Instalando Microsoft Visual C++ Runtime..."; Flags: waituntilterminated runhidden; Check: NeedVCRuntime; AfterInstall: VerifyVCRuntime

; Suite Office usada somente como mecanismo headless. O usuário não precisa interagir com o MSI.
Filename: "{sys}\msiexec.exe"; Parameters: "/i ""{tmp}\LibreOffice_26.8.0_Win_x86-64.msi"" /qn /norestart REGISTER_NO_MSO_TYPES=1 CREATEDESKTOPLINK=0 ISCHECKFORPRODUCTUPDATES=0 RebootYesNo=No"; StatusMsg: "Instalando suporte a Word, Excel e PowerPoint..."; Flags: waituntilterminated runhidden; Check: NeedLibreOffice; AfterInstall: VerifyLibreOffice

; Corrige instalações antigas que ocultavam a pasta inteira. Em seguida oculta só o lixo técnico na raiz.
Filename: "{cmd}"; Parameters: "/C attrib -h ""{app}\*"" /S /D >nul 2>&1 & exit /b 0"; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/C attrib +h ""{app}\*.dll"" >nul 2>&1 & attrib +h ""{app}\*.json"" >nul 2>&1 & attrib +h ""{app}\*.pri"" >nul 2>&1 & attrib +h ""{app}\*.pdb"" >nul 2>&1 & attrib +h ""{app}\*.xml"" >nul 2>&1 & attrib +h ""{app}\*.winmd"" >nul 2>&1 & attrib +h ""{app}\RestartAgent.exe"" >nul 2>&1 & attrib +h ""{app}\{#BrandIcon}"" >nul 2>&1 & attrib -h ""{app}\{#AppExeName}"" >nul 2>&1 & attrib -h ""{app}\unins*.exe"" >nul 2>&1 & attrib -h ""{app}\unins*.dat"" >nul 2>&1 & exit /b 0"; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExeName}"; Description: "Abrir {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
var
  DependencyPage: TOutputMsgMemoWizardPage;

function ContainsFileRecursive(const Root, FileName: String; Depth: Integer): Boolean;
var
  FindRec: TFindRec;
  Child: String;
begin
  Result := False;
  if (Root = '') or (Depth < 0) then
    Exit;

  if FileExists(AddBackslash(Root) + FileName) then
  begin
    Result := True;
    Exit;
  end;

  if (Depth = 0) or (not DirExists(Root)) then
    Exit;

  if FindFirst(AddBackslash(Root) + '*', FindRec) then
  begin
    try
      repeat
        if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
           (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Child := AddBackslash(Root) + FindRec.Name;
          if ContainsFileRecursive(Child, FileName, Depth - 1) then
          begin
            Result := True;
            Exit;
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function NeedImageMagick: Boolean;
begin
  Result := not ContainsFileRecursive(ExpandConstant('{app}\bin\ImageMagick'), 'magick.exe', 6);
end;

function NeedFFmpeg: Boolean;
var
  Root: String;
begin
  Root := ExpandConstant('{app}\bin\FFmpeg');
  Result := (not ContainsFileRecursive(Root, 'ffmpeg.exe', 6)) or
            (not ContainsFileRecursive(Root, 'ffprobe.exe', 6));
end;


function LibreOfficeExecutable: String;
var
  InstallPath: String;
begin
  Result := ExpandConstant('{autopf}\LibreOffice\program\soffice.com');
  if FileExists(Result) then
    Exit;
  Result := ExpandConstant('{autopf}\LibreOffice\program\soffice.exe');
  if FileExists(Result) then
    Exit;

  Result := '';
  InstallPath := '';
  if RegQueryStringValue(HKLM64, 'SOFTWARE\LibreOffice\UNO\InstallPath', '', InstallPath) then
  begin
    if FileExists(AddBackslash(InstallPath) + 'soffice.com') then
      Result := AddBackslash(InstallPath) + 'soffice.com'
    else if FileExists(AddBackslash(InstallPath) + 'soffice.exe') then
      Result := AddBackslash(InstallPath) + 'soffice.exe';
  end;
end;

function NeedLibreOffice: Boolean;
begin
  Result := LibreOfficeExecutable = '';
end;

function NeedVCRuntime: Boolean;
var
  Installed: Cardinal;
begin
  Installed := 0;
  Result := (not RegQueryDWordValue(HKLM64,
    'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed)) or
    (Installed <> 1);
end;

procedure VerifyVCRuntime;
begin
  if NeedVCRuntime then
    RaiseException('O Microsoft Visual C++ Runtime não foi instalado corretamente. Execute o instalador novamente.');
end;

procedure VerifyLibreOffice;
begin
  if NeedLibreOffice then
    RaiseException('O suporte a Word, Excel e PowerPoint não foi instalado corretamente. Execute o instalador novamente.');
end;


procedure InitializeWizard;
begin
  DependencyPage := CreateOutputMsgMemoPage(wpSelectDir,
    'Componentes necessários',
    'O NITH Converter prepara automaticamente tudo o que precisa para funcionar.',
    'Clique em Avançar. Componentes que já estiverem instalados serão ignorados.',
    'ImageMagick' + #13#10 +
    '  Imagens, formatos avançados e criação de PDF.' + #13#10#13#10 +
    'FFmpeg + FFprobe' + #13#10 +
    '  Áudio, vídeo, extração de áudio e progresso das conversões.' + #13#10#13#10 +
    'PDF / PS / EPS' + #13#10 +
    '  Mecanismo Ghostscript integrado ao próprio aplicativo; não instala outro programa.' + #13#10#13#10 +
    'Word / Excel / PowerPoint' + #13#10 +
    '  LibreOffice é baixado e instalado silenciosamente quando não estiver presente (~358 MB).' + #13#10#13#10 +
    'Microsoft Visual C++ Runtime' + #13#10 +
    '  Bibliotecas nativas exigidas por componentes do aplicativo.' + #13#10#13#10 +
    'ImageMagick, FFmpeg, LibreOffice e o runtime VC++ são preparados automaticamente quando faltarem. O mecanismo PDF/PS/EPS já está dentro do NITH Converter. Nenhuma janela de CMD ou PowerShell será aberta.');
end;
