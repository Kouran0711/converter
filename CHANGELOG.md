# Changelog

## 1.7.0
- Corrigido erro de sintaxe do PowerShell que interrompia o GitHub Actions antes da preparação das dependências.
- Preparação de dependências mais resiliente: consulta de digest do FFmpeg virou validação adicional, sem bloquear o build quando o GitHub não publica digest.
- Ghostscript 10.08.0 usa SHA-256 fixado e verificado para o instalador oficial x64.
- Workflow agora valida a sintaxe de todos os scripts PowerShell antes de iniciar downloads e compilação.
- Mantidas as dependências incorporadas (ImageMagick, FFmpeg/FFprobe e Ghostscript) e o VC++ Runtime no instalador.

## 1.6.2
- Instalador passa a incluir ImageMagick, FFmpeg, FFprobe e Ghostscript no próprio aplicativo; não é necessário instalar esses mecanismos manualmente.
- Instalador também prepara o Microsoft Visual C++ 2015-2022 Runtime x64, usado por componentes nativos.
- Pipeline de release baixa, verifica, testa e incorpora as dependências automaticamente antes de publicar.
- Atualizador corrigido para downloads grandes: cliente separado com timeout de 30 minutos, HTTP/1.1, proxy do Windows e fallback por BITS e curl.exe.
- Verificação de atualização prioriza o manifesto `nith-update.json`, reduzindo dependência do limite público da API do GitHub.
- Tela de mecanismos agora é apenas informativa, sem links/botões de instalação externos.
- Link da Nith Digital na barra lateral abre `nithdigital.com.br`.

## 1.5.3
- Corrigida a limpeza visual da pasta de instalação: versões anteriores ocultavam tudo, inclusive pastas e desinstalador.
- Em upgrades, o instalador restaura primeiro a visibilidade dos itens antigos que ficaram ocultos.
- Agora somente arquivos técnicos do runtime no diretório raiz ficam ocultos, como DLL, PRI, JSON, PDB, XML e WINMD.
- `NITHConverter.exe`, `unins*.exe`, `unins*.dat`, pastas e arquivos de suporte importantes permanecem visíveis no Explorer.
- `RestartAgent.exe` e o arquivo de ícone versionado continuam ocultos por serem itens internos de suporte.
- Mantidas as correções do atualizador introduzidas na 1.5.2.

## 1.5.2
- Atualizador refeito para consultar a lista de releases estáveis e escolher a maior versão sem depender apenas do marcador “Latest”.
- Corrigido o caso em que um manifesto antigo fazia o app dizer incorretamente que já estava atualizado.
- Suporte explícito ao instalador `NITH.Converter.exe`, além dos nomes legados.
- Versão instalada passa a ser lida de `InformationalVersion`/`FileVersion`, com validação no build antes de publicar a release.
- O updater usa SHA-256 do manifesto, do arquivo `.sha256` ou do digest fornecido pelo próprio GitHub quando disponível.
- Releases antigas sem checksum continuam detectáveis; o app informa quando o download não pôde ser validado por SHA-256.
- Workflow marca a release publicada como estável e `Latest` e valida a presença dos assets obrigatórios.
- Tela de atualização mostra separadamente a versão instalada e a versão mais recente encontrada no GitHub.


## 1.3.1
- Correção completa do aproveitamento de largura da interface em tela maximizada.
- Painel de opções ampliado e componentes reorganizados para evitar cortes e elementos amontoados.
- Nova identidade visual azul da Nith mantida com logo transparente.
- Ícone do executável, janela, instalador e atalhos unificado na nova marca.
- Atualizador refeito para usar manifesto da release antes da API do GitHub, reduzindo falhas e limites da API.
- Mensagens de atualização não culpam mais a conexão do usuário quando o GitHub retorna erro temporário.
- Instalador publicado com nome simples `NITH Converter.exe`.
- Arquivos internos do runtime ficam ocultos na pasta de instalação padrão, deixando visível o executável principal.


## 1.3.0
- Correção da tela de abertura após a reformulação visual: os elementos animados `LogoHolder` e `Orbit` voltaram a ser nomeados no XAML para corresponder ao code-behind.
- Mantém a nova interface Nith, logo transparente e conversão de áudio.


## 1.2.1
- Interface reformulada com layout mais aproveitado em telas grandes e menos espaço vazio nas laterais.
- Visual atualizado com gradientes em azul inspirados na identidade da Nith Digital.
- Logo principal substituída por uma nova identidade visual em fundo transparente.
- Informações técnicas de C# / WinUI removidas da interface pública.
- A versão exibida continua dinâmica e acompanha automaticamente a versão compilada/release.


## 1.2.0
- Conversão de áudio adicionada: MP3, WAV, FLAC, AAC, M4A, OGG, OPUS e WMA.
- Entrada de arquivos de áudio pelo seletor e arrastar/soltar.
- Extração de áudio de arquivos de vídeo.
- Ajustes de bitrate, taxa de amostragem e mono/estéreo.
- A versão exibida na interface passa a acompanhar diretamente a versão usada no build/release.

## 1.1.0

- Preparação completa para GitHub e GitHub Releases.
- Atualizador com verificação automática ao iniciar.
- Download automático opcional de novas versões.
- Validação SHA-256 do instalador antes da instalação.
- Instalação/atualização elevada por UAC via Inno Setup.
- Workflow de release automático para tags `vX.Y.Z`.
- Tela de abertura com logo animada.
- Interface de conversão e configurações reorganizada.
- Novas opções de FPS, resolução, bitrate de áudio e DPI de documentos.
- Catálogo ampliado de entradas ImageMagick/FFmpeg.
- Assinatura de projeto: Nith Digital - nithdigital.com.br.
