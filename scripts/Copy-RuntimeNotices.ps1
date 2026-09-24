[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AssetsFile,
    [Parameter(Mandatory = $true)][string]$PublishDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json
$packageFolders = @($assets.packageFolders.PSObject.Properties | ForEach-Object { $_.Name })
$packages = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($library in $assets.libraries.PSObject.Properties) {
    if ($library.Value.type -eq 'package') { $null = $packages.Add($library.Name) }
}

# Runtime packs need not appear among ordinary NuGet libraries in project.assets.json.
$runtimeConfig = Join-Path $PublishDirectory 'NITHConverter.runtimeconfig.json'
if (Test-Path -LiteralPath $runtimeConfig) {
    $runtime = Get-Content -LiteralPath $runtimeConfig -Raw | ConvertFrom-Json
    $frameworksProperty = $runtime.runtimeOptions.PSObject.Properties['includedFrameworks']
    if ($null -ne $frameworksProperty) {
        foreach ($framework in $frameworksProperty.Value) {
            if ($framework.name -eq 'Microsoft.NETCore.App') {
                $null = $packages.Add("Microsoft.NETCore.App.Runtime.win-x64/$($framework.version)")
            }
        }
    }
}

$noticeRoot = Join-Path $PublishDirectory 'licenses\dotnet-nuget'
New-Item -ItemType Directory -Path $noticeRoot -Force | Out-Null
$inventory = [Collections.Generic.List[object]]::new()
foreach ($package in ($packages | Sort-Object)) {
    $packageDirectory = $null
    foreach ($packageFolder in $packageFolders) {
        $candidate = Join-Path $packageFolder $package.ToLowerInvariant().Replace('/', '\')
        if (Test-Path -LiteralPath $candidate -PathType Container) { $packageDirectory = $candidate; break }
    }
    if ($null -eq $packageDirectory) { throw "Pacote restaurado não encontrado para coleta de avisos: $package" }
    $destination = Join-Path $noticeRoot $package.Replace('/', '\')
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $noticeFiles = @(Get-ChildItem -LiteralPath $packageDirectory -File -Recurse -Depth 2 | Where-Object {
        $_.Name -match '^(licen[cs]e|copying|notice|third[-_ ]?party)' -or $_.Extension -eq '.nuspec'
    })
    foreach ($notice in $noticeFiles) {
        $relative = $notice.FullName.Substring($packageDirectory.Length).TrimStart('\', '/')
        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $notice.FullName -Destination $target
    }
    $inventory.Add([pscustomobject]@{
        package = $package
        noticeFiles = @($noticeFiles | ForEach-Object { $_.FullName.Substring($packageDirectory.Length).TrimStart('\', '/') })
    })
}
$inventory | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $noticeRoot 'inventory.json') -Encoding UTF8
Write-Host "Avisos preservados para $($inventory.Count) pacotes restaurados."
