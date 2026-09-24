[CmdletBinding()]
param(
    [string]$DependencyDirectory = '',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if ([string]::IsNullOrWhiteSpace($DependencyDirectory)) {
    $DependencyDirectory = Join-Path $projectRoot 'Installer\Dependencies'
}
$DependencyDirectory = [IO.Path]::GetFullPath($DependencyDirectory)
$payload = Join-Path $DependencyDirectory 'payload'
$cache = Join-Path $DependencyDirectory '.cache'
$prerequisites = Join-Path $DependencyDirectory 'prerequisites'
$manifestPath = Join-Path $DependencyDirectory 'bundle-manifest.json'

$ImageMagickVersion = '7.1.2-31'
$ImageMagickFile = "ImageMagick-$ImageMagickVersion-Q16-HDRI-x64-static.exe"
$ImageMagickUrl = "https://download.imagemagick.org/archive/binaries/$ImageMagickFile"
$FFmpegVersion = '9.0'
$FFmpegAsset = 'ffmpeg-n9.0-latest-win64-lgpl-9.0.zip'
$FFmpegTag = 'latest'
$FFmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$FFmpegTag/$FFmpegAsset"
$GhostscriptVersion = '10.08.0'
$GhostscriptTag = 'gs10080'
$GhostscriptAsset = 'gs10080w64.exe'
$GhostscriptUrl = "https://github.com/ArtifexSoftware/ghostpdl-downloads/releases/download/$GhostscriptTag/$GhostscriptAsset"
$GhostscriptSha256 = '52a91b8bf09298788d7a57b9206127026c23eacd75405f0a131e26dc381dce50'
$VcRedistUrl = 'https://aka.ms/vc14/vc_redist.x64.exe'

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
        $sha.Dispose()
    }
}

function Invoke-Download {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    $last = $null
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        try {
            Write-Host "Baixando ($attempt/4): $Uri"
            Invoke-WebRequest -Uri $Uri -OutFile $Destination -MaximumRedirection 10 -Headers @{ 'User-Agent' = 'NITHConverter-Build/1.7.0' }
            if ((Get-Item -LiteralPath $Destination).Length -lt 1024) { throw "Download muito pequeno: $Uri" }
            return
        }
        catch {
            $last = $_
            Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
            if ($attempt -lt 4) { Start-Sleep -Seconds ([Math]::Min(8, $attempt * 2)) }
        }
    }
    throw "Falha ao baixar $Uri. $($last.Exception.Message)"
}

function Get-GitHubAssetSha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$AssetName
    )
    $headers = @{
        'Accept' = 'application/vnd.github+json'
        'User-Agent' = 'NITHConverter-Build/1.7.0'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    try {
        $endpoint = if ($Tag -ieq 'latest') {
            "https://api.github.com/repos/$Repository/releases/latest"
        }
        else {
            "https://api.github.com/repos/$Repository/releases/tags/$Tag"
        }
        $release = Invoke-RestMethod -Uri $endpoint -Headers $headers
        $asset = @($release.assets) | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
        if ($null -eq $asset) {
            throw ("Asset não encontrado em {0}/{1}: {2}" -f $Repository, $Tag, $AssetName)
        }
        if ($asset.digest -match '^sha256:([A-Fa-f0-9]{64})$') {
            return $Matches[1].ToLowerInvariant()
        }
        Write-Warning ("O GitHub não forneceu digest SHA-256 para {0}. O pacote será validado por HTTPS, estrutura do arquivo e teste funcional." -f $AssetName)
        return $null
    }
    catch {
        Write-Warning ("Não foi possível consultar o digest do GitHub para {0}: {1}. A preparação continuará e o binário será testado antes da publicação." -f $AssetName, $_.Exception.Message)
        return $null
    }
}
function Assert-Hash {
    param([string]$Path, [string]$Expected)
    $actual = Get-Sha256Hex -Path $Path
    if ($actual -ine $Expected) { throw "SHA-256 divergente para $([IO.Path]::GetFileName($Path)). Esperado $Expected, recebido $actual." }
}

function Invoke-ProcessChecked {
    param([string]$FilePath, [string[]]$ArgumentList)
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FilePath
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    foreach ($argument in $ArgumentList) { [void]$start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw "Não foi possível iniciar $([IO.Path]::GetFileName($FilePath))." }
    try {
        $process.WaitForExit()
        if ($process.ExitCode -notin @(0, 3010, 1641)) {
            throw "$([IO.Path]::GetFileName($FilePath)) retornou código $($process.ExitCode)."
        }
    }
    finally { $process.Dispose() }
}

if ($Force) {
    Remove-Item -LiteralPath $payload -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $cache -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $prerequisites -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $manifestPath -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $payload, $cache, $prerequisites -Force | Out-Null

# 1) ImageMagick: instalador oficial assinado, instalado apenas em uma pasta temporária do build.
$imInstaller = Join-Path $cache $ImageMagickFile
if (-not (Test-Path -LiteralPath $imInstaller)) { Invoke-Download -Uri $ImageMagickUrl -Destination $imInstaller }
try {
    $signature = Get-AuthenticodeSignature -FilePath $imInstaller
    if ($signature.Status -eq 'Valid') { Write-Host "Assinatura ImageMagick válida: $($signature.SignerCertificate.Subject)" }
    else { Write-Warning "Não foi possível validar a assinatura Authenticode do ImageMagick no runner: $($signature.Status). O download veio do domínio oficial via HTTPS." }
} catch { Write-Warning "Validação Authenticode do ImageMagick indisponível: $($_.Exception.Message)" }
$imStage = Join-Path $cache 'imagemagick-stage'
Remove-Item -LiteralPath $imStage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $imStage -Force | Out-Null
Invoke-ProcessChecked -FilePath $imInstaller -ArgumentList @('/SP-', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', "/DIR=$imStage")
$magickExe = Get-ChildItem -LiteralPath $imStage -Filter 'magick.exe' -Recurse -File | Select-Object -First 1
if ($null -eq $magickExe) { throw 'O ImageMagick foi preparado, mas magick.exe não foi encontrado.' }
$imRoot = $magickExe.Directory.FullName
$imDest = Join-Path $payload 'bin\ImageMagick'
Remove-Item -LiteralPath $imDest -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $imDest -Force | Out-Null
Get-ChildItem -LiteralPath $imRoot -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $imDest -Recurse -Force }

# 2) FFmpeg: build LGPL estático. Quando disponível, o digest publicado pelo GitHub é conferido antes de extrair; o teste funcional continua obrigatório.
$ffmpegZip = Join-Path $cache $FFmpegAsset
if (-not (Test-Path -LiteralPath $ffmpegZip)) { Invoke-Download -Uri $FFmpegUrl -Destination $ffmpegZip }
$ffmpegHash = Get-GitHubAssetSha256 -Repository 'BtbN/FFmpeg-Builds' -Tag $FFmpegTag -AssetName $FFmpegAsset
if (-not [string]::IsNullOrWhiteSpace($ffmpegHash)) { Assert-Hash -Path $ffmpegZip -Expected $ffmpegHash }
$ffmpegStage = Join-Path $cache 'ffmpeg-stage'
Remove-Item -LiteralPath $ffmpegStage -Recurse -Force -ErrorAction SilentlyContinue
Expand-Archive -LiteralPath $ffmpegZip -DestinationPath $ffmpegStage -Force
$ffmpegExe = Get-ChildItem -LiteralPath $ffmpegStage -Filter 'ffmpeg.exe' -Recurse -File | Select-Object -First 1
$ffprobeExe = Get-ChildItem -LiteralPath $ffmpegStage -Filter 'ffprobe.exe' -Recurse -File | Select-Object -First 1
if ($null -eq $ffmpegExe -or $null -eq $ffprobeExe) { throw 'FFmpeg/ffprobe não foram encontrados no pacote baixado.' }
$ffmpegDest = Join-Path $payload 'bin\FFmpeg'
Remove-Item -LiteralPath $ffmpegDest -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $ffmpegDest -Force | Out-Null
Copy-Item -LiteralPath $ffmpegExe.FullName -Destination (Join-Path $ffmpegDest 'ffmpeg.exe') -Force
Copy-Item -LiteralPath $ffprobeExe.FullName -Destination (Join-Path $ffmpegDest 'ffprobe.exe') -Force

# 3) Ghostscript: versão oficial fixada, verificada por SHA-256 conhecido e extraída em uma pasta privada do aplicativo.
$gsInstaller = Join-Path $cache $GhostscriptAsset
if (-not (Test-Path -LiteralPath $gsInstaller)) { Invoke-Download -Uri $GhostscriptUrl -Destination $gsInstaller }
Assert-Hash -Path $gsInstaller -Expected $GhostscriptSha256
$gsStage = Join-Path $cache 'ghostscript-stage'
Remove-Item -LiteralPath $gsStage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $gsStage -Force | Out-Null
# O instalador oficial do Ghostscript usa NSIS: /S silencioso e /D=... deve ser o último parâmetro.
Invoke-ProcessChecked -FilePath $gsInstaller -ArgumentList @('/S', "/D=$gsStage")
$gsExe = Get-ChildItem -LiteralPath $gsStage -Filter 'gswin64c.exe' -Recurse -File | Select-Object -First 1
if ($null -eq $gsExe) { throw 'Ghostscript foi preparado, mas gswin64c.exe não foi encontrado.' }
$gsRoot = $gsExe.Directory.Parent.FullName
$gsDest = Join-Path $payload 'bin\Ghostscript'
Remove-Item -LiteralPath $gsDest -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $gsDest -Force | Out-Null
Get-ChildItem -LiteralPath $gsRoot -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $gsDest -Recurse -Force }

# 4) Visual C++ 2015-2022 Runtime: pré-requisito comum de ferramentas nativas no Windows.
$vcRedist = Join-Path $prerequisites 'VC_redist.x64.exe'
if (-not (Test-Path -LiteralPath $vcRedist)) { Invoke-Download -Uri $VcRedistUrl -Destination $vcRedist }
try {
    $vcSignature = Get-AuthenticodeSignature -FilePath $vcRedist
    if ($vcSignature.Status -ne 'Valid') { throw "Assinatura do VC++ Runtime inválida: $($vcSignature.Status)" }
    if ($vcSignature.SignerCertificate.Subject -notmatch 'Microsoft') { throw 'O VC++ Runtime não está assinado pela Microsoft.' }
} catch { throw "Falha ao validar o VC++ Runtime oficial. $($_.Exception.Message)" }

# Avisos/licenças acompanham os mecanismos dentro do mesmo instalador.
$licenses = Join-Path $payload 'licenses'
New-Item -ItemType Directory -Path (Join-Path $licenses 'ImageMagick'), (Join-Path $licenses 'FFmpeg'), (Join-Path $licenses 'Ghostscript') -Force | Out-Null
$imLicense = Join-Path $licenses 'ImageMagick\LICENSE.txt'
try { Invoke-Download -Uri "https://raw.githubusercontent.com/ImageMagick/ImageMagick/$ImageMagickVersion/LICENSE" -Destination $imLicense }
catch { "ImageMagick $ImageMagickVersion`r`nLicense: https://imagemagick.org/script/license.php" | Set-Content -LiteralPath $imLicense -Encoding utf8 }
$ffLicense = Join-Path $licenses 'FFmpeg\COPYING.LGPLv2.1.txt'
try { Invoke-Download -Uri "https://raw.githubusercontent.com/FFmpeg/FFmpeg/n$FFmpegVersion/COPYING.LGPLv2.1" -Destination $ffLicense }
catch { "FFmpeg $FFmpegVersion LGPL build`r`nLicense/source: https://ffmpeg.org/legal.html" | Set-Content -LiteralPath $ffLicense -Encoding utf8 }
$ffSource = Join-Path $licenses 'FFmpeg\SOURCE.txt'
@"
FFmpeg build: BtbN/FFmpeg-Builds ($FFmpegAsset)
Binary source: https://github.com/BtbN/FFmpeg-Builds
FFmpeg source: https://github.com/FFmpeg/FFmpeg/tree/n$FFmpegVersion
Build configuration is recorded below.
"@ | Set-Content -LiteralPath $ffSource -Encoding utf8
& (Join-Path $ffmpegDest 'ffmpeg.exe') -hide_banner -buildconf 2>&1 | Out-File -LiteralPath (Join-Path $licenses 'FFmpeg\build-configuration.txt') -Encoding utf8
$gsLicense = Join-Path $licenses 'Ghostscript\LICENSE.txt'
$licenseCandidate = Get-ChildItem -LiteralPath $gsDest -Recurse -File | Where-Object { $_.Name -match '^(LICENSE|COPYING)(\.txt)?$' } | Select-Object -First 1
if ($null -ne $licenseCandidate) { Copy-Item -LiteralPath $licenseCandidate.FullName -Destination $gsLicense -Force }
else {
    try { Invoke-Download -Uri "https://raw.githubusercontent.com/ArtifexSoftware/ghostpdl/$GhostscriptTag/LICENSE" -Destination $gsLicense }
    catch { "Ghostscript $GhostscriptVersion`r`nLicense information: https://www.ghostscript.com/licensing/" | Set-Content -LiteralPath $gsLicense -Encoding utf8 }
}
@"
Ghostscript $GhostscriptVersion
Official download: $GhostscriptUrl
Source: https://github.com/ArtifexSoftware/ghostpdl/tree/$GhostscriptTag
Ghostscript is offered upstream under GNU AGPL or a commercial Artifex license. Review the applicable license before redistribution.
"@ | Set-Content -LiteralPath (Join-Path $licenses 'Ghostscript\SOURCE.txt') -Encoding utf8

# Remove qualquer desinstalador temporário que tenha vindo das extrações: o NITH usa os runtimes de forma privada.
Get-ChildItem -LiteralPath $payload -Recurse -Force -File | Where-Object { $_.Name -match '^unins\d*\.(exe|dat|msg)$' } | Remove-Item -Force -ErrorAction SilentlyContinue

# Manifesto determinístico com hash de cada arquivo realmente embarcado.
$files = @()
Get-ChildItem -LiteralPath $payload -Recurse -Force -File | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($payload.Length + 1).Replace('\', '/')
    $files += [ordered]@{ path = $relative; sha256 = (Get-Sha256Hex -Path $_.FullName); size = $_.Length }
}
$manifest = [ordered]@{
    schemaVersion = 2
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    components = @(
        [ordered]@{ name='ImageMagick'; version=$ImageMagickVersion; license='ImageMagick'; executable='bin/ImageMagick/magick.exe'; source=$ImageMagickUrl },
        [ordered]@{ name='FFmpeg'; version=$FFmpegVersion; license='LGPL build'; executable='bin/FFmpeg/ffmpeg.exe'; probe='bin/FFmpeg/ffprobe.exe'; source=$FFmpegUrl },
        [ordered]@{ name='Ghostscript'; version=$GhostscriptVersion; license='AGPL/commercial upstream'; executable='bin/Ghostscript/bin/gswin64c.exe'; source=$GhostscriptUrl },
        [ordered]@{ name='VC++ Runtime'; version='2015-2022 x64'; license='Microsoft'; executable='prerequisites/VC_redist.x64.exe'; source=$VcRedistUrl }
    )
    files = $files
    prerequisites = @(
        [ordered]@{ path='prerequisites/VC_redist.x64.exe'; sha256=(Get-Sha256Hex -Path $vcRedist); size=(Get-Item -LiteralPath $vcRedist).Length }
    )
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Host "Dependências preparadas: ImageMagick $ImageMagickVersion, FFmpeg $FFmpegVersion, Ghostscript $GhostscriptVersion e VC++ Runtime."
Write-Host "Manifesto: $manifestPath"
