using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderMultiSelectCaptureTests
{
    [Test]
    public async Task Apply_RecordsOneSemanticStep()
    {
        using var recorder = MultiSelectCaptureFixture.Create("Alpha", "Gamma");

        recorder.Start();
        recorder.Capture(
            recorder.OpenButton,
            recorder.Item("Alpha"),
            recorder.Item("Gamma"),
            recorder.ApplyButton);

        await AssertSemanticStep(
            recorder.Session,
            "Page.SelectMultiItems(static page => page.Categories, new[] { \"Alpha\", \"Gamma\" });");
    }

    [Test]
    public async Task Cancel_RecordsOneSemanticStep()
    {
        using var recorder = MultiSelectCaptureFixture.Create("Beta");

        recorder.Start();
        recorder.Capture(
            recorder.OpenButton,
            recorder.Item("Beta"),
            recorder.CancelButton);

        await AssertSemanticStep(
            recorder.Session,
            "Page.CancelMultiSelection(static page => page.Categories, new[] { \"Beta\" });");
    }

    [Test]
    public async Task ItemsContainerSelection_IsSuppressed()
    {
        var items = MultiSelectCaptureFixture.CreateItemsContainer(selectedItem: "Alpha");
        using var session = MultiSelectCaptureFixture.CreateSession(
            MultiSelectCaptureFixture.CreateOptions(),
            items);

        session.Start();
        session.RegisterPointerInputForTesting(items);
        session.CaptureListBoxSelectionForTesting(items);

        await Assert.That(session.StepJournal).IsEmpty();
    }

    [Test]
    public async Task AmbiguousCommitHint_IsRejectedWithoutPrimitiveFallback()
    {
        var options = MultiSelectCaptureFixture.CreateOptions();
        options.MultiSelectHints.Add(new RecorderMultiSelectHint(
            "SecondaryCategories",
            MultiSelectCaptureFixture.CreateParts()));
        using var recorder = MultiSelectCaptureFixture.Create(options);

        recorder.Start();
        recorder.Capture(recorder.ApplyButton);

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal).IsEmpty();
            await Assert.That(recorder.Session.LatestPreview).IsEmpty();
            await Assert.That(recorder.Session.LatestStatus).Contains("ambiguous");
        }
    }

    private static async Task AssertSemanticStep(RecorderSession session, string expectedCommand)
    {
        using (Assert.Multiple())
        {
            await Assert.That(session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(session.StepJournal[0].Preview).Contains(expectedCommand);
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("Page.SetChecked");
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("Page.ClickButton");
        }
    }
}
