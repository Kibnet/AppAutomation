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
        var describedAssertion = ((IRecorderRelativeDateSessionDetails)session)
            .TryGetDateConfiguration(assertionEntry.StepId, out var assertionDate);
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
            await Assert.That(checkSelection).IsNotNull();
            await Assert.That(string.IsNullOrEmpty(checkSelection!.ValueDescriptionError)).IsTrue();
            await Assert.That(checkSelection.ValueDescription!.ValueKind).IsEqualTo(RecorderValueKind.Date);
            await Assert.That(initialDayOffset).IsEqualTo("0");
            await Assert.That(describedAssertion).IsTrue();
            await Assert.That(assertionDate!.Primary.ReferenceKind).IsEqualTo(RecorderDateReferenceKind.RelativeToToday);
            await Assert.That(assertionDate.Primary.DayOffset).IsEqualTo(5);
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
    public async Task Session_AppliesRelativeDateWithoutLosingExistingValidation()
    {
        var root = new StackPanel();
        using var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            new AppAutomationRecorderOptions { ShowOverlay = false },
            validationRootProvider: () => root,
            attachWindowHandlers: false);
        var stepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.SetDate,
            Descriptor("RequiredDate", UiControlType.DateTimePicker),
            DateValue: new DateTime(2026, 9, 6),
            Warning: "Existing selector warning.",
            ValidationStatus: RecorderValidationStatus.Warning,
            ValidationMessage: "Existing selector warning.",
            CanPersist: true,
            StepId: stepId,
            ReviewState: RecorderStepReviewState.NeedsReview));
        var details = (IRecorderRelativeDateSessionDetails)session;

        var describedBefore = details.TryGetDateConfiguration(stepId, out var before);
        var applied = details.SetStepDateExpressions(stepId, Relative(10), secondary: null);
        var describedAfter = details.TryGetDateConfiguration(stepId, out var after);
        var journal = session.StepJournal.Single();

        using (Assert.Multiple())
        {
            await Assert.That(describedBefore).IsTrue();
            await Assert.That(before!.Primary.ReferenceKind).IsEqualTo(RecorderDateReferenceKind.Exact);
            await Assert.That(applied).IsTrue();
            await Assert.That(describedAfter).IsTrue();
            await Assert.That(after!.Primary.ReferenceKind).IsEqualTo(RecorderDateReferenceKind.RelativeToToday);
            await Assert.That(after.Primary.DayOffset).IsEqualTo(10);
            await Assert.That(journal.Preview).Contains("DateTime.Today.AddDays(10)");
            await Assert.That(journal.ValidationStatus).IsEqualTo(RecorderValidationStatus.Warning);
            await Assert.That(journal.StatusMessage).IsEqualTo("Existing selector warning.");
        }
    }

    [Test]
    public async Task Overlay_DateJournalEditorValidatesAppliesAndCancelsWithoutSpuriousAutosave()
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

        var exactDateButtons = FindDateModeButtons(overlay);
        var editDate = overlay.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Date: Exact", StringComparison.Ordinal));
        editDate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = overlay.LastDateExpressionEditorForTesting
            ?? throw new InvalidOperationException("Date journal editor was not opened.");
        var mode = editor.GetLogicalDescendants()
            .OfType<ComboBox>()
            .Single(control => control.Name == "RecorderJournalDateMode");
        var offset = editor.GetLogicalDescendants()
            .OfType<TextBox>()
            .Single(control => control.Name == "RecorderJournalDateOffset");
        var apply = editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Apply", StringComparison.Ordinal));
        var cancel = editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Cancel", StringComparison.Ordinal));
        mode.SelectedIndex = 1;
        offset.Text = "invalid";
        offset.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        var validation = editor.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(control => control.Name == "RecorderJournalDateValidation");

        var invalidApplyEnabled = apply.IsEnabled;
        var invalidMessageVisible = validation.IsVisible;
        var invalidMessage = validation.Text;
        offset.Text = "10";
        offset.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var details = (IRecorderRelativeDateSessionDetails)session;
        details.TryGetDateConfiguration(dateStepId, out var afterCancel);
        var autosaveAfterCancel = Volatile.Read(ref autosaveCallCount);

        editDate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        editor = overlay.LastDateExpressionEditorForTesting
            ?? throw new InvalidOperationException("Date journal editor was not reopened.");
        mode = editor.GetLogicalDescendants()
            .OfType<ComboBox>()
            .Single(control => control.Name == "RecorderJournalDateMode");
        offset = editor.GetLogicalDescendants()
            .OfType<TextBox>()
            .Single(control => control.Name == "RecorderJournalDateOffset");
        apply = editor.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content?.ToString(), "Apply", StringComparison.Ordinal));
        mode.SelectedIndex = 1;
        offset.Text = "10";
        offset.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUntilAsync(() => Volatile.Read(ref autosaveCallCount) == 1);
        overlay.RefreshForTesting();
        var relativeDateButtons = FindDateModeButtons(overlay);
        details.TryGetDateConfiguration(dateStepId, out var afterApply);

        using (Assert.Multiple())
        {
            await Assert.That(exactDateButtons).IsEquivalentTo(["Date: Exact"]);
            await Assert.That(invalidApplyEnabled).IsFalse();
            await Assert.That(invalidMessageVisible).IsTrue();
            await Assert.That(invalidMessage).IsEqualTo("Enter a whole number of days.");
            await Assert.That(afterCancel!.Primary.ReferenceKind).IsEqualTo(RecorderDateReferenceKind.Exact);
            await Assert.That(autosaveAfterCancel).IsEqualTo(0);
            await Assert.That(relativeDateButtons).IsEquivalentTo(["Date: Today +10d"]);
            await Assert.That(afterApply!.Primary.ReferenceKind).IsEqualTo(RecorderDateReferenceKind.RelativeToToday);
            await Assert.That(afterApply.Primary.DayOffset).IsEqualTo(10);
            await Assert.That(autosaveCallCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Session_RejectsInvalidBusyAndIgnoredDateChangesWithoutLosingAppliedOffset()
    {
        var autosaveCompletion = new TaskCompletionSource<RecorderSaveResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            new AppAutomationRecorderOptions { ShowOverlay = false },
            validationRootProvider: static () => null,
            attachWindowHandlers: false,
            autosaveOperation: (_, _, _) => autosaveCompletion.Task);
        var stepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(DateStep(
            "RequiredDate",
            new DateTime(2026, 9, 6)) with { StepId = stepId });
        session.Start();
        var details = (IRecorderRelativeDateSessionDetails)session;

        var applied = details.SetStepDateExpressions(stepId, Relative(5), secondary: null);
        var rejectedWhileBusy = details.SetStepDateExpressions(stepId, Relative(6), secondary: null);
        autosaveCompletion.SetResult(RecorderSaveResult.Failed("Expected test completion."));
        await WaitUntilAsync(() => !session.IsBusy);
        var rejectedInvalid = details.SetStepDateExpressions(
            stepId,
            Relative(int.MaxValue),
            secondary: null);
        session.Stop();
        session.SetStepIgnored(stepId, isIgnored: true);
        var rejectedWhileIgnored = details.SetStepDateExpressions(stepId, Relative(7), secondary: null);
        details.TryGetDateConfiguration(stepId, out var configuration);

        using (Assert.Multiple())
        {
            await Assert.That(applied).IsTrue();
            await Assert.That(rejectedWhileBusy).IsFalse();
            await Assert.That(rejectedInvalid).IsFalse();
            await Assert.That(rejectedWhileIgnored).IsFalse();
            await Assert.That(configuration!.Primary.DayOffset).IsEqualTo(5);
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
