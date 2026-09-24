# Atualizações automáticas do NITH Converter

Este projeto usa **GitHub Releases** como canal de atualização.

## Como funciona

1. O aplicativo lê sua versão real dos metadados gravados no executável (`InformationalVersion` / `FileVersion`).
2. Se **Verificar ao abrir o aplicativo** estiver ativado, consulta as releases públicas de `Kouran0711/converter`.
3. Ignora drafts e pre-releases, interpreta as tags `vX.Y.Z` e escolhe a **maior versão semântica**.
4. Compara essa versão com a instalada. O nome do instalador não precisa conter o número da versão.
5. Para a release escolhida, procura `nith-update.json`, `NITH.Converter.exe`, o `.sha256` correspondente e também aceita o digest SHA-256 retornado pelo próprio GitHub.
6. Se existir uma versão superior, baixa o instalador para `%LOCALAPPDATA%\NITH Converter\Updates`.
7. Quando existe hash esperado, confere o SHA-256 antes de liberar a instalação. Releases antigas sem hash ainda são reconhecidas, mas a interface informa que a validação não estava disponível.
8. Ao clicar em **Instalar agora**, o instalador é iniciado com elevação e o Windows mostra UAC.
9. O Inno Setup atualiza `Program Files` com `PrivilegesRequired=admin`, `CloseApplications=yes` e `RestartApplications=yes`.

O código mantém um fallback via `/releases/latest` para situações em que a API pública do GitHub estiver temporariamente indisponível. Se esse fallback indicar que a versão instalada é a mais nova, mas a lista completa não puder ser confirmada, o aplicativo **não afirma que está atualizado**; ele mostra que a verificação não pôde ser concluída.

## Arquivos publicados em cada release

O workflow gera e publica automaticamente:

```text
NITH.Converter.exe
NITH.Converter.exe.sha256
NITH.Converter.Portable.zip
NITH.Converter.Portable.zip.sha256
nith-update.json
```

Exemplo de manifesto:

```json
{
  "schema": 2,
  "version": "1.5.3",
  "tag": "v1.5.3",
  "title": "NITH Converter 1.5.3",
  "installer": "NITH.Converter.exe",
  "sha256": "<sha256>",
  "size": 12345678
}
```

## Publicar a versão 1.5.3

Depois de enviar o código para `main`, abra **GitHub > Actions > Build e publicar release > Run workflow** e informe:

```text
1.5.3
```

O workflow:

- usa `windows-latest`;
- configura .NET 10;
- instala Inno Setup;
- executa `scripts/Publish.ps1 -Installer -Version <versão>`;
- valida que a versão gravada no executável corresponde ao número solicitado;
- cria instalador, pacote portátil, hashes e manifesto;
- cria ou atualiza a release;
- garante que ela não é draft nem pre-release;
- marca a release como **Latest**;
- confere se os assets obrigatórios realmente estão publicados.

Também é possível publicar por tag:

```powershell
git tag v1.5.3
git push origin v1.5.3
```

## Publicar a próxima atualização

Exemplo para `1.5.4`:

```powershell
git add -A
git commit -m "NITH Converter 1.5.4"
git push origin main
```

Depois execute o workflow manual com `1.5.4`, ou crie a tag:

```powershell
git tag v1.5.4
git push origin v1.5.4
```

Quem estiver usando `1.5.3` deverá ver **Instalada: 1.5.3 · GitHub: 1.5.4**, baixar o instalador e receber a opção **Instalar agora**.

## Regra de versão

Use versões crescentes no formato:

```text
v1.5.3
v1.5.4
v1.6.0
v2.0.0
```

Não reutilize a mesma tag para builds diferentes.

## Testar o atualizador

1. Publique e instale `v1.5.3`.
2. Faça uma pequena mudança no código.
3. Publique `v1.5.4`.
4. Abra a instalação `1.5.3`.
5. Em **Configurações > Atualizações**, confirme que a tela mostra **Instalada: 1.5.3 · GitHub: 1.5.4**.
6. Baixe a atualização e confirme a mensagem de SHA-256 validado.
7. Clique em **Instalar agora**, aceite o UAC e confirme que a versão `1.5.4` aparece após reiniciar.

## Se o GitHub Action falhar

Abra **GitHub > Actions > Build e publicar release** e veja o primeiro passo em vermelho. Os pontos mais comuns são SDK .NET incompatível, erro de compilação WinUI, Inno Setup, versão fora do formato ou permissão de `contents: write`.

Em **Settings > Actions > General**, confirme que o workflow pode usar `GITHUB_TOKEN` com acesso de escrita.

## Segurança

O SHA-256 detecta arquivo diferente do publicado, mas não substitui assinatura digital de código. Para distribuição pública madura, assine o executável e o instalador com certificado de code signing e mantenha as credenciais fora do repositório.

**Nith Digital - nithdigital.com.br**
