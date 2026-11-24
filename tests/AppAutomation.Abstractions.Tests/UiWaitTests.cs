using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class UiWaitTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancelledWaitDoesNotReadTheSource(bool asynchronous)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var reads = 0;

        await Assert.That(async () => await Poll(asynchronous, () => ++reads, static _ => true,
            new UiWaitOptions(), cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(reads).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationDuringACallbackCannotBecomeSuccess(bool asynchronous)
    {
        foreach (var cancelInCondition in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            var conditions = 0;
            await Assert.That(async () => await Poll(asynchronous,
                () => { if (!cancelInCondition) cancellation.Cancel(); return true; },
                value => { conditions++; cancellation.Cancel(); return value; },
                new UiWaitOptions(), cancellation.Token)).Throws<OperationCanceledException>();
            await Assert.That(conditions).IsEqualTo(cancelInCondition ? 1 : 0);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AReadFinishingAfterTheDeadlineCannotBecomeSuccess(bool asynchronous)
    {
        var reads = 0;
        var conditions = 0;
        var result = await Poll(asynchronous, () =>
        {
            reads++;
            Thread.Sleep(TimeSpan.FromMilliseconds(200));
            return true;
        }, value => { conditions++; return value; }, new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(100) });

        await Assert.That(result.Success).IsFalse();
        await Assert.That(reads).IsEqualTo(1);
        await Assert.That(conditions).IsEqualTo(0);
    }

    [Test]
    public async Task PollIntervalLongerThanTheTimeoutDoesNotStartALateRead()
    {
        var timeout = TimeSpan.FromMilliseconds(250);
        var provider = new RecordingTimeProvider(timeout + TimeSpan.FromMilliseconds(100));
        var options = new UiWaitOptions
        {
            Timeout = timeout,
            PollInterval = TimeSpan.FromSeconds(2),
            TimeProvider = provider
        };
        var reads = 0;
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await UiWait.TryUntilAsync(() => ++reads, static _ => false, options, watchdog.Token)
            .WaitAsync(watchdog.Token);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(reads).IsEqualTo(1);
        await Assert.That(provider.Delays.Count).IsEqualTo(1);
        await Assert.That(provider.Delays[0]).IsLessThanOrEqualTo(timeout);
    }

    private static Task<UiWaitResult<T>> Poll<T>(bool asynchronous, Func<T> read, Predicate<T> condition,
        UiWaitOptions options, CancellationToken cancellationToken = default) => asynchronous
        ? UiWait.TryUntilAsync(read, condition, options, cancellationToken)
        : Task.FromResult(UiWait.TryUntil(read, condition, options, cancellationToken));

    private sealed class RecordingTimeProvider(TimeSpan earliestCallback) : TimeProvider
    {
        public List<TimeSpan> Delays { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delays.Add(dueTime);
            // Deliver the callback after the real deadline, even when the requested delay was clamped.
            return TimeProvider.System.CreateTimer(callback, state,
                dueTime < earliestCallback ? earliestCallback : dueTime, period);
        }
    }
}
