# Dependências — modo offline legado

O instalador oficial do NITH Converter é **online** para ImageMagick e FFmpeg. O Ghostscript é integrado ao publish por `Ghostscript.NativeAssets` e não executa instalador externo. O Setup verifica e baixa somente ImageMagick/FFmpeg ausentes no computador do usuário.

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
