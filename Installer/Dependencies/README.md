# Dependências geradas

Não faça commit dos binários desta pasta.

Durante um build de instalador, `scripts/Prepare-Dependencies.ps1` cria automaticamente:

- `payload/` — ImageMagick, FFmpeg/ffprobe, Ghostscript e licenças/avisos;
- `prerequisites/VC_redist.x64.exe` — pré-requisito Microsoft executado pelo setup;
- `.cache/` — downloads e extrações temporárias;
- `bundle-manifest.json` — hashes e metadados do bundle.

Em seguida `scripts/Test-DependencyBundle.ps1` valida o conteúdo antes do publish.
