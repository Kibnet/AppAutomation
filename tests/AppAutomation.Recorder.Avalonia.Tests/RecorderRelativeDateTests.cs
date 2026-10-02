using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using AppAutomation.Recorder.Avalonia.UI;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

[NotInParallel("RecorderOverlay")]
public sealed class RecorderRelativeDateTests
{
    [Test]
    public async Task Generator_KeepsExactDateAndRendersRelativeOffsets()
    {
        var generator = CreateGenerator();
        var preview = generator.GeneratePreview(
        [
            DateStep("ExactDate", new DateTime(2026, 9, 6)),
            DateStep("TodayDate", new DateTime(2026, 9, 6), Relative(0)),
            DateStep("FutureDate", new DateTime(2026, 9, 6), Relative(10)),
            DateStep("PastDate", new DateTime(2026, 9, 6), Relative(-7))
        ]);

        using (Assert.Multiple())
        {
            await Assert.That(preview).Contains(
                "Page.SetDate(static page => page.ExactDate, new global::System.DateTime(2026, 9, 6));");
            await Assert.That(preview).Contains(
                "Page.SetDate(static page => page.TodayDate, DateTime.Today);");
            await Assert.That(preview).Contains(
                "Page.SetDate(static page => page.FutureDate, DateTime.Today.AddDays(10));");
            await Assert.That(preview).Contains(
                "Page.SetDate(static page => page.PastDate, DateTime.Today.AddDays(-7));");
            await Assert.That(preview).DoesNotContain("global::System.DateTime.Today");
        }
    }

    [Test]
    public async Task Generator_RendersRangeAndNamedAndIndexedGridDateExpressionsIndependently()
    {
        var generator = CreateGenerator();
        var gridStep = new RecordedStep(
            RecordedActionKind.EditGridCellDate,
            Descriptor("ItemsGrid", UiControlType.Grid),
            DateValue: new DateTime(2026, 9, 13),
            GridCellEditCommitMode: GridCellEditCommitMode.Commit,
            DateExpression: Relative(7))
        {
            GridRowConditions = [new RecordedGridRowCondition("ItemNumber", "10")],
            GridTargetColumnName = "RequiredDate"
        };

        var indexedGridStep = new RecordedStep(
            RecordedActionKind.EditGridCellDate,
            Descriptor("ItemsGrid", UiControlType.Grid),
            RowIndex: 2,
            ColumnIndex: 3,
            DateValue: new DateTime(2026, 9, 4),
            GridCellEditCommitMode: GridCellEditCommitMode.Commit,
            DateExpression: Relative(-2));

        var preview = generator.GeneratePreview(
        [
            new RecordedStep(
                RecordedActionKind.SetDateRangeFilter,
                Descriptor("DateFilter", UiControlType.DateRangeFilter),
                DateValue: new DateTime(2026, 8, 7),
                SecondDateValue: new DateTime(2026, 9, 6),
                DateExpression: Relative(-30),
                SecondDateExpression: Relative(0)),
            gridStep,
            indexedGridStep
        ]);

        using (Assert.Multiple())
        {
            await Assert.That(preview).Contains(
                "Page.SetDateRangeFilter(static page => page.DateFilter, DateTime.Today.AddDays(-30), DateTime.Today);");
            await Assert.That(preview).Contains(
                "Page.EditGridCellDate(static page => page.ItemsGrid, GridRowSelector.ByCell(\"ItemNumber\", \"10\"), \"RequiredDate\", DateTime.Today.AddDays(7));");
            await Assert.That(preview).Contains(
                "Page.EditGridCellDate(static page => page.ItemsGrid, 2, 3, DateTime.Today.AddDays(-2));");
        }
    }

    [Test]
    public async Task PopupDateProxy_RecordsLogicalDateAndCheckKeepsTypedRelativeDate()
    {
        var selectedDate = DateTime.Today.AddDays(7);
        var requiredDateRoot = new StackPanel();
        var requiredDateValue = new TextBox
        {
            Text = selectedDate.ToString("d", System.Globalization.CultureInfo.CurrentCulture)
        };
        var requiredDateOpen = new Button();
        var popupCalendar = new Calendar();
        SetAutomationId(requiredDateRoot, "RequiredDate");
        SetAutomationId(requiredDateValue, "RequiredDateValue");
        SetAutomationId(requiredDateOpen, "RequiredDateOpen");
        SetAutomationId(popupCalendar, "RequiredDateCalendar");
        requiredDateRoot.Children.Add(requiredDateValue);
        requiredDateRoot.Children.Add(requiredDateOpen);

        var createdDateRoot = new StackPanel();
        var createdDateValue = new TextBox
        {
            Text = DateTime.Today.ToString("d", System.Globalization.CultureInfo.CurrentCulture),
            IsEnabled = false
        };
        SetAutomationId(createdDateRoot, "CreatedDate");
        SetAutomationId(createdDateValue, "CreatedDateValue");
        createdDateRoot.Children.Add(createdDateValue);

        var root = new StackPanel();
        root.Children.Add(requiredDateRoot);
        root.Children.Add(createdDateRoot);
        root.Children.Add(popupCalendar);

        var options = new AppAutomationRecorderOptions { ShowOverlay = false };
        options.ConfigureDateTimePickerProxy(
            "RequiredDate",
            DatePickerParts.ByAutomationIds(
                "RequiredDate",
                "RequiredDateValue",
                "RequiredDateOpen",
                "RequiredDateCalendar"));
        options.ConfigureDateTimePickerProxy(
            "CreatedDate",
            DatePickerParts.ByAutomationIds("CreatedDate", "CreatedDateValue"));
        var factory = new RecorderStepFactory(options, () => root);

        root.Children.Remove(popupCalendar);
        var selection = factory.TryCreateCalendarStep(popupCalendar, selectedDate);
        var directTextAssertion = factory.TryCreateAssertionStep(
            createdDateValue,
            RecorderAssertionMode.Text);
        var directTextAssertionPreview = CreateGenerator().GeneratePreview(directTextAssertion.Step!);

        using var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options,
            validationRootProvider: () => root,
            attachWindowHandlers: false);
        session.AddRecordedStepForTesting(selection.Step!);
        var selectionRevalidated = session.RetryStepValidation(selection.Step!.StepId);
        var revalidatedSelection = session.StepJournal.Single();
        session.Clear();
        RecorderCheckTargetSelection? checkSelection = null;
        session.CheckTargetSelected += (_, eventArgs) => checkSelection = eventArgs.Selection;
        session.Start();
        session.BeginCheckTargetSelection();
        session.SelectCheckTargetForTesting(createdDateValue);
        var overlay = new RecorderOverlay();
        overlay.Attach(session, options);
        var assertionEditor = overlay.CreateLiteralAssertionEditorForTesting(
            checkSelection
            ?? throw new InvalidOperationException("Check did not select the configured date value."));
        var dateMode = assertionEditor.GetLogicalDescendants()
            .OfType<ComboBox>()
            .Single(control => control.Name == "RecorderLiteralDateMode");
        var dayOffset = assertionEditor.GetLogicalDescendants()
            .OfType<TextBox>()
            .Single(control => control.Name == "RecorderLiteralDateOffset");
        var addAssertion = assertionEditor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Add", StringComparison.Ordinal));
        var initialDayOffset = dayOffset.Text;
        dateMode.SelectedIndex = 1;
        dayOffset.Text = "5";
        addAssertion.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var selectedStep = selection.Step! with { DateExpression = Relative(7) };
        var assertionEntry = session.StepJournal.Single();
        var describedAssertion = ((IRecorderStepEditingSessionDetails)session)
            .TryCreateStepEditDraft(assertionEntry.StepId, out var assertionDraft, out _);
        var preview = CreateGenerator().GeneratePreview(selectedStep) + Environment.NewLine + assertionEntry.Preview;

        using (Assert.Multiple())
        {
            await Assert.That(selection.Success).IsTrue();
            await Assert.That(selection.Step!.ActionKind).IsEqualTo(RecordedActionKind.SetDate);
            await Assert.That(selection.Step.Control.LocatorValue).IsEqualTo("RequiredDate");
            await Assert.That(selection.Step.Control.ControlType).IsEqualTo(UiControlType.DateTimePicker);
            await Assert.That(selection.Step.CanPersist).IsTrue();
            await Assert.That(selectionRevalidated).IsTrue();
            await Assert.That(revalidatedSelection.CanPersist).IsTrue();
            await Assert.That(revalidatedSelection.StatusMessage ?? string.Empty)
                .DoesNotContain("not compatible");
            await Assert.That(directTextAssertion.Success).IsTrue();
            await Assert.That(directTextAssertion.Step!.ActionKind).IsEqualTo(RecordedActionKind.AssertValue);
            await Assert.That(directTextAssertion.Step.Control.LocatorValue).IsEqualTo("CreatedDate");
            await Assert.That(directTextAssertion.Step.Control.ControlType).IsEqualTo(UiControlType.DateTimePicker);
            await Assert.That(directTextAssertion.Step.ValueAccessorKind)
                .IsEqualTo(RecorderValueAccessorKind.SelectedDate);
            await Assert.That(directTextAssertion.Step.DateValue).IsEqualTo(DateTime.Today);
            await Assert.That(directTextAssertion.Step.CanPersist).IsTrue();
            await Assert.That(directTextAssertion.Step.ValidationMessage ?? string.Empty)
                .DoesNotContain("wrapper/composite");
            await Assert.That(directTextAssertionPreview)
                .Contains("Page.CreatedDate.SelectedDate).IsEqualTo(");
            await Assert.That(directTextAssertionPreview).DoesNotContain("WaitUntilTextEquals");
            await Assert.That(checkSelection).IsNotNull();
            await Assert.That(string.IsNullOrEmpty(checkSelection!.ValueDescriptionError)).IsTrue();
            await Assert.That(checkSelection.ValueDescription!.ValueKind).IsEqualTo(RecorderValueKind.Date);
            await Assert.That(initialDayOffset).IsEqualTo("0");
            await Assert.That(describedAssertion).IsTrue();
            await Assert.That(assertionDraft!.DateExpression!.ReferenceKind).IsEqualTo(RecorderDateReferenceKind.RelativeToToday);
            await Assert.That(assertionDraft.DateExpression.DayOffset).IsEqualTo(5);
            await Assert.That(preview).Contains(
                "Page.SetDate(static page => page.RequiredDate, DateTime.Today.AddDays(7));");
            await Assert.That(preview).Contains(
                "await Assert.That(Page.CreatedDate.SelectedDate).IsEqualTo(DateTime.Today.AddDays(5));");
            await Assert.That(preview).DoesNotContain("RequiredDateCalendar");
            await Assert.That(preview).DoesNotContain("RequiredDateValue");
            await Assert.That(preview).DoesNotContain("EnterText");
            await Assert.That(preview).DoesNotContain("SetToggled");
        }
    }

    [Test]
    public async Task Overlay_CommonStepEditorValidatesAppliesAndCancelsRelativeDateWithoutSpuriousAutosave()
    {
        var root = new StackPanel();
        var autosaveCallCount = 0;
        using var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            new AppAutomationRecorderOptions { ShowOverlay = false },
            validationRootProvider: () => root,
            attachWindowHandlers: false,
            autosaveOperation: (steps, _, _) =>
            {
                Interlocked.Increment(ref autosaveCallCount);
                return Task.FromResult(RecorderSaveResult.Completed(
                    "Autosaved.",
                    pageFilePath: "MainWindowPage.Recorded.autosave.cs",
                    scenarioFilePath: "MainWindowScenariosBase.Recorded.autosave.cs",
                    persistedStepCount: steps.Count,
                    skippedStepCount: 0));
            });
        var dateStepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(DateStep(
            "RequiredDate",
            new DateTime(2026, 9, 6)) with { StepId = dateStepId });
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.ClickButton,
            Descriptor("SaveButton", UiControlType.Button)));
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());
        session.Start();

        var editDate = overlay.GetLogicalDescendants()
            .OfType<Button>()
            .First(button => string.Equals(button.Content?.ToString(), "Edit", StringComparison.Ordinal));
        editDate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = overlay.LastStepEditorForTesting
            ?? throw new InvalidOperationException("Common step editor was not opened.");
        var date = editor.GetLogicalDescendants()
            .OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditDateValue");
        var apply = editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply");
        var cancel = editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditCancel");
        date.Text = "invalid";
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var validation = editor.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Name == "RecorderStepEditValidation");

        var invalidMessageVisible = validation.IsVisible;
        var invalidMessage = validation.Text;
        var editorRemainedOpen = overlay.LastStepEditorForTesting is not null;
        date.Text = "today+10";
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var details = (IRecorderStepEditingSessionDetails)session;
        details.TryCreateStepEditDraft(dateStepId, out var afterCancel, out _);
        var autosaveAfterCancel = Volatile.Read(ref autosaveCallCount);

        editDate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        editor = overlay.LastStepEditorForTesting
            ?? throw new InvalidOperationException("Common step editor was not reopened.");
        date = editor.GetLogicalDescendants()
            .OfType<TextBox>()
            .Single(control => control.Name == "RecorderStepEditDateValue");
        apply = editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => button.Name == "RecorderStepEditApply");
        date.Text = "today+10";
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntilAsync(() => Volatile.Read(ref autosaveCallCount) == 1);
        overlay.RefreshForTesting();
        details.TryCreateStepEditDraft(dateStepId, out var afterApply, out _);
        var remainingDateModeButtons = FindDateModeButtons(overlay);

        using (Assert.Multiple())
        {
            await Assert.That(invalidMessageVisible).IsTrue();
            await Assert.That(invalidMessage).Contains("Enter a date");
            await Assert.That(editorRemainedOpen).IsTrue();
            await Assert.That(afterCancel!.DateExpression).IsNull();
            await Assert.That(autosaveAfterCancel).IsEqualTo(0);
            await Assert.That(remainingDateModeButtons).IsEmpty();
            await Assert.That(afterApply!.DateExpression!.ReferenceKind).IsEqualTo(RecorderDateReferenceKind.RelativeToToday);
            await Assert.That(afterApply.DateExpression.DayOffset).IsEqualTo(10);
            await Assert.That(session.StepJournal[0].Preview).Contains("DateTime.Today.AddDays(10)");
            await Assert.That(autosaveCallCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Save_MergesRelativeDateScenariosWithOneSystemUsingAndCompiles()
    {
        using var project = RecorderScenarioDestinationProject.Create(
            RecorderScenarioDestinationSources.CompilableMainWindowPage,
            RecorderScenarioDestinationSources.CompilableScenario);
        var first = await project.SaveAsync(
            project.CreateSaveContext("Future date", "relative-date-a"),
            [DateStep("RequiredDate", new DateTime(2026, 9, 6), Relative(10))]);
        var second = await project.SaveAsync(
            project.CreateSaveContext("Date range", "relative-date-b"),
            [
                new RecordedStep(
                    RecordedActionKind.SetDateRangeFilter,
                    Descriptor("DateFilter", UiControlType.DateRangeFilter),
                    DateValue: new DateTime(2026, 8, 7),
                    SecondDateValue: new DateTime(2026, 9, 6),
                    DateExpression: Relative(-30),
                    SecondDateExpression: Relative(0))
            ]);
        var scenarioSource = await File.ReadAllTextAsync(second.ScenarioFilePath!);
        var compileErrors = RecorderGeneratedSourceCompiler.Compile(project.RootPath);

        using (Assert.Multiple())
        {
            await Assert.That(first.Success).IsTrue();
            await Assert.That(second.Success).IsTrue();
            await Assert.That(first.ScenarioFilePath).IsEqualTo(second.ScenarioFilePath);
            await Assert.That(CountOccurrences(scenarioSource, "using System;")).IsEqualTo(1);
            await Assert.That(scenarioSource).Contains("DateTime.Today.AddDays(10)");
            await Assert.That(scenarioSource).Contains("DateTime.Today.AddDays(-30)");
            await Assert.That(scenarioSource).Contains("DateTime.Today);");
            await Assert.That(compileErrors).IsEmpty();
        }
    }

    private static RecordedStep DateStep(
        string propertyName,
        DateTime date,
        RecorderDateExpression? expression = null) =>
        new(
            RecordedActionKind.SetDate,
            Descriptor(propertyName, UiControlType.DateTimePicker),
            DateValue: date,
            DateExpression: expression);

    private static RecorderDateExpression Relative(int dayOffset) =>
        new(RecorderDateReferenceKind.RelativeToToday, dayOffset);

    private static string[] FindDateModeButtons(RecorderOverlay overlay) =>
        overlay.GetLogicalDescendants()
            .OfType<Button>()
            .Select(static button => button.Content?.ToString())
            .Where(static content => content?.StartsWith("Date", StringComparison.Ordinal) == true)
            .Cast<string>()
            .ToArray();

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static void SetAutomationId(Control control, string automationId) =>
        AutomationProperties.SetAutomationId(control, automationId);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Recorder state did not settle within the test timeout.");
            }

            await Task.Delay(10);
        }
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

    private static AuthoringCodeGenerator CreateGenerator() =>
        new(new AuthoringProjectScanner(), logger: null);
}
