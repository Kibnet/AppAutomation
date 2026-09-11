using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

[NotInParallel("AvaloniaMenuCapture")]
public sealed class RecorderMenuCaptureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DirectItem_RecordsOneSemanticInvocation(bool keyboard)
    {
        using var recorder = MenuCaptureFixture.Create();

        recorder.Start();
        recorder.InvokeMenuItem(recorder.DirectLeaf, keyboard);

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
        recorder.InvokeMenuItem(recorder.NestedLeaf, keyboard);

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
        recorder.InvokeMenuItem(recorder.Parent);

        await Assert.That(recorder.Session.StepJournal).IsEmpty();
    }

    [Test]
    public async Task DuplicateSiblingCaption_ReturnsActionableAmbiguity()
    {
        using var recorder = MenuCaptureFixture.Create(duplicateMenuLeaf: true);

        var result = new RecorderStepFactory(new AppAutomationRecorderOptions())
            .TryCreateMenuItemStep(recorder.NestedLeaf);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Message).Contains("ambiguous among siblings");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ContextLeafInvocation_RecordsOneOwnerScopedAction(bool keyboard)
    {
        using var recorder = MenuCaptureFixture.Create();

        recorder.Start();
        recorder.InvokeContextMenuItem(recorder.PrimaryOwner, recorder.PrimaryContextLeaf, keyboard);

        using (Assert.Multiple())
        {
            await Assert.That(recorder.Session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(recorder.OnlyStep.CanPersist).IsTrue();
            await Assert.That(recorder.OnlyStep.ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
            await Assert.That(recorder.OnlyStep.Preview)
                .Contains("Page.InvokeContextMenuItem(static page => page.ItemSurface, new[] { \"Pin\" });");
            await Assert.That(recorder.OnlyStep.Preview).DoesNotContain("ClickButton");
            await Assert.That(recorder.OnlyStep.Preview).DoesNotContain("InvokeMenuItem");
        }
    }

    [Test]
    public async Task ContextNestedLeaf_RecordsExactVisibleCaptionPath()
    {
        using var recorder = MenuCaptureFixture.Create();

        recorder.Start();
        recorder.InvokeContextMenuItem(recorder.PrimaryOwner, recorder.NestedContextLeaf);

        await Assert.That(recorder.OnlyStep.Preview)
            .Contains("page.ItemSurface, new[] { \"Export\", \"Summary\" }");
    }

    [Test]
    public async Task ContextTwoOwners_UseTheOwnerThatOpenedTheMenu()
    {
        using var recorder = MenuCaptureFixture.Create();

        recorder.Start();
        recorder.InvokeContextMenuItem(recorder.SecondaryOwner, recorder.SecondaryContextLeaf);

        using (Assert.Multiple())
        {
            await Assert.That(recorder.OnlyStep.Preview).Contains("page.SecondarySurface");
            await Assert.That(recorder.OnlyStep.Preview).DoesNotContain("page.ItemSurface");
        }
    }

    [Test]
    public async Task ContextCloseWithoutLeafInvocation_CreatesNoStep()
    {
        using var recorder = MenuCaptureFixture.Create();

        recorder.Start();
        recorder.CancelContextMenu(recorder.PrimaryOwner);

        await Assert.That(recorder.Session.StepJournal).IsEmpty();
    }

    [Test]
    public async Task ContextDuplicateSiblingCaption_ReturnsActionableAmbiguity()
    {
        using var recorder = MenuCaptureFixture.Create(duplicateContextLeaf: true);

        var result = new RecorderStepFactory(new AppAutomationRecorderOptions())
            .TryCreateContextMenuItemStep(
                recorder.NestedContextLeaf,
                recorder.PrimaryOwner,
                out var belongsToOwner);

        using (Assert.Multiple())
        {
            await Assert.That(belongsToOwner).IsTrue();
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Message).Contains("ambiguous among siblings");
        }
    }
}
