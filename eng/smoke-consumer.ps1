[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$PackagesPath,
    [string]$Version,
    [string]$WorkspaceRoot,
    [switch]$SkipPack,
    [switch]$KeepWorkspace
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "versioning.ps1")

function Get-VersionFromPackagesPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    $leafName = Split-Path -Path $Path -Leaf
    if ([string]::IsNullOrWhiteSpace($leafName)) {
        return $null
    }

    try {
        return Assert-AppAutomationVersion -Version $leafName
    }
    catch {
        return $null
    }
}

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
        [string]$PackagesPath
    )

    $configDirectory = Split-Path -Path $Path -Parent
    $globalPackagesFolder = Join-Path $configDirectory "p"

@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value="$globalPackagesFolder" />
  </config>
  <packageSources>
    <clear />
    <add key="local-appautomation" value="$PackagesPath" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -Path $Path -Encoding UTF8
}

function Write-WorkspaceGlobalJson {
    param([string]$Path)

    $sdkVersion = (& dotnet --version).Trim()
    if ([string]::IsNullOrWhiteSpace($sdkVersion)) {
        throw "Unable to resolve current dotnet SDK version for smoke workspace."
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

function Set-GeneratedTestHostScaffold {
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

$repoRoot = Get-RepoRoot
$resolvedVersion = if (-not [string]::IsNullOrWhiteSpace($Version)) {
    Resolve-AppAutomationVersion -RepoRoot $repoRoot -Version $Version
}
elseif (-not [string]::IsNullOrWhiteSpace($PackagesPath)) {
    $inferredVersion = Get-VersionFromPackagesPath -Path $PackagesPath
    if ([string]::IsNullOrWhiteSpace($inferredVersion)) {
        Resolve-AppAutomationVersion -RepoRoot $repoRoot
    }
    else {
        $inferredVersion
    }
}
else {
    Resolve-AppAutomationVersion -RepoRoot $repoRoot
}

if ([string]::IsNullOrWhiteSpace($PackagesPath)) {
    $PackagesPath = Join-Path $repoRoot "artifacts\packages\$resolvedVersion"
}

if (-not (Test-Path $PackagesPath)) {
    if ($SkipPack) {
        throw "Packages path was not found: $PackagesPath"
    }

    Write-Host "Packages were not found at $PackagesPath. Running eng/pack.ps1 first."
    & (Join-Path $PSScriptRoot "pack.ps1") -Configuration $Configuration -Version $resolvedVersion
    if ($LASTEXITCODE -ne 0) {
        throw "eng/pack.ps1 failed while preparing smoke packages."
    }
}

if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("aa-smoke-" + [System.Guid]::NewGuid().ToString("N").Substring(0, 8))
}

New-Item -ItemType Directory -Path $WorkspaceRoot -Force | Out-Null

$authoringProjectDir = Join-Path $WorkspaceRoot "Smoke.Authoring"
$runtimeProjectDir = Join-Path $WorkspaceRoot "Smoke.Headless.Tests"
New-Item -ItemType Directory -Path (Join-Path $authoringProjectDir "Pages") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $authoringProjectDir "Tests") -Force | Out-Null
New-Item -ItemType Directory -Path $runtimeProjectDir -Force | Out-Null

$nugetConfig = Join-Path $WorkspaceRoot "NuGet.Config"
$globalJsonPath = Join-Path $WorkspaceRoot "global.json"
$solutionPath = Join-Path $WorkspaceRoot "Smoke.AppAutomation.sln"
$authoringProjectPath = Join-Path $authoringProjectDir "Smoke.Authoring.csproj"
$runtimeProjectPath = Join-Path $runtimeProjectDir "Smoke.Headless.Tests.csproj"
Write-NuGetConfig -Path $nugetConfig -PackagesPath $PackagesPath
Write-WorkspaceGlobalJson -Path $globalJsonPath

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="AppAutomation.Abstractions" Version="$resolvedVersion" />
    <PackageReference Include="AppAutomation.Authoring" Version="$resolvedVersion" />
    <PackageReference Include="AppAutomation.TUnit" Version="$resolvedVersion" />
    <PackageReference Include="TUnit.Assertions" Version="1.12.111" />
    <PackageReference Include="TUnit.Core" Version="1.12.111" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path $authoringProjectPath -Encoding UTF8

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="AppAutomation.Abstractions" Version="$resolvedVersion" />
    <PackageReference Include="AppAutomation.Avalonia.Headless" Version="$resolvedVersion" />
    <PackageReference Include="AppAutomation.TUnit" Version="$resolvedVersion" />
    <PackageReference Include="TUnit" Version="1.12.111" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Smoke.Authoring\Smoke.Authoring.csproj" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path $runtimeProjectPath -Encoding UTF8

@"
using AppAutomation.Abstractions;

namespace Smoke.Authoring.Pages;

[UiControl("Input", UiControlType.TextBox, "Input")]
[UiControl("SubmitButton", UiControlType.Button, "SubmitButton")]
public sealed partial class SmokePage : UiPage
{
    public SmokePage(IUiControlResolver resolver) : base(resolver)
    {
    }
}
"@ | Set-Content -Path (Join-Path $authoringProjectDir "Pages\SmokePage.cs") -Encoding UTF8

@"
using Smoke.Authoring.Pages;
using AppAutomation.TUnit;
using TUnit.Assertions;
using TUnit.Core;

namespace Smoke.Authoring.Tests;

public abstract class SmokeScenariosBase<TSession> : UiTestBase<TSession, SmokePage>
    where TSession : class, IUiTestSession
{
    [Test]
    public async Task Generated_members_are_available()
    {
        await Assert.That(Page.Input.AutomationId).IsEqualTo("Input");
        await Assert.That(Page.SubmitButton.AutomationId).IsEqualTo("SubmitButton");
    }
}
"@ | Set-Content -Path (Join-Path $authoringProjectDir "Tests\SmokeScenariosBase.cs") -Encoding UTF8

@"
using Smoke.Authoring.Generated;
using Smoke.Authoring.Pages;
using Smoke.Authoring.Tests;
using AppAutomation.Abstractions;
using AppAutomation.TUnit;
using TUnit.Core;

namespace Smoke.Headless.Tests;

[InheritsTests]
public sealed class SmokeHeadlessRuntimeTests : SmokeScenariosBase<SmokeHeadlessRuntimeTests.FakeSession>
{
    private static readonly Type HeadlessSessionType = typeof(AppAutomation.Avalonia.Headless.Session.DesktopAppSession);

    protected override FakeSession LaunchSession()
    {
        var manifest = new SmokeAuthoringManifestProvider().GetManifest();
        if (manifest.Pages.Count != 1 || manifest.Pages[0].Controls.Count != 2)
        {
            throw new InvalidOperationException("Generated manifest was not produced from NuGet source generator.");
        }

        return new FakeSession();
    }

    protected override SmokePage CreatePage(FakeSession session)
    {
        return new SmokePage(new FakeResolver());
    }

    public sealed class FakeSession : IUiTestSession
    {
        public void Dispose()
        {
        }
    }

    private sealed class FakeResolver : IUiControlResolver
    {
        private readonly Dictionary<string, object> _controls = new(StringComparer.Ordinal)
        {
            ["Input"] = new FakeTextBox("Input"),
            ["SubmitButton"] = new FakeButton("SubmitButton")
        };

        public UiRuntimeCapabilities Capabilities { get; } = new("smoke-headless");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            if (_controls.TryGetValue(definition.PropertyName, out var control) && control is TControl typed)
            {
                return typed;
            }

            throw new InvalidOperationException($"Control '{definition.PropertyName}' was not registered.");
        }
    }

    private abstract class FakeControl : IUiControl
    {
        protected FakeControl(string automationId)
        {
            AutomationId = automationId;
            Name = automationId;
        }

        public string AutomationId { get; }

        public string Name { get; protected set; }

        public bool IsEnabled => true;
    }

    private sealed class FakeTextBox : FakeControl, ITextBoxControl
    {
        public FakeTextBox(string automationId) : base(automationId)
        {
            Text = string.Empty;
        }

        public string Text { get; set; }

        public void Enter(string value)
        {
            Text = value;
        }
    }

    private sealed class FakeButton : FakeControl, IButtonControl
    {
        public FakeButton(string automationId) : base(automationId)
        {
        }

        public void Invoke()
        {
        }
    }
}
"@ | Set-Content -Path (Join-Path $runtimeProjectDir "SmokeHeadlessRuntimeTests.cs") -Encoding UTF8

Invoke-Dotnet -WorkingDirectory $WorkspaceRoot -Arguments @("new", "sln", "--name", "Smoke.AppAutomation", "--format", "sln")
Invoke-Dotnet -WorkingDirectory $WorkspaceRoot -Arguments @("sln", $solutionPath, "add", $authoringProjectPath)
Invoke-Dotnet -WorkingDirectory $WorkspaceRoot -Arguments @("sln", $solutionPath, "add", $runtimeProjectPath)
Invoke-Dotnet -WorkingDirectory $WorkspaceRoot -Arguments @("restore", $solutionPath)
Invoke-Dotnet -WorkingDirectory $WorkspaceRoot -Arguments @("build", $solutionPath, "-c", $Configuration, "--no-restore")

$templateConsumerName = "TemplateConsumer"
$templateWorkspace = Join-Path $WorkspaceRoot "tc"
$templateHive = Join-Path $WorkspaceRoot "h"
New-Item -ItemType Directory -Path $templateWorkspace -Force | Out-Null
Write-NuGetConfig -Path (Join-Path $templateWorkspace "NuGet.Config") -PackagesPath $PackagesPath
Write-WorkspaceGlobalJson -Path (Join-Path $templateWorkspace "global.json")

Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @(
    "new", "tool-manifest")

Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @(
    "tool", "install",
    "AppAutomation.Tooling",
    "--version", $resolvedVersion,
    "--add-source", $PackagesPath)

Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @(
    "new", "install",
    "AppAutomation.Templates@$resolvedVersion",
    "--add-source", $PackagesPath,
    "--debug:custom-hive", $templateHive,
    "--force")

Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @(
    "new", "appauto-avalonia",
    "--name", $templateConsumerName,
    "--AppAutomationVersion", $resolvedVersion,
    "--debug:custom-hive", $templateHive)

Set-GeneratedTestHostScaffold -WorkspaceRoot $templateWorkspace -ConsumerName $templateConsumerName

$templateHeadlessProject = Join-Path $templateWorkspace "tests\$templateConsumerName.UiTests.Headless\$templateConsumerName.UiTests.Headless.csproj"
$templateFlaUiProject = Join-Path $templateWorkspace "tests\$templateConsumerName.UiTests.FlaUI\$templateConsumerName.UiTests.FlaUI.csproj"

Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @("restore", $templateHeadlessProject)
Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @("build", $templateHeadlessProject, "-c", $Configuration, "--no-restore")
Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @("restore", $templateFlaUiProject)
Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @("build", $templateFlaUiProject, "-c", $Configuration, "--no-restore")

Invoke-Dotnet -WorkingDirectory $templateWorkspace -Arguments @("tool", "run", "appautomation", "--", "doctor", "--repo-root", ".", "--strict")

Write-Host "Consumer smoke succeeded. Workspace: $WorkspaceRoot"

if (-not $KeepWorkspace) {
    try {
        Invoke-Dotnet -WorkingDirectory $WorkspaceRoot -Arguments @("build-server", "shutdown")
    }
    catch {
        # Best effort. Cleanup fallback below will still run.
    }

    $removed = $false
    for ($attempt = 0; $attempt -lt 3 -and -not $removed; $attempt++) {
        Start-Sleep -Seconds 2
        try {
            if (Test-Path $WorkspaceRoot) {
                Remove-Item -Path $WorkspaceRoot -Recurse -Force -ErrorAction Stop
            }
        }
        catch {
            cmd /c "rd /s /q `"$WorkspaceRoot`"" | Out-Null
        }

        $removed = -not (Test-Path $WorkspaceRoot)
    }

    if (-not $removed) {
        Write-Host "Temporary smoke workspace was left on disk: $WorkspaceRoot"
    }
}
