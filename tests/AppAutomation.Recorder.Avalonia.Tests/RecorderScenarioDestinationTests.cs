using System.Text.RegularExpressions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.UI;
using Avalonia.Automation;
using Avalonia.Controls;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

[NotInParallel]
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
    public async Task SelectedDestination_SaveCreatesUniqueGenericPartials()
    {
        using var project = CreateGenericScenarioProject();
        var context = project.CreateSaveContext();

        var first = await project.SaveAsync(context);
        var second = await project.SaveAsync(context);
        var firstSource = await File.ReadAllTextAsync(first.ScenarioFilePath!);
        var secondSource = await File.ReadAllTextAsync(second.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(first.Success).IsTrue();
            await Assert.That(second.Success).IsTrue();
            await Assert.That(first.ScenarioFilePath).IsNotEqualTo(second.ScenarioFilePath);
            await Assert.That(first.PageFilePath).IsNotEqualTo(second.PageFilePath);
            await Assert.That(Path.GetDirectoryName(first.ScenarioFilePath!))
                .IsEqualTo(Path.Combine(project.RootPath, "Recorded", "Customers"));
            await Assert.That(firstSource).Contains("namespace Sample.Authoring.Tests.Customers;");
            await Assert.That(firstSource).Contains("partial class CreateCustomerTests<TSession, TSetup>");
            await Assert.That(ExtractMethodName(firstSource)).IsNotEqualTo(ExtractMethodName(secondSource));
        }
    }

    [Test]
    public async Task SelectedDestination_SaveDoesNotModifyPreviousGeneratedFiles()
    {
        using var project = CreateGenericScenarioProject();
        var context = project.CreateSaveContext();
        var first = await project.SaveAsync(context);
        var firstScenarioSource = await File.ReadAllTextAsync(first.ScenarioFilePath!);
        var firstPageSource = await File.ReadAllTextAsync(first.PageFilePath!);
        var firstScenarioWriteTime = File.GetLastWriteTimeUtc(first.ScenarioFilePath!);
        var firstPageWriteTime = File.GetLastWriteTimeUtc(first.PageFilePath!);

        await project.SaveAsync(context);

        using (Assert.Multiple())
        {
            await Assert.That(await File.ReadAllTextAsync(first.ScenarioFilePath!)).IsEqualTo(firstScenarioSource);
            await Assert.That(File.GetLastWriteTimeUtc(first.ScenarioFilePath!)).IsEqualTo(firstScenarioWriteTime);
            await Assert.That(await File.ReadAllTextAsync(first.PageFilePath!)).IsEqualTo(firstPageSource);
            await Assert.That(File.GetLastWriteTimeUtc(first.PageFilePath!)).IsEqualTo(firstPageWriteTime);
        }
    }

    [Test]
    public async Task SelectedDestination_ExportCreatesUniqueFilesInRequestedDirectory()
    {
        using var project = CreateGenericScenarioProject();
        var context = project.CreateSaveContext();
        var exportDirectory = Path.Combine(project.RootPath, "Export");

        var first = await project.SaveAsync(context, exportDirectory);
        var second = await project.SaveAsync(context, exportDirectory);
        var firstSource = await File.ReadAllTextAsync(first.ScenarioFilePath!);
        var secondSource = await File.ReadAllTextAsync(second.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(Path.GetDirectoryName(first.ScenarioFilePath!)).IsEqualTo(exportDirectory);
            await Assert.That(second.ScenarioFilePath).IsNotEqualTo(first.ScenarioFilePath);
            await Assert.That(ExtractMethodName(secondSource)).IsNotEqualTo(ExtractMethodName(firstSource));
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
        overlay.RefreshForTesting();

        await AssertReadyState(overlay, destination);
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
        var recordButton = FindRequired<Button>(overlay, "RecordButton");

        using (Assert.Multiple())
        {
            await Assert.That(progress.IsVisible).IsFalse();
            await Assert.That(status.IsVisible).IsFalse();
            await Assert.That(destinationBox.SelectedItem).IsEqualTo(destination);
            await Assert.That(nameBox.Text).IsEqualTo("RecordedSmoke");
            await Assert.That(recordButton.IsEnabled).IsTrue();
            await Assert.That(AutomationProperties.GetAutomationId(destinationBox))
                .IsEqualTo("RecorderScenarioDestination");
            await Assert.That(AutomationProperties.GetAutomationId(nameBox))
                .IsEqualTo("RecorderScenarioName");
        }
    }

    private static TControl FindRequired<TControl>(RecorderOverlay overlay, string name)
        where TControl : Control
    {
        return overlay.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"Recorder overlay control '{name}' was not found.");
    }

    private static string ExtractMethodName(string source)
    {
        return Regex.Match(
            source,
            "public void (?<name>[^\\(]+)\\(",
            RegexOptions.CultureInvariant).Groups["name"].Value;
    }
}

internal static class RecorderScenarioSelectionTestExtensions
{
    public static string CurrentPathForTesting(this IRecorderScenarioSelectionDetails details)
    {
        return ((IRecorderScenarioPathDetails)details).CurrentScenarioFilePath;
    }
}
