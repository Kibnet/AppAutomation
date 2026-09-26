[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$skillDirectory = Join-Path $repoRoot "skills\appautomation"
$expectedFiles = @(
    "SKILL.md",
    "references/adoption-and-authoring.md",
    "references/visual-evidence.md"
)

foreach ($relativePath in $expectedFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $skillDirectory $relativePath) -PathType Leaf)) {
        throw "Skill file is missing: $relativePath"
    }
}

$actualFiles = @(Get-ChildItem -LiteralPath $skillDirectory -File -Recurse |
    ForEach-Object { [System.IO.Path]::GetRelativePath($skillDirectory, $_.FullName).Replace('\', '/') } |
    Sort-Object)
$expectedSorted = @($expectedFiles | Sort-Object)
if (($actualFiles -join '|') -ne ($expectedSorted -join '|')) {
    throw "Unexpected skill files. Expected: $($expectedSorted -join ', '); actual: $($actualFiles -join ', ')"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "artifacts\appautomation-skill.zip"
}
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedOutput) -Force | Out-Null

Compress-Archive -LiteralPath $skillDirectory -DestinationPath $resolvedOutput -Force

Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedOutput)
try {
    $entryNames = @($archive.Entries |
        Where-Object { -not [string]::IsNullOrEmpty($_.Name) } |
        ForEach-Object { $_.FullName.Replace('\', '/') } |
        Sort-Object)
    $expectedEntries = @($expectedFiles |
        ForEach-Object { "appautomation/$_" } |
        Sort-Object)
    if (($entryNames -join '|') -ne ($expectedEntries -join '|')) {
        throw "Skill archive has an unexpected layout. Expected: $($expectedEntries -join ', '); actual: $($entryNames -join ', ')"
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Packed AppAutomation skill: $resolvedOutput"
