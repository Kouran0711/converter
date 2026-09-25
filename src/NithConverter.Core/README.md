# NithConverter.Core

Núcleo de conversão e atualização do NITH Converter.

O aplicativo executa mecanismos externos por processo, com argumentos estruturados e diretório temporário privado por conversão. A descoberta prioriza os runtimes embarcados em `bin/ImageMagick`, `bin/FFmpeg` e `bin/Ghostscript` e mantém busca no sistema apenas como compatibilidade/fallback.

Imagens animadas e arquivos multipágina usam o primeiro quadro/página. PDF/PS/EPS usam ImageMagick com o Ghostscript embarcado; DOC/DOCX/XLS/XLSX/PPT/PPTX/ODT/ODS/ODP/RTF passam primeiro pelo LibreOffice headless; a política de segurança do ImageMagick continua respeitada e não é relaxada pelo aplicativo.

O instalador oficial é preparado pelo pipeline da raiz do projeto. O usuário final não precisa instalar ImageMagick, FFmpeg ou Ghostscript manualmente.
