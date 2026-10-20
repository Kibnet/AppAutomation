using Avalonia.Controls;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderPointerGestureTrackerTests
{
    [Test]
    public async Task ButtonIsCapturedOncePerGesture_ButCanBeCapturedInNextGesture()
    {
        var tracker = new RecorderPointerGestureTracker();
        var button = new Button();

        tracker.Begin();
        await Assert.That(tracker.TryMarkButton(button)).IsTrue();
        await Assert.That(tracker.TryMarkButton(button)).IsFalse();

        tracker.Begin();
        await Assert.That(tracker.TryMarkButton(button)).IsTrue();
        tracker.Reset();
        await Assert.That(tracker.TryMarkButton(button)).IsTrue();
    }

    [Test]
    public async Task GridSelectionIsConsumedOnlyByItsOwnActiveGesture()
    {
        var tracker = new RecorderPointerGestureTracker();
        var first = Guid.NewGuid();
        tracker.Begin();
        tracker.RememberGridRowSelection(first);

        await Assert.That(tracker.TryConsumeLinkedGridRowSelection(out var selected)).IsTrue();
        await Assert.That(selected).IsEqualTo(first);
        await Assert.That(tracker.TryConsumeLinkedGridRowSelection(out _)).IsFalse();

        tracker.RememberGridRowSelection(first);
        tracker.Begin();
        await Assert.That(tracker.TryConsumeLinkedGridRowSelection(out _)).IsFalse();
    }
}
