# Atualizações automáticas do NITH Converter

Este projeto usa GitHub Releases como canal de atualização.

## Fluxo atual

1. O aplicativo lê a versão gravada no executável.
2. Tenta baixar `releases/latest/download/nith-update.json`, que é pequeno e não depende do limite normal da API para a descoberta básica.
3. Se o manifesto não estiver disponível, consulta até 100 releases pela API e escolhe a maior versão estável `vX.Y.Z`.
4. Como último fallback, segue `/releases/latest`.
5. Quando encontra versão superior, identifica `NITH.Converter.exe` e o SHA-256 publicado.
6. O instalador é baixado para `%LOCALAPPDATA%\NITH Converter\Updates`.
7. O download principal usa `HttpClient` com timeout de até 30 minutos, HTTP/1.1 compatível com proxies, proxy do sistema e credenciais padrão quando aplicável.
8. Se a pilha .NET falhar, o aplicativo tenta BITS (transferência nativa do Windows) e depois o `curl.exe` fornecido pelo Windows, com redirects e retries.
9. Antes da instalação, o arquivo precisa parecer um PE/EXE válido e, quando há hash publicado, o SHA-256 precisa conferir.
10. **Instalar agora** inicia o setup com UAC; o Inno Setup atualiza `Program Files`.

## Correção do problema das versões 1.6.1 e anteriores

O código antigo reutilizava um único `HttpClient` com timeout de 25 segundos tanto para consultar metadados pequenos quanto para baixar o instalador inteiro. Em conexões normais, um instalador maior podia ultrapassar esse limite e aparecer como falha de rede mesmo com a Internet funcionando.

A partir da 1.6.2, consulta e download usam clientes/limites separados. A interface também passa a preservar a mensagem técnica útil quando o download falha, em vez de sempre culpar genericamente a conexão.

Como a correção fica dentro do aplicativo novo, uma instalação antiga cujo updater já está falhando pode precisar instalar a 1.6.2 manualmente uma única vez. As atualizações posteriores passam a usar o mecanismo novo.

## Assets obrigatórios da release

O workflow publica no mínimo:

```text
NITH.Converter.exe
NITH.Converter.exe.sha256
NITH.Converter.Portable.zip
NITH.Converter.Portable.zip.sha256
nith-update.json
```

Exemplo do manifesto:

```json
{
  "schema": 2,
  "version": "1.6.2",
  "tag": "v1.6.2",
  "title": "NITH Converter 1.6.2",
  "installer": "NITH.Converter.exe",
  "sha256": "...",
  "size": 123456789
}
```

O número da versão vem do manifesto/tag e dos metadados do executável; ele não precisa fazer parte do nome do instalador.

## Publicação

Pelo GitHub Actions, use **Build e publicar release → Run workflow** e informe a versão, por exemplo `1.6.2`. O workflow prepara as dependências, publica o app, valida os mecanismos, gera o instalador e os checksums, cria/atualiza a release estável e confirma os assets obrigatórios.

Também é possível disparar pelo push de uma tag `vX.Y.Z`.

## Diagnóstico

Se uma atualização falhar, a mensagem exibida agora diferencia falha de descoberta de release, limite/API, asset ausente, timeout, HTML no lugar do EXE, checksum divergente e falha no fallback `curl`. Os logs locais ficam em `%LOCALAPPDATA%\NITH Converter`.
