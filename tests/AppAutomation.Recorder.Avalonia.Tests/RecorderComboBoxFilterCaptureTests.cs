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

    private static async Task AssertSemanticStep(RecorderSession session, string expectedCommand)
    {
        using (Assert.Multiple())
        {
            await Assert.That(session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(session.StepJournal[0].Preview).Contains(expectedCommand);
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("Page.SetChecked");
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("Page.SelectListBoxItem");
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("StatusFilterApplyButton");
        }
    }
}
