using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using AppAutomation.Recorder.Avalonia.UI;
using Avalonia.Controls;
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
    public async Task Generator_RendersRangeAndNamedGridDateExpressionsIndependently()
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

        var preview = generator.GeneratePreview(
        [
            new RecordedStep(
                RecordedActionKind.SetDateRangeFilter,
                Descriptor("DateFilter", UiControlType.DateRangeFilter),
                DateValue: new DateTime(2026, 8, 7),
                SecondDateValue: new DateTime(2026, 9, 6),
                DateExpression: Relative(-30),
                SecondDateExpression: Relative(0)),
            gridStep
        ]);

        using (Assert.Multiple())
        {
            await Assert.That(preview).Contains(
                "Page.SetDateRangeFilter(static page => page.DateFilter, DateTime.Today.AddDays(-30), DateTime.Today);");
            await Assert.That(preview).Contains(
                "Page.EditGridCellDate(static page => page.ItemsGrid, GridRowSelector.ByCell(\"ItemNumber\", \"10\"), \"RequiredDate\", DateTime.Today.AddDays(7));");
        }
    }

    [Test]
    public async Task Generator_RendersRelativeLiteralDateAssertion()
    {
        var generator = CreateGenerator();
        var assertion = new RecordedStep(
            RecordedActionKind.AssertValue,
            Descriptor("RequiredDate", UiControlType.DateTimePicker),
            DateValue: new DateTime(2026, 9, 11),
            ValueKind: RecorderValueKind.Date,
            ValueAccessorKind: RecorderValueAccessorKind.SelectedDate,
            ComparisonKind: RecorderComparisonKind.Equal,
            HasExpectedLiteral: true,
            DateExpression: Relative(5));

        var preview = generator.GeneratePreview(assertion);

        await Assert.That(preview).Contains("DateTime.Today.AddDays(5)");
        await Assert.That(preview).DoesNotContain("global::System.DateTime.Today");
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
    public async Task Overlay_OffersDateModeOnlyForDateBearingJournalSteps()
    {
        var root = new StackPanel();
        using var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            new AppAutomationRecorderOptions { ShowOverlay = false },
            validationRootProvider: () => root,
            attachWindowHandlers: false);
        var dateStepId = Guid.NewGuid();
        session.AddRecordedStepForTesting(DateStep(
            "RequiredDate",
            new DateTime(2026, 9, 6)) with { StepId = dateStepId });
        session.AddRecordedStepForTesting(new RecordedStep(
            RecordedActionKind.ClickButton,
            Descriptor("SaveButton", UiControlType.Button)));
        var overlay = new RecorderOverlay();
        overlay.Attach(session, new AppAutomationRecorderOptions());

        var exactDateButtons = FindDateModeButtons(overlay);
        ((IRecorderRelativeDateSessionDetails)session).SetStepDateExpressions(
            dateStepId,
            Relative(10),
            secondary: null);
        overlay.RefreshForTesting();
        var relativeDateButtons = FindDateModeButtons(overlay);

        using (Assert.Multiple())
        {
            await Assert.That(exactDateButtons).IsEquivalentTo(["Date: Exact"]);
            await Assert.That(relativeDateButtons).IsEquivalentTo(["Date: Today +10d"]);
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
