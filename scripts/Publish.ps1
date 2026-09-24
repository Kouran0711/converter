[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$BundleDependencies,
    [switch]$Installer,
    [string]$IsccPath = '',
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$project = Join-Path $projectRoot 'src\NithConverter\NithConverter.csproj'
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$publishDirectory = Join-Path $artifactsRoot 'publish\win-x64'
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet }
          elseif ($null -ne $dotnetCommand) { $dotnetCommand.Source }
          else { throw 'Instale o SDK .NET indicado em global.json ou coloque-o em .tools/dotnet.' }

if (-not [string]::IsNullOrWhiteSpace($Version) -and $Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw 'Versão inválida. Use o formato 1.2.3 ou 1.2.3.4.'
}

if ($BundleDependencies) {
    & (Join-Path $PSScriptRoot 'Test-DependencyBundle.ps1')
}

# A fresh publish folder avoids unintentionally shipping dependencies from an earlier build.
# Validate the final path and every existing ancestor before a recursive delete.
$expectedDirectory = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\publish\win-x64'))
if ([IO.Path]::GetFullPath($publishDirectory) -ine $expectedDirectory -or
    -not $expectedDirectory.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Diretório de publicação fora do workspace.'
}
$ancestor = $publishDirectory
while ($ancestor.Length -gt $projectRoot.Length) {
    if (Test-Path -LiteralPath $ancestor) {
        $item = Get-Item -LiteralPath $ancestor -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Publicação recusada em link ou junction: $ancestor"
        }
    }
    $ancestor = Split-Path -Parent $ancestor
}
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

Push-Location -LiteralPath $projectRoot
try {
    $publishArgs = @(
        'publish', $project, '--configuration', $Configuration, '--runtime', 'win-x64', '--self-contained', 'true',
        '--output', $publishDirectory, '-p:Platform=x64', '-p:WindowsPackageType=None',
        '-p:WindowsAppSDKSelfContained=true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false',
        '-p:DebugType=None', '-p:DebugSymbols=false'
    )
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $parsedVersion = [Version]$Version
        $assemblyVersion = '{0}.{1}.{2}.{3}' -f $parsedVersion.Major, $parsedVersion.Minor, ([Math]::Max(0, $parsedVersion.Build)), ([Math]::Max(0, $parsedVersion.Revision))
        $publishArgs += "-p:Version=$Version"
        $publishArgs += "-p:AssemblyVersion=$assemblyVersion"
        $publishArgs += "-p:FileVersion=$assemblyVersion"
        $publishArgs += "-p:InformationalVersion=$Version"
    }
    & $dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (código $LASTEXITCODE)." }
}
finally { Pop-Location }

$executable = Join-Path $publishDirectory 'NITHConverter.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'A publicação não produziu NITHConverter.exe.' }
& (Join-Path $PSScriptRoot 'Copy-RuntimeNotices.ps1') `
    -AssetsFile (Join-Path $projectRoot 'src\NithConverter\obj\project.assets.json') `
    -PublishDirectory $publishDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $publishDirectory
if ($BundleDependencies) {
    $dependencies = Join-Path $projectRoot 'Installer\Dependencies'
    Get-ChildItem -LiteralPath (Join-Path $dependencies 'payload') -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $publishDirectory -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $dependencies 'bundle-manifest.json') -Destination $publishDirectory
}
Write-Host "Aplicativo publicado: $executable"

if ($Installer) {
    if ([string]::IsNullOrWhiteSpace($IsccPath)) {
        $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($null -ne $compiler) { $IsccPath = $compiler.Source }
        else {
            $candidates = @(
                (Join-Path $projectRoot '.tools\inno\ISCC.exe'),
                (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
                (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
            )
            $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        }
    }
    if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path -LiteralPath $IsccPath)) {
        throw 'Aplicativo publicado. Para gerar o instalador, instale Inno Setup 6.3+ e informe -IsccPath se necessário.'
    }
    $installerVersion = if (-not [string]::IsNullOrWhiteSpace($Version)) { $Version } else { [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).FileVersion }
    & $IsccPath "/DAppVersion=$installerVersion" "/DPublishDir=$publishDirectory" (Join-Path $projectRoot 'Installer\NithConverter.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC falhou (código $LASTEXITCODE)." }
}
