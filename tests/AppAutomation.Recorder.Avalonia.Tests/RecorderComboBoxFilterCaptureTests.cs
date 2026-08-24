using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderComboBoxFilterCaptureTests
{
    [Test]
    public async Task Apply_WithNoValues_RecordsEmptyFilterSet()
    {
        using var recorder = ComboBoxFilterCaptureFixture.Create();

        recorder.Start();
        recorder.Capture(recorder.OpenButton, recorder.ApplyButton!);

        await AssertSemanticStep(
            recorder.Session,
            "Page.ApplyFilterSelection(static page => page.StatusFilter, global::System.Array.Empty<string>());");
    }

    [Test]
    public async Task Apply_WithOneValue_RecordsFilterCommand()
    {
        using var recorder = ComboBoxFilterCaptureFixture.Create("Pending");

        recorder.Start();
        recorder.Capture(
            recorder.OpenButton,
            recorder.Item("Pending"),
            recorder.ApplyButton!);

        await AssertSemanticStep(
            recorder.Session,
            "Page.ApplyFilterSelection(static page => page.StatusFilter, new[] { \"Pending\" });");
    }

    [Test]
    public async Task Apply_WithSeveralValues_UsesSameFilterCommand()
    {
        using var recorder = ComboBoxFilterCaptureFixture.Create("Pending", "Closed");

        recorder.Start();
        recorder.Capture(
            recorder.OpenButton,
            recorder.Item("Pending"),
            recorder.Item("Closed"),
            recorder.ApplyButton!);

        await AssertSemanticStep(
            recorder.Session,
            "Page.ApplyFilterSelection(static page => page.StatusFilter, new[] { \"Closed\", \"Pending\" });");
    }

    [Test]
    public async Task Cancel_RecordsPendingValues()
    {
        using var recorder = ComboBoxFilterCaptureFixture.Create("Open", "Closed");

        recorder.Start();
        recorder.Capture(
            recorder.OpenButton,
            recorder.Item("Open"),
            recorder.Item("Closed"),
            recorder.CancelButton!);

        await AssertSemanticStep(
            recorder.Session,
            "Page.CancelFilterSelection(static page => page.StatusFilter, new[] { \"Closed\", \"Open\" });");
    }

    [Test]
    public async Task Cancel_CapturesPendingValuesBeforeApplicationRestoresCommittedSelection()
    {
        using var recorder = ComboBoxFilterCaptureFixture.Create("Open", "Closed");
        recorder.CancelButton!.Click += (_, _) => recorder.SelectOnly("Pending");

        recorder.Start();
        recorder.Click(recorder.CancelButton);

        await AssertSemanticStep(
            recorder.Session,
            "Page.CancelFilterSelection(static page => page.StatusFilter, new[] { \"Closed\", \"Open\" });");
    }

    [Test]
    public async Task ImmediateSelection_UsesSameFilterCommand()
    {
        using var recorder = ComboBoxFilterCaptureFixture.CreateImmediate("Closed");

        recorder.Start();
        recorder.CaptureImmediateSelection();

        await AssertSemanticStep(
            recorder.Session,
            "Page.ApplyFilterSelection(static page => page.StatusFilter, new[] { \"Closed\" });");
    }

    [Test]
    public async Task ConfiguredFilter_DoesNotProduceRuntimeAdapterWarning()
    {
        var options = new AppAutomationRecorderOptions();
        options.ComboBoxFilterHints.Add(new RecorderComboBoxFilterHint(
            "StatusFilter",
            ComboBoxFilterParts.ByAutomationIds(
                "StatusFilterRoot",
                "StatusFilterOpenButton",
                "StatusFilterItems",
                "StatusFilterApplyButton",
                "StatusFilterCancelButton")));
        var step = new RecordedStep(
            RecordedActionKind.ApplyFilterSelection,
            new RecordedControlDescriptor(
                "StatusFilter",
                UiControlType.ComboBoxFilter,
                "StatusFilter",
                UiLocatorKind.AutomationId,
                FallbackToName: false,
                AvaloniaTypeName: typeof(global::Avalonia.Controls.Control).FullName
                    ?? nameof(global::Avalonia.Controls.Control),
                Warning: null),
            StringValues: ["Closed"]);

        var result = new RecorderCommandRuntimeValidator(options).Validate(step);

        using (Assert.Multiple())
        {
            await Assert.That(result.ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(result.RuntimeValidationFindings).IsNotNull();
            await Assert.That(result.RuntimeValidationFindings!.Any(static finding => finding.ShouldSurface)).IsFalse();
        }
    }

    private static async Task AssertSemanticStep(RecorderSession session, string expectedCommand)
    {
        using (Assert.Multiple())
        {
            await Assert.That(session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(session.StepJournal[0].Preview).Contains(expectedCommand);
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("Page.SetChecked");
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("Page.SelectListBoxItem");
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("StatusFilterApplyButton");
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("recorder warning");
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("configured parts");
        }
    }
}
