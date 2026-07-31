using System.Runtime.Serialization;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia.Tests;

internal sealed record RecorderSourceFile(string Name, string Content);

internal static class RecorderScenarioDestinationSources
{
    public static RecorderSourceFile DiscoveryScenarios { get; } = new(
        "Scenarios.cs",
        """
        namespace Sample.Authoring.Tests
        {
            public partial class RootScenarios;

            namespace Customers
            {
                public partial class CreateCustomerTests<TSession, TSetup>;

                public class NotPartial;

                public partial class Outer
                {
                    public partial class Nested;
                }
            }
        }
        """);

    public static RecorderSourceFile OutsideNamespace { get; } = new(
        "Outside.cs",
        """
        namespace Sample.Authoring.TestsExtra;
        public partial class OutsideScenarios;
        """);

    public static RecorderSourceFile OperationScenarios { get; } = new(
        "Operations.cs",
        """
        namespace Sample.Authoring.Tests.Operations;
        public partial class OperationScenarios;
        """);

    public static RecorderSourceFile GeneratedScenario { get; } = new(
        "Generated.g.cs",
        """
        namespace Sample.Authoring.Tests;
        public partial class GeneratedScenarios;
        """);

    public static RecorderSourceFile AmbiguousGenericScenarios { get; } = new(
        "Ambiguous.cs",
        """
        namespace Sample.Authoring.Tests;

        public partial class Scenario<TSession>;
        public partial class Scenario<TSession, TSetup>;
        """);

    public static RecorderSourceFile CustomerScenario { get; } = new(
        "CreateCustomerTests.cs",
        """
        namespace Sample.Authoring.Tests.Customers;
        public partial class CreateCustomerTests<TSession, TSetup>;
        """);

    public static RecorderSourceFile CustomerScenarioWithOneTypeParameter { get; } = new(
        "Scenarios.cs",
        """
        namespace Sample.Authoring.Tests.Customers;
        public partial class CreateCustomerTests<TSession>;
        """);

    public static RecorderSourceFile RootScenario { get; } = new(
        "Scenarios.cs",
        """
        namespace Sample.Authoring.Tests;
        public partial class Scenarios;
        """);

    public static RecorderSourceFile MainWindowPage { get; } = new(
        "MainWindowPage.cs",
        """
        namespace Sample.Authoring.Pages;
        public partial class MainWindowPage;
        """);

    public static RecorderSourceFile MainWindowPageWithSaveButton { get; } = new(
        "MainWindowPage.cs",
        """
        namespace Sample.Authoring.Pages;
        [UiControl("SaveButton", UiControlType.Button, "SaveButton")]
        public partial class MainWindowPage;
        """);
}

internal sealed class RecorderScenarioDestinationProject : IDisposable
{
    private const string ScenarioNamespaceRoot = "Sample.Authoring.Tests";
    private const string OutputSubdirectoryRoot = "Recorded";
    private readonly AuthoringProjectScanner _scanner = new();
    private readonly TemporaryDirectory _directory = new();

    private RecorderScenarioDestinationProject(IEnumerable<RecorderSourceFile> files)
    {
        foreach (var file in files)
        {
            WriteSource(file);
        }

        Generator = new AuthoringCodeGenerator(_scanner, logger: null);
        Options = CreateInteractiveOptions(RootPath);
    }

    public string RootPath => _directory.Path;

    public AuthoringCodeGenerator Generator { get; }

    public AppAutomationRecorderOptions Options { get; }

    public static RecorderScenarioDestinationProject Create(params RecorderSourceFile[] files)
    {
        return new RecorderScenarioDestinationProject(files);
    }

    public ScenarioDestinationDiscoveryResult Discover()
    {
        return _scanner.DiscoverScenarioDestinations(
            RootPath,
            ScenarioNamespaceRoot,
            OutputSubdirectoryRoot);
    }

    public RecordedScenarioDestination DiscoverSingleDestination()
    {
        return Discover().Destinations.Single();
    }

    public RecorderScenarioSaveContext CreateSaveContext(
        string scenarioName = "Customer flow",
        string draftIdentity = "draft-a")
    {
        return new RecorderScenarioSaveContext(
            DiscoverSingleDestination(),
            scenarioName,
            draftIdentity);
    }

    public Task<RecorderSaveResult> SaveAsync(
        RecorderScenarioSaveContext context,
        string? outputDirectory = null)
    {
        return Generator.SaveAsync(
            RecorderTestWindow.CreateStub(),
            Options,
            [RecorderTestSteps.CreateButtonClick("SaveButton")],
            outputDirectory,
            context);
    }

    public Task<RecorderSaveResult> AutosaveAsync(RecorderScenarioSaveContext context)
    {
        return Generator.AutosaveAsync(
            RecorderTestWindow.CreateStub(),
            Options,
            [RecorderTestSteps.CreateButtonClick("SaveButton")],
            outputDirectoryOverride: null,
            context);
    }

    public void DeleteSource(string fileName)
    {
        File.Delete(SourcePath(fileName));
    }

    public string SourcePath(string fileName)
    {
        return Path.Combine(RootPath, fileName);
    }

    public static AppAutomationRecorderOptions CreateInteractiveOptions(
        string projectDirectory,
        string scenarioName = "RecordedScenario")
    {
        return new AppAutomationRecorderOptions
        {
            AuthoringProjectDirectory = projectDirectory,
            PageNamespace = "Sample.Authoring.Pages",
            PageClassName = "MainWindowPage",
            ScenarioName = scenarioName,
            ScenarioSelection = new RecorderScenarioSelectionOptions
            {
                IsEnabled = true,
                ScenarioNamespaceRoot = ScenarioNamespaceRoot,
                OutputSubdirectoryRoot = OutputSubdirectoryRoot
            },
            DiagnosticLog = new RecorderDiagnosticLogOptions { WriteToFile = false }
        };
    }

    public void Dispose()
    {
        _directory.Dispose();
    }

    private void WriteSource(RecorderSourceFile file)
    {
        File.WriteAllText(SourcePath(file.Name), file.Content);
    }
}

internal sealed class InteractiveScenarioSelectionFixture : IDisposable
{
    private readonly TaskCompletionSource<RecorderSaveResult> _saveCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private InteractiveScenarioSelectionFixture(string projectDirectory)
    {
        var options = RecorderScenarioDestinationProject.CreateInteractiveOptions(
            projectDirectory,
            scenarioName: "RecordedSmoke");
        Session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options,
            validationRootProvider: static () => null,
            attachWindowHandlers: false,
            saveOperation: (_, _, _) => _saveCompletion.Task);
        Selection = Session as IRecorderScenarioSelectionDetails
            ?? throw new InvalidOperationException("Recorder session does not expose scenario selection details.");
    }

    public RecorderSession Session { get; }

    public IRecorderScenarioSelectionDetails Selection { get; }

    public static InteractiveScenarioSelectionFixture Create(string projectDirectory)
    {
        return new InteractiveScenarioSelectionFixture(projectDirectory);
    }

    public async Task WaitForScanAsync()
    {
        await WaitForAsync(() => !Selection.IsScanning, "scenario destination scan");
    }

    public async Task WaitForSaveToStartAsync()
    {
        await WaitForAsync(() => Session.IsBusy, "recorder save operation");
    }

    public void SelectValidTarget(string scenarioName = "Customer flow")
    {
        var destination = Selection.ScenarioDestinations.Single();
        if (!Selection.TrySelectScenarioDestination(destination)
            || !Selection.TrySetScenarioName(scenarioName))
        {
            throw new InvalidOperationException("The valid recorder target could not be selected.");
        }
    }

    public void CompleteSaveWithFailure()
    {
        _saveCompletion.SetResult(RecorderSaveResult.Failed("Expected test completion."));
    }

    public void Dispose()
    {
        Session.Dispose();
    }

    private static async Task WaitForAsync(Func<bool> condition, string operation)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"Timed out waiting for {operation}.");
            }

            await Task.Delay(20);
        }
    }
}

internal static class RecorderTestSteps
{
    public static RecordedStep CreateButtonClick(string automationId)
    {
        return new RecordedStep(
            RecordedActionKind.ClickButton,
            new RecordedControlDescriptor(
                automationId,
                UiControlType.Button,
                automationId,
                UiLocatorKind.AutomationId,
                FallbackToName: false,
                AvaloniaTypeName: typeof(Button).FullName ?? nameof(Button),
                Warning: null),
            StepId: Guid.NewGuid());
    }
}

internal static class RecorderTestWindow
{
    public static Window CreateStub()
    {
#pragma warning disable SYSLIB0050
        return (Window)FormatterServices.GetUninitializedObject(typeof(TestRecorderWindow));
#pragma warning restore SYSLIB0050
    }

    private sealed class TestRecorderWindow : Window;
}

internal sealed class FakeScenarioSelectionSession :
    IAppAutomationRecorderSession,
    IRecorderScenarioSelectionDetails,
    IRecorderScenarioPathDetails
{
    public RecorderSessionState State { get; private set; }

    public int StepCount => 0;

    public int PersistableStepCount => 0;

    public string LatestPreview => string.Empty;

    public string LatestStatus => string.Empty;

    public RecorderValidationStatus LatestValidationStatus => RecorderValidationStatus.Valid;

    public bool IsScenarioSelectionEnabled => true;

    public bool IsScanning { get; set; }

    public string? ScenarioSelectionError { get; set; }

    public IReadOnlyList<RecordedScenarioDestination> ScenarioDestinations { get; set; } = [];

    public RecordedScenarioDestination? SelectedScenarioDestination { get; set; }

    public string ScenarioName { get; set; } = string.Empty;

    public bool CanStartRecording { get; set; }

    public bool CanChangeScenarioTarget { get; set; }

    public string CurrentScenarioFilePath => "future.g.cs";

    public void Start() => State = RecorderSessionState.Recording;

    public void Stop() => State = RecorderSessionState.Off;

    public void Clear()
    {
    }

    public string ExportPreview() => string.Empty;

    public Task<RecorderSaveResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(RecorderSaveResult.Failed("Not used."));
    }

    public Task<RecorderSaveResult> SaveToDirectoryAsync(
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(RecorderSaveResult.Failed("Not used."));
    }

    public bool TrySelectScenarioDestination(RecordedScenarioDestination? destination)
    {
        SelectedScenarioDestination = destination;
        return true;
    }

    public bool TrySetScenarioName(string? scenarioName)
    {
        ScenarioName = scenarioName ?? string.Empty;
        return true;
    }

    public void Dispose()
    {
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "AppAutomation.Recorder.Avalonia.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
