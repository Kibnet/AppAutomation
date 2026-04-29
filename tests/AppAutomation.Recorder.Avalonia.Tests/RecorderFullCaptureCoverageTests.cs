using System.Runtime.Serialization;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderFullCaptureCoverageTests
{
    [Test]
    public async Task CoverageMatrix_AllRecordedActionsRenderDslAndRuntimeValidate()
    {
        var steps = CreateActionCoverageSteps();
        var missingActions = Enum.GetValues<RecordedActionKind>()
            .Where(action => steps.All(step => step.ActionKind != action))
            .ToArray();
        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var validator = new RecorderCommandRuntimeValidator(new AppAutomationRecorderOptions());

        await Assert.That(missingActions.Length).IsEqualTo(0);

        foreach (var step in steps)
        {
            var preview = generator.GeneratePreview(step);
            var validated = validator.Validate(step);
            var findings = validated.RuntimeValidationFindings ?? Array.Empty<RecorderRuntimeValidationFinding>();

            using (Assert.Multiple())
            {
                await Assert.That(preview.Contains($"Page.{step.ActionKind}(", StringComparison.Ordinal)).IsEqualTo(true);
                await Assert.That(preview.Contains("Unsupported recorded action", StringComparison.Ordinal)).IsEqualTo(false);
                await Assert.That(validated.CanPersist).IsEqualTo(true);
                await Assert.That(validated.ValidationStatus == RecorderValidationStatus.Invalid).IsEqualTo(false);
                await Assert.That(findings.Any(static finding => finding.Severity == RecorderRuntimeValidationSeverity.Invalid)).IsEqualTo(false);
                await Assert.That(findings.Any(static finding => finding.Code.Contains("action-unsupported", StringComparison.Ordinal))).IsEqualTo(false);
                await Assert.That(findings.Any(static finding => finding.Code.Contains("payload-missing", StringComparison.Ordinal))).IsEqualTo(false);
                await Assert.That(findings.Any(static finding => finding.Code.Contains("control-type-mismatch", StringComparison.Ordinal))).IsEqualTo(false);
            }
        }
    }

    [Test]
    public async Task CodeGenerator_RendersCompositeActionsWithOptionalCommitModes()
    {
        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var preview = generator.GeneratePreview(
        [
            new RecordedStep(
                RecordedActionKind.SetDateRangeFilter,
                Descriptor("DateFilter", UiControlType.DateRangeFilter),
                DateValue: new DateTime(2026, 4, 1),
                FilterCommitMode: FilterPopupCommitMode.Cancel),
            new RecordedStep(
                RecordedActionKind.SetNumericRangeFilter,
                Descriptor("NumericFilter", UiControlType.NumericRangeFilter),
                DoubleValue: 10.5,
                SecondDoubleValue: 99.25),
            new RecordedStep(
                RecordedActionKind.SelectExportFolder,
                Descriptor("ExportPicker", UiControlType.FolderExport),
                StringValue: @"C:\Exports\Arm",
                FolderExportCommitMode: FolderExportCommitMode.Cancel),
            new RecordedStep(
                RecordedActionKind.EditGridCellText,
                Descriptor("EditableGrid", UiControlType.Grid),
                StringValue: "Edited",
                RowIndex: 0,
                ColumnIndex: 1,
                GridCellEditCommitMode: GridCellEditCommitMode.Cancel),
            new RecordedStep(
                RecordedActionKind.EditGridCellNumber,
                Descriptor("EditableGrid", UiControlType.Grid),
                DoubleValue: 42.5,
                RowIndex: 0,
                ColumnIndex: 2),
            new RecordedStep(
                RecordedActionKind.EditGridCellDate,
                Descriptor("EditableGrid", UiControlType.Grid),
                DateValue: new DateTime(2026, 4, 28),
                RowIndex: 0,
                ColumnIndex: 3),
            new RecordedStep(
                RecordedActionKind.SelectGridCellComboItem,
                Descriptor("EditableGrid", UiControlType.Grid),
                StringValue: "Approved",
                RowIndex: 0,
                ColumnIndex: 4)
        ]);

        using (Assert.Multiple())
        {
            await Assert.That(preview.Contains(
                "Page.SetDateRangeFilter(static page => page.DateFilter, new global::System.DateTime(2026, 4, 1), null, FilterPopupCommitMode.Cancel);",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(preview.Contains(
                "Page.SetNumericRangeFilter(static page => page.NumericFilter, 10.5, 99.25);",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(preview.Contains(
                "Page.SelectExportFolder(static page => page.ExportPicker, \"C:\\\\Exports\\\\Arm\", FolderExportCommitMode.Cancel);",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(preview.Contains(
                "Page.EditGridCellText(static page => page.EditableGrid, 0, 1, \"Edited\", GridCellEditCommitMode.Cancel);",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(preview.Contains(
                "Page.EditGridCellNumber(static page => page.EditableGrid, 0, 2, 42.5);",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(preview.Contains(
                "Page.EditGridCellDate(static page => page.EditableGrid, 0, 3, new global::System.DateTime(2026, 4, 28));",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(preview.Contains(
                "Page.SelectGridCellComboItem(static page => page.EditableGrid, 0, 4, \"Approved\");",
                StringComparison.Ordinal)).IsEqualTo(true);
        }
    }

    [Test]
    public async Task TryCreateAssertionStep_CapturesProgressListAndNotificationAssertions()
    {
        var options = new AppAutomationRecorderOptions();
        options.NotificationHints.Add(new RecorderNotificationHint(
            "ExportToast",
            NotificationControlParts.ByAutomationIds("ExportToastText", dismissButtonAutomationId: "ExportToastDismissButton")));
        var root = new StackPanel();
        var progress = new ProgressBar { Value = 76.5 };
        var selectedList = new ListBox
        {
            ItemsSource = new[] { "Pending", "Ready" },
            SelectedItem = "Ready"
        };
        var countedList = new ListBox
        {
            ItemsSource = new[] { "One", "Two", "Three" }
        };
        var notificationText = new Label { Content = "Export completed" };
        AutomationProperties.SetAutomationId(progress, "ReloadProgress");
        AutomationProperties.SetAutomationId(selectedList, "StatusList");
        AutomationProperties.SetAutomationId(countedList, "HistoryList");
        AutomationProperties.SetAutomationId(notificationText, "ExportToastText");
        root.Children.Add(progress);
        root.Children.Add(selectedList);
        root.Children.Add(countedList);
        root.Children.Add(notificationText);
        var factory = new RecorderStepFactory(options, () => root);

        var progressResult = factory.TryCreateAssertionStep(progress, RecorderAssertionMode.Auto);
        var listContainsResult = factory.TryCreateAssertionStep(selectedList, RecorderAssertionMode.Auto);
        var listCountResult = factory.TryCreateAssertionStep(countedList, RecorderAssertionMode.Auto);
        var notificationResult = factory.TryCreateAssertionStep(notificationText, RecorderAssertionMode.Text);

        using (Assert.Multiple())
        {
            await Assert.That(progressResult.Success).IsEqualTo(true);
            await Assert.That(progressResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilProgressAtLeast);
            await Assert.That(progressResult.Step.DoubleValue).IsEqualTo(76.5);
            await Assert.That(progressResult.Step.Control.ControlType).IsEqualTo(UiControlType.ProgressBar);

            await Assert.That(listContainsResult.Success).IsEqualTo(true);
            await Assert.That(listContainsResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilListBoxContains);
            await Assert.That(listContainsResult.Step.StringValue).IsEqualTo("Ready");

            await Assert.That(listCountResult.Success).IsEqualTo(true);
            await Assert.That(listCountResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilHasItemsAtLeast);
            await Assert.That(listCountResult.Step.IntValue).IsEqualTo(3);

            await Assert.That(notificationResult.Success).IsEqualTo(true);
            await Assert.That(notificationResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilNotificationContains);
            await Assert.That(notificationResult.Step.StringValue).IsEqualTo("Export completed");
            await Assert.That(notificationResult.Step.Control.ControlType).IsEqualTo(UiControlType.Notification);
            await Assert.That(notificationResult.Step.Control.LocatorValue).IsEqualTo("ExportToast");
        }
    }

    [Test]
    public async Task TryCreateCompositeWorkflowSteps_WithConfiguredParts_CapturesHighLevelActions()
    {
        var options = CreateCompositeCoverageOptions();
        var root = CreateCompositeCoverageRoot(
            out var dateApplyButton,
            out var numericApplyButton,
            out var folderSelectButton,
            out var textEditButton,
            out var numberEditButton,
            out var dateEditButton,
            out var comboEditButton,
            out var dateFromPicker,
            out var numericFromTextBox,
            out var folderTextBox,
            out var textEditValue,
            out var dateEditValue,
            out var comboEditValue);
        var factory = new RecorderStepFactory(options, () => root);

        var dateResult = factory.TryCreateDateRangeFilterStep(dateApplyButton);
        var numericResult = factory.TryCreateNumericRangeFilterStep(numericApplyButton);
        var folderResult = factory.TryCreateFolderExportStep(folderSelectButton);
        var textEditResult = factory.TryCreateGridEditStep(textEditButton);
        var numberEditResult = factory.TryCreateGridEditStep(numberEditButton);
        var dateEditResult = factory.TryCreateGridEditStep(dateEditButton);
        var comboEditResult = factory.TryCreateGridEditStep(comboEditButton);

        using (Assert.Multiple())
        {
            await Assert.That(dateResult.Success).IsEqualTo(true);
            await Assert.That(dateResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.SetDateRangeFilter);
            await Assert.That(dateResult.Step.DateValue).IsEqualTo(new DateTime(2026, 4, 1));
            await Assert.That(dateResult.Step.SecondDateValue).IsEqualTo(new DateTime(2026, 4, 30));
            await Assert.That(dateResult.Step.Control.ControlType).IsEqualTo(UiControlType.DateRangeFilter);

            await Assert.That(numericResult.Success).IsEqualTo(true);
            await Assert.That(numericResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.SetNumericRangeFilter);
            await Assert.That(numericResult.Step.DoubleValue).IsEqualTo(5.5);
            await Assert.That(numericResult.Step.SecondDoubleValue).IsEqualTo(42.25);
            await Assert.That(numericResult.Step.Control.ControlType).IsEqualTo(UiControlType.NumericRangeFilter);

            await Assert.That(folderResult.Success).IsEqualTo(true);
            await Assert.That(folderResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.SelectExportFolder);
            await Assert.That(folderResult.Step.StringValue).IsEqualTo(@"C:\Exports\Arm");
            await Assert.That(folderResult.Step.Control.ControlType).IsEqualTo(UiControlType.FolderExport);

            await Assert.That(textEditResult.Success).IsEqualTo(true);
            await Assert.That(textEditResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.EditGridCellText);
            await Assert.That(textEditResult.Step.StringValue).IsEqualTo("Edited");
            await Assert.That(textEditResult.Step.RowIndex).IsEqualTo(0);
            await Assert.That(textEditResult.Step.ColumnIndex).IsEqualTo(1);

            await Assert.That(numberEditResult.Success).IsEqualTo(true);
            await Assert.That(numberEditResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.EditGridCellNumber);
            await Assert.That(numberEditResult.Step.DoubleValue).IsEqualTo(12.75);

            await Assert.That(dateEditResult.Success).IsEqualTo(true);
            await Assert.That(dateEditResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.EditGridCellDate);
            await Assert.That(dateEditResult.Step.DateValue).IsEqualTo(new DateTime(2026, 4, 28));

            await Assert.That(comboEditResult.Success).IsEqualTo(true);
            await Assert.That(comboEditResult.Step!.ActionKind).IsEqualTo(RecordedActionKind.SelectGridCellComboItem);
            await Assert.That(comboEditResult.Step.StringValue).IsEqualTo("Approved");

            await Assert.That(factory.ShouldSuppressCompositeDateSelection(dateFromPicker)).IsEqualTo(true);
            await Assert.That(factory.ShouldSuppressCompositeTextEntry(numericFromTextBox)).IsEqualTo(true);
            await Assert.That(factory.ShouldSuppressCompositeTextEntry(folderTextBox)).IsEqualTo(true);
            await Assert.That(factory.ShouldSuppressCompositeTextEntry(textEditValue)).IsEqualTo(true);
            await Assert.That(factory.ShouldSuppressCompositeDateSelection(dateEditValue)).IsEqualTo(true);
            await Assert.That(factory.ShouldSuppressCompositeSelection(comboEditValue)).IsEqualTo(true);
        }
    }

    [Test]
    public async Task RecorderSession_CapturesConfiguredCompositeButtons_AsHighLevelSteps()
    {
        var options = CreateCompositeCoverageOptions();
        var root = CreateCompositeCoverageRoot(
            out var dateApplyButton,
            out _,
            out var folderSelectButton,
            out var textEditButton,
            out _,
            out _,
            out _,
            out _,
            out _,
            out var folderTextBox,
            out var textEditValue,
            out _,
            out _);
        using var session = new RecorderSession(CreateWindowStub(), options, () => root, attachWindowHandlers: false);
        var details = session;
        var dateOpenButton = FindButton(root, "DateOpenButton");
        var folderOpenButton = FindButton(root, "ExportOpenButton");

        session.Start();
        session.RefreshObservedControlsForTesting();
        session.CaptureButtonClickForTesting(folderOpenButton);
        session.RegisterKeyboardInputForTesting(folderTextBox);
        folderTextBox.Text = @"C:\Exports\Arm";
        session.CaptureButtonClickForTesting(folderSelectButton);
        session.RegisterKeyboardInputForTesting(textEditValue);
        textEditValue.Text = "Edited";
        session.CaptureButtonClickForTesting(textEditButton);
        session.CaptureButtonClickForTesting(dateOpenButton);
        session.CaptureButtonClickForTesting(dateApplyButton);

        using (Assert.Multiple())
        {
            await Assert.That(details.StepJournal.Count).IsEqualTo(3);
            await Assert.That(details.StepJournal[0].Preview).Contains("Page.SelectExportFolder(static page => page.ExportPicker, \"C:\\\\Exports\\\\Arm\");");
            await Assert.That(details.StepJournal[1].Preview).Contains("Page.EditGridCellText(static page => page.EditableGrid, 0, 1, \"Edited\");");
            await Assert.That(details.StepJournal[2].Preview).Contains("Page.SetDateRangeFilter(static page => page.DateFilter, new global::System.DateTime(2026, 4, 1), new global::System.DateTime(2026, 4, 30));");
            await Assert.That(details.StepJournal.Any(static entry => entry.Preview.Contains("Page.EnterText", StringComparison.Ordinal))).IsEqualTo(false);
            await Assert.That(details.StepJournal.Any(static entry => entry.Preview.Contains("Page.ClickButton", StringComparison.Ordinal))).IsEqualTo(false);
        }
    }

    private static IReadOnlyList<RecordedStep> CreateActionCoverageSteps()
    {
        var date = new DateTime(2026, 4, 28);
        return
        [
            new RecordedStep(RecordedActionKind.EnterText, Descriptor("SearchBox", UiControlType.TextBox), StringValue: "alpha"),
            new RecordedStep(RecordedActionKind.ClickButton, Descriptor("RunButton", UiControlType.Button)),
            new RecordedStep(RecordedActionKind.SetChecked, Descriptor("AgreeCheckBox", UiControlType.CheckBox), BoolValue: true),
            new RecordedStep(RecordedActionKind.SetToggled, Descriptor("PinToggleButton", UiControlType.ToggleButton), BoolValue: true),
            new RecordedStep(RecordedActionKind.SelectComboItem, Descriptor("OperationCombo", UiControlType.ComboBox), StringValue: "GCD"),
            new RecordedStep(RecordedActionKind.SetSliderValue, Descriptor("ScaleSlider", UiControlType.Slider), DoubleValue: 2.5),
            new RecordedStep(RecordedActionKind.SetSpinnerValue, Descriptor("CountSpinner", UiControlType.TextBox), DoubleValue: 7),
            new RecordedStep(RecordedActionKind.SelectTabItem, Descriptor("ControlMixTabItem", UiControlType.TabItem)),
            new RecordedStep(RecordedActionKind.SelectTreeItem, Descriptor("NavigationTree", UiControlType.Tree), StringValue: "Orders"),
            new RecordedStep(RecordedActionKind.SetDate, Descriptor("StartDatePicker", UiControlType.DateTimePicker), DateValue: date),
            new RecordedStep(RecordedActionKind.WaitUntilTextEquals, Descriptor("StatusLabel", UiControlType.Label), StringValue: "Ready"),
            new RecordedStep(RecordedActionKind.WaitUntilTextContains, Descriptor("StatusLabel", UiControlType.Label), StringValue: "Ready"),
            new RecordedStep(RecordedActionKind.WaitUntilIsChecked, Descriptor("AgreeCheckBox", UiControlType.CheckBox), BoolValue: true),
            new RecordedStep(RecordedActionKind.WaitUntilIsToggled, Descriptor("PinToggleButton", UiControlType.ToggleButton), BoolValue: true),
            new RecordedStep(RecordedActionKind.WaitUntilIsSelected, Descriptor("PrimaryRadioButton", UiControlType.RadioButton), BoolValue: true),
            new RecordedStep(RecordedActionKind.WaitUntilIsEnabled, Descriptor("AnyControl", UiControlType.AutomationElement), BoolValue: true),
            new RecordedStep(RecordedActionKind.SelectListBoxItem, Descriptor("HistoryList", UiControlType.ListBox), StringValue: "Fibonacci"),
            new RecordedStep(RecordedActionKind.WaitUntilGridRowsAtLeast, Descriptor("OrdersGrid", UiControlType.Grid), IntValue: 3),
            new RecordedStep(RecordedActionKind.WaitUntilGridCellEquals, Descriptor("OrdersGrid", UiControlType.Grid), RowIndex: 0, ColumnIndex: 1, StringValue: "EX-13"),
            new RecordedStep(RecordedActionKind.SearchAndSelect, Descriptor("HistoryOperationPicker", UiControlType.SearchPicker), StringValue: "least", ItemValue: "Least Common Multiple"),
            new RecordedStep(RecordedActionKind.OpenGridRow, Descriptor("OrdersGrid", UiControlType.Grid), RowIndex: 0),
            new RecordedStep(RecordedActionKind.SortGridByColumn, Descriptor("OrdersGrid", UiControlType.Grid), StringValue: "Value"),
            new RecordedStep(RecordedActionKind.ScrollGridToEnd, Descriptor("OrdersGrid", UiControlType.Grid)),
            new RecordedStep(RecordedActionKind.CopyGridCell, Descriptor("OrdersGrid", UiControlType.Grid), RowIndex: 0, ColumnIndex: 1),
            new RecordedStep(RecordedActionKind.ExportGrid, Descriptor("OrdersGrid", UiControlType.Grid)),
            new RecordedStep(RecordedActionKind.ConfirmDialog, Descriptor("DeleteDialog", UiControlType.Dialog)),
            new RecordedStep(RecordedActionKind.CancelDialog, Descriptor("DeleteDialog", UiControlType.Dialog)),
            new RecordedStep(RecordedActionKind.DismissDialog, Descriptor("DeleteDialog", UiControlType.Dialog)),
            new RecordedStep(RecordedActionKind.DismissNotification, Descriptor("ExportToast", UiControlType.Notification)),
            new RecordedStep(RecordedActionKind.OpenOrActivateShellPane, Descriptor("Shell", UiControlType.ShellNavigation), StringValue: "Customers"),
            new RecordedStep(RecordedActionKind.ActivateShellPane, Descriptor("Shell", UiControlType.ShellNavigation), StringValue: "Orders"),
            new RecordedStep(RecordedActionKind.SearchAndSelectGridCell, Descriptor("OrdersGrid", UiControlType.Grid), RowIndex: 0, ColumnIndex: 1, StringValue: "prod", ItemValue: "Product 42"),
            new RecordedStep(RecordedActionKind.WaitUntilProgressAtLeast, Descriptor("ReloadProgress", UiControlType.ProgressBar), DoubleValue: 90),
            new RecordedStep(RecordedActionKind.WaitUntilListBoxContains, Descriptor("HistoryList", UiControlType.ListBox), StringValue: "Fibonacci"),
            new RecordedStep(RecordedActionKind.WaitUntilHasItemsAtLeast, Descriptor("HistoryList", UiControlType.ListBox), IntValue: 2),
            new RecordedStep(RecordedActionKind.WaitUntilNotificationContains, Descriptor("ExportToast", UiControlType.Notification), StringValue: "Export ready"),
            new RecordedStep(RecordedActionKind.SetDateRangeFilter, Descriptor("DateFilter", UiControlType.DateRangeFilter), DateValue: date.AddDays(-7), SecondDateValue: date),
            new RecordedStep(RecordedActionKind.SetNumericRangeFilter, Descriptor("NumericFilter", UiControlType.NumericRangeFilter), DoubleValue: 10, SecondDoubleValue: 20),
            new RecordedStep(RecordedActionKind.SelectExportFolder, Descriptor("ExportPicker", UiControlType.FolderExport), StringValue: @"C:\Exports\Arm"),
            new RecordedStep(RecordedActionKind.EditGridCellText, Descriptor("OrdersGrid", UiControlType.Grid), RowIndex: 0, ColumnIndex: 1, StringValue: "Edited"),
            new RecordedStep(RecordedActionKind.EditGridCellNumber, Descriptor("OrdersGrid", UiControlType.Grid), RowIndex: 0, ColumnIndex: 2, DoubleValue: 42.5),
            new RecordedStep(RecordedActionKind.EditGridCellDate, Descriptor("OrdersGrid", UiControlType.Grid), RowIndex: 0, ColumnIndex: 3, DateValue: date),
            new RecordedStep(RecordedActionKind.SelectGridCellComboItem, Descriptor("OrdersGrid", UiControlType.Grid), RowIndex: 0, ColumnIndex: 4, StringValue: "Approved")
        ];
    }

    private static AppAutomationRecorderOptions CreateCompositeCoverageOptions()
    {
        var options = new AppAutomationRecorderOptions { ShowOverlay = false };
        options.DateRangeFilterHints.Add(new RecorderDateRangeFilterHint(
            "DateFilter",
            DateRangeFilterParts.ByAutomationIds(
                "DateFrom",
                "DateTo",
                "DateApplyButton",
                "DateCancelButton",
                openButtonAutomationId: "DateOpenButton")));
        options.NumericRangeFilterHints.Add(new RecorderNumericRangeFilterHint(
            "NumericFilter",
            NumericRangeFilterParts.ByAutomationIds(
                "NumericFrom",
                "NumericTo",
                "NumericApplyButton",
                "NumericCancelButton",
                openButtonAutomationId: "NumericOpenButton",
                editorKind: FilterValueEditorKind.TextBox)));
        options.FolderExportHints.Add(new RecorderFolderExportHint(
            "ExportPicker",
            FolderExportControlParts.ByAutomationIds(
                "ExportOpenButton",
                "ExportPathInput",
                "ExportSelectButton",
                "ExportCancelButton",
                statusAutomationId: "ExportStatusLabel")));
        options.GridEditHints.Add(new RecorderGridEditHint(
            "GridTextCommitButton",
            "EditableGrid",
            "GridTextValue",
            0,
            1));
        options.GridEditHints.Add(new RecorderGridEditHint(
            "GridNumberCommitButton",
            "EditableGrid",
            "GridNumberValue",
            0,
            2,
            GridCellEditorKind.Number));
        options.GridEditHints.Add(new RecorderGridEditHint(
            "GridDateCommitButton",
            "EditableGrid",
            "GridDateValue",
            0,
            3,
            GridCellEditorKind.Date));
        options.GridEditHints.Add(new RecorderGridEditHint(
            "GridComboCommitButton",
            "EditableGrid",
            "GridComboValue",
            0,
            4,
            GridCellEditorKind.ComboBox));
        return options;
    }

    private static StackPanel CreateCompositeCoverageRoot(
        out Button dateApplyButton,
        out Button numericApplyButton,
        out Button folderSelectButton,
        out Button textEditButton,
        out Button numberEditButton,
        out Button dateEditButton,
        out Button comboEditButton,
        out DatePicker dateFromPicker,
        out TextBox numericFromTextBox,
        out TextBox folderTextBox,
        out TextBox textEditValue,
        out DatePicker dateEditValue,
        out ComboBox comboEditValue)
    {
        var root = new StackPanel();
        dateFromPicker = DatePicker("DateFrom", new DateTime(2026, 4, 1));
        var dateToPicker = DatePicker("DateTo", new DateTime(2026, 4, 30));
        var dateOpenButton = Button("DateOpenButton");
        dateApplyButton = Button("DateApplyButton");
        var dateCancelButton = Button("DateCancelButton");
        numericFromTextBox = TextBox("NumericFrom", "5.5");
        var numericToTextBox = TextBox("NumericTo", "42.25");
        var numericOpenButton = Button("NumericOpenButton");
        numericApplyButton = Button("NumericApplyButton");
        var numericCancelButton = Button("NumericCancelButton");
        folderTextBox = TextBox("ExportPathInput", @"C:\Exports\Arm");
        var folderOpenButton = Button("ExportOpenButton");
        folderSelectButton = Button("ExportSelectButton");
        var folderCancelButton = Button("ExportCancelButton");
        textEditValue = TextBox("GridTextValue", "Edited");
        textEditButton = Button("GridTextCommitButton");
        var numberEditValue = TextBox("GridNumberValue", "12.75");
        numberEditButton = Button("GridNumberCommitButton");
        dateEditValue = DatePicker("GridDateValue", new DateTime(2026, 4, 28));
        dateEditButton = Button("GridDateCommitButton");
        comboEditValue = new ComboBox
        {
            ItemsSource = new[] { "Pending", "Approved" },
            SelectedItem = "Approved"
        };
        AutomationProperties.SetAutomationId(comboEditValue, "GridComboValue");
        comboEditButton = Button("GridComboCommitButton");

        root.Children.Add(dateFromPicker);
        root.Children.Add(dateToPicker);
        root.Children.Add(dateOpenButton);
        root.Children.Add(dateApplyButton);
        root.Children.Add(dateCancelButton);
        root.Children.Add(numericFromTextBox);
        root.Children.Add(numericToTextBox);
        root.Children.Add(numericOpenButton);
        root.Children.Add(numericApplyButton);
        root.Children.Add(numericCancelButton);
        root.Children.Add(folderTextBox);
        root.Children.Add(folderOpenButton);
        root.Children.Add(folderSelectButton);
        root.Children.Add(folderCancelButton);
        root.Children.Add(textEditValue);
        root.Children.Add(textEditButton);
        root.Children.Add(numberEditValue);
        root.Children.Add(numberEditButton);
        root.Children.Add(dateEditValue);
        root.Children.Add(dateEditButton);
        root.Children.Add(comboEditValue);
        root.Children.Add(comboEditButton);
        return root;
    }

    private static RecordedControlDescriptor Descriptor(string propertyName, UiControlType controlType)
    {
        return new RecordedControlDescriptor(
            propertyName,
            controlType,
            propertyName,
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: typeof(Control).FullName ?? nameof(Control),
            Warning: null);
    }

    private static Button Button(string automationId)
    {
        var button = new Button { Content = automationId };
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }

    private static TextBox TextBox(string automationId, string text)
    {
        var textBox = new TextBox { Text = text };
        AutomationProperties.SetAutomationId(textBox, automationId);
        return textBox;
    }

    private static DatePicker DatePicker(string automationId, DateTime date)
    {
        var datePicker = new DatePicker
        {
            SelectedDate = new DateTimeOffset(date)
        };
        AutomationProperties.SetAutomationId(datePicker, automationId);
        return datePicker;
    }

    private static Button FindButton(Panel root, string automationId)
    {
        return root.Children
            .OfType<Button>()
            .Single(button => string.Equals(AutomationProperties.GetAutomationId(button), automationId, StringComparison.Ordinal));
    }

    private static Window CreateWindowStub()
    {
#pragma warning disable SYSLIB0050
        return (Window)FormatterServices.GetUninitializedObject(typeof(TestRecorderWindow));
#pragma warning restore SYSLIB0050
    }

    private sealed class TestRecorderWindow : Window
    {
    }
}
