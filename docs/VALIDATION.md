# Checklist de validação

Este arquivo é um roteiro de teste para cada release. Resultados antigos e artefatos locais não são mantidos no repositório Git.

## Build

```powershell
dotnet --version
.\scripts\Publish.ps1 -Installer
```

Confirme que existem:

```text
artifacts/publish/win-x64/NITHConverter.exe
artifacts/installer/NITHConverter-Setup-x64-*.exe
```

## Interface

- splash animado aparece e encerra sem travar a janela;
- logo/ícone aparecem corretamente;
- escolher arquivo e drag & drop funcionam;
- navegação Converter / Histórico / Configurações funciona;
- versão e assinatura Nith Digital aparecem corretamente.

## Conversão

Teste pelo menos:

- PNG -> JPG e WEBP;
- JPG -> PNG;
- PDF -> PNG com 96/150/300 DPI;
- MP4 -> MP4 com qualidade/FPS diferentes;
- MP4 -> WEBM;
- MP4 -> GIF com FPS/cores diferentes;
- áudio mantido e removido;
- nomes com espaços e acentos;
- destino existente e cancelamento.

Os formatos avançados devem ser testados com o build exato do ImageMagick/FFmpeg distribuído ou documentado.

## Instalador

- executar o setup deve mostrar UAC;
- instalar em `Program Files`;
- atalhos funcionam;
- reinstalação por cima de versão anterior funciona;
- desinstalação remove o programa sem apagar arquivos convertidos/configurações locais indevidamente.

## Atualização GitHub

1. Instale a versão `vX.Y.Z`.
2. Publique `vX.Y.(Z+1)`.
3. Abra a versão antiga.
4. Confirme detecção da release.
5. Confirme download automático e validação SHA-256.
6. Clique **Instalar agora**.
7. Aceite UAC.
8. Confirme que a versão nova foi instalada e que o aplicativo volta a abrir.

## Segurança

- nunca coloque `.pfx`, senhas, tokens ou chaves no Git;
- valide que `.gitignore` exclui `bin`, `obj`, `artifacts`, `.tools` e certificados;
- para distribuição pública, use assinatura digital de código.

**Nith Digital - nithdigital.com.br**
