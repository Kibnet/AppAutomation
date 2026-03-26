using AppAutomation.Session.Contracts;
using AppAutomation.TestHost.Avalonia;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.TestHost.Avalonia.Tests;

public sealed class LaunchContractTests
{
    [Test]
    public async Task AutomationLaunchContext_PrefersAmbientOverride_OverEnvironment()
    {
        var ambient = new AutomationLaunchContext("AmbientScenario", @"C:\ambient.json", source: "ambient-test");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [AutomationLaunchContext.ScenarioNameEnvironmentVariable] = "EnvironmentScenario",
            [AutomationLaunchContext.ScenarioPayloadPathEnvironmentVariable] = @"C:\environment.json"
        };

        using var scope = AutomationLaunchContext.PushAmbientOverride(ambient);
        var resolved = AutomationLaunchContext.GetRequired(name => environment.TryGetValue(name, out var value) ? value : null);

        using (Assert.Multiple())
        {
            await Assert.That(resolved.ScenarioName).IsEqualTo("AmbientScenario");
            await Assert.That(resolved.PayloadPath).IsEqualTo(@"C:\ambient.json");
            await Assert.That(resolved.Source).IsEqualTo("ambient-test");
        }
    }

    [Test]
    public async Task AutomationLaunchContext_ReadRequiredPayload_ReportsScenarioPathAndType()
    {
        using var workspace = TemporaryWorkspace.Create();
        var payloadPath = Path.Combine(workspace.FullPath, "payload.json");
        File.WriteAllText(payloadPath, "{ invalid json");

        var context = new AutomationLaunchContext("BrokenScenario", payloadPath, source: "test");

        Exception? exception = null;
        try
        {
            _ = context.ReadRequiredPayload<LaunchPayload>();
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.Message).Contains("BrokenScenario");
            await Assert.That(exception.Message).Contains(payloadPath);
            await Assert.That(exception.Message).Contains(typeof(LaunchPayload).FullName!);
        }
    }

    [Test]
    public async Task AutomationPreflight_MasksSecrets_AndAggregatesSources()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Settings.json");
        Exception? exception = null;

        try
        {
            AutomationPreflight.Create("Login smoke")
                .RequireValue("ServerUrl", "http://localhost:5000", "env:SERVER_URL")
                .RequireValue("Login", "alice@example.com", "env:LOGIN", secret: true)
                .RequireValue("Password", null, "env:PASSWORD", secret: true)
                .RequireExistingFile("SettingsPath", missingPath, "launchSettings")
                .ThrowIfInvalid();
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception is AutomationPreflightException).IsEqualTo(true);
            await Assert.That(exception!.Message).Contains("Login smoke");
            await Assert.That(exception.Message).Contains("[env:LOGIN] = set");
            await Assert.That(exception.Message).DoesNotContain("alice@example.com");
            await Assert.That(exception.Message).Contains("[env:PASSWORD] = missing");
            await Assert.That(exception.Message).Contains("[launchSettings] = " + missingPath);
        }
    }

    [Test]
    public async Task AvaloniaHeadlessLaunchHost_CreateWithScenario_SetsAmbientContext_AndCleansUp()
    {
        var scenario = new AutomationLaunchScenario<LaunchPayload>(
            "SignedInSmoke",
            new LaunchPayload("alice@example.com"));

        AutomationLaunchContext? contextFromBeforeLaunch = null;
        AutomationLaunchContext? contextFromWindowFactory = null;
        Exception? factoryException = null;

        var options = AvaloniaHeadlessLaunchHost.Create(
            () =>
            {
                contextFromWindowFactory = AutomationLaunchContext.GetRequired();
                throw new InvalidOperationException("Factory executed.");
            },
            scenario,
            beforeLaunchAsync: cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                contextFromBeforeLaunch = AutomationLaunchContext.GetRequired();
                return ValueTask.CompletedTask;
            });

        await options.BeforeLaunchAsync!(CancellationToken.None);
        try
        {
            _ = options.CreateMainWindow!.Invoke();
        }
        catch (Exception ex)
        {
            factoryException = ex;
        }

        var resolvedContext = contextFromWindowFactory
            ?? throw new InvalidOperationException("Window factory did not capture the launch context.");
        var payloadPath = resolvedContext.PayloadPath!;
        var payload = resolvedContext.ReadRequiredPayload<LaunchPayload>();

        using (Assert.Multiple())
        {
            await Assert.That(contextFromBeforeLaunch).IsNotNull();
            await Assert.That(factoryException).IsNotNull();
            await Assert.That(factoryException!.Message).Contains("Factory executed.");
            await Assert.That(resolvedContext.ScenarioName).IsEqualTo("SignedInSmoke");
            await Assert.That(payload.UserName).IsEqualTo("alice@example.com");
            await Assert.That(File.Exists(payloadPath)).IsEqualTo(true);
        }

        options.DisposeCallback!.Invoke();

        using (Assert.Multiple())
        {
            await Assert.That(AutomationLaunchContext.TryGetCurrent(static _ => null)).IsNull();
            await Assert.That(File.Exists(payloadPath)).IsEqualTo(false);
        }
    }

    [Test]
    public async Task AvaloniaDesktopLaunchHost_CreateLaunchOptions_WithScenario_AddsEnvironmentVariables_AndCleansUp()
    {
        using var workspace = TemporaryWorkspace.Create();
        WriteFakeDesktopRepo(workspace.FullPath, includeExecutable: true, includeSource: false);

        var descriptor = new AvaloniaDesktopAppDescriptor(
            solutionFileNames: ["FakeDesktop.sln"],
            desktopProjectRelativePaths: ["src\\FakeDesktop\\FakeDesktop.csproj"],
            desktopTargetFramework: "net8.0",
            executableName: "FakeDesktop.exe");
        var scenario = new AutomationLaunchScenario<LaunchPayload>(
            "SignedInSmoke",
            new LaunchPayload("bob@example.com"));

        var options = AvaloniaDesktopLaunchHost.CreateLaunchOptions(
            descriptor,
            scenario,
            new AvaloniaDesktopLaunchOptions
            {
                BuildBeforeLaunch = false,
                EnvironmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["EXISTING_VALUE"] = "42"
                }
            },
            repositoryRoot: workspace.FullPath);

        var payloadPath = options.EnvironmentVariables[AutomationLaunchContext.ScenarioPayloadPathEnvironmentVariable]!;

        using (Assert.Multiple())
        {
            await Assert.That(options.EnvironmentVariables[AutomationLaunchContext.ScenarioNameEnvironmentVariable]).IsEqualTo("SignedInSmoke");
            await Assert.That(options.EnvironmentVariables["EXISTING_VALUE"]).IsEqualTo("42");
            await Assert.That(File.Exists(payloadPath)).IsEqualTo(true);
        }

        options.DisposeCallback!.Invoke();
        await Assert.That(File.Exists(payloadPath)).IsEqualTo(false);
    }

    [Test]
    public async Task AvaloniaDesktopLaunchHost_IsolatedBuildOutput_WritesOutsideProjectBin_AndCleansAutoTemp()
    {
        using var workspace = TemporaryWorkspace.Create();
        WriteFakeDesktopRepo(workspace.FullPath, includeExecutable: false, includeSource: true);

        var descriptor = new AvaloniaDesktopAppDescriptor(
            solutionFileNames: ["FakeDesktop.sln"],
            desktopProjectRelativePaths: ["src\\FakeDesktop\\FakeDesktop.csproj"],
            desktopTargetFramework: "net8.0",
            executableName: "FakeDesktop.exe");

        var options = AvaloniaDesktopLaunchHost.CreateLaunchOptions(
            descriptor,
            new AvaloniaDesktopLaunchOptions
            {
                UseIsolatedBuildOutput = true,
                BuildConfiguration = "Debug",
                BuildBeforeLaunch = true,
                BuildOncePerProcess = false
            },
            repositoryRoot: workspace.FullPath);

        var defaultOutputPath = Path.Combine(
            workspace.FullPath,
            "src",
            "FakeDesktop",
            "bin",
            "Debug",
            "net8.0",
            "FakeDesktop.exe");
        var isolatedRoot = Path.GetFullPath(Path.Combine(options.ExecutablePath, "..", "..", ".."));

        using (Assert.Multiple())
        {
            await Assert.That(File.Exists(options.ExecutablePath)).IsEqualTo(true);
            await Assert.That(options.ExecutablePath.Contains("AppAutomationDesktopBuild-", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(File.Exists(defaultOutputPath)).IsEqualTo(false);
        }

        options.DisposeCallback!.Invoke();
        await Assert.That(Directory.Exists(isolatedRoot)).IsEqualTo(false);
    }

    private static void WriteFakeDesktopRepo(string repositoryRoot, bool includeExecutable, bool includeSource)
    {
        Directory.CreateDirectory(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "FakeDesktop.sln"), string.Empty);

        var projectDirectory = Path.Combine(repositoryRoot, "src", "FakeDesktop");
        Directory.CreateDirectory(projectDirectory);

        File.WriteAllText(Path.Combine(projectDirectory, "FakeDesktop.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""");

        if (includeSource)
        {
            File.WriteAllText(Path.Combine(projectDirectory, "Program.cs"), """
Console.WriteLine("Fake desktop");
""");
        }

        if (!includeExecutable)
        {
            return;
        }

        var executablePath = Path.Combine(projectDirectory, "bin", "Debug", "net8.0", "FakeDesktop.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, "fake");
    }

    private sealed record LaunchPayload(string UserName);

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string fullPath)
        {
            FullPath = fullPath;
        }

        public string FullPath { get; }

        public static TemporaryWorkspace Create()
        {
            var fullPath = Path.Combine(
                Path.GetTempPath(),
                "AppAutomationTestHostTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fullPath);
            return new TemporaryWorkspace(fullPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(FullPath))
            {
                Directory.Delete(FullPath, recursive: true);
            }
        }
    }
}
