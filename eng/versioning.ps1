[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
}

function Assert-AppAutomationVersion {
    param([string]$Version)

    if ([string]::IsNullOrWhiteSpace($Version)) {
        throw "AppAutomation version is required."
    }

    $normalized = $Version.Trim()
    if ($normalized -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') {
        throw "Version '$normalized' is not a supported package version."
    }

    return $normalized
}

function Get-ConfiguredAppAutomationVersion {
    param([string]$RepoRoot)

    [xml]$versions = Get-Content -Raw (Join-Path $RepoRoot "eng\Versions.props")
    $configuredVersion = [string]$versions.Project.PropertyGroup.AppAutomationVersion
    if ([string]::IsNullOrWhiteSpace($configuredVersion)) {
        throw "AppAutomationVersion was not found in eng/Versions.props."
    }

    return Assert-AppAutomationVersion -Version $configuredVersion
}

function Convert-AppAutomationReleaseTagToVersion {
    param([string]$Tag)

    $releasePrefix = "appautomation-v"
    if ([string]::IsNullOrWhiteSpace($Tag)) {
        throw "Release tag is required."
    }

    $normalizedTag = $Tag.Trim()
    if (-not $normalizedTag.StartsWith($releasePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Release tag '$normalizedTag' must start with '$releasePrefix'."
    }

    $resolvedVersion = $normalizedTag.Substring($releasePrefix.Length)
    return Assert-AppAutomationVersion -Version $resolvedVersion
}

function Resolve-AppAutomationVersion {
    param(
        [string]$RepoRoot,
        [string]$Version,
        [string]$Tag
    )

    if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
        $RepoRoot = Get-RepoRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        return Assert-AppAutomationVersion -Version $Version
    }

    if (-not [string]::IsNullOrWhiteSpace($Tag)) {
        return Convert-AppAutomationReleaseTagToVersion -Tag $Tag
    }

    return Get-ConfiguredAppAutomationVersion -RepoRoot $RepoRoot
}
