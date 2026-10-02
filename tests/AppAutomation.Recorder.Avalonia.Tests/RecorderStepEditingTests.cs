using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.UI;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

[NotInParallel("RecorderOverlay")]
public sealed class RecorderStepEditingTests
{
    [Test]
    public async Task CheckpointRenameKeepsStableIdAndRefreshesDependentAssertionAndCheckMenu()
    {
        var root = new StackPanel();
        using var session = CreateSession(root);
        var checkpointId = Guid.NewGuid();
        var checkpointStepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(CheckpointStep(
            checkpointStepId,
            checkpointId,
            "ProductCheckpoint"));
        session.AddRecordedStepForTesting(CheckpointAssertionStep(Guid.NewGuid(), checkpointId));
        var editing = (IRecorderStepEditingSessionDetails)session;

        var created = editing.TryCreateStepEditDraft(checkpointStepId, out var draft, out var createError);
        var result = editing.ApplyStepEdit(draft! with { VariableName = "selectedProduct" });
        var checkpoint = session.Checkpoints.Single();
        var journal = session.StepJournal;

        var checkpointMenuHeader = RecorderOverlay.FormatCheckpointMenuHeaderForTesting(checkpoint);

        using (Assert.Multiple())
        {
            await Assert.That(created).IsTrue();
            await Assert.That(createError).IsEmpty();
            await Assert.That(result.Success).IsTrue();
            await Assert.That(checkpoint.CheckpointId).IsEqualTo(checkpointId);
            await Assert.That(checkpoint.VariableName).IsEqualTo("selectedProduct");
            await Assert.That(journal[0].Preview).Contains("var selectedProduct");
            await Assert.That(journal[1].Preview).Contains("selectedProduct");
            await Assert.That(checkpointMenuHeader).StartsWith("selectedProduct (");
        }
    }

    [Test]
    [Arguments("await")]
    [Arguments("not valid")]
    [Arguments("")]
    public async Task InvalidCheckpointNameDoesNotMutateRecordedStep(string invalidName)
    {
        using var session = CreateSession(new StackPanel());
        var checkpointId = Guid.NewGuid();
        var stepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(CheckpointStep(stepId, checkpointId, "BeforeSave"));
        var editing = (IRecorderStepEditingSessionDetails)session;
        editing.TryCreateStepEditDraft(stepId, out var draft, out _);

        var result = editing.ApplyStepEdit(draft! with { VariableName = invalidName });

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Message).IsNotEmpty();
            await Assert.That(session.Checkpoints.Single().VariableName).IsEqualTo("beforeSave");
        }
    }

    [Test]
    public async Task OpeningAndApplyingCheckpointEditorDoesNotChangeGeneratedVariableCasing()
    {
        using var session = CreateSession(new StackPanel());
        var stepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(CheckpointStep(
            stepId,
            Guid.NewGuid(),
            "ProductCheckpoint"));
        var editing = (IRecorderStepEditingSessionDetails)session;

        var created = editing.TryCreateStepEditDraft(stepId, out var draft, out var error);
        var result = editing.ApplyStepEdit(draft!);

        using (Assert.Multiple())
        {
            await Assert.That(created).IsTrue();
            await Assert.That(error).IsEmpty();
            await Assert.That(draft!.VariableName).IsEqualTo("productCheckpoint");
            await Assert.That(result.Success).IsTrue();
            await Assert.That(session.StepJournal.Single().Preview).Contains("var productCheckpoint");
        }
    }

    [Test]
    public async Task GeneratedAndCopiedValueDefinitionsCanBeRenamedWithoutChangingStableIds()
    {
        using var session = CreateSession(new StackPanel());
        var generatedValueId = Guid.NewGuid();
        var copiedValueId = Guid.NewGuid();
        var generatedStepId = Guid.NewGuid();
        var copiedStepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.EnterText,
            Descriptor("GeneratedInput", UiControlType.TextBox),
            StringValue: "Recorded_value",
            StepId: generatedStepId,
            GeneratedValueId: generatedValueId,
            GeneratedValueVariableName: "generatedValue1",
            GeneratedValueOrdinal: 1,
            DefinesGeneratedValue: true));
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.CaptureCopiedValue,
            Descriptor("CopiedSource", UiControlType.TextBox),
            StringValue: "Copied value",
            StepId: copiedStepId,
            ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text,
            CopiedValueId: copiedValueId,
            CopiedValueVariableName: "copiedValue"));
        var editing = (IRecorderStepEditingSessionDetails)session;

        await Apply(editing, generatedStepId, draft => draft with { VariableName = "uniqueCustomer" });
        await Apply(editing, copiedStepId, draft => draft with { VariableName = "customerFromClipboard" });

        using (Assert.Multiple())
        {
            await Assert.That(session.GeneratedValues.Single().GeneratedValueId).IsEqualTo(generatedValueId);
            await Assert.That(session.GeneratedValues.Single().VariableName).IsEqualTo("uniqueCustomer");
            await Assert.That(session.CopiedValues.Single().CopiedValueId).IsEqualTo(copiedValueId);
            await Assert.That(session.CopiedValues.Single().VariableName).IsEqualTo("customerFromClipboard");
            await Assert.That(session.StepJournal[0].Preview).Contains("uniqueCustomer");
            await Assert.That(session.StepJournal[1].Preview).Contains("customerFromClipboard");
        }
    }

    [Test]
    public async Task DuplicateVariableNameAndStaleDraftAreRejectedWithoutMutation()
    {
        using var session = CreateSession(new StackPanel());
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstStepId = Guid.NewGuid();
        var secondStepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(CheckpointStep(firstStepId, firstId, "firstValue"));
        session.AddRecordedStepForTesting(CheckpointStep(secondStepId, secondId, "secondValue"));
        var editing = (IRecorderStepEditingSessionDetails)session;
        editing.TryCreateStepEditDraft(secondStepId, out var duplicateDraft, out _);

        var duplicateResult = editing.ApplyStepEdit(duplicateDraft! with { VariableName = "firstValue" });
        editing.TryCreateStepEditDraft(firstStepId, out var staleDraft, out _);
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.ClickButton,
            Descriptor("SaveButton", UiControlType.Button)));
        var staleResult = editing.ApplyStepEdit(staleDraft! with { VariableName = "renamedValue" });

        using (Assert.Multiple())
        {
            await Assert.That(duplicateResult.Success).IsFalse();
            await Assert.That(duplicateResult.Message).Contains("already used");
            await Assert.That(staleResult.Success).IsFalse();
            await Assert.That(staleResult.Message).Contains("scenario changed");
            await Assert.That(session.Checkpoints.Select(option => option.VariableName))
                .IsEquivalentTo(["firstValue", "secondValue"]);
        }

        editing.TryCreateStepEditDraft(secondStepId, out var ignoredDraft, out _);
        session.SetStepIgnored(secondStepId, isIgnored: true);
        var ignoredResult = editing.ApplyStepEdit(ignoredDraft! with { VariableName = "ignoredName" });
        await Assert.That(ignoredResult.Success).IsFalse();
        await Assert.That(ignoredResult.Message).Contains("Restore the recorded step");
    }

    [Test]
    public async Task AssertionCanSwitchFromLiteralToRenamedCheckpointAndChangeComparison()
    {
        using var session = CreateSession(new StackPanel());
        var checkpointId = Guid.NewGuid();
        session.AddRecordedStepForTesting(CheckpointStep(Guid.NewGuid(), checkpointId, "selectedProduct"));
        var assertionStepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.AssertValue,
            Descriptor("Product", UiControlType.TextBox),
            StringValue: "Old product",
            StepId: assertionStepId,
            ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text,
            ComparisonKind: RecorderComparisonKind.Equal,
            HasExpectedLiteral: true));
        var editing = (IRecorderStepEditingSessionDetails)session;
        editing.TryCreateStepEditDraft(assertionStepId, out var draft, out _);

        var result = editing.ApplyStepEdit(draft! with
        {
            ComparisonKind = RecorderComparisonKind.NotEqual,
            ExpectedSource = RecorderExpectedValueSourceKind.Checkpoint,
            ExpectedCheckpointId = checkpointId
        });

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(session.StepJournal[1].Preview).Contains("IsNotEqualTo");
            await Assert.That(session.StepJournal[1].Preview).Contains("selectedProduct");
            await Assert.That(session.StepJournal[1].Preview).DoesNotContain("Old product");
        }
    }

    [Test]
    public async Task CalculatedAssertionCanEditOperationOperandsAndSelectUiValue()
    {
        var root = new StackPanel();
        var adjustment = new NumericUpDown { Value = 3 };
        AutomationProperties.SetAutomationId(adjustment, "Adjustment");
        root.Children.Add(adjustment);
        using var session = CreateSession(root);
        var checkpointId = Guid.NewGuid();
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.CaptureCheckpoint,
            Descriptor("QuantityBefore", UiControlType.Spinner),
            DoubleValue: 10,
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericValue,
            CheckpointId: checkpointId,
            CheckpointVariableName: "quantityBefore"));
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.AssertValue,
            Descriptor("Remaining", UiControlType.Spinner),
            ValueKind: RecorderValueKind.Number,
            ValueAccessorKind: RecorderValueAccessorKind.NumericValue,
            ComparisonKind: RecorderComparisonKind.Equal,
            NumericExpectedExpression: new RecorderNumericExpectedExpression(
                RecorderArithmeticOperation.Add,
                RecorderNumericOperand.FromCheckpoint(checkpointId),
                RecorderNumericOperand.FromLiteral(1))));
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        session.Start();
        overlay.GetLogicalDescendants()
            .OfType<Button>()
            .Last(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = overlay.LastStepEditorForTesting!;
        editor.GetLogicalDescendants()
            .OfType<ComboBox>()
            .Single(control => control.Name == "RecorderStepEditCalculatedOperation")
            .SelectedItem = RecorderArithmeticOperation.Subtract;
        editor.GetLogicalDescendants()
            .OfType<ComboBox>()
            .Single(control => control.Name == "RecorderStepEditCalculatedRightSource")
            .SelectedItem = RecorderNumericOperandKind.Control;
        editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(control => control.Name == "RecorderStepEditCalculatedRightSelect")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var selected = session.SelectNumericOperandTargetForTesting(adjustment);
        editor = overlay.LastStepEditorForTesting!;
        var selectedControl = editor.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Name == "RecorderStepEditCalculatedRightSelected");
        editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        using (Assert.Multiple())
        {
            await Assert.That(selected).IsTrue();
            await Assert.That(selectedControl.Text).Contains("Adjustment");
            await Assert.That(session.StepJournal[^1].Preview).Contains("quantityBefore - Page.Adjustment.Value");
        }
    }

    [Test]
    public async Task OrdinaryTextNumberIntegerBooleanDateTimeAndStringSetPayloadsCanBeEdited()
    {
        using var session = CreateSession(new StackPanel());
        var steps = new[]
        {
            new RecordedStep(RecordedActionKind.EnterText, Descriptor("Name", UiControlType.TextBox), StringValue: "old", StepId: Guid.NewGuid()),
            new RecordedStep(RecordedActionKind.SetSpinnerValue, Descriptor("Amount", UiControlType.Spinner), DoubleValue: 10, StepId: Guid.NewGuid()),
            new RecordedStep(RecordedActionKind.WaitUntilHasItemsAtLeast, Descriptor("Items", UiControlType.ListBox), IntValue: 2, StepId: Guid.NewGuid()),
            new RecordedStep(RecordedActionKind.WaitUntilGridRowsAtLeast, Descriptor("Grid", UiControlType.Grid), IntValue: 2, StepId: Guid.NewGuid()),
            new RecordedStep(RecordedActionKind.SetChecked, Descriptor("Approved", UiControlType.CheckBox), BoolValue: false, StepId: Guid.NewGuid()),
            new RecordedStep(
                RecordedActionKind.SetDate,
                Descriptor("DueDate", UiControlType.DateTimePicker),
                DateValue: new DateTime(2026, 10, 2),
                Warning: "Existing selector warning.",
                ValidationStatus: RecorderValidationStatus.Warning,
                ValidationMessage: "Existing selector warning.",
                StepId: Guid.NewGuid(),
                ReviewState: RecorderStepReviewState.NeedsReview),
            new RecordedStep(RecordedActionKind.SetTime, Descriptor("DueTime", UiControlType.TimePicker), TimeValue: new TimeSpan(8, 0, 0), StepId: Guid.NewGuid()),
            new RecordedStep(RecordedActionKind.SelectMultiItems, Descriptor("Statuses", UiControlType.MultiSelect), StringValues: ["Old"], StepId: Guid.NewGuid())
        };
        foreach (var step in steps)
        {
            session.AddRecordedStepForTesting(step);
        }

        var integerStepsAreEditable = session.StepJournal
            .Where(entry => entry.StepId == steps[2].StepId || entry.StepId == steps[3].StepId)
            .All(static entry => entry.CanEdit);

        var editing = (IRecorderStepEditingSessionDetails)session;
        await Apply(editing, steps[0].StepId, draft => draft with { StringValue = "new" });
        await Apply(editing, steps[1].StepId, draft => draft with { DoubleValue = 1200.5 });
        await Apply(editing, steps[2].StepId, draft => draft with { IntValue = 3 });
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        var integerEditor = OpenEditor(overlay, steps[3].StepId);
        var integerInput = integerEditor.GetLogicalDescendants().OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditIntegerValue");
        var integerApply = integerEditor.GetLogicalDescendants().OfType<Button>()
            .Single(control => control.Name == "RecorderStepEditApply");
        integerInput.Text = "-1";
        integerInput.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        var invalidIntegerDisabledApply = !integerApply.IsEnabled;
        integerInput.Text = "4";
        integerInput.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        var validIntegerEnabledApply = integerApply.IsEnabled;
        ClickApply(integerEditor);
        await Apply(editing, steps[4].StepId, draft => draft with { BoolValue = true });
        await Apply(editing, steps[5].StepId, draft => draft with
        {
            DateValue = DateTime.Today.AddDays(3),
            DateExpression = new RecorderDateExpression(RecorderDateReferenceKind.RelativeToToday, 3)
        });
        await Apply(editing, steps[6].StepId, draft => draft with { TimeValue = new TimeSpan(9, 45, 0) });
        await Apply(editing, steps[7].StepId, draft => draft with { StringValues = ["Alpha", "Omega"] });
        var preview = string.Join(Environment.NewLine, session.StepJournal.Select(entry => entry.Preview));

        using (Assert.Multiple())
        {
            await Assert.That(preview).Contains("\"new\"");
            await Assert.That(preview).Contains("1200.5");
            await Assert.That(preview).Contains("WaitUntilHasItemsAtLeast(static page => page.Items, 3)");
            await Assert.That(preview).Contains("WaitUntilGridRowsAtLeast(static page => page.Grid, 4)");
            await Assert.That(invalidIntegerDisabledApply).IsTrue();
            await Assert.That(validIntegerEnabledApply).IsTrue();
            await Assert.That(integerStepsAreEditable).IsTrue();
            await Assert.That(preview).Contains("true");
            await Assert.That(preview).Contains("DateTime.Today.AddDays(3)");
            await Assert.That(session.StepJournal[5].ValidationStatus).IsEqualTo(RecorderValidationStatus.Warning);
            await Assert.That(session.StepJournal[5].StatusMessage).IsEqualTo("Existing selector warning.");
            await Assert.That(preview).Contains("351000000000L");
            await Assert.That(preview).Contains("\"Alpha\"");
            await Assert.That(preview).Contains("\"Omega\"");
        }
    }

    [Test]
    public async Task InvalidNumericAndTimePayloadsAreRejectedEvenWhenRuntimeValidationIsDisabled()
    {
        using var session = CreateSession(new StackPanel());
        var numberStepId = Guid.NewGuid();
        var timeStepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.SetSpinnerValue,
            Descriptor("Amount", UiControlType.Spinner),
            DoubleValue: 10,
            StepId: numberStepId));
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.SetTime,
            Descriptor("DueTime", UiControlType.TimePicker),
            TimeValue: new TimeSpan(8, 0, 0),
            StepId: timeStepId));
        var editing = (IRecorderStepEditingSessionDetails)session;
        editing.TryCreateStepEditDraft(numberStepId, out var numberDraft, out _);
        editing.TryCreateStepEditDraft(timeStepId, out var timeDraft, out _);

        var numberResult = editing.ApplyStepEdit(numberDraft! with { DoubleValue = double.NaN });
        var timeResult = editing.ApplyStepEdit(timeDraft! with { TimeValue = TimeSpan.FromHours(25) });

        var repairedStepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.SetSpinnerValue,
            Descriptor("RepairedAmount", UiControlType.Spinner),
            DoubleValue: double.NaN,
            Warning: "Existing selector warning.",
            ValidationStatus: RecorderValidationStatus.Invalid,
            ValidationMessage: "Recorded action 'SetSpinnerValue' requires a finite numeric payload. Headless validation warning: headless-runtime-readiness-warning.",
            CanPersist: false,
            StepId: repairedStepId,
            RuntimeValidationFindings:
            [
                new RecorderRuntimeValidationFinding(
                    RecorderRuntimeValidationTarget.Headless,
                    RecorderRuntimeValidationSeverity.Invalid,
                    "headless-payload-invalid-double",
                    "Recorded action 'SetSpinnerValue' requires a finite numeric payload.",
                    BlocksTarget: true),
                new RecorderRuntimeValidationFinding(
                    RecorderRuntimeValidationTarget.Headless,
                    RecorderRuntimeValidationSeverity.Warning,
                    "headless-runtime-readiness-warning",
                    "The target can be read but requires runtime confirmation.",
                    BlocksTarget: false)
            ]));
        editing.TryCreateStepEditDraft(repairedStepId, out var repairedDraft, out _);
        var repairedResult = editing.ApplyStepEdit(repairedDraft! with { DoubleValue = 42 });
        var repairedJournalEntry = session.StepJournal.Single(entry => entry.StepId == repairedStepId);

        var legacyPayloadStepId = Guid.NewGuid();
        var legacyPayload = new RecordedStep(
            RecordedActionKind.SetTime,
            Descriptor("LegacyTime", UiControlType.TimePicker),
            TimeValue: TimeSpan.FromHours(25),
            ValidationStatus: RecorderValidationStatus.Invalid,
            ValidationMessage: "Recorded action 'SetTime' requires a time-of-day payload.",
            CanPersist: false,
            StepId: legacyPayloadStepId);
        var legacyAutosavePath = Path.Combine(Path.GetTempPath(), $"recorder-legacy-payload-{Guid.NewGuid():N}.cs");
        try
        {
            var state = new RecorderAutosaveState(
                "Scenario", "draft", "Recorded_Scenario", DateTimeOffset.UtcNow, [legacyPayload]);
            File.WriteAllText(legacyAutosavePath, RecorderAutosaveStateSerializer.CreateMarker(state));
            var restored = RecorderAutosaveStateSerializer.TryRead(legacyAutosavePath, out var autosave, out _);
            await Assert.That(restored).IsTrue();
            session.AddRecordedStepForTesting(autosave!.Steps.Single());
        }
        finally
        {
            File.Delete(legacyAutosavePath);
        }
        editing.TryCreateStepEditDraft(legacyPayloadStepId, out var legacyPayloadDraft, out _);
        var legacyPayloadResult = editing.ApplyStepEdit(legacyPayloadDraft! with { TimeValue = TimeSpan.FromHours(8) });
        var legacyPayloadJournalEntry = session.StepJournal.Single(entry => entry.StepId == legacyPayloadStepId);

        var unrelatedFailureId = Guid.NewGuid();
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.SetSpinnerValue,
            Descriptor("MissingAmount", UiControlType.Spinner),
            DoubleValue: double.NaN,
            ValidationStatus: RecorderValidationStatus.Invalid,
            ValidationMessage: "The target selector is unavailable.",
            CanPersist: false,
            StepId: unrelatedFailureId));
        editing.TryCreateStepEditDraft(unrelatedFailureId, out var unrelatedFailureDraft, out _);
        var unrelatedFailureResult = editing.ApplyStepEdit(unrelatedFailureDraft! with { DoubleValue = 42 });

        using (Assert.Multiple())
        {
            await Assert.That(numberResult.Success).IsFalse();
            await Assert.That(numberResult.Message).Contains("finite numeric payload");
            await Assert.That(timeResult.Success).IsFalse();
            await Assert.That(timeResult.Message).Contains("time-of-day payload");
            await Assert.That(session.StepJournal[0].Preview).Contains("10");
            await Assert.That(session.StepJournal[1].Preview).Contains("288000000000L");
            await Assert.That(repairedResult.Success).IsTrue();
            await Assert.That(repairedJournalEntry.ValidationStatus).IsEqualTo(RecorderValidationStatus.Warning);
            await Assert.That(repairedJournalEntry.CanPersist).IsTrue();
            await Assert.That(repairedJournalEntry.StatusMessage).Contains("headless-runtime-readiness-warning");
            await Assert.That(repairedJournalEntry.StatusMessage).Contains("Existing selector warning.");
            await Assert.That(repairedJournalEntry.FailureCode).IsEqualTo("validation-warning");
            await Assert.That(repairedJournalEntry.Preview).Contains("42");
            await Assert.That(legacyPayloadResult.Success).IsTrue();
            await Assert.That(legacyPayloadJournalEntry.CanPersist).IsTrue();
            await Assert.That(session.PersistableStepCount).IsEqualTo(4);
            await Assert.That(unrelatedFailureResult.Success).IsFalse();
            await Assert.That(unrelatedFailureResult.Message).Contains("target selector is unavailable");
        }
    }

    [Test]
    public async Task RelativeDateOverflowShowsValidationAndKeepsEditorOpen()
    {
        using var session = CreateSession(new StackPanel());
        var stepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.SetDate,
            Descriptor("DueDate", UiControlType.DateTimePicker),
            DateValue: new DateTime(2026, 10, 2),
            StepId: stepId));
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        var editor = OpenEditor(overlay, stepId);
        var date = editor.GetLogicalDescendants()
            .OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditDateValue");

        date.Text = "today+2147483647";
        editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var validation = editor.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Name == "RecorderStepEditValidation");

        using (Assert.Multiple())
        {
            await Assert.That(overlay.LastStepEditorForTesting).IsSameReferenceAs(editor);
            await Assert.That(validation.IsVisible).IsTrue();
            await Assert.That(validation.Text).Contains("outside the supported range");
            await Assert.That(session.StepJournal.Single().Preview).Contains("2026, 10, 2");
        }
    }

    [Test]
    public async Task NullableRangeBoundsCanBeAddedClearedAndCannotBothBeEmpty()
    {
        using var session = CreateSession(new StackPanel());
        var numberStepId = Guid.NewGuid();
        var dateStepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.SetNumericRangeFilter,
            Descriptor("AmountRange", UiControlType.NumericRangeFilter),
            SecondDoubleValue: 20,
            StepId: numberStepId));
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.SetDateRangeFilter,
            Descriptor("DateRange", UiControlType.DateRangeFilter),
            SecondDateValue: new DateTime(2026, 10, 10),
            StepId: dateStepId));
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());

        var numberEditor = OpenEditor(overlay, numberStepId);
        var numberFrom = numberEditor.GetLogicalDescendants().OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditNumberValue");
        var numberTo = numberEditor.GetLogicalDescendants().OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditSecondNumberValue");
        numberFrom.Text = "10";
        numberTo.Text = string.Empty;
        ClickApply(numberEditor);

        var dateEditor = OpenEditor(overlay, dateStepId);
        var dateFrom = dateEditor.GetLogicalDescendants().OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditDateValue");
        var dateTo = dateEditor.GetLogicalDescendants().OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditSecondDateValue");
        dateFrom.Text = "today+2";
        dateTo.Text = string.Empty;
        ClickApply(dateEditor);

        numberEditor = OpenEditor(overlay, numberStepId);
        numberEditor.GetLogicalDescendants().OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditNumberValue")
            .Text = string.Empty;
        numberEditor.GetLogicalDescendants().OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditSecondNumberValue")
            .Text = string.Empty;
        ClickApply(numberEditor);
        var validation = numberEditor.GetLogicalDescendants().OfType<TextBlock>()
            .Single(control => control.Name == "RecorderStepEditValidation");

        using (Assert.Multiple())
        {
            await Assert.That(session.StepJournal[0].Preview).Contains("10");
            await Assert.That(session.StepJournal[0].Preview).Contains("null");
            await Assert.That(session.StepJournal[1].Preview).Contains("DateTime.Today.AddDays(2)");
            await Assert.That(session.StepJournal[1].Preview).Contains("null");
            await Assert.That(overlay.LastStepEditorForTesting).IsSameReferenceAs(numberEditor);
            await Assert.That(validation.Text).Contains("at least one numeric bound");
        }
    }

    [Test]
    public async Task AssertionEditorOffersOnlyEarlierSourcesCompatibleWithComparison()
    {
        using var session = CreateSession(new StackPanel());
        var earlierCheckpointId = Guid.NewGuid();
        var earlierGeneratedId = Guid.NewGuid();
        var assertionStepId = Guid.NewGuid();
        var laterCheckpointId = Guid.NewGuid();
        var laterGeneratedId = Guid.NewGuid();
        session.AddRecordedStepForTesting(CheckpointStep(Guid.NewGuid(), earlierCheckpointId, "earlierCheckpoint"));
        session.AddRecordedStepForTesting(GeneratedTextStep(earlierGeneratedId, "earlierGenerated", 1));
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.AssertValue,
            Descriptor("Product", UiControlType.TextBox),
            StringValue: "Expected",
            StepId: assertionStepId,
            ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text,
            ComparisonKind: RecorderComparisonKind.Equal,
            HasExpectedLiteral: true));
        session.AddRecordedStepForTesting(CheckpointStep(Guid.NewGuid(), laterCheckpointId, "laterCheckpoint"));
        session.AddRecordedStepForTesting(GeneratedTextStep(laterGeneratedId, "laterGenerated", 2));
        var editing = (IRecorderStepEditingSessionDetails)session;
        editing.TryCreateStepEditDraft(assertionStepId, out var draft, out _);
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        var editor = OpenEditor(overlay, assertionStepId);
        var comparison = editor.GetLogicalDescendants().OfType<ComboBox>()
            .Single(control => control.Name == "RecorderStepEditComparison");
        var expectedSource = editor.GetLogicalDescendants().OfType<ComboBox>()
            .Single(control => control.Name == "RecorderStepEditExpectedSource");

        comparison.SelectedItem = RecorderComparisonKind.Contains;
        var containsSources = expectedSource.ItemsSource!.Cast<RecorderExpectedValueSourceKind>().ToArray();
        comparison.SelectedItem = RecorderComparisonKind.NotEqual;
        var inequalitySources = expectedSource.ItemsSource!.Cast<RecorderExpectedValueSourceKind>().ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(draft!.CompatibleCheckpoints.Select(option => option.CheckpointId))
                .IsEquivalentTo([earlierCheckpointId]);
            await Assert.That(draft.CompatibleGeneratedValues.Select(option => option.GeneratedValueId))
                .IsEquivalentTo([earlierGeneratedId]);
            await Assert.That(containsSources).IsEquivalentTo([RecorderExpectedValueSourceKind.Literal]);
            await Assert.That(inequalitySources).Contains(RecorderExpectedValueSourceKind.Literal);
            await Assert.That(inequalitySources).Contains(RecorderExpectedValueSourceKind.Checkpoint);
            await Assert.That(inequalitySources).Contains(RecorderExpectedValueSourceKind.GeneratedValue);
            await Assert.That(inequalitySources).DoesNotContain(RecorderExpectedValueSourceKind.Calculated);
        }
    }

    [Test]
    public async Task EscapeClearsPendingStepRetargetSelection()
    {
        var root = new StackPanel();
        var replacement = new TextBox { Text = "Replacement" };
        AutomationProperties.SetAutomationId(replacement, "ReplacementProduct");
        root.Children.Add(replacement);
        using var session = CreateSession(root);
        var stepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(CheckpointStep(stepId, Guid.NewGuid(), "selectedProduct"));
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        session.Start();
        var editor = OpenEditor(overlay, stepId);
        editor.GetLogicalDescendants().OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditChangeSource")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        overlay.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape,
            Source = overlay
        });
        var target = overlay.LastStepEditorForTesting!.GetLogicalDescendants().OfType<TextBlock>()
            .Single(control => control.Name == "RecorderStepEditTarget");

        using (Assert.Multiple())
        {
            await Assert.That(((IRecorderCheckpointSessionDetails)session).IsCheckTargetSelectionActive).IsFalse();
            await Assert.That(overlay.PendingStepRetargetRoleForTesting).IsNull();
            await Assert.That(target.Text).IsEqualTo("Product");
        }
    }

    [Test]
    public async Task FormattedGridNumberEditPreservesUserInputText()
    {
        using var session = CreateSession(new StackPanel());
        var stepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.EditGridCellNumber,
            Descriptor("ItemsGrid", UiControlType.Grid),
            DoubleValue: 1200,
            StepId: stepId,
            RowIndex: 0,
            ColumnIndex: 1)
        {
            NumericInputText = "1\u00A0200",
            NumericInputCultureName = "ru-RU"
        });
        var editing = (IRecorderStepEditingSessionDetails)session;
        editing.TryCreateStepEditDraft(stepId, out var draft, out _);

        var result = editing.ApplyStepEdit(draft! with
        {
            DoubleValue = 10001,
            NumericInputText = "10\u00A0001"
        });

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(session.StepJournal.Single().Preview).Contains("10001");
            await Assert.That(session.StepJournal.Single().Preview).Contains("10\\u00A0001");
        }
    }

    [Test]
    public async Task OverlayOpensInlineCheckpointEditorAndApplyUpdatesJournal()
    {
        using var session = CreateSession(new StackPanel());
        var stepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(CheckpointStep(stepId, Guid.NewGuid(), "oldName"));
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        var edit = overlay.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal));

        edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = overlay.LastStepEditorForTesting
            ?? throw new InvalidOperationException("Step editor was not opened.");
        var name = editor.GetLogicalDescendants()
            .OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditVariableName");
        var apply = editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply");
        name.Text = "renamedCheckpoint";
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        using (Assert.Multiple())
        {
            await Assert.That(session.Checkpoints.Single().VariableName).IsEqualTo("renamedCheckpoint");
            await Assert.That(session.StepJournal.Single().Preview).Contains("renamedCheckpoint");
            await Assert.That(overlay.LastStepEditorForTesting).IsNull();
        }
    }

    [Test]
    public async Task OverlayCancelLeavesCheckpointUnchanged()
    {
        using var session = CreateSession(new StackPanel());
        session.AddRecordedStepForTesting(CheckpointStep(Guid.NewGuid(), Guid.NewGuid(), "originalName"));
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        overlay.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = overlay.LastStepEditorForTesting!;
        editor.GetLogicalDescendants()
            .OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditVariableName")
            .Text = "discardedName";
        editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditCancel")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await Assert.That(session.Checkpoints.Single().VariableName).IsEqualTo("originalName");
    }

    [Test]
    public async Task OverlayEditsLiteralAssertionValueAndComparisonInline()
    {
        using var session = CreateSession(new StackPanel());
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.AssertValue,
            Descriptor("Status", UiControlType.TextBox),
            StringValue: "Old",
            StepId: Guid.NewGuid(),
            ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text,
            ComparisonKind: RecorderComparisonKind.Equal,
            HasExpectedLiteral: true));
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        overlay.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = overlay.LastStepEditorForTesting!;
        var expectedLiteral = editor.GetLogicalDescendants()
            .OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditExpectedLiteral");
        expectedLiteral.Text = "Ready";
        expectedLiteral.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        var previewBeforeSelectionChange = editor.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Name == "RecorderStepEditCodePreview")
            .Text;
        editor.GetLogicalDescendants()
            .OfType<ComboBox>()
            .Single(control => control.Name == "RecorderStepEditComparison")
            .SelectedItem = RecorderComparisonKind.NotEqual;
        var livePreview = editor.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Name == "RecorderStepEditCodePreview")
            .Text;
        editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        using (Assert.Multiple())
        {
            await Assert.That(livePreview).Contains("IsNotEqualTo");
            await Assert.That(livePreview).Contains("\"Ready\"");
            await Assert.That(previewBeforeSelectionChange).Contains("\"Old\"");
            await Assert.That(session.StepJournal.Single().Preview).Contains("IsNotEqualTo");
            await Assert.That(session.StepJournal.Single().Preview).Contains("\"Ready\"");
            await Assert.That(session.StepJournal.Single().Preview).DoesNotContain("\"Old\"");
        }
    }

    [Test]
    public async Task OverlayShowsEditOnlyForEditableStepsAndKeepsOneInlineEditorOpen()
    {
        using var session = CreateSession(new StackPanel());
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.ClickButton,
            Descriptor("SaveButton", UiControlType.Button)));
        session.AddRecordedStepForTesting(CheckpointStep(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "firstProduct"));
        session.AddRecordedStepForTesting(CheckpointStep(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "secondProduct") with
        {
            Control = Descriptor("SecondProduct", UiControlType.TextBox)
        });
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());

        var editButtons = overlay.GetLogicalDescendants()
            .OfType<Button>()
            .Where(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .ToArray();
        editButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editorsAfterFirstOpen = overlay.GetLogicalDescendants().OfType<RecorderStepEditor>().Count();
        editButtons = overlay.GetLogicalDescendants()
            .OfType<Button>()
            .Where(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .ToArray();
        editButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editorsAfterSecondOpen = overlay.GetLogicalDescendants().OfType<RecorderStepEditor>().ToArray();
        var target = editorsAfterSecondOpen.Single().GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Name == "RecorderStepEditTarget");

        using (Assert.Multiple())
        {
            await Assert.That(editButtons.Length).IsEqualTo(2);
            await Assert.That(editorsAfterFirstOpen).IsEqualTo(1);
            await Assert.That(editorsAfterSecondOpen.Length).IsEqualTo(1);
            await Assert.That(target.Text).IsEqualTo("SecondProduct");
        }
    }

    [Test]
    public async Task OverlayKeepsEditorOpenWhenVariableNameIsInvalid()
    {
        using var session = CreateSession(new StackPanel());
        session.AddRecordedStepForTesting(CheckpointStep(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "originalName"));
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        overlay.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = overlay.LastStepEditorForTesting!;
        editor.GetLogicalDescendants()
            .OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditVariableName")
            .Text = "await";
        editor.GetLogicalDescendants()
            .OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditVariableName")
            .RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        var applyButton = editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply");
        var applyDisabledForInvalidName = !applyButton.IsEnabled;
        applyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var validation = editor.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Name == "RecorderStepEditValidation");

        using (Assert.Multiple())
        {
            await Assert.That(overlay.LastStepEditorForTesting).IsSameReferenceAs(editor);
            await Assert.That(applyDisabledForInvalidName).IsTrue();
            await Assert.That(validation.IsVisible).IsTrue();
            await Assert.That(validation.Text).Contains("reserved C# keyword");
            await Assert.That(session.Checkpoints.Single().VariableName).IsEqualTo("originalName");
        }
    }

    [Test]
    public async Task RepairingInvalidPayloadRequestsOnePersistableAutosaveWhileRecording()
    {
        var autosaveCalls = 0;
        var autosaveCompletion = new TaskCompletionSource<RecorderSaveResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        IReadOnlyList<RecordedStep>? autosavedSteps = null;
        using var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            new AppAutomationRecorderOptions
            {
                ShowOverlay = false,
                Validation = new RecorderValidationOptions
                {
                    ValidateSelectors = false,
                    ValidateRuntimeTargets = false,
                    CaptureInvalidSteps = true
                }
            },
            validationRootProvider: static () => new StackPanel(),
            attachWindowHandlers: false,
            autosaveOperation: (steps, _, _) =>
            {
                autosavedSteps = steps.ToArray();
                Interlocked.Increment(ref autosaveCalls);
                return autosaveCompletion.Task;
            });
        var stepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.SetSpinnerValue,
            Descriptor("Amount", UiControlType.Spinner),
            DoubleValue: double.NaN,
            ValidationStatus: RecorderValidationStatus.Invalid,
            ValidationMessage: "Recorded action 'SetSpinnerValue' requires a finite numeric payload.",
            CanPersist: false,
            StepId: stepId,
            RuntimeValidationFindings:
            [
                new RecorderRuntimeValidationFinding(
                    RecorderRuntimeValidationTarget.Headless,
                    RecorderRuntimeValidationSeverity.Invalid,
                    "headless-payload-invalid-double",
                    "Recorded action 'SetSpinnerValue' requires a finite numeric payload.",
                    BlocksTarget: true)
            ]));
        session.Start();
        var editing = (IRecorderStepEditingSessionDetails)session;
        editing.TryCreateStepEditDraft(stepId, out var draft, out _);

        var result = editing.ApplyStepEdit(draft! with { DoubleValue = 42 });
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while ((!session.IsBusy || Volatile.Read(ref autosaveCalls) == 0) && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        var busyResult = editing.ApplyStepEdit(draft with { DoubleValue = 43 });
        autosaveCompletion.SetResult(RecorderSaveResult.Completed(
            "Autosaved.",
            "Page.autosave.cs",
            "Scenario.autosave.cs",
            autosavedSteps?.Count ?? 0,
            0));

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(Volatile.Read(ref autosaveCalls)).IsEqualTo(1);
            await Assert.That(busyResult.Success).IsFalse();
            await Assert.That(busyResult.Message).Contains("Wait for");
            await Assert.That(autosavedSteps).IsNotNull();
            await Assert.That(autosavedSteps!.Single().CanPersist).IsTrue();
            await Assert.That(autosavedSteps.Single().DoubleValue).IsEqualTo(42);
        }
    }

    [Test]
    public async Task OverlayChangeSourceRetargetsCheckpointBeforeApply()
    {
        var root = new StackPanel();
        var replacement = new TextBox { Text = "Replacement product" };
        AutomationProperties.SetAutomationId(replacement, "ReplacementProduct");
        root.Children.Add(replacement);
        using var session = CreateSession(root);
        session.AddRecordedStepForTesting(CheckpointStep(Guid.NewGuid(), Guid.NewGuid(), "selectedProduct"));
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        session.Start();
        overlay.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        overlay.LastStepEditorForTesting!
            .GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditChangeSource")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var selected = session.SelectCheckTargetForTesting(replacement);
        var editor = overlay.LastStepEditorForTesting!;
        var target = editor.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Name == "RecorderStepEditTarget");
        var currentPreview = editor.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Name == "RecorderStepEditCurrentPreview");
        editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        using (Assert.Multiple())
        {
            await Assert.That(selected).IsTrue();
            await Assert.That(target.Text).IsEqualTo("ReplacementProduct");
            await Assert.That(currentPreview.Text).Contains("Replacement product");
            await Assert.That(session.StepJournal.Single().Preview).Contains("ReplacementProduct");
        }
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task RetargetedCheckpointUsesReplacementWarning(bool sourceHasWarning, bool targetHasWarning)
    {
        using var session = CreateSession(new StackPanel());
        var stepId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var source = CheckpointStep(stepId, checkpointId, "selectedProduct") with
        {
            Warning = sourceHasWarning ? "Old target warning." : null,
            ValidationStatus = sourceHasWarning ? RecorderValidationStatus.Warning : RecorderValidationStatus.Valid,
            ValidationMessage = sourceHasWarning ? "Old target warning." : null
        };
        var replacement = CheckpointStep(stepId, checkpointId, "selectedProduct") with
        {
            Control = Descriptor("ReplacementProduct", UiControlType.TextBox),
            Warning = targetHasWarning ? "New target warning." : null,
            ValidationStatus = targetHasWarning ? RecorderValidationStatus.Warning : RecorderValidationStatus.Valid,
            ValidationMessage = targetHasWarning ? "New target warning." : null
        };
        session.AddRecordedStepForTesting(source);
        var editing = (IRecorderStepEditingSessionDetails)session;
        editing.TryCreateStepEditDraft(stepId, out var draft, out _);

        var result = editing.ApplyStepEdit(draft! with { RetargetPrototype = replacement });
        var journalEntry = session.StepJournal.Single();
        var preview = journalEntry.Preview;

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(preview.Contains("Old target warning.", StringComparison.Ordinal)).IsFalse();
            await Assert.That(preview.Contains("New target warning.", StringComparison.Ordinal)).IsEqualTo(targetHasWarning);
            await Assert.That(journalEntry.ValidationStatus).IsEqualTo(
                targetHasWarning ? RecorderValidationStatus.Warning : RecorderValidationStatus.Valid);
        }
    }

    [Test]
    public async Task AutosaveRoundTripPreservesExplicitVariableNameAndStableId()
    {
        var checkpointId = Guid.NewGuid();
        var step = CheckpointStep(Guid.NewGuid(), checkpointId, "SelectedProduct") with
        {
            PreserveVariableName = true
        };
        var state = new RecorderAutosaveState(
            "Scenario",
            "draft",
            "Recorded_Scenario",
            DateTimeOffset.UtcNow,
            [step]);
        var filePath = Path.Combine(Path.GetTempPath(), $"recorder-step-edit-{Guid.NewGuid():N}.cs");
        try
        {
            File.WriteAllText(
                filePath,
                RecorderAutosaveStateSerializer.CreateMarker(state) + Environment.NewLine);

            var read = RecorderAutosaveStateSerializer.TryRead(filePath, out var restored, out var error);
            var restoredStep = restored!.Steps.Single();
            var graph = RecorderScenarioGraphValidator.Validate(restored.Steps);

            using (Assert.Multiple())
            {
                await Assert.That(read).IsTrue();
                await Assert.That(error).IsNull();
                await Assert.That(restoredStep.CheckpointId).IsEqualTo(checkpointId);
                await Assert.That(restoredStep.PreserveVariableName).IsTrue();
                await Assert.That(graph.CheckpointVariables[checkpointId]).IsEqualTo("SelectedProduct");
            }
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Test]
    public async Task EditedCheckpointGraphGeneratesCompilableScenarioSource()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage,
            RecorderScenarioDestinationSources.CompilableScenario);
        var capturedSteps = Array.Empty<RecordedStep>();
        using var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            new AppAutomationRecorderOptions
            {
                ShowOverlay = false,
                Validation = new RecorderValidationOptions
                {
                    ValidateSelectors = false,
                    ValidateRuntimeTargets = false,
                    CaptureInvalidSteps = true
                }
            },
            validationRootProvider: static () => new StackPanel(),
            attachWindowHandlers: false,
            autosaveOperation: (steps, _, _) =>
            {
                capturedSteps = steps.ToArray();
                return Task.FromResult(RecorderSaveResult.Completed(
                    "Autosaved.",
                    "Page.autosave.cs",
                    "Scenario.autosave.cs",
                    steps.Count,
                    0));
            });
        var checkpointId = Guid.NewGuid();
        var checkpointStepId = Guid.NewGuid();
        var assertionStepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(CheckpointStep(
            checkpointStepId,
            checkpointId,
            "ProductCheckpoint"));
        session.AddRecordedStepForTesting(CheckpointAssertionStep(assertionStepId, checkpointId));
        session.Start();
        var editing = (IRecorderStepEditingSessionDetails)session;
        editing.TryCreateStepEditDraft(checkpointStepId, out var checkpointDraft, out _);

        var editResult = editing.ApplyStepEdit(checkpointDraft! with { VariableName = "productBeforeSave" });
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (capturedSteps.Length == 0 && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        var save = await project.SaveAsync(
            project.CreateSaveContext("Edited checkpoint flow", "step-edit-draft"),
            capturedSteps);
        var compileErrors = RecorderGeneratedSourceCompiler.Compile(project.RootPath);
        var scenarioSource = await File.ReadAllTextAsync(save.ScenarioFilePath!);

        using (Assert.Multiple())
        {
            await Assert.That(editResult.Success).IsTrue();
            await Assert.That(save.Success).IsTrue();
            await Assert.That(scenarioSource).Contains("var productBeforeSave");
            await Assert.That(scenarioSource).Contains("IsEqualTo(productBeforeSave)");
            await Assert.That(compileErrors).IsEmpty();
        }
    }

    private static async Task Apply(
        IRecorderStepEditingSessionDetails editing,
        Guid stepId,
        Func<RecorderStepEditDraft, RecorderStepEditDraft> update)
    {
        var created = editing.TryCreateStepEditDraft(stepId, out var draft, out var error);
        await Assert.That(created).IsTrue();
        await Assert.That(error).IsEmpty();
        var result = editing.ApplyStepEdit(update(draft!));
        await Assert.That(result.Success).IsTrue();
    }

    private static RecordedStep CheckpointStep(Guid stepId, Guid checkpointId, string variableName) =>
        new(
            RecordedActionKind.CaptureCheckpoint,
            Descriptor("Product", UiControlType.TextBox),
            StringValue: "Current product",
            StepId: stepId,
            ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text,
            CheckpointId: checkpointId,
            CheckpointVariableName: variableName);

    private static RecordedStep CheckpointAssertionStep(Guid stepId, Guid checkpointId) =>
        new(
            RecordedActionKind.AssertValue,
            Descriptor("Product", UiControlType.TextBox),
            StepId: stepId,
            ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text,
            ComparisonKind: RecorderComparisonKind.Equal,
            ExpectedCheckpointId: checkpointId);

    private static RecordedStep GeneratedTextStep(Guid generatedValueId, string variableName, int ordinal) =>
        new(
            RecordedActionKind.EnterText,
            Descriptor($"GeneratedInput{ordinal}", UiControlType.TextBox),
            StringValue: $"Generated {ordinal}",
            StepId: Guid.NewGuid(),
            GeneratedValueId: generatedValueId,
            GeneratedValueVariableName: variableName,
            GeneratedValueOrdinal: ordinal,
            DefinesGeneratedValue: true);

    private static RecorderStepEditor OpenEditor(RecorderOverlay overlay, Guid stepId)
    {
        overlay.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => button.Tag is Guid candidate
                && candidate == stepId
                && string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return overlay.LastStepEditorForTesting as RecorderStepEditor
            ?? throw new InvalidOperationException("Step editor was not opened.");
    }

    private static void ClickApply(RecorderStepEditor editor) =>
        editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static RecorderSession CreateSession(Control root) =>
        new(
            RecorderTestWindow.CreateStub(),
            new AppAutomationRecorderOptions
            {
                ShowOverlay = false,
                Validation = new RecorderValidationOptions
                {
                    ValidateSelectors = false,
                    ValidateRuntimeTargets = false,
                    CaptureInvalidSteps = true
                }
            },
            validationRootProvider: () => root,
            attachWindowHandlers: false);

    private static RecordedControlDescriptor Descriptor(string propertyName, UiControlType controlType) =>
        new(
            propertyName,
            controlType,
            propertyName,
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: typeof(Control).FullName ?? nameof(Control),
            Warning: null);
}
