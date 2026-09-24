# Núcleo do NITH Converter

Biblioteca .NET 10 sem pacotes externos. A UI recebe apenas modelos, serviços assíncronos e progresso; os executáveis permanecem processos separados.

- `FormatCatalog` é a fonte única dos formatos aceitos e respectivas saídas.
- `DependencyService` faz uma descoberta pontual em `Task.Run`, começando por `bin`, `bin/ImageMagick` e `bin/FFmpeg`. O resultado fica em cache até uma verificação explícita. Não há polling nem execução de CLI na inicialização.
- `ConversionService` mantém apenas uma conversão ativa, abre a origem sem permitir alteração, gera um arquivo temporário e efetiva a saída por renomeação no mesmo volume. Uma saída existente só é substituída com `Overwrite=true`. Mudanças no destino durante o trabalho exigem nova confirmação. O formato idêntico à origem produz `nome-convertido.ext`.
- Operações de metadados, criação de processos e diretórios acontecem fora da UI. Streams grandes usam I/O assíncrono. Caminhos com 240 caracteres ou mais usam uma cópia de trabalho curta, pois algumas distribuições das ferramentas mantêm limitações Win32 de caminho; a cópia final continua assíncrona e a publicação da saída continua atômica.
- `ProcessHelper` utiliza `ArgumentList`, shell desativado, janela oculta, prioridade abaixo do normal, pipes assíncronos e buffers limitados a 16 KiB por stream. Cancelamento mata a árvore, aguarda o processo e observa as tarefas de drenagem. Um Windows Job Object encerra os descendentes quando o aplicativo termina. Se o ambiente recusar o Job Object, permanece o encerramento explícito da árvore. Exceções de observadores não param a drenagem.
- FFmpeg atualiza progresso no máximo quatro vezes por segundo, com duração real do ffprobe. Quando a duração não está disponível, o progresso é indeterminado. A conclusão só é emitida depois da renomeação final. Não há timer da biblioteca.
- ImageMagick utiliza até quatro threads, 256 MiB de cache RAM, 512 MiB de mapeamento e 2 GiB de cache em disco. São limites do cache de pixels, e não um teto absoluto da memória do processo. FFmpeg usa até quatro threads do codec e uma para filtros; o custo depende do codec, resolução e arquivo.
- Histórico limitado a 100 entradas e JSON máximo de 2 MiB; configurações limitadas a 64 KiB na leitura. Escrita serializada, temporária e substituição atômica. Logs têm rotação em 512 KiB e registram apenas eventos, formatos, códigos e duração — nomes/caminhos não são enviados ao log. O histórico, por sua função, armazena nomes e caminho da saída localmente; pode ser apagado.

## Decisões iniciais dos formatos

Imagens animadas e arquivos multipágina usam o primeiro quadro/página. Leitura de PDF requer Ghostscript encontrado no sistema; escrita de PDF não requer esse componente. A política de segurança do ImageMagick continua respeitada e não é relaxada pelo aplicativo.

GIF de vídeo usa 12 FPS e até 960 pixels como padrão, com paleta em fluxo. A interface permite alterar FPS, largura e cores. MP4 usa MPEG-4 Part 2/AAC e WebM usa VP9/Opus; qualidade, resolução, FPS, áudio e bitrate são definidos por `ConversionOptions`. Documentos PDF/PS/EPS permitem escolher o DPI antes da renderização da primeira página.

`GitHubUpdateService` consulta somente a release pública mais recente do repositório oficial, baixa o instalador quando solicitado pela configuração do aplicativo e exige checksum SHA-256 correspondente. A elevação e execução do instalador ficam na camada Windows/UI.

Não há download ou instalação silenciosa de ImageMagick, FFmpeg ou Ghostscript. A licença concreta de cada distribuição de binários deve ser verificada no empacotamento. O núcleo não altera as políticas de segurança da máquina ou do ImageMagick.

**Nith Digital - nithdigital.com.br**
