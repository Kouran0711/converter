# Validação antes da release

## Build

- Compilar/publishar `win-x64` em Release.
- Confirmar que a versão gravada em `NITHConverter.exe` corresponde à versão solicitada.
- Gerar os assets visuais sem erro.

## Dependências embarcadas

O workflow deve executar `Prepare-Dependencies.ps1` e `Test-DependencyBundle.ps1` automaticamente para releases com instalador.

Confirmar no publish final:

```text
bin/ImageMagick/magick.exe
bin/FFmpeg/ffmpeg.exe
bin/FFmpeg/ffprobe.exe
bin/Ghostscript/bin/gswin64c.exe
bundle-manifest.json
```

O setup também deve conter `VC_redist.x64.exe` como pré-requisito temporário. O teste do bundle confere hashes e deve iniciar ImageMagick, FFmpeg, ffprobe e Ghostscript no runner Windows.

Após instalar em uma VM limpa Windows 10/11 x64:

- a tela de mecanismos deve mostrar ImageMagick, FFmpeg e Ghostscript como instalados/prontos;
- não deve ser necessário clicar em links ou instalar esses mecanismos manualmente;
- converter pelo menos uma imagem, um áudio, um vídeo e ler um PDF/PS/EPS;
- confirmar que o executável, o desinstalador e as pastas importantes continuam visíveis em `Program Files`.

## Atualizador

1. Instale a versão anterior.
2. Publique uma versão superior estável com `nith-update.json`, instalador e SHA-256.
3. Clique **Verificar agora** e confirme que a versão disponível é exibida.
4. Faça o download em uma conexão limitada para confirmar que ele não é interrompido pelo antigo limite de 25 segundos.
5. Confirme o SHA-256.
6. Clique **Instalar agora**, aceite o UAC e confirme a nova versão após reiniciar.
7. Teste também com proxy do sistema quando aplicável.

## Release

O workflow deve falhar se qualquer dependência obrigatória, instalador, manifesto ou checksum estiver ausente. Só considere a release pronta quando a execução Windows do GitHub Actions estiver verde.
