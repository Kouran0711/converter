# Atualizações automáticas do NITH Converter

Este projeto já está preparado para usar **GitHub Releases** como canal de atualização.

## Como funciona

1. O NITH Converter inicia normalmente.
2. Se **Verificar ao abrir o aplicativo** estiver ativado, consulta a release pública mais recente em `Kouran0711/converter`.
3. Compara a tag da release (`v1.2.1`, por exemplo) com a versão do executável instalado.
4. Se existir uma versão superior, procura o instalador `NITHConverter-Setup-x64-*.exe` e seu `.sha256`.
5. Se **Baixar novas versões automaticamente** estiver ativado, baixa os dois em `%LOCALAPPDATA%\NITH Converter\Updates`.
6. O SHA-256 do instalador baixado é comparado com o checksum publicado.
7. Depois da validação, aparece **Instalar agora**.
8. O instalador é iniciado com elevação (`runas`), então o Windows mostra UAC.
9. O Inno Setup atualiza a instalação em `Program Files`. Ele foi configurado com `PrivilegesRequired=admin`, `CloseApplications=yes` e `RestartApplications=yes`.

## Primeira publicação no GitHub

Na pasta do projeto:

```powershell
git init
git add .
git commit -m "NITH Converter 1.2.0"
git branch -M main
git remote add origin https://github.com/Kouran0711/converter.git
git push -u origin main
```

Se `origin` já existir:

```powershell
git remote -v
git remote set-url origin https://github.com/Kouran0711/converter.git
git push -u origin main
```

## Criar a primeira release

O código desta entrega está com versão base `1.2.0`. Depois que `main` estiver no GitHub:

```powershell
git tag v1.2.0
git push origin v1.2.0
```

A tag dispara `.github/workflows/release.yml`.

O workflow:

- usa `windows-latest`;
- instala/configura .NET 10;
- instala Inno Setup;
- executa `scripts/Publish.ps1 -Installer -Version <versão>`;
- cria um ZIP portátil;
- calcula SHA-256 do instalador e do ZIP;
- cria uma GitHub Release com notas automáticas.

## Publicar a próxima atualização

Faça suas alterações e aumente a versão. Você não precisa editar o `.csproj` toda vez se publicar pelo workflow, porque a versão da tag é enviada ao build.

Exemplo para `1.2.1`:

```powershell
git add .
git commit -m "Atualização 1.2.1"
git push origin main
git tag v1.2.1
git push origin v1.2.1
```

Quem estiver usando `1.2.0` verá `1.2.1`, o instalador será baixado automaticamente (configuração padrão) e ficará pronto para instalação.

## Regra de versão

Use tags no formato:

```text
v1.2.0
v1.2.1
v1.2.0
v2.0.0
```

Evite reutilizar a mesma tag para builds diferentes.

## Testar o atualizador

1. Publique e instale `v1.2.0`.
2. Faça uma pequena mudança no código.
3. Publique a tag `v1.2.1`.
4. Abra a instalação `1.2.0`.
5. O app deve encontrar `1.2.1` e baixar o instalador.
6. Confira a tela **Configurações > Atualizações**.
7. Clique em **Instalar agora** e aceite o UAC.
8. Depois da instalação, confirme a versão exibida no menu/configurações.

## Se o GitHub Action falhar

Abra **GitHub > Actions > Build e publicar release** e veja o primeiro passo em vermelho. Os pontos mais comuns são:

- SDK .NET incompatível;
- erro de compilação WinUI;
- Inno Setup não instalado pelo runner;
- tag fora do formato `vX.Y.Z`;
- permissão de `contents: write` bloqueada pela configuração do repositório.

Em **Settings > Actions > General**, confirme que os workflows podem usar o `GITHUB_TOKEN` com acesso de escrita quando necessário.

## Segurança

O checksum SHA-256 detecta arquivo corrompido ou diferente do publicado, mas não substitui assinatura digital de código. Para distribuição pública mais madura, o próximo passo recomendado é assinar `NITHConverter.exe` e o instalador com um certificado de assinatura de código e guardar o certificado/segredo fora do repositório, preferencialmente em GitHub Actions Secrets ou em um serviço de assinatura.

**Nith Digital - nithdigital.com.br**
