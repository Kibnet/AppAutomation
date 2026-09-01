using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.UI;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace AppAutomation.Recorder.Avalonia.Tests;

[NotInParallel]
public sealed class RecorderGeneratedValueTests
{
    [Test]
    public async Task GeneratedValue_CanBeReusedAndComparedByCheck()
    {
        var first = TextBox("PrimaryCode");
        var second = TextBox("ConfirmationCode");
        var root = new StackPanel { Children = { first, second } };
        using var session = CreateSession(root);
        RecorderGeneratedValueTargetSelection? generatedSelection = null;
        RecorderCheckTargetSelection? checkSelection = null;
        session.GeneratedValueTargetSelected += (_, eventArgs) => generatedSelection = eventArgs.Selection;
        session.CheckTargetSelected += (_, eventArgs) => checkSelection = eventArgs.Selection;
        session.Start();

        session.BeginGeneratedValueTargetSelection();
        session.SelectGeneratedValueTargetForTesting(first);
        session.ApplyGeneratedValue(generatedSelection!);
        var generatedValue = session.GeneratedValues.Single();

        using (Assert.Multiple())
        {
            await Assert.That(generatedSelection).IsNotNull();
            await Assert.That(generatedSelection!.DefinesGeneratedValue).IsTrue();
            await Assert.That(first.Text).Matches("^Recorded_[0-9]{8}_[0-9]{9}_1$");
            await Assert.That(session.StepCount).IsEqualTo(1);
            await Assert.That(session.PersistableStepCount).IsEqualTo(1);
        }

        generatedSelection = null;
        session.BeginGeneratedValueTargetSelection(generatedValue.GeneratedValueId);
        session.SelectGeneratedValueTargetForTesting(second);
        session.ApplyGeneratedValue(generatedSelection!);

        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(second);
        session.CaptureGeneratedValueAssertion(
            checkSelection!,
            generatedValue.GeneratedValueId,
            RecorderComparisonKind.Equal);
        session.CaptureGeneratedValueAssertion(
            checkSelection!,
            generatedValue.GeneratedValueId,
            RecorderComparisonKind.NotEqual);

        var preview = session.ExportPreview();
        using (Assert.Multiple())
        {
            await Assert.That(second.Text).IsEqualTo(first.Text);
            await Assert.That(session.GeneratedValues.Count).IsEqualTo(1);
            await Assert.That(CountOccurrences(preview, "RecordedValueGenerator.Start()")).IsEqualTo(1);
            await Assert.That(CountOccurrences(preview, "recordedValues.Create(1)")).IsEqualTo(1);
            await Assert.That(preview).Contains(
                "Page.EnterText(static page => page.PrimaryCode, generatedValue1);");
            await Assert.That(preview).Contains(
                "Page.EnterText(static page => page.ConfirmationCode, generatedValue1);");
            await Assert.That(preview).Contains(
                "await Assert.That(Page.ConfirmationCode.Text).IsEqualTo(generatedValue1);");
            await Assert.That(preview).Contains(
                "await Assert.That(Page.ConfirmationCode.Text).IsNotEqualTo(generatedValue1);");
            await Assert.That(session.StepJournal.All(static entry => entry.CanPersist)).IsTrue();
        }
    }

    [Test]
    public async Task GeneratedValue_OrdinalIsCommittedOnAddAndNotReusedAfterRemoval()
    {
        var first = TextBox("FirstValue");
        var second = TextBox("SecondValue");
        var third = TextBox("ThirdValue");
        var root = new StackPanel { Children = { first, second, third } };
        using var session = CreateSession(root);
        RecorderGeneratedValueTargetSelection? selection = null;
        session.GeneratedValueTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;
        session.Start();

        session.BeginGeneratedValueTargetSelection();
        session.SelectGeneratedValueTargetForTesting(first);
        var cancelledOrdinal = selection!.GeneratedValue.Ordinal;

        selection = null;
        session.BeginGeneratedValueTargetSelection();
        session.SelectGeneratedValueTargetForTesting(first);
        session.ApplyGeneratedValue(selection!);
        var firstStepId = session.StepJournal.Single().StepId;

        selection = null;
        session.BeginGeneratedValueTargetSelection();
        session.SelectGeneratedValueTargetForTesting(second);
        session.ApplyGeneratedValue(selection!);
        var removedOrdinal = selection!.GeneratedValue.Ordinal;
        var secondStepId = session.StepJournal[^1].StepId;
        session.RemoveStep(secondStepId);

        selection = null;
        session.BeginGeneratedValueTargetSelection();
        session.SelectGeneratedValueTargetForTesting(third);
        session.ApplyGeneratedValue(selection!);

        using (Assert.Multiple())
        {
            await Assert.That(cancelledOrdinal).IsEqualTo(1);
            await Assert.That(first.Text).EndsWith("_1");
            await Assert.That(removedOrdinal).IsEqualTo(2);
            await Assert.That(third.Text).EndsWith("_3");
            await Assert.That(session.StepJournal.Any(entry => entry.StepId == firstStepId)).IsTrue();
            await Assert.That(session.GeneratedValues.Select(value => value.Ordinal)).IsEquivalentTo([1, 3]);
        }
    }

    [Test]
    public async Task GeneratedValueGraph_RejectsForwardReferenceAndRemovalInvalidatesUse()
    {
        var generatedValueId = Guid.NewGuid();
        var definition = GeneratedEnterText("SourceValue", generatedValueId, defines: true, ordinal: 1);
        var use = GeneratedEnterText("TargetValue", generatedValueId, defines: false, ordinal: 1);
        var forward = RecorderScenarioGraphValidator.Validate([use, definition]);
        var duplicateOrdinal = RecorderScenarioGraphValidator.Validate(
            [definition, GeneratedEnterText("OtherValue", Guid.NewGuid(), defines: true, ordinal: 1)]);
        using var session = CreateSession(new StackPanel());
        session.AddRecordedStepForTesting(definition);
        session.AddRecordedStepForTesting(use);

        session.RemoveStep(session.StepJournal[0].StepId);

        using (Assert.Multiple())
        {
            await Assert.That(forward.Success).IsFalse();
            await Assert.That(forward.Error).Contains("missing or later generated value");
            await Assert.That(duplicateOrdinal.Success).IsFalse();
            await Assert.That(duplicateOrdinal.Error).Contains("ordinal '1' is defined more than once");
            await Assert.That(session.StepJournal.Single().CanPersist).IsFalse();
            await Assert.That(session.StepJournal.Single().StatusMessage)
                .Contains("missing or later generated value");
        }
    }

    [Test]
    public async Task GenerateValue_UsesConfiguredTextBoxProxyWithoutWarningComment()
    {
        var options = CreateOptions();
        options.ConfigureTextBoxProxy("CustomerEditor", "CustomerEditor_Input");
        var input = TextBox("CustomerEditor_Input");
        var logicalEditor = new Border { Child = input };
        AutomationProperties.SetAutomationId(logicalEditor, "CustomerEditor");
        var root = new StackPanel { Children = { logicalEditor } };
        using var session = CreateSession(root, options);
        RecorderGeneratedValueTargetSelection? selection = null;
        session.GeneratedValueTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;
        session.Start();

        session.BeginGeneratedValueTargetSelection();
        session.SelectGeneratedValueTargetForTesting(input);
        session.ApplyGeneratedValue(selection!);

        var journal = session.StepJournal.Single();
        using (Assert.Multiple())
        {
            await Assert.That(selection!.ControlName).IsEqualTo("CustomerEditor");
            await Assert.That(journal.CanPersist).IsTrue();
            await Assert.That(journal.ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(journal.Preview).Contains(
                "Page.EnterText(static page => page.CustomerEditor, generatedValue1);");
            await Assert.That(journal.Preview).DoesNotContain("CustomerEditor_Input");
            await Assert.That(journal.Preview).DoesNotContain("recorder warning");
        }
    }

    [Test]
    public async Task GenerateValue_InvalidTargetsProduceErrorsWithoutSteps()
    {
        var readOnly = TextBox("ReadOnlyValue");
        readOnly.IsReadOnly = true;
        var disabled = TextBox("DisabledValue");
        disabled.IsEnabled = false;
        var first = TextBox("FirstCandidate");
        var second = TextBox("SecondCandidate");
        var surface = new DockPanel();
        var root = new StackPanel { Children = { readOnly, disabled, first, second, surface } };
        using var session = CreateSession(root);
        session.Start();

        session.BeginGeneratedValueTargetSelection();
        session.SelectGeneratedValueTargetForTesting(readOnly);
        var readOnlyError = session.LatestStatus;

        session.BeginGeneratedValueTargetSelection();
        session.SelectGeneratedValueTargetForTesting(disabled);
        var disabledError = session.LatestStatus;

        session.BeginGeneratedValueTargetSelection();
        session.SelectGeneratedValueTargetForTesting(surface, [surface]);
        var unsupportedError = session.LatestStatus;

        session.BeginGeneratedValueTargetSelection();
        session.SelectGeneratedValueTargetForTesting(surface, [surface, first, second]);
        var ambiguousError = session.LatestStatus;

        using (Assert.Multiple())
        {
            await Assert.That(readOnlyError).Contains("enabled writable text field");
            await Assert.That(disabledError).Contains("enabled writable text field");
            await Assert.That(unsupportedError).Contains("Select a writable text field");
            await Assert.That(ambiguousError).Contains("more than one writable text field");
            await Assert.That(session.StepCount).IsEqualTo(0);
            await Assert.That(session.GeneratedValues).IsEmpty();
        }
    }

    [Test]
    public async Task Overlay_OffersGeneratedValueModeAndCancelLeavesJournalUntouched()
    {
        var input = TextBox("GeneratedValueTarget");
        var root = new StackPanel { Children = { input } };
        using var session = CreateSession(root);
        session.Start();
        RecorderGeneratedValueTargetSelection? selection = null;
        session.GeneratedValueTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        overlay.RefreshForTesting();
        var generateButton = overlay.FindControl<Button>("GenerateValueButton");
        var menu = overlay.CreateGeneratedValueMenuForTesting();
        var create = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "New value"));

        create.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        session.SelectGeneratedValueTargetForTesting(input);
        var confirmation = overlay.CreateGeneratedValueConfirmationForTesting(selection!);
        confirmation.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => Equals(button.Content, "Cancel"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        using (Assert.Multiple())
        {
            await Assert.That(generateButton).IsNotNull();
            await Assert.That(generateButton!.IsEnabled).IsTrue();
            await Assert.That(session.IsGeneratedValueTargetSelectionActive).IsFalse();
            await Assert.That(session.StepCount).IsEqualTo(0);
            await Assert.That(string.IsNullOrEmpty(input.Text)).IsTrue();
        }
    }

    [Test]
    public async Task Autosave_GeneratedValueGraphCarriesRecoveryStateAndFinalSourceCompiles()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage,
            RecorderScenarioDestinationSources.CompilableScenario);
        var context = project.CreateSaveContext("Generated value flow", "generated-value-draft");
        var generatedValueId = Guid.NewGuid();
        var steps = new[]
        {
            new RecordedStep(
                RecordedActionKind.EnterText,
                Descriptor("CustomerName", UiControlType.TextBox),
                StringValue: "Recorded_20260901_141502347_1",
                GeneratedValueId: generatedValueId,
                GeneratedValueVariableName: "generatedValue1",
                GeneratedValueOrdinal: 1,
                DefinesGeneratedValue: true),
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("CustomerName", UiControlType.TextBox),
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.Text,
                ComparisonKind: RecorderComparisonKind.Equal,
                ExpectedGeneratedValueId: generatedValueId)
        };

        var autosave = await project.AutosaveAsync(context, steps);
        var autosaveSource = await File.ReadAllTextAsync(autosave.ScenarioFilePath!);
        var customerName = TextBox("CustomerName");
        var additionalName = TextBox("AdditionalName");
        var root = new StackPanel { Children = { customerName, additionalName } };
        var options = RecorderScenarioDestinationProject.CreateInteractiveOptions(
            project.RootPath,
            scenarioName: "Generated value flow");
        using var restoredSession = CreateSession(root, options);
        restoredSession.ScenarioDiscoveryTaskForTesting.GetAwaiter().GetResult();
        var scenarioSelection = (IRecorderScenarioSelectionDetails)restoredSession;
        scenarioSelection.TrySelectScenarioDestination(scenarioSelection.ScenarioDestinations.Single());
        scenarioSelection.TrySetScenarioName("Generated value flow");

        var restored = await scenarioSelection.RestoreAutosaveAsync();
        RecorderGeneratedValueTargetSelection? generatedSelection = null;
        restoredSession.GeneratedValueTargetSelected += (_, eventArgs) => generatedSelection = eventArgs.Selection;
        restoredSession.Start();
        restoredSession.BeginGeneratedValueTargetSelection();
        restoredSession.SelectGeneratedValueTargetForTesting(additionalName);
        restoredSession.ApplyGeneratedValue(generatedSelection!);
        restoredSession.Stop();

        var save = await restoredSession.SaveAsync();
        var source = await File.ReadAllTextAsync(save.ScenarioFilePath!);
        var compileErrors = RecorderGeneratedSourceCompiler.Compile(project.RootPath);

        using (Assert.Multiple())
        {
            await Assert.That(restored).IsTrue();
            await Assert.That(restoredSession.StepCount).IsEqualTo(3);
            await Assert.That(restoredSession.GeneratedValues.Select(value => value.Ordinal))
                .IsEquivalentTo([1, 2]);
            await Assert.That(generatedSelection!.GeneratedValue.Ordinal).IsEqualTo(2);
            await Assert.That(save.Success).IsTrue();
            await Assert.That(autosaveSource)
                .Contains("// AppAutomation recorder autosave state: ");
            await Assert.That(source).Contains("var recordedValues = RecordedValueGenerator.Start();");
            await Assert.That(source).Contains("var generatedValue1 = recordedValues.Create(1);");
            await Assert.That(source).Contains("var generatedValue2 = recordedValues.Create(2);");
            await Assert.That(source).Contains("IsEqualTo(generatedValue1)");
            await Assert.That(source).DoesNotContain("Recorded_20260901_141502347_1");
            await Assert.That(compileErrors).IsEmpty();
        }
    }

    private static TextBox TextBox(string automationId)
    {
        var textBox = new TextBox();
        AutomationProperties.SetAutomationId(textBox, automationId);
        return textBox;
    }

    private static RecorderSession CreateSession(Control root, AppAutomationRecorderOptions? options = null)
    {
        return new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options ?? CreateOptions(),
            validationRootProvider: () => root,
            attachWindowHandlers: false);
    }

    private static AppAutomationRecorderOptions CreateOptions() => new()
    {
        Validation = new RecorderValidationOptions
        {
            ValidateSelectors = true,
            ValidateRuntimeTargets = false,
            CaptureInvalidSteps = true
        }
    };

    private static RecordedControlDescriptor Descriptor(string automationId, UiControlType type) =>
        new(
            automationId,
            type,
            automationId,
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: type.ToString(),
            Warning: null);

    private static RecordedStep GeneratedEnterText(
        string automationId,
        Guid generatedValueId,
        bool defines,
        int ordinal) =>
        new(
            RecordedActionKind.EnterText,
            Descriptor(automationId, UiControlType.TextBox),
            StringValue: "Recorded_20260901_141502347_1",
            GeneratedValueId: generatedValueId,
            GeneratedValueVariableName: "generatedValue1",
            GeneratedValueOrdinal: ordinal,
            DefinesGeneratedValue: defines,
            StepId: Guid.NewGuid());

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;
}
