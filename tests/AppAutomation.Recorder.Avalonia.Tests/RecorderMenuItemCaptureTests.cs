using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

[NotInParallel]
public sealed class RecorderMenuItemCaptureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DirectItem_RecordsOneSemanticInvocation(bool keyboard)
    {
        using var recorder = MenuCaptureFixture.Create();

        recorder.Start();
        recorder.Invoke(recorder.DirectLeaf, keyboard);

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(recorder.OnlyStep.ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(recorder.OnlyStep.Preview)
                .Contains("Page.InvokeMenuItem(static page => page.RefreshMenuItem);");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NestedLeaf_RecordsOrderedVisibleCaptionPath(bool keyboard)
    {
        using var recorder = MenuCaptureFixture.Create();

        recorder.Start();
        recorder.Invoke(recorder.NestedLeaf, keyboard);

        using (Assert.Multiple())
        {
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(recorder.OnlyStep.ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(recorder.OnlyStep.Preview)
                .Contains("Page.InvokeMenuItem(static page => page.MainMenu, new[] { \"Actions\", \"Export\", \"Snapshot\" });");
            await Assert.That(recorder.OnlyStep.Preview).DoesNotContain("ClickButton");
        }
    }

    [Test]
    public async Task ParentOpenWithoutLeafInvocation_CreatesNoStep()
    {
        using var recorder = MenuCaptureFixture.Create();

        recorder.Start();
        recorder.Invoke(recorder.Parent);

        await Assert.That(recorder.Session.StepJournal).IsEmpty();
    }

    [Test]
    public async Task DuplicateSiblingCaption_ReturnsActionableAmbiguity()
    {
        using var recorder = MenuCaptureFixture.Create(duplicateNestedLeaf: true);

        var result = new RecorderStepFactory(new AppAutomationRecorderOptions())
            .TryCreateMenuItemStep(recorder.NestedLeaf);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Message).Contains("ambiguous among siblings");
        }
    }
}
