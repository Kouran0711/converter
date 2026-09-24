# NITH Converter

Conversor de arquivos para Windows feito para transformar imagens, documentos e vídeos de forma simples, com controle de qualidade, resolução, FPS, áudio e outros ajustes sem sair do aplicativo.

**Nith Digital - nithdigital.com.br**

## O que ele faz

- Converte imagens para PNG, JPG, WEBP, BMP, TIFF, GIF, PDF, ICO e TGA.
- Abre diversos formatos comuns suportados pelo ImageMagick, incluindo PNG/JPG/WEBP/BMP/TIFF/GIF/ICO/TGA, AVIF/HEIC/HEIF, JP2, DDS, PCX, PNM, PSD, XCF e SVG quando os delegates necessários estão disponíveis.
- Lê PDF, PS e EPS com Ghostscript e permite escolher o DPI da renderização.
- Converte vídeos comuns como MP4, AVI, MOV, MKV, WEBM, M4V, WMV, FLV, MPEG/MPG, TS/MTS/M2TS, 3GP e OGV quando o FFmpeg instalado oferece suporte.
- Saída de vídeo em MP4, WEBM ou GIF.
- Ajustes de qualidade de imagem, compressão PNG, largura máxima, qualidade de vídeo, FPS, bitrate de áudio, áudio ligado/desligado e cores de GIF.
- Histórico local de conversões e pasta padrão de saída.
- Tela de abertura animada e interface WinUI 3.
- Verificação de atualização pelo GitHub Releases.
- Download automático do instalador de atualização, com verificação SHA-256 antes de permitir a instalação.

> A disponibilidade real de alguns formatos depende da compilação do ImageMagick, FFmpeg, Ghostscript e dos codecs/delegates instalados no computador.

## Atualizações automáticas

O aplicativo consulta a release pública mais recente de `Kouran0711/converter`. Quando encontra uma versão superior à instalada, ele pode baixar automaticamente o instalador `NITHConverter-Setup-x64-*.exe` e o arquivo `.sha256` correspondente.

O instalador é validado antes de ficar disponível. Ao clicar em **Instalar agora**, o Windows mostra a confirmação de administrador (UAC), o Inno Setup atualiza os arquivos em `Program Files` e usa o Restart Manager para fechar/reabrir o aplicativo quando possível.

O atualizador só reconhece releases que tenham **os dois arquivos**:

```text
NITHConverter-Setup-x64-<versao>.exe
NITHConverter-Setup-x64-<versao>.exe.sha256
```

O workflow em `.github/workflows/release.yml` gera isso automaticamente quando uma tag `vX.Y.Z` é enviada ao GitHub.

Veja o guia completo em [`docs/UPDATES.md`](docs/UPDATES.md).

## Desenvolver e compilar

Requisitos recomendados:

- Windows 10/11 x64
- .NET 10 SDK (o `global.json` define a linha usada pelo projeto)
- Visual Studio Build Tools/Visual Studio com ferramentas Windows/WinUI
- Inno Setup 6.3+ para gerar o instalador

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
.\scripts\Publish.ps1 -Installer -Version 1.1.1
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
git tag v1.1.1
git push origin v1.1.1
```

O GitHub Actions compila no Windows, cria o instalador, pacote portátil e checksums SHA-256 e publica a GitHub Release automaticamente.

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

## Privacidade

As conversões são locais. Configurações, histórico limitado e logs ficam em `%LOCALAPPDATA%\NITH Converter`. A verificação de atualização acessa apenas a API pública do GitHub e os assets da release.

---

**Nith Digital - nithdigital.com.br**  
Repositório: `github.com/Kouran0711/converter`
