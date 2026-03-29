[CmdletBinding()]
param(
    [string]$Version,
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [int]$TimeoutSeconds = 300,
    [int]$RetryIntervalSeconds = 15,
    [string]$WorkspaceRoot,
    [string]$PackagesCacheRoot,
    [switch]$KeepWorkspace
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "versioning.ps1")

function Invoke-Dotnet {
    param(
        [string]$WorkingDirectory,
        [string[]]$Arguments
    )

    Push-Location $WorkingDirectory
    try {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') failed."
        }
    }
    finally {
        Pop-Location
    }
}

function Write-NuGetConfig {
    param(
        [string]$Path,
        [string]$PrimarySource,
        [string]$GlobalPackagesFolder
    )

    $nugetOrg = "https://api.nuget.org/v3/index.json"
    $secondarySources = @()

    if (-not [string]::Equals($PrimarySource, $nugetOrg, [System.StringComparison]::OrdinalIgnoreCase)) {
        $secondarySources += "    <add key=`"nuget.org`" value=`"$nugetOrg`" />"
    }

    $secondarySourcesText = if ($secondarySources.Count -eq 0) {
        ""
    }
    else {
        "`r`n" + ($secondarySources -join "`r`n")
    }

@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value="$globalPackagesFolder" />
  </config>
  <packageSources>
    <clear />
    <add key="verification-source" value="$PrimarySource" />$secondarySourcesText
  </packageSources>
</configuration>
"@ | Set-Content -Path $Path -Encoding UTF8
}

function Remove-DirectoryWithRetry {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $true
    }

    $removed = $false
    for ($attempt = 0; $attempt -lt 12 -and -not $removed; $attempt++) {
        if ($attempt -gt 0) {
            Start-Sleep -Seconds 5
        }

        try {
            if (Test-Path -LiteralPath $Path) {
                Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            }
        }
        catch {
            cmd /c "rd /s /q `"$Path`"" 2>$null | Out-Null
        }

        $removed = -not (Test-Path -LiteralPath $Path)
    }

    return $removed
}

function Write-WorkspaceGlobalJson {
    param([string]$Path)

    $sdkVersion = (& dotnet --version).Trim()
    if ([string]::IsNullOrWhiteSpace($sdkVersion)) {
        throw "Unable to resolve current dotnet SDK version for verification workspace."
    }

@"
{
  "sdk": {
    "version": "$sdkVersion",
    "rollForward": "latestFeature"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
"@ | Set-Content -Path $Path -Encoding UTF8
}

function Complete-GeneratedTestHostScaffoldForStrictVerification {
    param(
        [string]$WorkspaceRoot,
        [string]$ConsumerName
    )

    $launchHostPath = Join-Path $WorkspaceRoot "tests\$ConsumerName.AppAutomation.TestHost\${ConsumerName}AppLaunchHost.cs"
    if (-not (Test-Path $launchHostPath)) {
        throw "Generated TestHost scaffold was not found: $launchHostPath"
    }

    $contents = Get-Content -Path $launchHostPath -Raw
    $updated = $contents

    $updated = $updated.Replace(
@"
public static Type AvaloniaAppType => throw new NotImplementedException(
        "Reference your Avalonia App type here, for example typeof(MyApp.Desktop.App).");
"@,
@"
public static Type AvaloniaAppType => typeof(${ConsumerName}AppLaunchHost);
"@)

    $updated = $updated.Replace("REPLACE_WITH_YOUR_SOLUTION.sln", "$ConsumerName.sln")
    $updated = $updated.Replace("src\\REPLACE_WITH_YOUR_DESKTOP_PROJECT\\REPLACE_WITH_YOUR_DESKTOP_PROJECT.csproj", "src\\$ConsumerName\\$ConsumerName.csproj")
    $updated = $updated.Replace("REPLACE_WITH_YOUR_DESKTOP_EXE.exe", "$ConsumerName.exe")
    $updated = $updated.Replace(
@"
        return AvaloniaHeadlessLaunchHost.Create(
            static () => throw new NotImplementedException(
                "Reference your Avalonia app and return the root Window instance here."));
"@,
@"
        return AvaloniaHeadlessLaunchHost.Create(
            static () => throw new NotSupportedException(
                "Replace the generated headless bootstrap with your AUT bootstrap before running UI sessions."));
"@)

    if ($updated -eq $contents) {
        throw "Generated TestHost scaffold did not contain the expected placeholders: $launchHostPath"
    }

    Set-Content -Path $launchHostPath -Value $updated -Encoding UTF8
}

function New-AttemptWorkspace {
    param(
        [string]$Root,
        [int]$AttemptNumber
    )

    $attemptRoot = Join-Path $Root ("a" + $AttemptNumber.ToString("D2"))
    if (Test-Path $attemptRoot) {
        Remove-Item -Path $attemptRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $attemptRoot -Force | Out-Null
    return $attemptRoot
}

$repoRoot = Get-RepoRoot
$resolvedVersion = if ([string]::IsNullOrWhiteSpace($Version)) {
    Resolve-AppAutomationVersion -RepoRoot $repoRoot
}
else {
    Resolve-AppAutomationVersion -RepoRoot $repoRoot -Version $Version
}

if ([string]::IsNullOrWhiteSpace($Source)) {
    throw "NuGet source is required. Pass -Source or set NUGET_SOURCE."
}

if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("aa-verify-" + [System.Guid]::NewGuid().ToString("N").Substring(0, 8))
}

if ([string]::IsNullOrWhiteSpace($PackagesCacheRoot)) {
    $PackagesCacheRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("aa-verify-packages-" + [System.Guid]::NewGuid().ToString("N").Substring(0, 8))
}

New-Item -ItemType Directory -Path $WorkspaceRoot -Force | Out-Null
New-Item -ItemType Directory -Path $PackagesCacheRoot -Force | Out-Null

$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
$attempt = 0
$lastError = $null
$success = $false

while ([DateTimeOffset]::UtcNow -lt $deadline -and -not $success) {
    $attempt++
    $attemptRoot = New-AttemptWorkspace -Root $WorkspaceRoot -AttemptNumber $attempt
    $templateConsumerName = "TemplateConsumer"
    $templateWorkspace = Join-Path $attemptRoot "tc"
    $templateHive = Join-Path $attemptRoot "h"

    try {
        New-Item -ItemType Directory -Path $templateWorkspace -Force | Out-Null
        Write-NuGetConfig -Path (Join-Path $templateWorkspace "NuGet.Config") -PrimarySource $Source -GlobalPackagesFolder $PackagesCacheRoot
        Write-WorkspaceGlobalJson -Path (Join-Path $templateWorkspace "global.json")

        Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @("new", "tool-manifest")
        Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @(
            "tool", "install",
            "AppAutomation.Tooling",
            "--version", $resolvedVersion,
            "--add-source", $Source)

        Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @(
            "new", "install",
            "AppAutomation.Templates@$resolvedVersion",
            "--add-source", $Source,
            "--debug:custom-hive", $templateHive,
            "--force")

        Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @(
            "new", "appauto-avalonia",
            "--name", $templateConsumerName,
            "--AppAutomationVersion", $resolvedVersion,
            "--debug:custom-hive", $templateHive)

        Write-Host "Completing generated TestHost placeholders before strict doctor verification."
        Complete-GeneratedTestHostScaffoldForStrictVerification -WorkspaceRoot $templateWorkspace -ConsumerName $templateConsumerName

        $templateHeadlessProject = Join-Path $templateWorkspace "tests\$templateConsumerName.UiTests.Headless\$templateConsumerName.UiTests.Headless.csproj"
        $templateFlaUiProject = Join-Path $templateWorkspace "tests\$templateConsumerName.UiTests.FlaUI\$templateConsumerName.UiTests.FlaUI.csproj"

        Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @("restore", $templateHeadlessProject)
        Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @("build", $templateHeadlessProject, "-c", "Release", "--no-restore")
        Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @("restore", $templateFlaUiProject)
        Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @("build", $templateFlaUiProject, "-c", "Release", "--no-restore")
        Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @("tool", "run", "appautomation", "--", "doctor", "--repo-root", ".", "--strict")

        $success = $true
    }
    catch {
        $lastError = $_
        if ([DateTimeOffset]::UtcNow -lt $deadline) {
            Write-Host "Published consumer verification attempt $attempt failed: $($_.Exception.Message)"
            Start-Sleep -Seconds $RetryIntervalSeconds
        }
    }
}

if (-not $success) {
    if ($lastError -is [System.Management.Automation.ErrorRecord]) {
        throw $lastError
    }

    throw "Published consumer verification failed within $TimeoutSeconds seconds."
}

Write-Host "Published consumer verification succeeded for version $resolvedVersion from $Source"

if (-not $KeepWorkspace) {
    try {
        Invoke-Dotnet -WorkingDirectory $WorkspaceRoot -Arguments @("build-server", "shutdown")
    }
    catch {
        # Best effort.
    }

    $workspaceRemoved = Remove-DirectoryWithRetry -Path $WorkspaceRoot
    $packagesRemoved = Remove-DirectoryWithRetry -Path $PackagesCacheRoot

    if (-not $workspaceRemoved) {
        Write-Host "Temporary verification workspace was left on disk: $WorkspaceRoot"
    }

    if (-not $packagesRemoved) {
        Write-Host "Temporary verification NuGet cache was left on disk: $PackagesCacheRoot"
    }
}
