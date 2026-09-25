# NITH Converter

Conversor de arquivos para Windows feito para transformar imagens, documentos, vídeos e áudios de forma simples, com controle de qualidade, resolução, FPS, bitrate, taxa de amostragem e outros ajustes sem sair do aplicativo.

**Nith Digital - nithdigital.com.br**

### Áudio
- Converte MP3, WAV, FLAC, AAC, M4A, OGG, OPUS, WMA e outros formatos aceitos pelo FFmpeg.
- Permite escolher bitrate, taxa de amostragem (22.05 a 96 kHz) e mono/estéreo.
- Também extrai o áudio de vídeos diretamente para um formato de áudio.

## O que ele faz

- Converte imagens para PNG, JPG, WEBP, BMP, TIFF, GIF, PDF, ICO e TGA.
- Abre diversos formatos comuns suportados pelo ImageMagick, incluindo PNG/JPG/WEBP/BMP/TIFF/GIF/ICO/TGA, AVIF/HEIC/HEIF, JP2, DDS, PCX, PNM, PSD, XCF e SVG quando os delegates necessários estão disponíveis.
- Lê PDF, PS e EPS com Ghostscript e permite escolher o DPI da renderização.
- Lê DOC, DOCX, XLS, XLSX, PPT, PPTX, ODT, ODS, ODP e RTF usando LibreOffice em modo headless; saída PDF preserva o documento completo e saídas de imagem usam a primeira página/planilha/slide.
- Converte vídeos comuns como MP4, AVI, MOV, MKV, WEBM, M4V, WMV, FLV, MPEG/MPG, TS/MTS/M2TS, 3GP e OGV quando o FFmpeg instalado oferece suporte.
- Saída de vídeo em MP4, WEBM ou GIF.
- Ajustes de qualidade de imagem, compressão PNG, largura máxima, qualidade de vídeo, FPS, bitrate de áudio, áudio ligado/desligado e cores de GIF.
- Histórico local de conversões e pasta padrão de saída.
- Tela de abertura animada, nova identidade visual e interface modernizada inspirada na Nith Digital.
- Verificação de atualização pelo GitHub Releases.
- Download automático do instalador de atualização, com verificação SHA-256 quando a release fornece hash/digest.

> O instalador oficial baixa automaticamente os mecanismos necessários no computador do usuário. A disponibilidade de formatos muito específicos ainda depende dos codecs/delegates presentes nesses builds.

## Dependências

O instalador oficial é **online**: o GitHub Actions compila apenas o NITH Converter. No computador do usuário, o próprio Setup baixa e prepara automaticamente os componentes ausentes, sem abrir CMD/PowerShell:

- ImageMagick portátil para imagens e formatos avançados.
- FFmpeg + FFprobe (LGPL shared) para áudio e vídeo.
- Ghostscript para PDF / PS / EPS.
- LibreOffice para Word / Excel / PowerPoint e OpenDocument (instalado automaticamente quando necessário).
- Microsoft Visual C++ Redistributable x64 quando necessário.

Em atualizações, componentes que já existem são reutilizados. Isso mantém `NITH.Converter.exe` menor e evita downloads enormes durante todo build no GitHub.

### Dependências de documentos (1.7.3)
O mecanismo de PDF/PS/EPS não executa mais o instalador do Ghostscript em segundo plano. O projeto referencia `Ghostscript.NET` + `Ghostscript.NativeAssets`, portanto a biblioteca nativa é publicada junto com o aplicativo. ImageMagick e FFmpeg continuam sendo obtidos pelo instalador online somente quando faltarem. Para documentos Office, o instalador também baixa e instala o LibreOffice 26.8.0 silenciosamente apenas quando ele ainda não estiver presente.

## Atualizações automáticas

O aplicativo consulta a lista pública de releases estáveis de `Kouran0711/converter`, interpreta as tags `vX.Y.Z` e escolhe a **maior versão semântica**. Assim ele não depende apenas do marcador “Latest” do GitHub para decidir se há atualização. Quando encontra uma versão superior à instalada, baixa o instalador estável `NITH.Converter.exe`.

A versão instalada é lida dos metadados gravados no executável durante o build. O workflow também valida que a versão do executável corresponde à versão informada na publicação antes de criar a release.

O instalador é validado por SHA-256 quando a release fornece o hash no `nith-update.json`, no arquivo `.sha256` ou no digest do próprio asset do GitHub. Releases antigas sem hash continuam detectáveis, mas o aplicativo informa que a verificação SHA-256 não estava disponível. Ao clicar em **Instalar agora**, o Windows mostra a confirmação de administrador (UAC), o Inno Setup atualiza os arquivos em `Program Files` e usa o Restart Manager para fechar/reabrir o aplicativo quando possível.

As releases novas incluem:

```text
NITH.Converter.exe
NITH.Converter.exe.sha256
nith-update.json
```

O workflow em `.github/workflows/release.yml` gera isso automaticamente quando uma tag `vX.Y.Z` é enviada ao GitHub.

Veja o guia completo em [`docs/UPDATES.md`](docs/UPDATES.md).

## Desenvolver e compilar

Requisitos recomendados:

- Windows 10/11 x64
- .NET 10 SDK (o `global.json` define a linha usada pelo projeto)
- Visual Studio Build Tools/Visual Studio com ferramentas Windows/WinUI
- Inno Setup 6.7.2+ para gerar o instalador online
- Internet no computador do usuário durante a primeira instalação dos mecanismos nativos

Publicar somente o aplicativo:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish.ps1
```

Publicar aplicativo + instalador:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish.ps1 -Installer
```

Publicar uma versão específica:

```powershell
.\scripts\Publish.ps1 -Installer -Version 1.7.3
```

Saídas:

```text
artifacts/publish/win-x64/
artifacts/installer/
```

O instalador exige administrador por projeto (`PrivilegesRequired=admin`). O aplicativo em si continua usando privilégios normais.

## Criar uma release

Depois de enviar o código para o GitHub, uma release pode ser criada apenas com uma tag:

```powershell
git tag v1.7.3
git push origin v1.7.3
```

O GitHub Actions compila no Windows, cria o instalador online, gera o checksum SHA-256 e o `nith-update.json`, e publica a GitHub Release automaticamente. ImageMagick e FFmpeg não são baixados manualmente no runner; o Ghostscript é restaurado pelo NuGet como dependência app-local e validado antes da release.

## Estrutura

| Pasta | Conteúdo |
| --- | --- |
| `src/NithConverter` | WinUI 3, telas, viewmodels, atualização e integração com Windows |
| `src/NithConverter.Core` | Conversões, formatos, ImageMagick, FFmpeg, persistência e cliente GitHub Releases |
| `Installer` | Instalador Inno Setup |
| `scripts` | publicação e geração da identidade visual |
| `tests` | testes de integração |
| `.github/workflows` | build/release automático |
| `docs` | documentação de distribuição, validação e atualizações |

Arquivos de build (`bin`, `obj`, `artifacts`, `.tools`, assets gerados etc.) ficam fora do Git por meio do `.gitignore`.

## Pasta de instalação

Para evitar a lista enorme de arquivos técnicos no Explorer, o instalador oculta somente arquivos de runtime do diretório raiz, como `.dll`, `.json`, `.pri`, `.pdb`, `.xml` e `.winmd`. O executável principal, o desinstalador e as pastas continuam visíveis. Em upgrades vindos de versões antigas, o instalador também restaura a visibilidade de itens que tenham sido ocultados pela regra anterior.

## Privacidade

As conversões são locais. Configurações, histórico limitado e logs ficam em `%LOCALAPPDATA%\NITH Converter`. A verificação de atualização acessa apenas endpoints públicos do GitHub Releases e a API pública do GitHub para localizar a release estável mais recente.

---

**Nith Digital - nithdigital.com.br**  
Repositório: `github.com/Kouran0711/converter`
