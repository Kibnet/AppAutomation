param(
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$globalJsonPath = Join-Path $repoRoot "global.json"

if ([string]::IsNullOrWhiteSpace($Version))
{
    $globalJson = Get-Content $globalJsonPath -Raw | ConvertFrom-Json
    $Version = [string]$globalJson.sdk.version
}

if ([string]::IsNullOrWhiteSpace($Version))
{
    throw "Unable to resolve the pinned SDK version from global.json."
}

$installDir = Join-Path $repoRoot ".dotnet"
$installerPath = Join-Path $env:TEMP "dotnet-install.ps1"

Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile $installerPath
& $installerPath -Version $Version -InstallDir $installDir -NoPath

$dotnetPath = Join-Path $installDir "dotnet.exe"
if (-not (Test-Path $dotnetPath))
{
    throw "dotnet-install.ps1 did not produce '$dotnetPath'."
}

& $dotnetPath --list-sdks
