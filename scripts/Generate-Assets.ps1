[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $ProjectRoot 'src\NithConverter\Assets\Generated'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$extensions = @('.png', '.jpg', '.jpeg', '.bmp', '.gif', '.tif', '.tiff', '.ico')
$candidates = @(Get-ChildItem -LiteralPath $ProjectRoot -File | Where-Object {
    $_.BaseName -ieq 'logo' -and $extensions -contains $_.Extension.ToLowerInvariant()
})
if ($candidates.Count -ne 1) {
    throw 'Mantenha exatamente uma imagem chamada logo na raiz (PNG, JPG, BMP, GIF, TIFF ou ICO).'
}
$source = $candidates[0].FullName
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256, 512)
$stampPath = Join-Path $OutputDirectory '.assets.sha256'
$fingerprint = (Get-Sha256Hex -Path $source) + ':' +
    (Get-Sha256Hex -Path $PSCommandPath)
$expected = @('app.ico') + @($sizes | ForEach-Object { "logo-$_.png" })
$missing = @($expected | Where-Object { -not (Test-Path -LiteralPath (Join-Path $OutputDirectory $_)) })
if ($missing.Count -eq 0 -and (Test-Path -LiteralPath $stampPath) -and
    ([IO.File]::ReadAllText($stampPath).Trim() -eq $fingerprint)) {
    return
}

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$sourceImage = [Drawing.Image]::FromFile($source)
$pngFrames = @{}
try {
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $attributes = [Drawing.Imaging.ImageAttributes]::new()
        $memory = [IO.MemoryStream]::new()
        try {
            $bitmap.SetResolution(96, 96)
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $ratio = [Math]::Min($size / $sourceImage.Width, $size / $sourceImage.Height)
            $width = [Math]::Max(1, [int][Math]::Round($sourceImage.Width * $ratio))
            $height = [Math]::Max(1, [int][Math]::Round($sourceImage.Height * $ratio))
            $destination = [Drawing.Rectangle]::new([int](($size - $width) / 2), [int](($size - $height) / 2), $width, $height)
            $graphics.DrawImage($sourceImage, $destination, 0, 0, $sourceImage.Width, $sourceImage.Height, [Drawing.GraphicsUnit]::Pixel, $attributes)
            $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
            $pngFrames[$size] = $memory.ToArray()
            $target = Join-Path $OutputDirectory "logo-$size.png"
            $temporary = "$target.$([Guid]::NewGuid().ToString('N')).tmp"
            [IO.File]::WriteAllBytes($temporary, $pngFrames[$size])
            Move-Item -LiteralPath $temporary -Destination $target -Force
        }
        finally {
            $memory.Dispose()
            $attributes.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
finally { $sourceImage.Dispose() }

# PNG-compressed ICO frames are supported by all Windows versions supported by WinUI 3.
$iconSizes = @($sizes | Where-Object { $_ -le 256 })
$iconStream = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$iconSizes.Count)
    $offset = 6 + (16 * $iconSizes.Count)
    foreach ($size in $iconSizes) {
        $dimension = if ($size -eq 256) { 0 } else { $size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$pngFrames[$size].Length)
        $writer.Write([uint32]$offset)
        $offset += $pngFrames[$size].Length
    }
    foreach ($size in $iconSizes) { $writer.Write([byte[]]$pngFrames[$size]) }
    $writer.Flush()
    $iconTarget = Join-Path $OutputDirectory 'app.ico'
    $temporary = "$iconTarget.$([Guid]::NewGuid().ToString('N')).tmp"
    [IO.File]::WriteAllBytes($temporary, $iconStream.ToArray())
    Move-Item -LiteralPath $temporary -Destination $iconTarget -Force
}
finally {
    $writer.Dispose()
    $iconStream.Dispose()
}
[IO.File]::WriteAllText($stampPath, $fingerprint, [Text.UTF8Encoding]::new($false))
Write-Host "Identidade gerada em $OutputDirectory"
