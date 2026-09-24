[CmdletBinding()]
param([string]$DependencyDirectory = '')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($DependencyDirectory)) {
    $DependencyDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'Installer\Dependencies'
}
$DependencyDirectory = [IO.Path]::GetFullPath($DependencyDirectory)
$manifestPath = Join-Path $DependencyDirectory 'bundle-manifest.json'
$payload = [IO.Path]::GetFullPath((Join-Path $DependencyDirectory 'payload'))
$prereqRoot = [IO.Path]::GetFullPath((Join-Path $DependencyDirectory 'prerequisites'))
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'bundle-manifest.json não foi gerado. Execute scripts/Prepare-Dependencies.ps1.' }
if (-not (Test-Path -LiteralPath $payload)) { throw 'Payload de dependências ausente.' }

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 2) { throw 'Versão de manifesto de dependências não suportada.' }
$required = @(
    'bin\ImageMagick\magick.exe',
    'bin\FFmpeg\ffmpeg.exe',
    'bin\FFmpeg\ffprobe.exe',
    'bin\Ghostscript\bin\gswin64c.exe'
)
foreach ($relative in $required) {
    $path = Join-Path $payload $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Dependência obrigatória ausente: $relative" }
}
$vc = Join-Path $prereqRoot 'VC_redist.x64.exe'
if (-not (Test-Path -LiteralPath $vc -PathType Leaf)) { throw 'VC_redist.x64.exe ausente.' }

$manifestFiles = @{}
foreach ($file in @($manifest.files)) { $manifestFiles[$file.path.Replace('/', '\')] = $file }
foreach ($file in Get-ChildItem -LiteralPath $payload -Recurse -Force -File) {
    if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Link/junction não permitido: $($file.FullName)" }
    $relative = $file.FullName.Substring($payload.Length + 1)
    if (-not $manifestFiles.ContainsKey($relative)) { throw "Arquivo sem hash no manifesto: $relative" }
    $expected = [string]$manifestFiles[$relative].sha256
    if ($expected -notmatch '^[A-Fa-f0-9]{64}$') { throw "SHA-256 inválido no manifesto: $relative" }
    if ((Get-Sha256Hex -Path $file.FullName) -ine $expected) { throw "Hash divergente: $relative" }
}
foreach ($entry in $manifestFiles.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $entry.Key) -PathType Leaf)) { throw "Arquivo do manifesto ausente: $($entry.Key)" }
}
$prereq = @($manifest.prerequisites) | Where-Object { $_.path -eq 'prerequisites/VC_redist.x64.exe' } | Select-Object -First 1
if ($null -eq $prereq -or (Get-Sha256Hex -Path $vc) -ine [string]$prereq.sha256) { throw 'Hash do VC++ Runtime não confere com o manifesto.' }

# Smoke tests reais no runner Windows. Se qualquer mecanismo não iniciar, a release falha aqui.
$commands = @(
    @{ Name='ImageMagick'; Path=(Join-Path $payload 'bin\ImageMagick\magick.exe'); Args=@('-version') },
    @{ Name='FFmpeg'; Path=(Join-Path $payload 'bin\FFmpeg\ffmpeg.exe'); Args=@('-version') },
    @{ Name='FFprobe'; Path=(Join-Path $payload 'bin\FFmpeg\ffprobe.exe'); Args=@('-version') },
    @{ Name='Ghostscript'; Path=(Join-Path $payload 'bin\Ghostscript\bin\gswin64c.exe'); Args=@('-version') }
)
foreach ($command in $commands) {
    & $command.Path @($command.Args) *> $null
    if ($LASTEXITCODE -ne 0) { throw "$($command.Name) não iniciou corretamente (código $LASTEXITCODE)." }
}

# Smoke tests funcionais: não basta o .exe abrir; cada engine precisa conseguir produzir um arquivo.
$smoke = Join-Path $DependencyDirectory '.smoke'
Remove-Item -LiteralPath $smoke -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $smoke -Force | Out-Null
try {
    $magick = Join-Path $payload 'bin\ImageMagick\magick.exe'
    $ffmpeg = Join-Path $payload 'bin\FFmpeg\ffmpeg.exe'
    $ghostscript = Join-Path $payload 'bin\Ghostscript\bin\gswin64c.exe'

    $imageOut = Join-Path $smoke 'imagemagick.png'
    & $magick '-size' '16x16' 'xc:#22bff5' $imageOut *> $null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $imageOut) -or (Get-Item -LiteralPath $imageOut).Length -eq 0) {
        throw 'ImageMagick iniciou, mas não conseguiu gerar uma imagem de teste.'
    }

    $audioOut = Join-Path $smoke 'ffmpeg.wav'
    & $ffmpeg '-hide_banner' '-loglevel' 'error' '-f' 'lavfi' '-i' 'sine=frequency=440:duration=0.15' '-c:a' 'pcm_s16le' '-y' $audioOut *> $null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $audioOut) -or (Get-Item -LiteralPath $audioOut).Length -eq 0) {
        throw 'FFmpeg iniciou, mas não conseguiu gerar um áudio de teste.'
    }

    $psIn = Join-Path $smoke 'ghostscript.ps'
    @'
%!PS-Adobe-3.0
%%Pages: 1
%%BoundingBox: 0 0 72 72
%%Page: 1 1
0 0 1 setrgbcolor
10 10 52 52 rectfill
showpage
%%EOF
'@ | Set-Content -LiteralPath $psIn -Encoding ascii
    $gsOut = Join-Path $smoke 'ghostscript.png'
    $gsRoot = Split-Path -Parent (Split-Path -Parent $ghostscript)
    $gsLibParts = @(
        (Join-Path $gsRoot 'lib'),
        (Join-Path $gsRoot 'Resource\Init'),
        (Join-Path $gsRoot 'Resource\Font')
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Container }
    $oldGsLib = $env:GS_LIB
    try {
        if ($gsLibParts.Count -gt 0) { $env:GS_LIB = ($gsLibParts -join [IO.Path]::PathSeparator) }
        & $ghostscript '-q' '-dSAFER' '-dBATCH' '-dNOPAUSE' '-sDEVICE=png16m' '-r72' "-sOutputFile=$gsOut" $psIn *> $null
    }
    finally { $env:GS_LIB = $oldGsLib }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $gsOut) -or (Get-Item -LiteralPath $gsOut).Length -eq 0) {
        throw 'Ghostscript iniciou, mas não conseguiu renderizar um documento de teste.'
    }
}
finally {
    Remove-Item -LiteralPath $smoke -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Bundle validado: $($manifestFiles.Count) arquivo(s), engines iniciaram e concluíram smoke tests funcionais."
