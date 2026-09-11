using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderExpanderCaptureTests
{
    [Test]
    [Arguments(false, false, true)]
    [Arguments(false, true, true)]
    [Arguments(true, false, false)]
    [Arguments(true, true, false)]
    public async Task UserStateChange_RecordsFinalState(
        bool initiallyExpanded,
        bool keyboard,
        bool expectedExpanded)
    {
        using var recorder = ExpanderCaptureFixture.Create(initiallyExpanded);

        recorder.Start();
        recorder.SetExpanded(expectedExpanded, keyboard);

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(recorder.OnlyStep.Preview)
                .Contains($"Page.SetExpanded(static page => page.DetailsExpander, {expectedExpanded.ToString().ToLowerInvariant()});");
            await Assert.That(recorder.OnlyStep.Preview).DoesNotContain("SetToggled");
        }
    }

    [Test]
    public async Task ProgrammaticOrRepeatedState_DoesNotRecordAction()
    {
        using var programmatic = ExpanderCaptureFixture.Create();
        programmatic.Start();
        programmatic.Expander.IsExpanded = true;

        using var repeated = ExpanderCaptureFixture.Create(initiallyExpanded: true);
        repeated.Start();
        repeated.SetExpanded(true, keyboard: false);

        using (Assert.Multiple())
        {
            await Assert.That(programmatic.Session.StepJournal).IsEmpty();
            await Assert.That(repeated.Session.StepJournal).IsEmpty();
        }
    }

    [Test]
    public async Task HeaderToggle_IsNotRecordedAsPrimitiveButton()
    {
        using var recorder = ExpanderCaptureFixture.Create();

        recorder.Start();
        recorder.CaptureHeaderClick();

        await Assert.That(recorder.Session.StepJournal).IsEmpty();
    }

    [Test]
    public async Task CheckedAssertion_RecordsExpandedState()
    {
        using var recorder = ExpanderCaptureFixture.Create(initiallyExpanded: true);

        recorder.Start();
        recorder.CaptureAssertion();

        using (Assert.Multiple())
        {
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(recorder.OnlyStep.Preview)
                .Contains("Page.WaitUntilIsExpanded(static page => page.DetailsExpander, true);");
        }
    }
}
