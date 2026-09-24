[CmdletBinding()]
param([string]$DependencyDirectory = '')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($DependencyDirectory)) {
    $DependencyDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'Installer\Dependencies'
}
$manifestPath = Join-Path $DependencyDirectory 'bundle-manifest.json'
$payload = [IO.Path]::GetFullPath((Join-Path $DependencyDirectory 'payload'))
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw 'O bundle precisa de bundle-manifest.json revisado. Consulte docs/DISTRIBUTION.md.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) { throw 'Versão de manifesto não suportada.' }
$components = @($manifest.components)
if ($components.Count -eq 0) { throw 'Nenhuma dependência foi declarada no manifesto.' }
$covered = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$componentNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

function Resolve-PayloadFile([string]$RelativePath) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath) -or $RelativePath.Contains(':')) {
        throw "Caminho inválido no manifesto: $RelativePath"
    }
    $resolved = [IO.Path]::GetFullPath((Join-Path $payload $RelativePath))
    if (-not $resolved.StartsWith($payload + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "O arquivo não está dentro do payload: $RelativePath"
    }
    $allowedRoots = @((Join-Path $payload 'bin'), (Join-Path $payload 'licenses'))
    if (-not ($allowedRoots | Where-Object { $resolved.StartsWith($_ + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) })) {
        throw "Arquivos do bundle devem ficar em bin/ ou licenses/: $RelativePath"
    }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "Arquivo ausente: $RelativePath" }
    $current = Get-Item -LiteralPath $resolved -Force
    while ($null -ne $current -and $current.FullName.Length -ge $payload.Length) {
        if (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Links e junctions não são aceitos no bundle: $RelativePath"
        }
        $current = if ($current -is [IO.FileInfo]) { $current.Directory } else { $current.Parent }
    }
    return $resolved
}

foreach ($component in $components) {
    if ($component.name -notin @('ImageMagick', 'FFmpeg')) { throw "Dependência não suportada: $($component.name)" }
    if (-not $componentNames.Add($component.name)) { throw "Dependência repetida: $($component.name)" }
    if ($component.reviewed -ne $true -or [string]::IsNullOrWhiteSpace($component.version) -or
        [string]::IsNullOrWhiteSpace($component.license)) {
        throw "Revise versão, licença e redistribuição de $($component.name) antes de empacotar."
    }
    $licenseFiles = @($component.licenseFiles)
    if ($licenseFiles.Count -eq 0) { throw "Textos de licença ausentes: $($component.name)" }
    foreach ($licenseFile in $licenseFiles) { $null = Resolve-PayloadFile $licenseFile }
    $null = Resolve-PayloadFile $component.executable
    $expectedExecutable = if ($component.name -eq 'FFmpeg') { 'bin/FFmpeg/ffmpeg.exe' } else { 'bin/ImageMagick/magick.exe' }
    if ($component.executable.Replace('\', '/') -ine $expectedExecutable) {
        throw "Executável esperado para $($component.name): $expectedExecutable"
    }
    if ($component.name -eq 'FFmpeg') {
        if ($component.license -notmatch '^(LGPL|GPL)-') { throw 'Declare a licença LGPL/GPL exata do build FFmpeg.' }
        $null = Resolve-PayloadFile $component.correspondingSourceFile
        $configuration = Resolve-PayloadFile $component.buildConfigurationFile
        $buildFlags = Get-Content -LiteralPath $configuration -Raw
        if ($buildFlags -match '--enable-nonfree') {
            throw 'Build FFmpeg com --enable-nonfree não pode compor este bundle redistribuível.'
        }
        if ($buildFlags -match '--enable-gpl' -and $component.license -notmatch '^GPL-') {
            throw 'A licença declarada não corresponde ao build FFmpeg com --enable-gpl.'
        }
        if ($buildFlags -match '--enable-version3' -and $component.license -notmatch '^(LGPL|GPL)-3') {
            throw 'A licença declarada não corresponde ao build FFmpeg com --enable-version3.'
        }
    }
    $files = @($component.files)
    if ($files.Count -eq 0) { throw "Lista de hashes vazia: $($component.name)" }
    foreach ($file in $files) {
        $resolved = Resolve-PayloadFile $file.path
        if ($file.sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw "SHA256 inválido: $($file.path)" }
        if ((Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash -ine $file.sha256) {
            throw "Hash divergente: $($file.path)"
        }
        if (-not $covered.Add($resolved)) { throw "Arquivo repetido no manifesto: $($file.path)" }
    }
}
foreach ($item in Get-ChildItem -LiteralPath $payload -Recurse -Force) {
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Link ou junction não permitido: $($item.Name)"
    }
    if (-not $item.PSIsContainer -and -not $covered.Contains($item.FullName)) {
        throw "Arquivo sem hash/revisão no manifesto: $($item.FullName.Substring($payload.Length + 1))"
    }
}
Write-Host "Bundle validado: $($components.Count) componente(s), $($covered.Count) arquivo(s)."
