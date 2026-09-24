# Build e distribuição do NITH Converter

## Publicação Windows x64

O projeto usa .NET 10, WinUI 3/Windows App SDK e Windows 10 build 19041 ou posterior.

Na raiz do projeto:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish.ps1
```

O resultado fica em:

```text
artifacts/publish/win-x64/
```

A publicação é self-contained para .NET e Windows App SDK. Distribua a pasta inteira; `PublishSingleFile=false` e `PublishTrimmed=false` são intencionais para compatibilidade com WinUI.

## Logo e ícones

Mantenha `logo.png` na raiz. Durante o build, `scripts/Generate-Assets.ps1` gera os PNGs de vários tamanhos e `app.ico` dentro de `src/NithConverter/Assets/Generated/`. Essa pasta é gerada e não entra no Git.

## Dependências de conversão

O aplicativo procura mecanismos na instalação local e em locais conhecidos do Windows:

- ImageMagick: imagens e criação de PDF;
- FFmpeg/ffprobe: vídeos e progresso;
- Ghostscript: leitura de PDF/PS/EPS.

Alguns formatos dependem dos delegates/codecs incluídos no build específico do ImageMagick ou FFmpeg.

## Instalador

Instale Inno Setup 6.3+ e execute:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish.ps1 -Installer
```

O instalador fica em `artifacts/installer/`.

O setup é **por máquina**, instala em `{autopf}\NITH Converter` (normalmente `Program Files`) e exige elevação:

```text
PrivilegesRequired=admin
```

Isso é intencional. O NITH Converter em si roda com privilégios normais; apenas instalação/atualização solicita UAC.

O Inno Setup está configurado para fechar aplicações que estejam usando os arquivos atualizados e tentar reiniciá-las ao final. O aplicativo registra suporte ao Windows Restart Manager.

## Atualizações pelo GitHub

O canal oficial é:

```text
https://github.com/Kouran0711/converter/releases
```

A atualização automática consulta a lista pública de releases estáveis e escolhe a maior versão semântica. Para a release escolhida, usa `nith-update.json`, checksum `.sha256` e/ou o digest SHA-256 do asset quando disponíveis. O workflow `.github/workflows/release.yml` produz esses arquivos automaticamente e marca a nova release como `Latest`.

Veja `docs/UPDATES.md`.

## Bundle opcional de ImageMagick/FFmpeg

`Installer/Dependencies/` continua disponível para preparar dependências redistribuíveis revisadas. Não inclua builds de terceiros sem revisar suas licenças e os codecs/delegates presentes.

Antes de empacotar:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-DependencyBundle.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish.ps1 -Installer -BundleDependencies
```

## Assinatura digital

O projeto não contém certificado nem segredo de assinatura. Para distribuição pública, assine o executável e o instalador com certificado de code signing e mantenha a chave fora do repositório.

**Nith Digital - nithdigital.com.br**
