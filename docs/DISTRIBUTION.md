# Build e distribuição do NITH Converter

## Publicação Windows x64

O projeto usa .NET 10, WinUI 3/Windows App SDK e Windows 10 build 19041 ou posterior.

Na raiz do projeto:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish.ps1
```

O resultado fica em `artifacts/publish/win-x64/`. A publicação é self-contained para .NET e Windows App SDK.

## Instalador completo com dependências

Para uma entrega destinada ao usuário final, execute:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish.ps1 -Installer -Version 1.6.2
```

Quando `-Installer` é usado, o build prepara automaticamente o pacote de dependências antes de publicar. Não é necessário usar um switch extra.

`scripts/Prepare-Dependencies.ps1` baixa e prepara:

- ImageMagick para imagens e criação/leitura de formatos avançados;
- FFmpeg e ffprobe para áudio, vídeo, extração e progresso real;
- Ghostscript para leitura de PDF/PS/EPS pelo ImageMagick;
- Microsoft Visual C++ Redistributable x64 como pré-requisito do setup.

Os três mecanismos de conversão são colocados dentro de `bin/` na própria instalação do NITH Converter. O VC++ Runtime é executado silenciosamente pelo Inno Setup. Assim o usuário não precisa procurar nem clicar em instaladores externos.

O build valida os downloads e gera `Installer/Dependencies/bundle-manifest.json` com hashes dos arquivos incorporados. `scripts/Test-DependencyBundle.ps1` verifica a integridade e faz smoke tests dos executáveis no runner Windows. Uma release não deve ser publicada se um mecanismo obrigatório faltar ou não iniciar.

As pastas `Installer/Dependencies/.cache`, `payload` e `prerequisites` são geradas e ficam fora do Git.

## Logo e ícones

Mantenha `logo.png` na raiz. Durante o build, `scripts/Generate-Assets.ps1` gera os assets em `src/NithConverter/Assets/Generated/`.

## Instalador

O setup é por máquina, instala normalmente em `Program Files\NITH Converter` e exige UAC (`PrivilegesRequired=admin`). O aplicativo em si continua com privilégios normais.

O Inno Setup usa `CloseApplications=yes` e `RestartApplications=yes`. Em atualização, ele fecha/reabre o aplicativo quando o Windows permitir.

## Atualizações pelo GitHub

O canal oficial é `https://github.com/Kouran0711/converter/releases`.

O aplicativo tenta primeiro o manifesto pequeno `nith-update.json` da release Latest. Para compatibilidade, também possui fallback pela API de Releases e pela página `/releases/latest`. O download do instalador usa um cliente separado com timeout longo, proxy do sistema e fallback pelo BITS e, por fim, pelo `curl.exe` do Windows.

Veja `docs/UPDATES.md`.

## Licenças de terceiros

ImageMagick, FFmpeg e Ghostscript são projetos independentes. As licenças/avisos preparados pelo build acompanham o pacote. Ghostscript é disponibilizado pelo fornecedor sob AGPL ou licença comercial; revise a licença aplicável à forma de distribuição adotada pela Nith Digital.

## Assinatura digital

O projeto não contém certificado nem segredo de assinatura. Para distribuição pública, assine o executável e o instalador com certificado de code signing e mantenha a chave fora do repositório.

**Nith Digital - nithdigital.com.br**
