using System.Text.RegularExpressions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.UI;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

[NotInParallel("RecorderOverlay")]
public sealed class RecorderScenarioDestinationTests
{
    [Test]
    public async Task FileReservation_RethrowsNonCollisionIoFailure()
    {
        var missingParent = Path.Combine(
            Path.GetTempPath(),
            $"appautomation-missing-{Guid.NewGuid():N}",
            "scenario.g.cs");

        await Assert.That(() => AuthoringCodeGenerator.TryReserveFile(missingParent))
            .Throws<DirectoryNotFoundException>();
    }

    [Test]
    public async Task DiscoverScenarioDestinations_MapsSourcePartialClasses()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.DiscoveryScenarios,
            RecorderScenarioDestinationSources.OutsideNamespace,
            RecorderScenarioDestinationSources.OperationScenarios,
            RecorderScenarioDestinationSources.GeneratedScenario);

        var result = project.Discover();
        var customers = FindDestination(result, "Customers.CreateCustomerTests");
        var root = FindDestination(result, "RootScenarios");

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Destinations.Select(static destination => destination.DisplayName))
                .IsEquivalentTo([
                    "Customers.CreateCustomerTests",
                    "Customers.Outer",
                    "Operations.OperationScenarios",
                    "RootScenarios"
                ]);
            await Assert.That(customers.OutputSubdirectory)
                .IsEqualTo(Path.Combine("Recorded", "Customers"));
            await Assert.That(root.OutputSubdirectory).IsEqualTo("Recorded");
        }
    }

    [Test]
    public async Task DiscoverScenarioDestinations_RejectsGenericArityAmbiguity()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.AmbiguousGenericScenarios);

        var result = project.Discover();

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Error).Contains("ambiguous");
            await Assert.That(result.Destinations).IsEmpty();
        }
    }

    [Test]
    public async Task InteractiveSession_GatesStartUntilDestinationAndNameAreValid()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CustomerScenarioWithOneTypeParameter);
        using var recorder = InteractiveScenarioSelectionFixture.Create(project.RootPath);

        recorder.Session.Start();
        await Assert.That(recorder.Session.State).IsEqualTo(RecorderSessionState.Off);

        await recorder.WaitForScanAsync();
        await Assert.That(recorder.Selection.CanStartRecording).IsFalse();

        var destination = recorder.Selection.ScenarioDestinations.Single();
        await Assert.That(recorder.Selection.TrySelectScenarioDestination(destination)).IsTrue();
        await Assert.That(recorder.Selection.TrySetScenarioName("../unsafe")).IsTrue();
        await Assert.That(recorder.Selection.CanStartRecording).IsFalse();
        await Assert.That(recorder.Selection.ScenarioSelectionError).Contains("cannot be used safely");

        await Assert.That(recorder.Selection.TrySetScenarioName("  Customer flow  ")).IsTrue();
        await Assert.That(recorder.Selection.CanStartRecording).IsTrue();
        await Assert.That(recorder.Selection.CurrentPathForTesting())
            .Contains(Path.Combine("Recorded", "Customers"));

        recorder.Session.Start();

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.State).IsEqualTo(RecorderSessionState.Recording);
            await Assert.That(recorder.Selection.CanChangeScenarioTarget).IsFalse();
            await Assert.That(recorder.Selection.ScenarioName).IsEqualTo("Customer flow");
        }
    }

    [Test]
    public async Task InteractiveSession_LocksTargetUntilRecordedStepsAreCleared()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CustomerScenarioWithOneTypeParameter);
        using var recorder = InteractiveScenarioSelectionFixture.Create(project.RootPath);
        await recorder.WaitForScanAsync();
        recorder.SelectValidTarget();

        recorder.Session.Start();
        recorder.Session.AddRecordedStepForTesting(RecorderTestSteps.CreateButtonClick("SaveButton"));
        recorder.Session.Clear();

        await Assert.That(recorder.Session.StepCount).IsEqualTo(1);
        recorder.Session.Stop();
        await Assert.That(recorder.Selection.CanChangeScenarioTarget).IsFalse();

        var saveTask = recorder.Session.SaveAsync();
        await recorder.WaitForSaveToStartAsync();
        recorder.Session.Clear();

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepCount).IsEqualTo(1);
            await Assert.That(recorder.Selection.CanChangeScenarioTarget).IsFalse();
        }

        recorder.CompleteSaveWithFailure();
        await saveTask;
        recorder.Session.Clear();

        await Assert.That(recorder.Selection.CanChangeScenarioTarget).IsTrue();
    }

    [Test]
    public async Task SelectedDestination_ReusesCanonicalGenericPartials()
    {
        using var project = CreateGenericScenarioProject();
        var context = project.CreateSaveContext();

        var first = await project.SaveAsync(context);
        var second = await project.SaveAsync(context);
        var scenarioSource = await File.ReadAllTextAsync(second.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(first.Success).IsTrue();
            await Assert.That(second.Success).IsTrue();
            await Assert.That(first.ScenarioFilePath).IsEqualTo(second.ScenarioFilePath);
            await Assert.That(Path.GetFileName(first.ScenarioFilePath!))
                .IsEqualTo("CreateCustomerTests.RecorderScenarios.g.cs");
            await Assert.That(Path.GetFileName(first.PageFilePath!))
                .IsEqualTo("MainWindowPage.RecorderControls.g.cs");
            await Assert.That(second.PageFilePath).IsNull();
            await Assert.That(Path.GetDirectoryName(first.ScenarioFilePath!))
                .IsEqualTo(Path.Combine(project.RootPath, "Recorded", "Customers"));
            await Assert.That(Path.GetDirectoryName(first.PageFilePath!)).IsEqualTo(project.RootPath);
            await Assert.That(Directory.EnumerateFiles(
                    Path.Combine(project.RootPath, "Recorded", "Customers"),
                    "*.RecorderScenarios.g.cs",
                    SearchOption.TopDirectoryOnly).Count())
                .IsEqualTo(1);
            await Assert.That(scenarioSource).Contains("namespace Sample.Authoring.Tests.Customers;");
            await Assert.That(scenarioSource).Contains("partial class CreateCustomerTests<TSession, TSetup>");
            await Assert.That(CountRecordedMethods(scenarioSource)).IsEqualTo(2);
        }
    }

    [Test]
    public async Task SelectedDestination_MergesNewControlsWithoutChangingUserSources()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.MainWindowPage,
            RecorderScenarioDestinationSources.RootScenario);
        var context = project.CreateSaveContext();
        var pageSourcePath = project.SourcePath(RecorderScenarioDestinationSources.MainWindowPage.Name);
        var scenarioSourcePath = project.SourcePath(RecorderScenarioDestinationSources.RootScenario.Name);
        var originalPageSource = await File.ReadAllTextAsync(pageSourcePath);
        var originalScenarioSource = await File.ReadAllTextAsync(scenarioSourcePath);

        var first = await project.SaveAsync(context, automationId: "SaveButton");
        var second = await project.SaveAsync(context, automationId: "CancelButton");
        var controlsSource = await File.ReadAllTextAsync(second.PageFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(first.PageFilePath).IsEqualTo(second.PageFilePath);
            await Assert.That(first.ScenarioFilePath).IsEqualTo(second.ScenarioFilePath);
            await Assert.That(controlsSource).Contains("\"SaveButton\"");
            await Assert.That(controlsSource).Contains("\"CancelButton\"");
            await Assert.That(Directory.EnumerateFiles(project.RootPath, "*.RecorderControls.g.cs", SearchOption.AllDirectories).Count())
                .IsEqualTo(1);
            await Assert.That(await File.ReadAllTextAsync(pageSourcePath)).IsEqualTo(originalPageSource);
            await Assert.That(await File.ReadAllTextAsync(scenarioSourcePath)).IsEqualTo(originalScenarioSource);
        }
    }

    [Test]
    public async Task SelectedDestination_ResolvesConstControlsAcrossPartialsAndAddsOnlyNewControls()
    {
        var automationIds = new RecorderSourceFile(
            "RecorderAutomationIds.cs",
            """
            namespace Sample.Authoring.Pages;

            internal static class RecorderAutomationIds
            {
                public const string SaveButton = "SaveButton";
            }
            """);
        var manualControls = new RecorderSourceFile(
            "MainWindowPage.Manual.cs",
            """
            using AppAutomation.Abstractions;

            namespace Sample.Authoring.Pages;

            [UiControl("SaveButton", UiControlType.Button, RecorderAutomationIds.SaveButton)]
            [UiControl("CancelButton", UiControlType.Button, CancelButtonAutomationId)]
            public partial class MainWindowPage
            {
                private const string CancelButtonAutomationId = "CancelButton";
            }
            """);
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.MainWindowPage,
            automationIds,
            manualControls,
            RecorderScenarioDestinationSources.RootScenario);
        var context = project.CreateSaveContext();

        var qualifiedConstantResult = await project.SaveAsync(context, automationId: "SaveButton");
        var localConstantResult = await project.SaveAsync(context, automationId: "CancelButton");
        var newControlResult = await project.SaveAsync(context, automationId: "NewButton");
        var repeatedResult = await project.SaveAsync(context, automationId: "NewButton");
        var controlsSource = await File.ReadAllTextAsync(newControlResult.PageFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(qualifiedConstantResult.Success).IsTrue();
            await Assert.That(qualifiedConstantResult.PageFilePath).IsNull();
            await Assert.That(localConstantResult.Success).IsTrue();
            await Assert.That(localConstantResult.PageFilePath).IsNull();
            await Assert.That(newControlResult.Success).IsTrue();
            await Assert.That(newControlResult.PageFilePath).IsNotNull();
            await Assert.That(repeatedResult.Success).IsTrue();
            await Assert.That(repeatedResult.PageFilePath).IsNull();
            await Assert.That(controlsSource).Contains("[UiControl(\"NewButton\", UiControlType.Button, \"NewButton\"");
            await Assert.That(controlsSource).DoesNotContain("[UiControl(\"SaveButton\"");
            await Assert.That(controlsSource).DoesNotContain("[UiControl(\"CancelButton\"");
            await Assert.That(Regex.Count(
                    controlsSource,
                    "\\[UiControl\\(\\\"NewButton\\\"",
                    RegexOptions.CultureInvariant))
                .IsEqualTo(1);
        }
    }

    [Test]
    public async Task SelectedDestination_ExportReusesCanonicalFileInRequestedDirectory()
    {
        using var project = CreateGenericScenarioProject();
        var context = project.CreateSaveContext();
        var exportDirectory = Path.Combine(project.RootPath, "Export");

        var first = await project.SaveAsync(context, exportDirectory);
        var second = await project.SaveAsync(context, exportDirectory);
        var scenarioSource = await File.ReadAllTextAsync(second.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(Path.GetDirectoryName(first.ScenarioFilePath!)).IsEqualTo(exportDirectory);
            await Assert.That(second.ScenarioFilePath).IsEqualTo(first.ScenarioFilePath);
            await Assert.That(CountRecordedMethods(scenarioSource)).IsEqualTo(2);
        }
    }

    [Test]
    public async Task SelectedDestination_UsesIndependentCanonicalFilePerDestination()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.MainWindowPage,
            RecorderScenarioDestinationSources.RootScenario,
            RecorderScenarioDestinationSources.OperationScenarios);
        var destinations = project.Discover().Destinations;
        var rootContext = new RecorderScenarioSaveContext(
            destinations.Single(static destination => destination.DisplayName == "Scenarios"),
            "Root flow",
            "root-draft");
        var operationContext = new RecorderScenarioSaveContext(
            destinations.Single(static destination => destination.DisplayName == "Operations.OperationScenarios"),
            "Operation flow",
            "operation-draft");

        var rootSave = await project.SaveAsync(rootContext);
        var operationSave = await project.SaveAsync(operationContext);

        using (Assert.Multiple())
        {
            await Assert.That(rootSave.ScenarioFilePath).IsNotEqualTo(operationSave.ScenarioFilePath);
            await Assert.That(Path.GetFileName(rootSave.ScenarioFilePath!))
                .IsEqualTo("Scenarios.RecorderScenarios.g.cs");
            await Assert.That(Path.GetFileName(operationSave.ScenarioFilePath!))
                .IsEqualTo("OperationScenarios.RecorderScenarios.g.cs");
            await Assert.That(File.Exists(rootSave.ScenarioFilePath!)).IsTrue();
            await Assert.That(File.Exists(operationSave.ScenarioFilePath!)).IsTrue();
        }
    }

    [Test]
    public async Task SelectedDestination_DoesNotOverwriteMalformedCanonicalScenario()
    {
        using var project = CreateGenericScenarioProject();
        var context = project.CreateSaveContext();
        var first = await project.SaveAsync(context);
        const string malformedSource =
            "// <auto-generated by AppAutomation Recorder />\nnamespace Sample.Authoring.Tests.Customers;\npublic partial class CreateCustomerTests<TSession, TSetup> {";
        await File.WriteAllTextAsync(first.ScenarioFilePath!, malformedSource);

        var second = await project.SaveAsync(context);

        using (Assert.Multiple())
        {
            await Assert.That(second.Success).IsFalse();
            await Assert.That(second.Message).Contains("invalid C#");
            await Assert.That(await File.ReadAllTextAsync(first.ScenarioFilePath!)).IsEqualTo(malformedSource);
        }
    }

    [Test]
    public async Task SelectedDestination_CanonicalGeneratedFilesCompileTogether()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage,
            RecorderScenarioDestinationSources.CompilableScenario);
        var context = project.CreateSaveContext();

        await project.SaveAsync(context, automationId: "SaveButton");
        var second = await project.SaveAsync(context, automationId: "CancelButton");
        var errors = RecorderGeneratedSourceCompiler.Compile(project.RootPath);
        var scenarioSource = await File.ReadAllTextAsync(second.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(errors).IsEmpty();
            await Assert.That(CountRecordedMethods(scenarioSource)).IsEqualTo(2);
            await Assert.That(Regex.Count(scenarioSource, "\\[Test\\]", RegexOptions.CultureInvariant))
                .IsEqualTo(2);
        }
    }

    [Test]
    public async Task SelectedDestination_FinalSaveRemovesAllAutosavesForStableDestination()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.MainWindowPage,
            RecorderScenarioDestinationSources.RootScenario);
        var firstDraft = project.CreateSaveContext(draftIdentity: "draft-a");
        var secondDraft = firstDraft with { DraftIdentity = "draft-b" };
        var otherNamedDraft = project.CreateSaveContext("Other flow", "other-name-draft");
        var finalContext = firstDraft with { DraftIdentity = "final-save" };

        var firstAutosave = await project.AutosaveAsync(firstDraft);
        var secondAutosave = await project.AutosaveAsync(secondDraft);
        var otherNamedAutosave = await project.AutosaveAsync(otherNamedDraft);
        var legacyAutosavePaths = new[]
            {
                firstAutosave.PageFilePath!,
                firstAutosave.ScenarioFilePath!
            };
        foreach (var filePath in legacyAutosavePaths)
        {
            var legacySource = string.Join(
                Environment.NewLine,
                (await File.ReadAllLinesAsync(filePath)).Where(static line =>
                    !line.StartsWith(
                        "// AppAutomation recorder autosave destination:",
                        StringComparison.Ordinal)));
            await File.WriteAllTextAsync(filePath, legacySource);
        }

        var relocatedAutosavePaths = new[]
            {
                secondAutosave.PageFilePath!,
                secondAutosave.ScenarioFilePath!,
                otherNamedAutosave.PageFilePath!,
                otherNamedAutosave.ScenarioFilePath!
            }
            .Select((filePath, index) =>
            {
                var relocatedPath = Path.Combine(
                    Path.GetDirectoryName(filePath)!,
                    $"recovery-{index}.g.cs.autosave");
                File.Move(filePath, relocatedPath);
                return relocatedPath;
            })
            .ToArray();
        var exportDirectory = Path.Combine(project.RootPath, "Export");
        var finalSave = await project.SaveWithFreshGeneratorAsync(finalContext, exportDirectory);

        using (Assert.Multiple())
        {
            await Assert.That(finalSave.Success).IsTrue();
            await Assert.That(legacyAutosavePaths.Where(File.Exists)).IsEmpty();
            await Assert.That(relocatedAutosavePaths.Where(File.Exists)).IsEmpty();
            await Assert.That(Path.GetFileName(finalSave.PageFilePath!))
                .IsEqualTo("MainWindowPage.RecorderControls.g.cs");
            await Assert.That(Path.GetFileName(finalSave.ScenarioFilePath!))
                .IsEqualTo("Scenarios.RecorderScenarios.g.cs");
            await Assert.That(Path.GetDirectoryName(finalSave.ScenarioFilePath!)).IsEqualTo(exportDirectory);
        }
    }

    [Test]
    public async Task SelectedDestination_FinalSaveKeepsAutosavesForAnotherDestination()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.MainWindowPage,
            RecorderScenarioDestinationSources.TwoRootScenarios);
        var destinations = project.Discover().Destinations;
        var savedContext = new RecorderScenarioSaveContext(
            destinations.Single(static destination => destination.ScenarioClassName == "Scenarios"),
            "Shared flow",
            "saved-draft");
        var otherContext = new RecorderScenarioSaveContext(
            destinations.Single(static destination => destination.ScenarioClassName == "OtherScenarios"),
            "Shared flow",
            "other-draft");

        var savedAutosave = await project.AutosaveAsync(savedContext);
        var otherAutosave = await project.AutosaveAsync(otherContext);
        var finalSave = await project.SaveWithFreshGeneratorAsync(savedContext);

        using (Assert.Multiple())
        {
            await Assert.That(finalSave.Success).IsTrue();
            await Assert.That(File.Exists(savedAutosave.PageFilePath!)).IsFalse();
            await Assert.That(File.Exists(savedAutosave.ScenarioFilePath!)).IsFalse();
            await Assert.That(File.Exists(otherAutosave.PageFilePath!)).IsTrue();
            await Assert.That(File.Exists(otherAutosave.ScenarioFilePath!)).IsTrue();
        }
    }

    [Test]
    public async Task SelectedDestination_SaveFailsWhenSourceClassWasDeleted()
    {
        using var project = CreateGenericScenarioProject();
        var context = project.CreateSaveContext();
        project.DeleteSource(RecorderScenarioDestinationSources.CustomerScenario.Name);

        var result = await project.SaveAsync(context);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Message).Contains("was not found");
        }
    }

    [Test]
    public async Task SelectedDestination_AutosaveReusesOnlyCurrentDraft()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.MainWindowPageWithSaveButton,
            RecorderScenarioDestinationSources.RootScenario);
        var firstDraft = project.CreateSaveContext("Smoke", "draft-a");
        var secondDraft = firstDraft with { DraftIdentity = "draft-b" };

        var first = await project.AutosaveAsync(firstDraft);
        var firstUpdate = await project.AutosaveAsync(firstDraft);
        var second = await project.AutosaveAsync(secondDraft);

        using (Assert.Multiple())
        {
            await Assert.That(first.Success).IsTrue();
            await Assert.That(first.PageFilePath).IsNull();
            await Assert.That(firstUpdate.ScenarioFilePath).IsEqualTo(first.ScenarioFilePath);
            await Assert.That(second.Success).IsTrue();
            await Assert.That(second.ScenarioFilePath).IsNotEqualTo(first.ScenarioFilePath);
            await Assert.That(Directory.EnumerateFiles(
                    project.RootPath,
                    "*.controls.g.cs.autosave",
                    SearchOption.AllDirectories))
                .IsEmpty();
        }
    }

    [Test]
    public async Task Overlay_TransitionsFromScanningToReadySelection()
    {
        var session = new FakeScenarioSelectionSession
        {
            IsScanning = true,
            ScenarioName = "RecordedSmoke"
        };
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());

        await AssertScanningState(overlay);

        var destination = CreateCustomerDestination();
        session.IsScanning = false;
        session.ScenarioDestinations = [destination];
        session.SelectedScenarioDestination = destination;
        session.CanStartRecording = true;
        session.CanChangeScenarioTarget = true;
        session.CanRestoreAutosave = true;
        overlay.RefreshForTesting();

        await AssertReadyState(overlay, destination);
        FindRequired<Button>(overlay, "RestoreAutosaveButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Assert.That(session.RestoreAutosaveCallCount).IsEqualTo(1);
    }

    private static RecorderScenarioDestinationProject CreateGenericScenarioProject()
    {
        return RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.MainWindowPage,
            RecorderScenarioDestinationSources.CustomerScenario);
    }

    private static RecordedScenarioDestination FindDestination(
        ScenarioDestinationDiscoveryResult result,
        string displayName)
    {
        return result.Destinations.Single(destination => destination.DisplayName == displayName);
    }

    private static RecordedScenarioDestination CreateCustomerDestination()
    {
        return new RecordedScenarioDestination(
            "Customers.CreateCustomerTests",
            "Sample.Authoring.Tests.Customers",
            "CreateCustomerTests",
            Path.Combine("Recorded", "Customers"));
    }

    private static async Task AssertScanningState(RecorderOverlay overlay)
    {
        var panel = FindRequired<Border>(overlay, "ScenarioSelectionPanel");
        var progress = FindRequired<ProgressBar>(overlay, "ScenarioScanProgress");
        var status = FindRequired<TextBlock>(overlay, "ScenarioScanStatus");
        var recordButton = FindRequired<Button>(overlay, "RecordButton");

        using (Assert.Multiple())
        {
            await Assert.That(panel.IsVisible).IsTrue();
            await Assert.That(progress.IsVisible).IsTrue();
            await Assert.That(progress.IsIndeterminate).IsTrue();
            await Assert.That(progress.Height).IsEqualTo(3);
            await Assert.That(status.Text).IsEqualTo("Идет сканирование…");
            await Assert.That(status.IsVisible).IsTrue();
            await Assert.That(recordButton.IsEnabled).IsFalse();
        }
    }

    private static async Task AssertReadyState(
        RecorderOverlay overlay,
        RecordedScenarioDestination destination)
    {
        var progress = FindRequired<ProgressBar>(overlay, "ScenarioScanProgress");
        var status = FindRequired<TextBlock>(overlay, "ScenarioScanStatus");
        var destinationBox = FindRequired<ComboBox>(overlay, "ScenarioDestinationComboBox");
        var nameBox = FindRequired<TextBox>(overlay, "ScenarioNameTextBox");
        var restoreButton = FindRequired<Button>(overlay, "RestoreAutosaveButton");
        var recordButton = FindRequired<Button>(overlay, "RecordButton");

        using (Assert.Multiple())
        {
            await Assert.That(progress.IsVisible).IsFalse();
            await Assert.That(status.IsVisible).IsFalse();
            await Assert.That(destinationBox.SelectedItem).IsEqualTo(destination);
            await Assert.That(nameBox.Text).IsEqualTo("RecordedSmoke");
            await Assert.That(recordButton.IsEnabled).IsTrue();
            await Assert.That(restoreButton.IsEnabled).IsTrue();
            await Assert.That(AutomationProperties.GetAutomationId(destinationBox))
                .IsEqualTo("RecorderScenarioDestination");
            await Assert.That(AutomationProperties.GetAutomationId(nameBox))
                .IsEqualTo("RecorderScenarioName");
            await Assert.That(AutomationProperties.GetAutomationId(restoreButton))
                .IsEqualTo("RecorderRestoreAutosave");
        }
    }

    private static TControl FindRequired<TControl>(RecorderOverlay overlay, string name)
        where TControl : Control
    {
        return overlay.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"Recorder overlay control '{name}' was not found.");
    }

    private static int CountRecordedMethods(string source)
    {
        return Regex.Count(
            source,
            "public void Recorded_[^\\(]+\\(",
            RegexOptions.CultureInvariant);
    }
}

internal static class RecorderScenarioSelectionTestExtensions
{
    public static string CurrentPathForTesting(this IRecorderScenarioSelectionDetails details)
    {
        return ((IRecorderScenarioPathDetails)details).CurrentScenarioFilePath;
    }
}
