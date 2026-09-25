# Validação antes da release

## Build

- Compilar/publishar `win-x64` em Release.
- Confirmar que a versão gravada em `NITHConverter.exe` corresponde à versão solicitada.
- Gerar os assets visuais sem erro.
- Confirmar que `artifacts/publish` não contém os executáveis externos de ImageMagick/FFmpeg; deve conter `gsdll64.dll` e `Ghostscript.NET.dll` do Ghostscript app-local.

## Instalador online

Em uma VM Windows 10/11 x64 limpa:

1. Execute `NITH.Converter.exe`.
2. Confirme a página **Componentes necessários** depois da escolha da pasta.
3. Avance e confirme que os downloads aparecem dentro do próprio Setup, sem CMD/PowerShell.
4. Conclua a instalação e abra Configurações.
5. ImageMagick, FFmpeg + FFprobe e Ghostscript devem aparecer como **Instalado · pronto**.
6. Converta pelo menos uma imagem, um áudio, um vídeo, um PDF/PS/EPS e um arquivo DOCX/XLSX/PPTX para PDF.
7. Execute o instalador de novo: componentes existentes devem ser reutilizados, sem novo download desnecessário.
8. Confirme que executável, desinstalador e pastas importantes continuam visíveis em `Program Files`.

## Atualizador

1. Instale a versão anterior.
2. Publique uma versão superior estável com `nith-update.json`, `NITH.Converter.exe` e `.sha256`.
3. Clique **Verificar agora** e confirme a versão disponível.
4. Baixe a atualização e confirme o SHA-256.
5. Clique **Instalar agora**, aceite o UAC e confirme que o instalador mostra progresso sem abrir console.
6. Depois da atualização, confirme a nova versão e os mecanismos instalados.
7. Teste também em conexão lenta e, quando possível, atrás de proxy do sistema.

## Release

O workflow deve gerar apenas o instalador, seu SHA-256 e `nith-update.json`. Depois de publicar, ele valida a URL `releases/latest/download/nith-update.json` e confirma que o asset público é um executável Windows. Só considere a release pronta quando a execução Windows do GitHub Actions estiver verde.

## Compilador do instalador

O workflow oficial baixa o Inno Setup 6.7.3 da release oficial e valida o SHA-256 antes de instalar o compilador. A linha 6.7.2+ é necessária porque contém correção para extração de archives cujo primeiro item está em subdiretório.
