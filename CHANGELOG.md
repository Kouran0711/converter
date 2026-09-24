# Changelog

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
