[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputRoot,
    [string]$Version,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "versioning.ps1")

$repoRoot = Get-RepoRoot
$resolvedVersion = Resolve-AppAutomationVersion -RepoRoot $repoRoot -Version $Version

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts\packages\$resolvedVersion"
}

$projects = @(
    "src\AppAutomation.Abstractions\AppAutomation.Abstractions.csproj",
    "src\AppAutomation.Authoring\AppAutomation.Authoring.csproj",
    "src\AppAutomation.Session.Contracts\AppAutomation.Session.Contracts.csproj",
    "src\AppAutomation.TUnit\AppAutomation.TUnit.csproj",
    "src\AppAutomation.FlaUI\AppAutomation.FlaUI.csproj",
    "src\AppAutomation.Avalonia.Headless\AppAutomation.Avalonia.Headless.csproj",
    "src\AppAutomation.TestHost.Avalonia\AppAutomation.TestHost.Avalonia.csproj",
    "src\AppAutomation.Tooling\AppAutomation.Tooling.csproj",
    "src\AppAutomation.Templates\AppAutomation.Templates.csproj"
)

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    $arguments = @(
        "pack",
        $projectPath,
        "-c", $Configuration,
        "--output", $OutputRoot,
        "/p:AppAutomationVersion=$resolvedVersion"
    )

    if ($NoBuild) {
        $arguments += "--no-build"
    }

    Write-Host "Packing $project -> $OutputRoot"
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $project"
    }
}

Write-Host "Packed AppAutomation version $resolvedVersion into $OutputRoot"
