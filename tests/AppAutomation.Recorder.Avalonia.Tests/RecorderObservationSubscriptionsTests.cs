using Avalonia.Controls;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderObservationSubscriptionsTests
{
    [Test]
    public async Task RebindInputRoot_DetachesPreviousRootBeforeAttachingNext()
    {
        var first = new Button();
        var second = new Button();
        var events = new List<string>();
        using var observer = new RecorderObservationSubscriptions(
            _ => static () => { },
            (_, _) => { },
            (_, _) => { },
            root => events.Add(ReferenceEquals(root, first) ? "attach-first" : "attach-second"),
            root => events.Add(ReferenceEquals(root, first) ? "detach-first" : "detach-second"),
            () => events.Add("reset"));

        observer.RebindInputRoot(first);
        observer.RebindInputRoot(first);
        observer.RebindInputRoot(second);
        observer.Dispose();

        await Assert.That(string.Join(",", events)).IsEqualTo(
            "reset,attach-first,detach-first,reset,attach-second,detach-second,reset");
    }

    [Test]
    public async Task Reconcile_SubscribeOnceAndDetachWhenControlDisappears()
    {
        var control = new TextBox();
        var subscriptions = 0;
        var detachments = 0;
        using var observer = new RecorderObservationSubscriptions(
            _ =>
            {
                subscriptions++;
                return () => detachments++;
            },
            (_, _) => { },
            (_, _) => { },
            _ => { },
            _ => { },
            () => { });

        observer.Reconcile([control]);
        observer.Reconcile([control]);
        await Assert.That(subscriptions).IsEqualTo(1);
        await Assert.That(detachments).IsEqualTo(0);

        observer.Reconcile(Array.Empty<Control>());
        await Assert.That(detachments).IsEqualTo(1);

        observer.Reconcile([control]);
        await Assert.That(subscriptions).IsEqualTo(2);
        observer.Dispose();
        await Assert.That(detachments).IsEqualTo(2);
    }

    [Test]
    public async Task EnsureObserved_ReconcileAndDisposeDoNotDuplicateSubscription()
    {
        var control = new TextBox();
        var subscriptions = 0;
        var detachments = 0;
        using var observer = new RecorderObservationSubscriptions(
            _ =>
            {
                subscriptions++;
                return () => detachments++;
            },
            (_, _) => { },
            (_, _) => { },
            _ => { },
            _ => { },
            () => { });

        observer.EnsureObserved(control);
        observer.Reconcile([control]);
        await Assert.That(observer.IsObserved(control)).IsTrue();
        await Assert.That(subscriptions).IsEqualTo(1);

        observer.Dispose();
        observer.Dispose();
        await Assert.That(detachments).IsEqualTo(1);
    }
}
