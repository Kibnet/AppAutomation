[CmdletBinding()]
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "sample\DotnetDebug.AppAutomation.Avalonia.Headless.Tests\DotnetDebug.AppAutomation.Avalonia.Headless.Tests.csproj"
$screenshotRoot = Join-Path $repoRoot "sample\DotnetDebug.AppAutomation.Avalonia.Headless.Tests\bin\$Configuration\net10.0\artifacts\ui-failures\avalonia-headless"
$testFilter = "/*/*/MainWindowHeadlessRuntimeTests/IntentionalFailure_CapturesScreenshotOnlyInFailureSmoke"
$before = @(Get-ChildItem -LiteralPath $screenshotRoot -Recurse -Filter "test-failure.png" -File -ErrorAction SilentlyContinue | ForEach-Object FullName)

$passingOutput = & dotnet run --no-build --project $project -c $Configuration -- --treenode-filter $testFilter --maximum-parallel-tests 1 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "The passing control failed. Output: $($passingOutput -join [Environment]::NewLine)"
}
$afterPassing = @(Get-ChildItem -LiteralPath $screenshotRoot -Recurse -Filter "test-failure.png" -File -ErrorAction SilentlyContinue | ForEach-Object FullName)
if (@($afterPassing | Where-Object { $before -notcontains $_ }).Count -ne 0) {
    throw "The passing control created an automatic failure screenshot."
}

$env:APPAUTOMATION_SCREENSHOT_FAILURE_SMOKE = "1"
try {
    $output = & dotnet run --no-build --project $project -c $Configuration -- --treenode-filter $testFilter --maximum-parallel-tests 1 2>&1
    $exitCode = $LASTEXITCODE
}
finally {
    Remove-Item Env:APPAUTOMATION_SCREENSHOT_FAILURE_SMOKE -ErrorAction SilentlyContinue
}

$text = $output -join [Environment]::NewLine
if ($exitCode -eq 0 -or $text -notmatch 'actual-state' -or $text -notmatch 'expected-state') {
    throw "The intentional assertion did not fail as expected. Exit=$exitCode. Output: $text"
}
if ($text -notmatch 'Headless failure smoke cleanup completed') {
    throw "The UI session cleanup marker was not emitted after the intentional failure. Output: $text"
}

$created = @(Get-ChildItem -LiteralPath $screenshotRoot -Recurse -Filter "test-failure.png" -File -ErrorAction SilentlyContinue |
    Where-Object { $before -notcontains $_.FullName })
if ($created.Count -ne 1) {
    throw "Expected exactly one new failure screenshot, found $($created.Count). Output: $text"
}

$bytes = [System.IO.File]::ReadAllBytes($created[0].FullName)
if ($bytes.Length -le 100 -or [Convert]::ToHexString($bytes[0..7]) -ne "89504E470D0A1A0A") {
    throw "The failure screenshot is not a valid PNG: $($created[0].FullName)"
}

$beforeCaptureError = @(Get-ChildItem -LiteralPath $screenshotRoot -Recurse -Filter "test-failure.png" -File -ErrorAction SilentlyContinue | ForEach-Object FullName)
$env:APPAUTOMATION_SCREENSHOT_FAILURE_SMOKE = "1"
$env:APPAUTOMATION_SCREENSHOT_CAPTURE_ERROR_SMOKE = "1"
try {
    $captureErrorOutput = & dotnet run --no-build --project $project -c $Configuration -- --treenode-filter $testFilter --maximum-parallel-tests 1 2>&1
    $captureErrorExitCode = $LASTEXITCODE
}
finally {
    Remove-Item Env:APPAUTOMATION_SCREENSHOT_FAILURE_SMOKE -ErrorAction SilentlyContinue
    Remove-Item Env:APPAUTOMATION_SCREENSHOT_CAPTURE_ERROR_SMOKE -ErrorAction SilentlyContinue
}
$captureErrorText = $captureErrorOutput -join [Environment]::NewLine
if ($captureErrorExitCode -eq 0 -or $captureErrorText -notmatch 'actual-state' -or
    $captureErrorText -notmatch 'expected-state' -or
    $captureErrorText -notmatch 'UI failure artifact capture failed' -or
    $captureErrorText -notmatch 'Headless failure smoke cleanup completed') {
    throw "The capture-error control did not preserve the original assertion and cleanup. Output: $captureErrorText"
}
$afterCaptureError = @(Get-ChildItem -LiteralPath $screenshotRoot -Recurse -Filter "test-failure.png" -File -ErrorAction SilentlyContinue | ForEach-Object FullName)
if (@($afterCaptureError | Where-Object { $beforeCaptureError -notcontains $_ }).Count -ne 0) {
    throw "The capture-error control unexpectedly created a failure screenshot."
}

Write-Host "Passing, assertion-failure, and capture-error controls passed. Failure PNG: $($created[0].FullName)"
