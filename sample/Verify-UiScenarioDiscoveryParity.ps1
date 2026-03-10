param(
    [string]$HeadlessProject = "tests/AppAutomation.UiTests.Avalonia.Headless/AppAutomation.UiTests.Avalonia.Headless.csproj",
    [string]$FlaUiProject = "tests/AppAutomation.UiTests.FlaUI.EasyUse/AppAutomation.UiTests.FlaUI.EasyUse.csproj",
    [string]$ArtifactsDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Path $ArtifactsDirectory -Force | Out-Null

$headlessListPath = Join-Path $ArtifactsDirectory "headless-tests.txt"
$flaUiListPath = Join-Path $ArtifactsDirectory "flaui-tests.txt"

dotnet test $HeadlessProject --list-tests | Out-File -FilePath $headlessListPath -Encoding utf8
dotnet test $FlaUiProject --list-tests | Out-File -FilePath $flaUiListPath -Encoding utf8

function Get-ScenarioMethodNames {
    param([string]$Path)

    Get-Content -Path $Path |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -match "^[A-Za-z].*_[A-Za-z].*" } |
        Sort-Object -Unique
}

$headlessScenarioNames = Get-ScenarioMethodNames -Path $headlessListPath
$flaUiScenarioNames = Get-ScenarioMethodNames -Path $flaUiListPath

if (-not $headlessScenarioNames) {
    throw "No scenario methods were extracted from '$headlessListPath'."
}

if (-not $flaUiScenarioNames) {
    throw "No scenario methods were extracted from '$flaUiListPath'."
}

$diff = Compare-Object -ReferenceObject $headlessScenarioNames -DifferenceObject $flaUiScenarioNames
if ($diff) {
    $diff | Format-Table -AutoSize | Out-String | Write-Error
    throw "Discovery mismatch by scenario method names."
}

Write-Host "Discovery parity check passed ($($headlessScenarioNames.Count) shared scenario methods)."
foreach ($name in $headlessScenarioNames) {
    Write-Host "  $name"
}
