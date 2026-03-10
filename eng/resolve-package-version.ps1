[CmdletBinding()]
param(
    [string]$Version,
    [string]$Tag
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "versioning.ps1")

$repoRoot = Get-RepoRoot
$resolvedVersion = Resolve-AppAutomationVersion -RepoRoot $repoRoot -Version $Version -Tag $Tag
Write-Output $resolvedVersion
