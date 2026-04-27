using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.Session.Contracts;
using AppAutomation.TestHost.Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.TestHost.Avalonia.Tests;

public sealed class LaunchContractTests
{
    private const string HeadlessRuntimeConstraint = "HeadlessRuntime";

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
    public async Task AutomationLaunchContext_ParallelHeadlessLaunchCallbacks_DoNotLeakAcrossTasks()
    {
        using var barrier = new Barrier(2);
        var scenarioA = new AutomationLaunchScenario<LaunchPayload>("ScenarioA", new LaunchPayload("alice@example.com"));
        var scenarioB = new AutomationLaunchScenario<LaunchPayload>("ScenarioB", new LaunchPayload("bob@example.com"));
        string? observedScenarioA = null;
        string? observedScenarioB = null;

        var optionsA = AvaloniaHeadlessLaunchHost.Create(
            static () => throw new NotSupportedException("Window factory is not used in this test."),
            scenarioA,
            beforeLaunchAsync: _ =>
            {
                barrier.SignalAndWait();
                observedScenarioA = AutomationLaunchContext.GetRequired().ScenarioName;
                return ValueTask.CompletedTask;
            });

        var optionsB = AvaloniaHeadlessLaunchHost.Create(
            static () => throw new NotSupportedException("Window factory is not used in this test."),
            scenarioB,
            beforeLaunchAsync: _ =>
            {
                barrier.SignalAndWait();
                observedScenarioB = AutomationLaunchContext.GetRequired().ScenarioName;
                return ValueTask.CompletedTask;
            });

        try
        {
            await Task.WhenAll(
                Task.Run(() => optionsA.BeforeLaunchAsync!(CancellationToken.None).AsTask()),
                Task.Run(() => optionsB.BeforeLaunchAsync!(CancellationToken.None).AsTask()));
        }
        finally
        {
            optionsA.DisposeCallback!.Invoke();
            optionsB.DisposeCallback!.Invoke();
        }

        using (Assert.Multiple())
        {
            await Assert.That(observedScenarioA).IsEqualTo("ScenarioA");
            await Assert.That(observedScenarioB).IsEqualTo("ScenarioB");
            await Assert.That(AutomationLaunchContext.TryGetCurrent(static _ => null)).IsNull();
        }
    }

    [Test]
    public async Task HeadlessDesktopSession_LaunchPreservesPrimaryException_WhenCleanupFails()
    {
        Exception? exception = null;

        try
        {
            _ = DesktopAppSession.Launch(new HeadlessAppLaunchOptions
            {
                BeforeLaunchAsync = _ => throw new InvalidOperationException("BeforeLaunch failed."),
                CreateMainWindow = static () => throw new NotSupportedException("CreateMainWindow should not run."),
                DisposeCallback = () => throw new ApplicationException("Dispose cleanup failed.")
            });
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        var cleanupException = exception?.Data["AppAutomation.CleanupException"] as ApplicationException;

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception is InvalidOperationException).IsEqualTo(true);
            await Assert.That(exception!.Message).Contains("BeforeLaunch failed.");
            await Assert.That(cleanupException).IsNotNull();
            await Assert.That(cleanupException!.Message).Contains("Dispose cleanup failed.");
        }
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessVisualGrid_EditGridCellText_CommitsValue()
    {
        using var headless = StartHeadlessRuntime();
        var window = HeadlessRuntime.Dispatch(() => CreateVisualGridWindow("OldValue"));
        var page = new VisualGridPage(new HeadlessControlResolver(window));

        page.EditGridCellText(
            static candidate => candidate.EremexDemoDataGridAutomationBridge,
            0,
            1,
            "EditedValue");

        var editedValue = page.EremexDemoDataGridAutomationBridge.GetRowByIndex(0)!.Cells[1].Value;
        await Assert.That(editedValue).IsEqualTo("EditedValue");
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessVisualGrid_EditGridCellText_CancelKeepsOriginalValue()
    {
        using var headless = StartHeadlessRuntime();
        var window = HeadlessRuntime.Dispatch(() => CreateVisualGridWindow("OriginalValue"));
        var page = new VisualGridPage(new HeadlessControlResolver(window));

        page.EditGridCellText(
            static candidate => candidate.EremexDemoDataGridAutomationBridge,
            0,
            1,
            "ChangedValue",
            GridCellEditCommitMode.Cancel);

        var editedValue = page.EremexDemoDataGridAutomationBridge.GetRowByIndex(0)!.Cells[1].Value;
        await Assert.That(editedValue).IsEqualTo("OriginalValue");
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessVisualGrid_EditGridCellDateAndCombo_CommitsTypedValues()
    {
        using var headless = StartHeadlessRuntime();
        var window = HeadlessRuntime.Dispatch(CreateVisualGridWindowWithEditors);
        var page = new VisualGridPage(new HeadlessControlResolver(window));

        page
            .EditGridCellDate(
                static candidate => candidate.EremexDemoDataGridAutomationBridge,
                0,
                1,
                new DateTime(2026, 4, 22))
            .SelectGridCellComboItem(
                static candidate => candidate.EremexDemoDataGridAutomationBridge,
                0,
                2,
                "Ready");

        var cells = page.EremexDemoDataGridAutomationBridge.GetRowByIndex(0)!.Cells;
        using (Assert.Multiple())
        {
            await Assert.That(cells[1].Value).IsEqualTo("2026-04-22");
            await Assert.That(cells[2].Value).IsEqualTo("Ready");
        }
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessProxyTextBox_ResolvesLogicalWrapperThroughInnerPart()
    {
        using var headless = StartHeadlessRuntime();
        var window = HeadlessRuntime.Dispatch(CreateProxyEditorWindow);
        var page = new ProxyEditorPage(
            new HeadlessControlResolver(window)
                .WithTextBoxProxy("ServerFilterEditor", "ServerFilterEditorInput", fallbackToName: false));

        page.EnterText(static candidate => candidate.ServerFilterEditor, "Updated");

        var updatedValue = page.ServerFilterEditor.Text;
        await Assert.That(updatedValue).IsEqualTo("Updated");
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
    public async Task AvaloniaDesktopLaunchHost_IsolatedBuildOutput_PreservesBuildOnceSemantics_AcrossLaunches()
    {
        using var workspace = TemporaryWorkspace.Create();
        WriteFakeDesktopRepo(workspace.FullPath, includeExecutable: false, includeSource: true);

        var descriptor = new AvaloniaDesktopAppDescriptor(
            solutionFileNames: ["FakeDesktop.sln"],
            desktopProjectRelativePaths: ["src\\FakeDesktop\\FakeDesktop.csproj"],
            desktopTargetFramework: "net8.0",
            executableName: "FakeDesktop.exe");
        var projectSourcePath = Path.Combine(workspace.FullPath, "src", "FakeDesktop", "Program.cs");

        var firstOptions = AvaloniaDesktopLaunchHost.CreateLaunchOptions(
            descriptor,
            new AvaloniaDesktopLaunchOptions
            {
                UseIsolatedBuildOutput = true,
                BuildConfiguration = "Debug",
                BuildBeforeLaunch = true,
                BuildOncePerProcess = true
            },
            repositoryRoot: workspace.FullPath);

        var firstIsolatedRoot = Path.GetFullPath(Path.Combine(firstOptions.ExecutablePath, "..", "..", ".."));
        firstOptions.DisposeCallback!.Invoke();

        File.WriteAllText(projectSourcePath, "this is not valid csharp");

        DesktopAppLaunchOptions? secondOptions = null;
        try
        {
            secondOptions = AvaloniaDesktopLaunchHost.CreateLaunchOptions(
                descriptor,
                new AvaloniaDesktopLaunchOptions
                {
                    UseIsolatedBuildOutput = true,
                    BuildConfiguration = "Debug",
                    BuildBeforeLaunch = true,
                    BuildOncePerProcess = true
                },
                repositoryRoot: workspace.FullPath);

            var secondIsolatedRoot = Path.GetFullPath(Path.Combine(secondOptions.ExecutablePath, "..", "..", ".."));

            using (Assert.Multiple())
            {
                await Assert.That(File.Exists(secondOptions.ExecutablePath)).IsEqualTo(true);
                await Assert.That(secondIsolatedRoot).IsEqualTo(firstIsolatedRoot);
                await Assert.That(Directory.Exists(firstIsolatedRoot)).IsEqualTo(true);
            }
        }
        finally
        {
            secondOptions?.DisposeCallback?.Invoke();
            if (Directory.Exists(firstIsolatedRoot))
            {
                Directory.Delete(firstIsolatedRoot, recursive: true);
            }
        }
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

    [Test]
    public async Task AvaloniaDesktopLaunchHost_BuildUsesDotnetHostPath_WhenPathDoesNotContainDotnet()
    {
        using var workspace = TemporaryWorkspace.Create();
        WriteFakeDesktopRepo(workspace.FullPath, includeExecutable: false, includeSource: true);

        var descriptor = new AvaloniaDesktopAppDescriptor(
            solutionFileNames: ["FakeDesktop.sln"],
            desktopProjectRelativePaths: ["src\\FakeDesktop\\FakeDesktop.csproj"],
            desktopTargetFramework: "net8.0",
            executableName: "FakeDesktop.exe");
        var dotnetHostPath = ResolveAvailableDotnetHostPath();
        var unreachablePathEntry = Path.Combine(workspace.FullPath, "missing-dotnet");

        using var pathScope = TemporaryEnvironmentVariableScope.Override("PATH", unreachablePathEntry);
        using var dotnetHostScope = TemporaryEnvironmentVariableScope.Override("DOTNET_HOST_PATH", dotnetHostPath);
        using var dotnetRootScope = TemporaryEnvironmentVariableScope.Override(
            "DOTNET_ROOT",
            Path.GetDirectoryName(dotnetHostPath));

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

        var isolatedRoot = Path.GetFullPath(Path.Combine(options.ExecutablePath, "..", "..", ".."));

        using (Assert.Multiple())
        {
            await Assert.That(File.Exists(options.ExecutablePath)).IsEqualTo(true);
            await Assert.That(options.ExecutablePath.Contains("AppAutomationDesktopBuild-", StringComparison.Ordinal)).IsEqualTo(true);
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

    private static string ResolveAvailableDotnetHostPath()
    {
        var dotnetHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(dotnetHostPath) && File.Exists(dotnetHostPath))
        {
            return Path.GetFullPath(dotnetHostPath);
        }

        var executableName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var pathEntry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(pathEntry, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not resolve a dotnet host path for the test process.");
    }

    private static HeadlessSessionScope StartHeadlessRuntime()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(TestAvaloniaApp));
        HeadlessRuntime.SetSession(session);
        return new HeadlessSessionScope(session);
    }

    private static Window CreateVisualGridWindow(string editableCellValue)
    {
        var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal };
        var firstCell = new TextBlock { Text = "EX-R1" };
        var editableCell = new TextBlock { Text = editableCellValue };
        AutomationProperties.SetAutomationId(row, "EremexDemoDataGridAutomationBridge_Row0");
        AutomationProperties.SetAutomationId(firstCell, "EremexDemoDataGridAutomationBridge_Row0_Cell0");
        AutomationProperties.SetAutomationId(editableCell, "EremexDemoDataGridAutomationBridge_Row0_Cell1");
        row.Children.Add(firstCell);
        row.Children.Add(editableCell);

        var bridge = new StackPanel();
        AutomationProperties.SetAutomationId(bridge, "EremexDemoDataGridAutomationBridge");
        bridge.Children.Add(row);

        return new Window { Content = bridge };
    }

    private static Window CreateVisualGridWindowWithEditors()
    {
        var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal };
        var firstCell = new TextBlock { Text = "EX-R1" };
        var dateCell = new DatePicker { SelectedDate = new DateTimeOffset(new DateTime(2026, 1, 1)) };
        var comboCell = new ComboBox
        {
            ItemsSource = new[] { "Draft", "Ready" },
            SelectedIndex = 0
        };
        AutomationProperties.SetAutomationId(row, "EremexDemoDataGridAutomationBridge_Row0");
        AutomationProperties.SetAutomationId(firstCell, "EremexDemoDataGridAutomationBridge_Row0_Cell0");
        AutomationProperties.SetAutomationId(dateCell, "EremexDemoDataGridAutomationBridge_Row0_Cell1");
        AutomationProperties.SetAutomationId(comboCell, "EremexDemoDataGridAutomationBridge_Row0_Cell2");
        row.Children.Add(firstCell);
        row.Children.Add(dateCell);
        row.Children.Add(comboCell);

        var bridge = new StackPanel();
        AutomationProperties.SetAutomationId(bridge, "EremexDemoDataGridAutomationBridge");
        bridge.Children.Add(row);

        return new Window { Content = bridge };
    }

    private static Window CreateProxyEditorWindow()
    {
        var wrapper = new Border();
        var innerEditor = new TextBox { Text = "Initial" };
        AutomationProperties.SetAutomationId(wrapper, "ServerFilterEditor");
        AutomationProperties.SetAutomationId(innerEditor, "ServerFilterEditorInput");
        wrapper.Child = innerEditor;

        return new Window { Content = wrapper };
    }

    private sealed record LaunchPayload(string UserName);

    private sealed class VisualGridPage : UiPage
    {
        public VisualGridPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IGridControl EremexDemoDataGridAutomationBridge =>
            Resolve<IGridControl>(VisualGridPageDefinitions.EremexDemoDataGridAutomationBridge);
    }

    public static class VisualGridPageDefinitions
    {
        public static UiControlDefinition EremexDemoDataGridAutomationBridge { get; } = new(
            "EremexDemoDataGridAutomationBridge",
            UiControlType.Grid,
            "EremexDemoDataGridAutomationBridge");
    }

    private sealed class ProxyEditorPage : UiPage
    {
        public ProxyEditorPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ITextBoxControl ServerFilterEditor =>
            Resolve<ITextBoxControl>(ProxyEditorPageDefinitions.ServerFilterEditor);
    }

    public static class ProxyEditorPageDefinitions
    {
        public static UiControlDefinition ServerFilterEditor { get; } = new(
            "ServerFilterEditor",
            UiControlType.TextBox,
            "ServerFilterEditor");
    }

    private sealed class TestAvaloniaApp : global::Avalonia.Application
    {
    }

    private sealed class HeadlessSessionScope : IDisposable
    {
        private readonly HeadlessUnitTestSession _session;

        public HeadlessSessionScope(HeadlessUnitTestSession session)
        {
            _session = session;
        }

        public void Dispose()
        {
            HeadlessRuntime.SetSession(null);
            _session.Dispose();
        }
    }

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

    private sealed class TemporaryEnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        private TemporaryEnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public static TemporaryEnvironmentVariableScope Override(string name, string? value)
        {
            return new TemporaryEnvironmentVariableScope(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }
}
