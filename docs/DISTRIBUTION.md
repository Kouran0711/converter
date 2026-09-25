# Build e distribuição do NITH Converter

## Publicação Windows x64

O projeto usa .NET 10, WinUI 3/Windows App SDK e Windows 10 build 19041 ou posterior. A publicação do aplicativo é self-contained para .NET e Windows App SDK.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish.ps1
```

O resultado fica em `artifacts/publish/win-x64/`.

## Instalador online

Para gerar a entrega ao usuário final:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish.ps1 -Installer -Version 1.7.3
```

O build não baixa ImageMagick ou FFmpeg manualmente. O Ghostscript entra pelo restore NuGet oficial (`Ghostscript.NativeAssets`) e fica app-local. O Inno Setup gera `NITH.Converter.exe`; no PC do usuário, durante a instalação, o Setup verifica e prepara automaticamente ImageMagick, FFmpeg e o runtime VC++ quando necessário:

- ImageMagick portátil em `bin\ImageMagick`;
- FFmpeg + FFprobe LGPL shared em `bin\FFmpeg`;
- Ghostscript x64 integrado para PDF/PS/EPS;
- LibreOffice 26.8.0 para DOC/DOCX/XLS/XLSX/PPT/PPTX/ODT/ODS/ODP/RTF, baixado pelo instalador somente quando ausente;
- Microsoft Visual C++ Redistributable x64 quando necessário.

Os downloads usam HTTPS dentro do próprio Inno Setup, respeitam proxy do Windows, seguem redirects e exibem progresso. Nenhuma janela de CMD ou PowerShell é necessária para essa instalação. Componentes já presentes são reutilizados.

O modo antigo de bundle continua disponível apenas quando chamado explicitamente com `-BundleDependencies`; ele não é usado pelo workflow oficial.

## Logo e ícones

Mantenha `logo.png` na raiz. Durante o build, `scripts/Generate-Assets.ps1` gera os assets em `src/NithConverter/Assets/Generated/`.

## Instalador

O setup é por máquina, instala normalmente em `Program Files\NITH Converter` e exige UAC (`PrivilegesRequired=admin`). O aplicativo em si continua com privilégios normais.

O Inno Setup usa `CloseApplications=yes` e `RestartApplications=yes`. Em atualização, ele fecha/reabre o aplicativo quando o Windows permitir.

Requer Inno Setup 6.7.2 ou mais recente por causa de `download` + `extractarchive`.

## Atualizações pelo GitHub

O canal oficial é `https://github.com/Kouran0711/converter/releases`. Cada release nova publica apenas:

```text
NITH.Converter.exe
NITH.Converter.exe.sha256
nith-update.json
```

O aplicativo tenta primeiro o manifesto pequeno `nith-update.json` da release Latest. Também possui fallback pela API de Releases e pela página `/releases/latest`. O download do instalador usa `HttpClient` com timeout longo e fallbacks BITS/curl ocultos. Como ImageMagick e FFmpeg continuam sendo obtidos sob demanda e o Ghostscript é app-local, o download de atualização permanece previsível e não depende do instalador silencioso do Ghostscript.

Veja `docs/UPDATES.md`.

## Licenças de terceiros

ImageMagick, FFmpeg e Ghostscript são projetos independentes. Ghostscript é disponibilizado pelo fornecedor sob AGPL ou licença comercial; revise a licença aplicável à forma de distribuição adotada pela Nith Digital antes de uma distribuição comercial/proprietária.

## Assinatura digital

O projeto não contém certificado nem segredo de assinatura. Para distribuição pública, assine o executável e o instalador com certificado de code signing e mantenha a chave fora do repositório.

**Nith Digital - nithdigital.com.br**
