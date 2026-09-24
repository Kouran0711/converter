# Dependências — modo offline legado

O instalador oficial do NITH Converter é **online**. O workflow normal de GitHub Actions **não baixa nem incorpora** ImageMagick, FFmpeg ou Ghostscript. Essas dependências são verificadas e baixadas pelo próprio instalador no computador do usuário.

Esta pasta existe apenas para o modo de build offline opcional:

```powershell
.\scripts\Publish.ps1 -Installer -BundleDependencies
```

Nesse modo explícito, `scripts/Prepare-Dependencies.ps1` pode criar:

- `payload/` — ImageMagick, FFmpeg/FFprobe, Ghostscript e avisos/licenças;
- `prerequisites/VC_redist.x64.exe`;
- `.cache/` — downloads e extrações temporárias;
- `bundle-manifest.json` — hashes e metadados do bundle.

`Test-DependencyBundle.ps1` valida esse conteúdo antes do publish. O workflow oficial de release não chama esse caminho.
