using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
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
public sealed class RecorderCheckpointAssertionTests
{
    [Test]
    public async Task Session_RemembersRuntimeValueAndComparesItWithoutRequiredIntermediateActions()
    {
        var root = new StackPanel();
        var customerName = new TextBox { Text = "Customer 42" };
        AutomationProperties.SetAutomationId(customerName, "CustomerName");
        root.Children.Add(customerName);
        using var session = CreateSession(root);
        session.SetLastHoveredControlForTesting(customerName);

        session.CaptureCheckpoint("customerBeforeAction");
        var checkpoint = session.Checkpoints.Single();
        session.CaptureCheckpointAssertion(checkpoint.CheckpointId);

        using (Assert.Multiple())
        {
            await Assert.That(session.StepCount).IsEqualTo(2);
            await Assert.That(session.ExportPreview()).IsEqualTo(
                "var customerBeforeAction = Page.CustomerName.Text;" + Environment.NewLine
                + "await Assert.That(Page.CustomerName.Text).IsEqualTo(customerBeforeAction);");
            await Assert.That(session.StepJournal.All(static entry => entry.CanPersist)).IsTrue();
        }
    }

    [Test]
    public async Task Session_IgnoredCheckpointInvalidatesAndRestoreRepairsDependentAssertion()
    {
        var root = new StackPanel();
        var value = new TextBox { Text = "Value" };
        AutomationProperties.SetAutomationId(value, "ObservedValue");
        root.Children.Add(value);
        using var session = CreateSession(root);
        session.SetLastHoveredControlForTesting(value);
        session.CaptureCheckpoint("valueBeforeAction");
        var checkpoint = session.Checkpoints.Single();
        session.CaptureCheckpointAssertion(checkpoint.CheckpointId);
        var checkpointStepId = session.StepJournal[0].StepId;

        session.SetStepIgnored(checkpointStepId, isIgnored: true);
        var invalidAssertion = session.StepJournal[1];
        session.SetStepIgnored(checkpointStepId, isIgnored: false);
        var restoredAssertion = session.StepJournal[1];

        using (Assert.Multiple())
        {
            await Assert.That(invalidAssertion.CanPersist).IsFalse();
            await Assert.That(invalidAssertion.FailureCode).IsEqualTo("checkpoint-graph-invalid");
            await Assert.That(invalidAssertion.StatusMessage).Contains("missing or later checkpoint");
            await Assert.That(restoredAssertion.CanPersist).IsTrue();
            await Assert.That(restoredAssertion.FailureCode).IsNull();
        }
    }

    [Test]
    public async Task Session_MovingAssertionBeforeCheckpointInvalidatesOnlyTheDependency()
    {
        var root = new StackPanel();
        var value = new TextBox { Text = "Value" };
        AutomationProperties.SetAutomationId(value, "ObservedValue");
        root.Children.Add(value);
        using var session = CreateSession(root);
        session.SetLastHoveredControlForTesting(value);
        session.CaptureCheckpoint("valueBeforeAction");
        session.CaptureCheckpointAssertion(session.Checkpoints.Single().CheckpointId);
        var assertionStepId = session.StepJournal[1].StepId;

        session.MoveStep(assertionStepId, RecorderStepMoveDirection.Earlier);

        using (Assert.Multiple())
        {
            await Assert.That(session.StepJournal[0].CanPersist).IsFalse();
            await Assert.That(session.StepJournal[0].StatusMessage).Contains("missing or later checkpoint");
            await Assert.That(session.StepJournal[1].CanPersist).IsTrue();
            await Assert.That(session.LatestValidationStatus).IsEqualTo(RecorderValidationStatus.Invalid);
            await Assert.That(session.LatestStatus).Contains("missing or later checkpoint");
        }
    }

    [Test]
    public async Task Session_RestoresUnderlyingWarningAfterCheckpointGraphIsRepaired()
    {
        var checkpointId = Guid.NewGuid();
        using var session = CreateSession(new StackPanel());
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.CaptureCheckpoint,
            Descriptor("SourceValue", UiControlType.TextBox),
            ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text,
            CheckpointId: checkpointId,
            CheckpointVariableName: "sourceValue"));
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.AssertValue,
            Descriptor("ObservedValue", UiControlType.TextBox),
            ValidationStatus: RecorderValidationStatus.Warning,
            ValidationMessage: "Name locator requires review.",
            ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text,
            ComparisonKind: RecorderComparisonKind.Equal,
            ExpectedCheckpointId: checkpointId));
        var assertionStepId = session.StepJournal[1].StepId;

        session.MoveStep(assertionStepId, RecorderStepMoveDirection.Earlier);
        session.MoveStep(assertionStepId, RecorderStepMoveDirection.Later);
        var restoredAssertion = session.StepJournal[1];

        using (Assert.Multiple())
        {
            await Assert.That(restoredAssertion.ValidationStatus).IsEqualTo(RecorderValidationStatus.Warning);
            await Assert.That(restoredAssertion.StatusMessage).IsEqualTo("Name locator requires review.");
            await Assert.That(restoredAssertion.FailureCode).IsEqualTo("validation-warning");
            await Assert.That(restoredAssertion.CanPersist).IsTrue();
        }
    }

    [Test]
    public async Task Session_AllowsCompatibleCheckpointComparisonAcrossDifferentControls()
    {
        var root = new StackPanel();
        var sourceValue = new TextBox { Text = "Value" };
        var comparisonValue = new TextBox { Text = "Value" };
        AutomationProperties.SetAutomationId(sourceValue, "SourceValue");
        AutomationProperties.SetAutomationId(comparisonValue, "ComparisonValue");
        root.Children.Add(sourceValue);
        root.Children.Add(comparisonValue);
        using var session = CreateSession(root);
        session.SetLastHoveredControlForTesting(sourceValue);
        session.CaptureCheckpoint("valueBeforeAction");
        session.SetLastHoveredControlForTesting(comparisonValue);
        session.CaptureCheckpointAssertion(session.Checkpoints.Single().CheckpointId);

        await Assert.That(session.ExportPreview()).IsEqualTo(
            "var valueBeforeAction = Page.SourceValue.Text;" + Environment.NewLine
            + "await Assert.That(Page.ComparisonValue.Text).IsEqualTo(valueBeforeAction);");
    }

    [Test]
    public async Task Session_RemovingCheckpointInvalidatesDependentAssertion()
    {
        var root = new StackPanel();
        var value = new TextBox { Text = "Value" };
        AutomationProperties.SetAutomationId(value, "ObservedValue");
        root.Children.Add(value);
        using var session = CreateSession(root);
        session.SetLastHoveredControlForTesting(value);
        session.CaptureCheckpoint("valueBeforeAction");
        session.CaptureCheckpointAssertion(session.Checkpoints.Single().CheckpointId);

        session.RemoveStep(session.StepJournal[0].StepId);

        using (Assert.Multiple())
        {
            await Assert.That(session.StepCount).IsEqualTo(1);
            await Assert.That(session.StepJournal[0].CanPersist).IsFalse();
            await Assert.That(session.StepJournal[0].FailureCode).IsEqualTo("checkpoint-graph-invalid");
            await Assert.That(session.StepJournal[0].StatusMessage).Contains("missing or later checkpoint");
            await Assert.That(session.LatestValidationStatus).IsEqualTo(RecorderValidationStatus.Invalid);
            await Assert.That(session.LatestStatus).Contains("missing or later checkpoint");
        }
    }

    [Test]
    public async Task Factory_SearchPickerUsesLogicalSelectedValueInsteadOfInnerInput()
    {
        var root = new StackPanel();
        var logicalRoot = new Border();
        var input = new TextBox { Text = "search text" };
        var results = new ComboBox
        {
            ItemsSource = new[] { "Item 42" },
            SelectedIndex = 0
        };
        AutomationProperties.SetAutomationId(logicalRoot, "CustomerPicker");
        AutomationProperties.SetAutomationId(input, "CustomerPickerInput");
        AutomationProperties.SetAutomationId(results, "CustomerPickerResults");
        root.Children.Add(logicalRoot);
        root.Children.Add(input);
        root.Children.Add(results);
        var options = new AppAutomationRecorderOptions();
        options.SearchPickerHints.Add(new RecorderSearchPickerHint(
            "CustomerPicker",
            SearchPickerParts.ByAutomationIds("CustomerPickerInput", "CustomerPickerResults")));
        var factory = new RecorderStepFactory(options, () => root);

        var checkpoint = factory.TryCreateCheckpointStep(input, "selectedCustomer");
        var literalAssertion = factory.TryCreateLiteralAssertionStep(results, "Item 42");

        using (Assert.Multiple())
        {
            await Assert.That(checkpoint.Success).IsTrue();
            await Assert.That(checkpoint.Step!.Control.LocatorValue).IsEqualTo("CustomerPicker");
            await Assert.That(checkpoint.Step.Control.ControlType).IsEqualTo(UiControlType.SearchPicker);
            await Assert.That(checkpoint.Step.ValueAccessorKind).IsEqualTo(RecorderValueAccessorKind.SelectedItemText);
            await Assert.That(literalAssertion.Success).IsTrue();
            await Assert.That(literalAssertion.Step!.StringValue).IsEqualTo("Item 42");
        }
    }

    [Test]
    public async Task Factory_HasValueAssertionDoesNotRequireCurrentSearchPickerSelection()
    {
        var root = new StackPanel();
        var logicalRoot = new Border();
        var input = new TextBox();
        var results = new ComboBox { ItemsSource = new[] { "Search result" } };
        AutomationProperties.SetAutomationId(logicalRoot, "CustomerPicker");
        AutomationProperties.SetAutomationId(input, "CustomerPickerInput");
        AutomationProperties.SetAutomationId(results, "CustomerPickerResults");
        root.Children.Add(logicalRoot);
        root.Children.Add(input);
        root.Children.Add(results);
        var options = new AppAutomationRecorderOptions();
        options.SearchPickerHints.Add(new RecorderSearchPickerHint(
            "CustomerPicker",
            SearchPickerParts.ByAutomationIds("CustomerPickerInput", "CustomerPickerResults")));
        var factory = new RecorderStepFactory(options, () => root);

        var captured = factory.TryCaptureSemanticValueSnapshot(input, out var snapshot, out var captureError);
        var assertion = factory.TryCreateHasValueAssertionStep(snapshot);
        var preview = CreateGenerator().GeneratePreview([assertion.Step!]);

        using (Assert.Multiple())
        {
            await Assert.That(captured).IsTrue();
            await Assert.That(captureError).IsEmpty();
            await Assert.That(assertion.Success).IsTrue();
            await Assert.That(assertion.Step!.Control.LocatorValue).IsEqualTo("CustomerPicker");
            await Assert.That(assertion.Step.ValueAccessorKind).IsEqualTo(RecorderValueAccessorKind.SelectedItemText);
            await Assert.That(assertion.Step.StringValue).IsNull();
            await Assert.That(assertion.Step.ComparisonKind).IsEqualTo(RecorderComparisonKind.HasValue);
            await Assert.That(preview)
                .IsEqualTo("await Assert.That(Page.CustomerPicker.SelectedItemText).IsNotEmpty();");
        }
    }

    [Test]
    public async Task Factory_OpenMultiSelectPopupDoesNotCapturePendingSelection()
    {
        var root = new StackPanel();
        var editor = new Border();
        var openButton = new Button();
        var items = new ListBox { IsVisible = true };
        var applyButton = new Button();
        AutomationProperties.SetAutomationId(editor, "StatusFilter");
        AutomationProperties.SetAutomationId(openButton, "StatusFilterOpen");
        AutomationProperties.SetAutomationId(items, "StatusFilterItems");
        AutomationProperties.SetAutomationId(applyButton, "StatusFilterApply");
        root.Children.Add(editor);
        root.Children.Add(openButton);
        root.Children.Add(items);
        root.Children.Add(applyButton);
        var options = new AppAutomationRecorderOptions();
        options.ComboBoxFilterHints.Add(new RecorderComboBoxFilterHint(
            "StatusFilter",
            ComboBoxFilterParts.ByAutomationIds(
                "StatusFilter",
                "StatusFilterOpen",
                "StatusFilterItems",
                "StatusFilterApply")));
        var factory = new RecorderStepFactory(options, () => root);

        var result = factory.TryCreateCheckpointStep(items);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Message).Contains("Apply or cancel");
    }

    [Test]
    public async Task Preview_ReadsCheckpointAtReplayAndUsesItInTUnitAssertion()
    {
        var checkpointId = Guid.NewGuid();
        var generator = CreateGenerator();
        var steps = new[]
        {
            new RecordedStep(
                RecordedActionKind.CaptureCheckpoint,
                Descriptor("CustomerPicker", UiControlType.SearchPicker),
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.SelectedItemText,
                CheckpointId: checkpointId,
                CheckpointVariableName: "customerBeforeSave"),
            new RecordedStep(
                RecordedActionKind.ClickButton,
                Descriptor("SaveButton", UiControlType.Button)),
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("CustomerPicker", UiControlType.SearchPicker),
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.SelectedItemText,
                ComparisonKind: RecorderComparisonKind.Equal,
                ExpectedCheckpointId: checkpointId)
        };

        var preview = generator.GeneratePreview(steps);

        await Assert.That(preview).IsEqualTo(
            "var customerBeforeSave = Page.CustomerPicker.SelectedItemText;" + Environment.NewLine
            + "Page.ClickButton(static page => page.SaveButton);" + Environment.NewLine
            + "await Assert.That(Page.CustomerPicker.SelectedItemText).IsEqualTo(customerBeforeSave);");
    }

    [Test]
    public async Task Preview_MaterializesStringSetCheckpointBeforeComparison()
    {
        var checkpointId = Guid.NewGuid();
        var generator = CreateGenerator();
        var steps = new[]
        {
            new RecordedStep(
                RecordedActionKind.CaptureCheckpoint,
                Descriptor("StatusFilter", UiControlType.ComboBoxFilter),
                ValueKind: RecorderValueKind.StringSet,
                ValueAccessorKind: RecorderValueAccessorKind.SelectedItems,
                CheckpointId: checkpointId,
                CheckpointVariableName: "statusesBeforeSave"),
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("StatusFilter", UiControlType.ComboBoxFilter),
                ValueKind: RecorderValueKind.StringSet,
                ValueAccessorKind: RecorderValueAccessorKind.SelectedItems,
                ComparisonKind: RecorderComparisonKind.Equivalent,
                ExpectedCheckpointId: checkpointId)
        };

        var preview = generator.GeneratePreview(steps);

        await Assert.That(preview).IsEqualTo(
            "var statusesBeforeSave = global::System.Linq.Enumerable.ToArray(Page.StatusFilter.SelectedItems);" + Environment.NewLine
            + "await Assert.That(Page.StatusFilter.SelectedItems).IsEquivalentTo(statusesBeforeSave);");
    }

    [Test]
    public async Task Preview_RendersLiteralAssertionWithoutWaitCommand()
    {
        var step = new RecordedStep(
            RecordedActionKind.AssertValue,
            Descriptor("StatusLabel", UiControlType.Label),
            StringValue: "Saved",
            ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text,
            ComparisonKind: RecorderComparisonKind.Equal,
            HasExpectedLiteral: true);

        var preview = CreateGenerator().GeneratePreview([step]);

        await Assert.That(preview)
            .IsEqualTo("await Assert.That(Page.StatusLabel.Text).IsEqualTo(\"Saved\");");
        await Assert.That(preview).DoesNotContain("WaitUntil");
    }

    [Test]
    public async Task Preview_RendersHasValueAssertionAccordingToSemanticKind()
    {
        var steps = new[]
        {
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("OptionalDate", UiControlType.DateTimePicker),
                ValueKind: RecorderValueKind.Date,
                ValueAccessorKind: RecorderValueAccessorKind.SelectedDate,
                ComparisonKind: RecorderComparisonKind.HasValue),
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("StatusFilter", UiControlType.ComboBoxFilter),
                ValueKind: RecorderValueKind.StringSet,
                ValueAccessorKind: RecorderValueAccessorKind.SelectedItems,
                ComparisonKind: RecorderComparisonKind.HasValue)
        };

        var graphValidation = RecorderScenarioGraphValidator.Validate(steps);
        var preview = CreateGenerator().GeneratePreview(steps);

        using (Assert.Multiple())
        {
            await Assert.That(graphValidation.Success).IsTrue();
            await Assert.That(preview).IsEqualTo(
                "await Assert.That(Page.OptionalDate.SelectedDate).IsNotNull();" + Environment.NewLine
                + "await Assert.That(Page.StatusFilter.SelectedItems).IsNotEmpty();");
        }
    }

    [Test]
    public async Task Preview_RendersEmptyAndNotEqualAssertionsWithTypedTUnitOperators()
    {
        var steps = new[]
        {
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("OptionalText", UiControlType.TextBox),
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.Text,
                ComparisonKind: RecorderComparisonKind.IsEmpty),
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("OptionalDate", UiControlType.DateTimePicker),
                ValueKind: RecorderValueKind.Date,
                ValueAccessorKind: RecorderValueAccessorKind.SelectedDate,
                ComparisonKind: RecorderComparisonKind.IsEmpty),
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("StatusLabel", UiControlType.Label),
                StringValue: "Archived",
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.Text,
                ComparisonKind: RecorderComparisonKind.NotEqual,
                HasExpectedLiteral: true)
        };

        var graphValidation = RecorderScenarioGraphValidator.Validate(steps);
        var preview = CreateGenerator().GeneratePreview(steps);

        using (Assert.Multiple())
        {
            await Assert.That(graphValidation.Success).IsTrue();
            await Assert.That(preview).IsEqualTo(
                "await Assert.That(Page.OptionalText.Text).IsEmpty();" + Environment.NewLine
                + "await Assert.That(Page.OptionalDate.SelectedDate).IsNull();" + Environment.NewLine
                + "await Assert.That(Page.StatusLabel.Text).IsNotEqualTo(\"Archived\");");
        }
    }

    [Test]
    public async Task Graph_ValidatesExpectedSourcesForEmptyAndNotEqualAssertions()
    {
        var invalidEmpty = new RecordedStep(
            RecordedActionKind.AssertValue,
            Descriptor("OptionalText", UiControlType.TextBox),
            StepId: Guid.NewGuid(),
            StringValue: "unexpected",
            ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text,
            ComparisonKind: RecorderComparisonKind.IsEmpty,
            HasExpectedLiteral: true);
        var invalidNotEqual = new RecordedStep(
            RecordedActionKind.AssertValue,
            Descriptor("StatusLabel", UiControlType.Label),
            StepId: Guid.NewGuid(),
            ValueKind: RecorderValueKind.Text,
            ValueAccessorKind: RecorderValueAccessorKind.Text,
            ComparisonKind: RecorderComparisonKind.NotEqual);

        var validation = RecorderScenarioGraphValidator.Validate([invalidEmpty, invalidNotEqual]);

        using (Assert.Multiple())
        {
            await Assert.That(validation.Success).IsFalse();
            await Assert.That(validation.StepErrors[invalidEmpty.StepId]).Contains("cannot define an expected value");
            await Assert.That(validation.StepErrors[invalidNotEqual.StepId]).Contains("exactly one expected value source");
        }
    }

    [Test]
    public async Task AssertionCapabilities_ClassifyEveryUiControlTypeWithoutImplicitValueFallback()
    {
        var valueCapabilities = new Dictionary<UiControlType, (RecorderValueKind ValueKind, RecorderValueAccessorKind AccessorKind)>
        {
            [UiControlType.TextBox] = (RecorderValueKind.Text, RecorderValueAccessorKind.Text),
            [UiControlType.Label] = (RecorderValueKind.Text, RecorderValueAccessorKind.Text),
            [UiControlType.ListBox] = (RecorderValueKind.Text, RecorderValueAccessorKind.SelectedItemText),
            [UiControlType.CheckBox] = (RecorderValueKind.Boolean, RecorderValueAccessorKind.IsChecked),
            [UiControlType.ComboBox] = (RecorderValueKind.Text, RecorderValueAccessorKind.SelectedItemText),
            [UiControlType.RadioButton] = (RecorderValueKind.Boolean, RecorderValueAccessorKind.IsSelected),
            [UiControlType.ToggleButton] = (RecorderValueKind.Boolean, RecorderValueAccessorKind.IsToggled),
            [UiControlType.Slider] = (RecorderValueKind.Number, RecorderValueAccessorKind.NumericValue),
            [UiControlType.ProgressBar] = (RecorderValueKind.Number, RecorderValueAccessorKind.NumericValue),
            [UiControlType.Calendar] = (RecorderValueKind.Date, RecorderValueAccessorKind.SelectedDate),
            [UiControlType.DateTimePicker] = (RecorderValueKind.Date, RecorderValueAccessorKind.SelectedDate),
            [UiControlType.Spinner] = (RecorderValueKind.Number, RecorderValueAccessorKind.NumericValue),
            [UiControlType.TreeItem] = (RecorderValueKind.Boolean, RecorderValueAccessorKind.IsSelected),
            [UiControlType.DataGridView] = (RecorderValueKind.GridCellText, RecorderValueAccessorKind.GridCellText),
            [UiControlType.DataGridViewCell] = (RecorderValueKind.GridCellText, RecorderValueAccessorKind.GridCellText),
            [UiControlType.TabItem] = (RecorderValueKind.Boolean, RecorderValueAccessorKind.IsSelected),
            [UiControlType.Grid] = (RecorderValueKind.GridCellText, RecorderValueAccessorKind.GridCellText),
            [UiControlType.GridCell] = (RecorderValueKind.GridCellText, RecorderValueAccessorKind.GridCellText),
            [UiControlType.SearchPicker] = (RecorderValueKind.Text, RecorderValueAccessorKind.SelectedItemText),
            [UiControlType.Notification] = (RecorderValueKind.Text, RecorderValueAccessorKind.Text),
            [UiControlType.MultiSelect] = (RecorderValueKind.StringSet, RecorderValueAccessorKind.SelectedItems),
            [UiControlType.ComboBoxFilter] = (RecorderValueKind.StringSet, RecorderValueAccessorKind.SelectedItems),
            [UiControlType.Search] = (RecorderValueKind.Text, RecorderValueAccessorKind.Text),
            [UiControlType.TimePicker] = (RecorderValueKind.Time, RecorderValueAccessorKind.SelectedTime),
            [UiControlType.Expander] = (RecorderValueKind.Boolean, RecorderValueAccessorKind.IsExpanded),
            [UiControlType.ColorPicker] = (RecorderValueKind.Color, RecorderValueAccessorKind.Color)
        };
        var contextualTypes = new HashSet<UiControlType>
        {
            UiControlType.DataGridView,
            UiControlType.DataGridViewCell,
            UiControlType.Grid,
            UiControlType.GridCell
        };

        foreach (var controlType in Enum.GetValues<UiControlType>())
        {
            var capability = RecorderAssertionCapabilities.Get(controlType);
            await Assert.That(capability.ControlType).IsEqualTo(controlType);
            await Assert.That(capability.RequiresConcreteTarget).IsEqualTo(contextualTypes.Contains(controlType));
            if (valueCapabilities.TryGetValue(controlType, out var expected))
            {
                await Assert.That(capability.ValueKinds).IsEquivalentTo([expected.ValueKind]);
                await Assert.That(capability.AccessorKinds).IsEquivalentTo([expected.AccessorKind]);
            }
            else
            {
                await Assert.That(capability.ValueKinds).IsEmpty();
                await Assert.That(capability.AccessorKinds).IsEmpty();
            }
        }
    }

    [Test]
    public async Task CheckMode_OffersDateChecksForStandaloneCalendar()
    {
        var root = new StackPanel();
        var calendar = new Calendar();
        AutomationProperties.SetAutomationId(calendar, "ScheduleCalendar");
        root.Children.Add(calendar);
        using var session = CreateSession(root);
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;

        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(calendar);

        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        var checkMenu = overlay.CreateCheckMenuForTesting(selection!);
        var presence = checkMenu.Items
            .OfType<MenuItem>()
            .Single(item => string.Equals(item.Header?.ToString(), "Has Value", StringComparison.Ordinal));
        var literalEditor = overlay.CreateLiteralAssertionEditorForTesting(selection!);
        var comparison = literalEditor.GetLogicalDescendants()
            .OfType<ComboBox>()
            .Single(control => control.Name == "RecorderExpectedComparison");
        var dateMode = literalEditor.GetLogicalDescendants()
            .OfType<ComboBox>()
            .Single(control => control.Name == "RecorderLiteralDateMode");
        ((IRecorderCheckpointSessionDetails)session).CaptureCheckpoint(selection!, "calendarDate");

        using (Assert.Multiple())
        {
            await Assert.That(selection!.ValueDescription?.ValueKind).IsEqualTo(RecorderValueKind.Date);
            await Assert.That(presence.Items.OfType<MenuItem>().Select(item => item.Header?.ToString() ?? string.Empty))
                .IsEquivalentTo(["IsNotNull", "IsNull"]);
            await Assert.That(comparison.Items.Cast<object>().Select(static item => item.ToString() ?? string.Empty))
                .IsEquivalentTo(["Equals", "Not equals"]);
            await Assert.That(dateMode.Items.Cast<object>().Select(static item => item.ToString() ?? string.Empty))
                .IsEquivalentTo(["Exact date", "Today ± days"]);
            await Assert.That(session.ExportPreview()).Contains("Page.ScheduleCalendar.SelectedDate");
        }
    }

    [Test]
    public async Task HasValueSemantics_MapOnlyMeaningfulValueKinds()
    {
        var notEmptyKinds = new[]
        {
            RecorderValueKind.Text,
            RecorderValueKind.Color,
            RecorderValueKind.StringSet,
            RecorderValueKind.GridCellText
        };
        var notNullKinds = new[]
        {
            RecorderValueKind.Date,
            RecorderValueKind.Time
        };

        foreach (var valueKind in notEmptyKinds)
        {
            var supported = RecorderValueAssertions.TryGetHasValueAssertionKind(
                valueKind,
                out var assertionKind);

            await Assert.That(supported).IsTrue();
            await Assert.That(assertionKind).IsEqualTo(RecorderHasValueAssertionKind.NotEmpty);
        }

        foreach (var valueKind in notNullKinds)
        {
            var supported = RecorderValueAssertions.TryGetHasValueAssertionKind(
                valueKind,
                out var assertionKind);

            await Assert.That(supported).IsTrue();
            await Assert.That(assertionKind).IsEqualTo(RecorderHasValueAssertionKind.NotNull);
        }

        await Assert.That(RecorderValueAssertions.TryGetHasValueAssertionKind(
                RecorderValueKind.Number,
                out _))
            .IsFalse();
        await Assert.That(RecorderValueAssertions.TryGetHasValueAssertionKind(
                RecorderValueKind.Boolean,
                out _))
            .IsFalse();
    }

    [Test]
    public async Task Save_MergesAsyncCheckpointScenariosAndRemovesAutosave()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage,
            RecorderScenarioDestinationSources.CompilableScenario);
        var context = project.CreateSaveContext("Checkpoint flow", "checkpoint-draft");
        var checkpointId = Guid.NewGuid();
        var steps = new[]
        {
            new RecordedStep(
                RecordedActionKind.CaptureCheckpoint,
                Descriptor("ObservedValue", UiControlType.TextBox),
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.Text,
                CheckpointId: checkpointId,
                CheckpointVariableName: "valueBeforeAction"),
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("ObservedValue", UiControlType.TextBox),
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.Text,
                ComparisonKind: RecorderComparisonKind.Equal,
                ExpectedCheckpointId: checkpointId)
        };

        var autosave = await project.AutosaveAsync(context, steps);
        var first = await project.SaveAsync(context, steps);
        var second = await project.SaveAsync(
            context with { ScenarioName = "Literal flow", DraftIdentity = "literal-draft" },
            [
                new RecordedStep(
                    RecordedActionKind.AssertValue,
                    Descriptor("ObservedValue", UiControlType.TextBox),
                    StringValue: "Expected value",
                    ValueKind: RecorderValueKind.Text,
                    ValueAccessorKind: RecorderValueAccessorKind.Text,
                    ComparisonKind: RecorderComparisonKind.Contains,
                    HasExpectedLiteral: true),
                new RecordedStep(
                    RecordedActionKind.AssertValue,
                    Descriptor("GeneratedIdentifier", UiControlType.TextBox),
                    ValueKind: RecorderValueKind.Text,
                    ValueAccessorKind: RecorderValueAccessorKind.Text,
                    ComparisonKind: RecorderComparisonKind.HasValue),
                LiteralAssertion(
                    Descriptor("EmptyValue", UiControlType.TextBox),
                    RecorderValueKind.Text,
                    RecorderValueAccessorKind.Text,
                    stringValue: string.Empty),
                LiteralAssertion(
                    Descriptor("AmountValue", UiControlType.Spinner),
                    RecorderValueKind.Number,
                    RecorderValueAccessorKind.NumericValue,
                    doubleValue: 42.5),
                LiteralAssertion(
                    Descriptor("EnabledCheck", UiControlType.CheckBox),
                    RecorderValueKind.Boolean,
                    RecorderValueAccessorKind.IsChecked,
                    boolValue: true),
                LiteralAssertion(
                    Descriptor("SelectedDate", UiControlType.DateTimePicker),
                    RecorderValueKind.Date,
                    RecorderValueAccessorKind.SelectedDate,
                    dateValue: new DateTime(2026, 8, 25)),
                LiteralAssertion(
                    Descriptor("OptionalDate", UiControlType.DateTimePicker),
                    RecorderValueKind.Date,
                    RecorderValueAccessorKind.SelectedDate),
                LiteralAssertion(
                    Descriptor("SelectedTime", UiControlType.TimePicker),
                    RecorderValueKind.Time,
                    RecorderValueAccessorKind.SelectedTime,
                    timeValue: new TimeSpan(9, 30, 0)),
                LiteralAssertion(
                    Descriptor("AccentColor", UiControlType.ColorPicker),
                    RecorderValueKind.Color,
                    RecorderValueAccessorKind.Color,
                    stringValue: "#FF336699"),
                new RecordedStep(
                    RecordedActionKind.CaptureCheckpoint,
                    Descriptor("StatusFilter", UiControlType.ComboBoxFilter),
                    ValueKind: RecorderValueKind.StringSet,
                    ValueAccessorKind: RecorderValueAccessorKind.SelectedItems,
                    CheckpointId: checkpointId,
                    CheckpointVariableName: "statusesBeforeAction"),
                new RecordedStep(
                    RecordedActionKind.AssertValue,
                    Descriptor("StatusFilter", UiControlType.ComboBoxFilter),
                    ValueKind: RecorderValueKind.StringSet,
                    ValueAccessorKind: RecorderValueAccessorKind.SelectedItems,
                    ComparisonKind: RecorderComparisonKind.Equivalent,
                    ExpectedCheckpointId: checkpointId),
                new RecordedStep(
                    RecordedActionKind.AssertValue,
                    Descriptor("OptionalDate", UiControlType.DateTimePicker),
                    ValueKind: RecorderValueKind.Date,
                    ValueAccessorKind: RecorderValueAccessorKind.SelectedDate,
                    ComparisonKind: RecorderComparisonKind.HasValue),
                new RecordedStep(
                    RecordedActionKind.AssertValue,
                    Descriptor("StatusFilter", UiControlType.ComboBoxFilter),
                    ValueKind: RecorderValueKind.StringSet,
                    ValueAccessorKind: RecorderValueAccessorKind.SelectedItems,
                    ComparisonKind: RecorderComparisonKind.HasValue)
            ]);
        var scenarioSource = await File.ReadAllTextAsync(second.ScenarioFilePath!);
        var controlsSource = await File.ReadAllTextAsync(first.PageFilePath!);
        var compileErrors = RecorderGeneratedSourceCompiler.Compile(project.RootPath);

        using (Assert.Multiple())
        {
            await Assert.That(first.Success).IsTrue();
            await Assert.That(second.Success).IsTrue();
            await Assert.That(File.Exists(autosave.ScenarioFilePath!)).IsFalse();
            await Assert.That(File.Exists(autosave.PageFilePath!)).IsFalse();
            await Assert.That(scenarioSource).Contains("using System.Threading.Tasks;");
            await Assert.That(CountOccurrences(scenarioSource, "using System.Threading.Tasks;")).IsEqualTo(1);
            await Assert.That(CountOccurrences(scenarioSource, "using TUnit.Assertions;")).IsEqualTo(1);
            await Assert.That(CountOccurrences(scenarioSource, "using TUnit.Assertions.Extensions;")).IsEqualTo(1);
            await Assert.That(scenarioSource).Contains("public async Task");
            await Assert.That(scenarioSource).DoesNotContain("global::System.Threading.Tasks.Task");
            await Assert.That(scenarioSource).Contains("await Assert.That(");
            await Assert.That(scenarioSource).Contains("Page.GeneratedIdentifier.Text).IsNotEmpty();");
            await Assert.That(scenarioSource).Contains("Page.OptionalDate.SelectedDate).IsNotNull();");
            await Assert.That(scenarioSource).Contains("Page.StatusFilter.SelectedItems).IsNotEmpty();");
            await Assert.That(scenarioSource).Contains(".Contains(");
            await Assert.That(scenarioSource).Contains(".IsEquivalentTo(");
            await Assert.That(scenarioSource).DoesNotContain("global::TUnit.Assertions");
            await Assert.That(scenarioSource).DoesNotContain("WaitUntilTextEquals");
            await Assert.That(CountOccurrences(controlsSource, "ObservedValue")).IsEqualTo(2);
            await Assert.That(compileErrors).IsEmpty();
        }
    }

    [Test]
    public async Task Save_AddsTaskUsingWhenAssertionIsMergedIntoActionOnlyFile()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage,
            RecorderScenarioDestinationSources.CompilableScenario);
        var context = project.CreateSaveContext("Action flow", "action-draft");

        var first = await project.SaveAsync(
            context,
            [new RecordedStep(
                RecordedActionKind.EnterText,
                Descriptor("ObservedValue", UiControlType.TextBox),
                StringValue: "Value")]);
        var second = await project.SaveAsync(
            context with { ScenarioName = "Assertion flow", DraftIdentity = "assertion-draft" },
            [LiteralAssertion(
                Descriptor("ObservedValue", UiControlType.TextBox),
                RecorderValueKind.Text,
                RecorderValueAccessorKind.Text,
                stringValue: "Value")]);
        var scenarioSource = await File.ReadAllTextAsync(second.ScenarioFilePath!);
        var compileErrors = RecorderGeneratedSourceCompiler.Compile(project.RootPath);

        using (Assert.Multiple())
        {
            await Assert.That(first.Success).IsTrue();
            await Assert.That(second.Success).IsTrue();
            await Assert.That(CountOccurrences(scenarioSource, "using System.Threading.Tasks;")).IsEqualTo(1);
            await Assert.That(CountOccurrences(scenarioSource, "using TUnit.Assertions;")).IsEqualTo(1);
            await Assert.That(CountOccurrences(scenarioSource, "using TUnit.Assertions.Extensions;")).IsEqualTo(1);
            await Assert.That(scenarioSource).Contains("public void");
            await Assert.That(scenarioSource).Contains("public async Task");
            await Assert.That(scenarioSource).Contains("await Assert.That(");
            await Assert.That(compileErrors).IsEmpty();
        }
    }

    [Test]
    public async Task Save_RejectsForwardCheckpointReferenceBeforeWritingFiles()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage,
            RecorderScenarioDestinationSources.CompilableScenario);
        var context = project.CreateSaveContext("Invalid checkpoint flow", "invalid-draft");
        var checkpointId = Guid.NewGuid();
        var steps = new[]
        {
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("ObservedValue", UiControlType.TextBox),
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.Text,
                ComparisonKind: RecorderComparisonKind.Equal,
                ExpectedCheckpointId: checkpointId),
            new RecordedStep(
                RecordedActionKind.CaptureCheckpoint,
                Descriptor("ObservedValue", UiControlType.TextBox),
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.Text,
                CheckpointId: checkpointId,
                CheckpointVariableName: "laterValue")
        };

        var result = await project.SaveAsync(context, steps);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Message).Contains("missing or later checkpoint");
            await Assert.That(result.ScenarioFilePath).IsNull();
            await Assert.That(result.PageFilePath).IsNull();
        }
    }

    [Test]
    public async Task GraphValidator_RejectsIncompatibleKindsAndOperators()
    {
        var checkpointId = Guid.NewGuid();
        var incompatibleKinds = RecorderScenarioGraphValidator.Validate(
        [
            new RecordedStep(
                RecordedActionKind.CaptureCheckpoint,
                Descriptor("AmountValue", UiControlType.Spinner),
                ValueKind: RecorderValueKind.Number,
                ValueAccessorKind: RecorderValueAccessorKind.NumericValue,
                CheckpointId: checkpointId,
                StepId: Guid.NewGuid()),
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("ObservedValue", UiControlType.TextBox),
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.Text,
                ComparisonKind: RecorderComparisonKind.Equal,
                ExpectedCheckpointId: checkpointId,
                StepId: Guid.NewGuid())
        ]);
        var invalidOperator = RecorderScenarioGraphValidator.Validate(
        [
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("StatusFilter", UiControlType.ComboBoxFilter),
                StringValues: ["Ready"],
                ValueKind: RecorderValueKind.StringSet,
                ValueAccessorKind: RecorderValueAccessorKind.SelectedItems,
                ComparisonKind: RecorderComparisonKind.Equal,
                HasExpectedLiteral: true,
                StepId: Guid.NewGuid())
        ]);

        using (Assert.Multiple())
        {
            await Assert.That(incompatibleKinds.Success).IsFalse();
            await Assert.That(incompatibleKinds.Error).Contains("incompatible checkpoint kind");
            await Assert.That(invalidOperator.Success).IsFalse();
            await Assert.That(invalidOperator.Error).Contains("cannot use Equal with StringSet");
        }
    }

    [Test]
    public async Task GraphValidator_RequiresExactlyOneExpectedValueSource()
    {
        var checkpointId = Guid.NewGuid();
        var withoutExpectation = RecorderScenarioGraphValidator.Validate(
        [
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("ObservedValue", UiControlType.TextBox),
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.Text,
                ComparisonKind: RecorderComparisonKind.Equal,
                StepId: Guid.NewGuid())
        ]);
        var withConflictingExpectations = RecorderScenarioGraphValidator.Validate(
        [
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("ObservedValue", UiControlType.TextBox),
                StringValue: "Expected value",
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.Text,
                ComparisonKind: RecorderComparisonKind.Equal,
                ExpectedCheckpointId: checkpointId,
                HasExpectedLiteral: true,
                StepId: Guid.NewGuid())
        ]);
        var hasValueWithExpectation = RecorderScenarioGraphValidator.Validate(
        [
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("ObservedValue", UiControlType.TextBox),
                StringValue: "Unexpected literal",
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.Text,
                ComparisonKind: RecorderComparisonKind.HasValue,
                HasExpectedLiteral: true,
                StepId: Guid.NewGuid())
        ]);

        using (Assert.Multiple())
        {
            await Assert.That(withoutExpectation.Success).IsFalse();
            await Assert.That(withoutExpectation.Error).Contains("exactly one expected value source");
            await Assert.That(withConflictingExpectations.Success).IsFalse();
            await Assert.That(withConflictingExpectations.Error).Contains("exactly one expected value source");
            await Assert.That(hasValueWithExpectation.Success).IsFalse();
            await Assert.That(hasValueWithExpectation.Error).Contains("cannot define an expected value");
        }
    }

    [Test]
    public async Task GraphValidator_RejectsHasValueForAlwaysPresentScalar()
    {
        var validation = RecorderScenarioGraphValidator.Validate(
        [
            new RecordedStep(
                RecordedActionKind.AssertValue,
                Descriptor("AmountValue", UiControlType.Spinner),
                ValueKind: RecorderValueKind.Number,
                ValueAccessorKind: RecorderValueAccessorKind.NumericValue,
                ComparisonKind: RecorderComparisonKind.HasValue,
                StepId: Guid.NewGuid())
        ]);

        using (Assert.Multiple())
        {
            await Assert.That(validation.Success).IsFalse();
            await Assert.That(validation.Error).Contains("cannot use");
        }
    }

    [Test]
    public async Task Preview_SanitizesKeywordAndCollisionCheckpointNamesDeterministically()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var contextualKeywordId = Guid.NewGuid();
        var steps = new[]
        {
            new RecordedStep(
                RecordedActionKind.CaptureCheckpoint,
                Descriptor("FirstValue", UiControlType.TextBox),
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.Text,
                CheckpointId: firstId,
                CheckpointVariableName: "class"),
            new RecordedStep(
                RecordedActionKind.CaptureCheckpoint,
                Descriptor("SecondValue", UiControlType.TextBox),
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.Text,
                CheckpointId: secondId,
                CheckpointVariableName: "class"),
            new RecordedStep(
                RecordedActionKind.CaptureCheckpoint,
                Descriptor("ThirdValue", UiControlType.TextBox),
                ValueKind: RecorderValueKind.Text,
                ValueAccessorKind: RecorderValueAccessorKind.Text,
                CheckpointId: contextualKeywordId,
                CheckpointVariableName: "await")
        };

        var preview = CreateGenerator().GeneratePreview(steps);

        await Assert.That(preview).Contains("var checkpointClass = Page.FirstValue.Text;");
        await Assert.That(preview).Contains("var checkpointClass2 = Page.SecondValue.Text;");
        await Assert.That(preview).Contains("var checkpointAwait = Page.ThirdValue.Text;");
    }

    [Test]
    public async Task CheckpointHotkeys_AreAdditiveAndDoNotExpandShortcutLegend()
    {
        var map = RecorderHotkeyMap.Create(new RecorderHotkeys());

        var rememberResolved = map.TryGetCommand(
            Key.M,
            KeyModifiers.Control | KeyModifiers.Shift,
            out var rememberCommand);
        var compareResolved = map.TryGetCommand(
            Key.V,
            KeyModifiers.Control | KeyModifiers.Shift,
            out var compareCommand);
        var legend = map.BuildLegend();

        using (Assert.Multiple())
        {
            await Assert.That(rememberResolved).IsTrue();
            await Assert.That(rememberCommand).IsEqualTo(RecorderCommandKind.CaptureCheckpoint);
            await Assert.That(compareResolved).IsTrue();
            await Assert.That(compareCommand).IsEqualTo(RecorderCommandKind.CaptureCheckpointAssertion);
            await Assert.That(legend).DoesNotContain("Remember Value");
            await Assert.That(legend).DoesNotContain("Compare Checkpoint");
        }
    }

    [Test]
    public async Task CheckMode_UsesTheNextClickedControlInsteadOfTheLastHoveredControl()
    {
        var root = new StackPanel();
        var hoveredValue = new TextBox { Text = "Hovered" };
        var clickedValue = new TextBox { Text = "Clicked" };
        AutomationProperties.SetAutomationId(hoveredValue, "HoveredValue");
        AutomationProperties.SetAutomationId(clickedValue, "ClickedValue");
        root.Children.Add(hoveredValue);
        root.Children.Add(clickedValue);
        using var session = CreateSession(root);
        session.SetLastHoveredControlForTesting(hoveredValue);
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;

        session.BeginCheckTargetSelection();
        var selected = session.SelectCheckTargetForTesting(clickedValue);

        using (Assert.Multiple())
        {
            await Assert.That(selected).IsTrue();
            await Assert.That(session.IsCheckTargetSelectionActive).IsFalse();
            await Assert.That(selection).IsNotNull();
            await Assert.That(ReferenceEquals(selection!.Target, clickedValue)).IsTrue();
            await Assert.That(session.StepCount).IsEqualTo(0);
        }

        ((IRecorderCheckpointSessionDetails)session).CaptureLiteralAssertion(
            selection!,
            "Clicked",
            RecorderComparisonKind.Equal);

        using (Assert.Multiple())
        {
            await Assert.That(session.StepCount).IsEqualTo(1);
            await Assert.That(session.ExportPreview()).Contains("Page.ClickedValue.Text");
            await Assert.That(session.ExportPreview()).DoesNotContain("Page.HoveredValue.Text");
        }
    }

    [Test]
    public async Task CheckMode_CapturesHasValueAssertionForSelectedValue()
    {
        var root = new StackPanel();
        var generatedIdentifier = new TextBox();
        AutomationProperties.SetAutomationId(generatedIdentifier, "GeneratedIdentifier");
        root.Children.Add(generatedIdentifier);
        using var session = CreateSession(root);
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;

        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(generatedIdentifier);
        ((IRecorderCheckpointSessionDetails)session).CapturePresenceAssertion(selection!, expectEmpty: false);

        using (Assert.Multiple())
        {
            await Assert.That(session.StepCount).IsEqualTo(1);
            await Assert.That(session.PersistableStepCount).IsEqualTo(1);
            await Assert.That(session.ExportPreview())
                .IsEqualTo("await Assert.That(Page.GeneratedIdentifier.Text).IsNotEmpty();");
            await Assert.That(session.StepJournal.Single().StatusMessage)
                .IsEqualTo("Assert GeneratedIdentifier.Text has value");
        }
    }

    [Test]
    public async Task CheckMenu_OffersEmptyAndNotEqualChoicesWithoutAddingPersistentControls()
    {
        var root = new StackPanel();
        var observedValue = new TextBox { Text = "Current value" };
        AutomationProperties.SetAutomationId(observedValue, "ObservedValue");
        root.Children.Add(observedValue);
        using var session = CreateSession(root);
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;
        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(observedValue);

        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        var checkMenu = overlay.CreateCheckMenuForTesting(selection!);
        var textPresence = checkMenu.Items
            .OfType<MenuItem>()
            .Single(item => string.Equals(item.Header?.ToString(), "Has Value", StringComparison.Ordinal));
        var literalEditor = overlay.CreateLiteralAssertionEditorForTesting(selection!);
        var comparison = literalEditor.GetLogicalDescendants()
            .OfType<ComboBox>()
            .Single(comboBox => comboBox.Name == "RecorderExpectedComparison");
        textPresence.Items
            .OfType<MenuItem>()
            .Single(item => string.Equals(item.Header?.ToString(), "IsEmpty", StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        var dateRoot = new StackPanel();
        var optionalDate = new DatePicker
        {
            SelectedDate = new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero)
        };
        AutomationProperties.SetAutomationId(optionalDate, "OptionalDate");
        dateRoot.Children.Add(optionalDate);
        using var dateSession = CreateSession(dateRoot);
        dateSession.Start();
        RecorderCheckTargetSelection? dateSelection = null;
        dateSession.CheckTargetSelected += (_, eventArgs) => dateSelection = eventArgs.Selection;
        dateSession.BeginCheckTargetSelection();
        dateSession.SelectCheckTargetForTesting(optionalDate);
        var dateOverlay = new RecorderOverlay();
        dateOverlay.Attach(dateSession, new AppAutomationRecorderOptions());
        var dateMenu = dateOverlay.CreateCheckMenuForTesting(dateSelection!);
        var datePresence = dateMenu.Items
            .OfType<MenuItem>()
            .Single(item => string.Equals(item.Header?.ToString(), "Has Value", StringComparison.Ordinal));

        using (Assert.Multiple())
        {
            await Assert.That(textPresence.Items.OfType<MenuItem>().Select(item => item.Header?.ToString() ?? string.Empty))
                .IsEquivalentTo(["IsNotEmpty", "IsEmpty"]);
            await Assert.That(datePresence.Items.OfType<MenuItem>().Select(item => item.Header?.ToString() ?? string.Empty))
                .IsEquivalentTo(["IsNotNull", "IsNull"]);
            await Assert.That(comparison.Items.Cast<object>().Select(static item => item.ToString()))
                .Contains("Not equals");
            await Assert.That(session.ExportPreview())
                .IsEqualTo("await Assert.That(Page.ObservedValue.Text).IsEmpty();");
        }
    }

    [Test]
    public async Task CheckMode_PrefersDisabledValueUnderPointerOverRoutedContainer()
    {
        var root = new StackPanel();
        var container = new DockPanel();
        var generatedIdentifier = new TextBox
        {
            Text = "Generated",
            IsEnabled = false
        };
        AutomationProperties.SetAutomationId(container, "DetailsPanel");
        AutomationProperties.SetAutomationId(generatedIdentifier, "GeneratedIdentifier");
        container.Children.Add(generatedIdentifier);
        root.Children.Add(container);
        using var session = CreateSession(root);
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;

        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(container, [container, generatedIdentifier]);

        using (Assert.Multiple())
        {
            await Assert.That(ReferenceEquals(selection!.Target, generatedIdentifier)).IsTrue();
            await Assert.That(selection.ValueDescription?.ValueKind).IsEqualTo(RecorderValueKind.Text);
            await Assert.That(selection.IsEnabled).IsFalse();
        }

        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        var checkMenu = overlay.CreateCheckMenuForTesting(selection);
        var enableItem = checkMenu.Items
            .OfType<MenuItem>()
            .Single(item => string.Equals(item.Header?.ToString(), "Enable…", StringComparison.Ordinal));
        var enabledEditor = overlay.CreateEnabledAssertionEditorForTesting(selection);
        var expectedEnabled = enabledEditor.GetLogicalDescendants()
            .OfType<ComboBox>()
            .Single(comboBox => comboBox.Name == "RecorderExpectedEnabled");
        var add = enabledEditor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Add", StringComparison.Ordinal));
        await Assert.That(expectedEnabled.SelectedIndex).IsEqualTo(1);
        expectedEnabled.SelectedIndex = 0;
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        using (Assert.Multiple())
        {
            await Assert.That(enableItem).IsNotNull();
            await Assert.That(checkMenu.Items.OfType<MenuItem>().Any(item =>
                    string.Equals(item.Header?.ToString(), "Assert enabled", StringComparison.Ordinal)
                    || string.Equals(item.Header?.ToString(), "Assert disabled", StringComparison.Ordinal)))
                .IsFalse();
            await Assert.That(expectedEnabled.Items.Count).IsEqualTo(2);
            await Assert.That(session.ExportPreview())
                .IsEqualTo("await Assert.That(Page.GeneratedIdentifier.IsEnabled).IsEqualTo(true);");
        }
    }

    [Test]
    public async Task CandidateGraph_RejectsDifferentLogicalTargetsInsteadOfPreferringConfiguredCandidate()
    {
        var root = new StackPanel();
        var nativeValue = new TextBox { Text = "Native" };
        var configuredPresenter = new Border();
        var configuredValue = new TextBox { Text = "Configured" };
        AutomationProperties.SetAutomationId(nativeValue, "NativeValue");
        AutomationProperties.SetAutomationId(configuredPresenter, "ConfiguredPresenter");
        AutomationProperties.SetAutomationId(configuredValue, "ConfiguredValue");
        root.Children.Add(nativeValue);
        root.Children.Add(configuredPresenter);
        root.Children.Add(configuredValue);
        var options = CreateSessionOptions();
        options.SemanticValueResolvers.Add(new TestSemanticValueResolver(source =>
            ReferenceEquals(source, configuredPresenter)
                ? RecorderSemanticValueResolution.Resolved(new RecorderSemanticValueTarget(
                    "ConfiguredValue",
                    UiControlType.TextBox,
                    RecorderValueKind.Text,
                    RecorderValueAccessorKind.Text)
                {
                    StringValue = "Configured"
                })
                : RecorderSemanticValueResolution.NotHandled));
        var factory = new RecorderStepFactory(options, () => root);

        var captured = factory.TryCaptureConfiguredSemanticValueSnapshot(
            [nativeValue, configuredPresenter],
            out var resolvedSource,
            out var snapshot,
            out var error);

        using (Assert.Multiple())
        {
            await Assert.That(captured).IsFalse();
            await Assert.That(resolvedSource).IsNull();
            await Assert.That(snapshot).IsNull();
            await Assert.That(error).Contains("multiple logical targets");
            await Assert.That(error).Contains("NativeValue");
            await Assert.That(error).Contains("ConfiguredValue");
        }
    }

    [Test]
    public async Task CandidateGraph_ResolvesEachCandidateOnceAndPreservesConfiguredFailure()
    {
        var root = new StackPanel();
        var ordinaryValue = new TextBox { Text = "Native" };
        var brokenValue = new TextBox { Text = "Fallback must not be used" };
        AutomationProperties.SetAutomationId(ordinaryValue, "OrdinaryValue");
        AutomationProperties.SetAutomationId(brokenValue, "BrokenValue");
        root.Children.Add(ordinaryValue);
        root.Children.Add(brokenValue);
        var resolutionCount = 0;
        var options = CreateSessionOptions();
        options.SemanticValueResolvers.Add(new TestSemanticValueResolver(source =>
        {
            resolutionCount++;
            return ReferenceEquals(source, brokenValue)
                ? RecorderSemanticValueResolution.Failed("Configured value presenter is incomplete.")
                : RecorderSemanticValueResolution.NotHandled;
        }));
        var factory = new RecorderStepFactory(options, () => root);

        var ordinaryCaptured = factory.TryCaptureConfiguredSemanticValueSnapshot(
            [ordinaryValue],
            out _,
            out var ordinarySnapshot,
            out var ordinaryError);
        var brokenCaptured = factory.TryCaptureConfiguredSemanticValueSnapshot(
            [brokenValue],
            out _,
            out var brokenSnapshot,
            out var brokenError);

        using (Assert.Multiple())
        {
            await Assert.That(ordinaryCaptured).IsTrue();
            await Assert.That(ordinarySnapshot!.Prototype.Control.LocatorValue).IsEqualTo("OrdinaryValue");
            await Assert.That(ordinaryError).IsEmpty();
            await Assert.That(brokenCaptured).IsFalse();
            await Assert.That(brokenSnapshot).IsNull();
            await Assert.That(brokenError).IsEqualTo("Configured value presenter is incomplete.");
            await Assert.That(resolutionCount).IsEqualTo(2);
        }
    }

    [Test]
    public async Task CheckMode_MapsEditorAndDisplayPresentersToOneStableGridValue()
    {
        var root = new StackPanel();
        var logicalGrid = new Border();
        var editor = new TextBox { Text = "Search result" };
        var display = new Button { Content = "Search result" };
        var eventSource = new DockPanel();
        AutomationProperties.SetAutomationId(logicalGrid, "ItemsGrid");
        AutomationProperties.SetAutomationId(editor, "ProductEditor");
        AutomationProperties.SetAutomationId(display, "ProductDisplay");
        AutomationProperties.SetAutomationId(eventSource, "MainSurface");
        root.Children.Add(logicalGrid);
        root.Children.Add(editor);
        root.Children.Add(eventSource);

        var options = CreateSessionOptions();
        options.SemanticValueResolvers.Add(new ProductPresenterValueResolver());
        using var session = CreateSession(root, options);
        session.Start();
        var details = (IRecorderCheckpointSessionDetails)session;
        RecorderCheckTargetSelection? editorSelection = null;
        session.CheckTargetSelected += (_, eventArgs) => editorSelection = eventArgs.Selection;

        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(editor);
        root.Children.Remove(editor);
        root.Children.Add(display);
        details.CaptureCheckpoint(editorSelection!, "productBeforeSave");

        var checkpoint = details.Checkpoints.FirstOrDefault();
        RecorderCheckTargetSelection? displaySelection = null;
        session.CheckTargetSelected += (_, eventArgs) => displaySelection = eventArgs.Selection;
        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(eventSource, [eventSource, display]);
        if (checkpoint is not null)
        {
            details.CaptureCheckpointAssertion(displaySelection!, checkpoint.CheckpointId);
        }

        var preview = session.ExportPreview();
        using (Assert.Multiple())
        {
            await Assert.That(editorSelection!.ValueDescription?.ValueKind).IsEqualTo(RecorderValueKind.GridCellText);
            await Assert.That(displaySelection!.ValueDescription?.ValueKind).IsEqualTo(RecorderValueKind.GridCellText);
            await Assert.That(ReferenceEquals(displaySelection.Target, display)).IsTrue();
            await Assert.That(displaySelection.ValueDescription!.SuggestedCheckpointName).DoesNotContain("MainSurface");
            await Assert.That(session.StepCount).IsEqualTo(2);
            await Assert.That(session.PersistableStepCount).IsEqualTo(2);
            await Assert.That(preview).Contains("GridRowSelector.ByCell(\"Key\", \"ITEM-42\")");
            await Assert.That(preview).Contains("\"Product\"");
            await Assert.That(preview).DoesNotContain("ProductEditor");
            await Assert.That(preview).DoesNotContain("ProductDisplay");
            await Assert.That(preview).DoesNotContain("MainSurface");
        }
    }

    [Test]
    public async Task CheckMode_AllowsContainerTargetsAndCanBeCancelled()
    {
        var container = new DockPanel();
        AutomationProperties.SetAutomationId(container, "LayoutRoot");
        using var session = CreateSession(container);
        session.Start();
        RecorderCheckTargetSelection? selection = null;
        session.CheckTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;

        session.BeginCheckTargetSelection();
        var selected = session.SelectCheckTargetForTesting(container);
        session.BeginCheckTargetSelection();
        session.CancelCheckTargetSelection();
        var selectedAfterCancel = session.SelectCheckTargetForTesting(container);

        using (Assert.Multiple())
        {
            await Assert.That(selected).IsTrue();
            await Assert.That(ReferenceEquals(selection!.Target, container)).IsTrue();
            await Assert.That(selectedAfterCancel).IsFalse();
            await Assert.That(session.IsCheckTargetSelectionActive).IsFalse();
            await Assert.That(session.StepCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Overlay_CheckButtonStartsOneShotTargetSelectionMode()
    {
        var root = new StackPanel();
        var value = new TextBox { Text = "Value" };
        AutomationProperties.SetAutomationId(value, "ObservedValue");
        root.Children.Add(value);
        using var session = CreateSession(root);
        session.Start();
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        overlay.RefreshForTesting();

        var checkButton = overlay.FindControl<Button>("CheckButton");
        var buttons = overlay.GetLogicalDescendants()
            .OfType<Button>()
            .Where(button => string.Equals(button.Content?.ToString(), "Check", StringComparison.Ordinal))
            .ToArray();
        checkButton!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        using (Assert.Multiple())
        {
            await Assert.That(checkButton.IsEnabled).IsTrue();
            await Assert.That(buttons.Length).IsEqualTo(1);
            await Assert.That(ToolTip.GetTip(checkButton)?.ToString()).Contains("Ctrl+Shift+M");
            await Assert.That(ToolTip.GetTip(checkButton)?.ToString()).Contains("Ctrl+Shift+V");
            await Assert.That(session.IsCheckTargetSelectionActive).IsTrue();
            await Assert.That(session.StepCount).IsEqualTo(0);
        }
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static RecordedStep LiteralAssertion(
        RecordedControlDescriptor descriptor,
        RecorderValueKind valueKind,
        RecorderValueAccessorKind accessorKind,
        string? stringValue = null,
        bool? boolValue = null,
        double? doubleValue = null,
        DateTime? dateValue = null,
        TimeSpan? timeValue = null) =>
        new(
            RecordedActionKind.AssertValue,
            descriptor,
            StringValue: stringValue,
            BoolValue: boolValue,
            DoubleValue: doubleValue,
            DateValue: dateValue,
            TimeValue: timeValue,
            ValueKind: valueKind,
            ValueAccessorKind: accessorKind,
            ComparisonKind: RecorderComparisonKind.Equal,
            HasExpectedLiteral: true);

    private static AuthoringCodeGenerator CreateGenerator() =>
        new(new AuthoringProjectScanner(), logger: null);

    private static RecorderSession CreateSession(Control root)
    {
        return CreateSession(root, CreateSessionOptions());
    }

    private static RecorderSession CreateSession(Control root, AppAutomationRecorderOptions options)
    {
        return new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options,
            validationRootProvider: () => root,
            attachWindowHandlers: false);
    }

    private static AppAutomationRecorderOptions CreateSessionOptions()
    {
        return new AppAutomationRecorderOptions
        {
            Validation = new RecorderValidationOptions
            {
                ValidateSelectors = true,
                ValidateRuntimeTargets = true,
                CaptureInvalidSteps = true
            }
        };
    }

    private sealed class ProductPresenterValueResolver : IRecorderSemanticValueResolver
    {
        public RecorderSemanticValueResolution Resolve(Control source)
        {
            var automationId = AutomationProperties.GetAutomationId(source);
            if (automationId is not ("ProductEditor" or "ProductDisplay"))
            {
                return RecorderSemanticValueResolution.NotHandled;
            }

            return RecorderSemanticValueResolution.Resolved(new RecorderSemanticValueTarget(
                "ItemsGrid",
                UiControlType.Grid,
                RecorderValueKind.GridCellText,
                RecorderValueAccessorKind.GridCellText)
            {
                StringValue = "Search result",
                GridContext = new RecorderSemanticGridValueTarget(
                    [new RecorderSemanticGridRowCondition("Key", "ITEM-42")],
                    "Product")
            });
        }
    }

    private sealed class TestSemanticValueResolver(Func<Control, RecorderSemanticValueResolution> resolve)
        : IRecorderSemanticValueResolver
    {
        public RecorderSemanticValueResolution Resolve(Control source) => resolve(source);
    }

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
