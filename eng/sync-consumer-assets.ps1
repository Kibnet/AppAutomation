[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "versioning.ps1")

$repoRoot = Get-RepoRoot
$resolvedVersion = if ([string]::IsNullOrWhiteSpace($Version)) {
    Resolve-AppAutomationVersion -RepoRoot $repoRoot
}
else {
    Resolve-AppAutomationVersion -RepoRoot $repoRoot -Version $Version
}

function Update-TextFile {
    param(
        [string]$Path,
        [scriptblock]$Transform
    )

    $original = Get-Content -Raw $Path
    $updated = & $Transform $original
    if ($updated -ne $original) {
        Set-Content -Path $Path -Value $updated -Encoding UTF8
    }
}

$readmePath = Join-Path $repoRoot "README.md"
$quickstartPath = Join-Path $repoRoot "docs\appautomation\quickstart.md"
$publishingPath = Join-Path $repoRoot "docs\appautomation\publishing.md"
$templateJsonPath = Join-Path $repoRoot "src\AppAutomation.Templates\content\AppAutomation.Avalonia.Consumer\.template.config\template.json"

$versionPattern = "\d+\.\d+\.\d+(?:[-A-Za-z0-9\.]+)?"

Update-TextFile -Path $readmePath -Transform {
    param($content)

    $content = [regex]::Replace(
        $content,
        "Replace ``$versionPattern`` with the desired package version\.",
        "Replace ``$resolvedVersion`` with the desired package version.")
    $content = [regex]::Replace(
        $content,
        "Замените ``$versionPattern`` на нужную версию пакетов\.",
        "Замените ``$resolvedVersion`` на нужную версию пакетов.")
    $content = [regex]::Replace(
        $content,
        "AppAutomation\.Templates(?:::|@)$versionPattern",
        "AppAutomation.Templates@$resolvedVersion")
    $content = [regex]::Replace(
        $content,
        "AppAutomation\.Tooling --version $versionPattern",
        "AppAutomation.Tooling --version $resolvedVersion")
    $content = [regex]::Replace(
        $content,
        "--AppAutomationVersion $versionPattern",
        "--AppAutomationVersion $resolvedVersion")

    return $content
}

Update-TextFile -Path $quickstartPath -Transform {
    param($content)

    $content = [regex]::Replace(
        $content,
        "AppAutomation\.Templates(?:::|@)$versionPattern",
        "AppAutomation.Templates@$resolvedVersion")
    $content = [regex]::Replace(
        $content,
        "AppAutomation\.Tooling --version $versionPattern",
        "AppAutomation.Tooling --version $resolvedVersion")
    $content = [regex]::Replace(
        $content,
        "--AppAutomationVersion $versionPattern",
        "--AppAutomationVersion $resolvedVersion")

    return $content
}

Update-TextFile -Path $publishingPath -Transform {
    param($content)

    $content = [regex]::Replace(
        $content,
        "-Version $versionPattern",
        "-Version $resolvedVersion")

    return $content
}

Update-TextFile -Path $templateJsonPath -Transform {
    param($content)

    $pattern = '(?<prefix>"AppAutomationVersion"\s*:\s*\{\s*"type"\s*:\s*"parameter"\s*,\s*"datatype"\s*:\s*"text"\s*,\s*"defaultValue"\s*:\s*")(?<version>[^"]+)(?<suffix>")'
    return [regex]::Replace(
        $content,
        $pattern,
        ('${prefix}' + $resolvedVersion + '${suffix}'),
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
}

Write-Host "Consumer-facing assets synchronized to version $resolvedVersion"
