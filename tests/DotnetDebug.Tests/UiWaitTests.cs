using EasyUse.TUnit.Core.Waiting;
using TUnit.Assertions;
using TUnit.Core;

public class UiWaitTests
{
    [Test]
    public async Task Until_ThrowsTimeout_WhenConditionNeverMet()
    {
        await Assert.That(() => UiWait.Until(
            () => 1,
            value => value == 2,
            new UiWaitOptions
            {
                Timeout = TimeSpan.FromMilliseconds(120),
                PollInterval = TimeSpan.FromMilliseconds(20)
            }))
            .Throws<TimeoutException>();
    }

    [Test]
    public async Task TryUntil_PollsUntilTimeout_WhenConditionNeverMet()
    {
        var calls = 0;

        var result = UiWait.TryUntil(
            () =>
            {
                calls++;
                return calls;
            },
            _ => false,
            new UiWaitOptions
            {
                Timeout = TimeSpan.FromMilliseconds(140),
                PollInterval = TimeSpan.FromMilliseconds(20)
            });

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsEqualTo(false);
            await Assert.That(calls >= 2).IsEqualTo(true);
        }
    }

    [Test]
    public async Task TryUntil_ThrowsWhenCancellationRequested()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(() => UiWait.TryUntil(
            () => 1,
            _ => false,
            new UiWaitOptions
            {
                Timeout = TimeSpan.FromSeconds(1),
                PollInterval = TimeSpan.FromMilliseconds(20)
            },
            cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task TryUntilAsync_ThrowsWhenCancellationRequested()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () => await UiWait.TryUntilAsync(
            () => 1,
            _ => false,
            new UiWaitOptions
            {
                Timeout = TimeSpan.FromSeconds(1),
                PollInterval = TimeSpan.FromMilliseconds(20)
            },
            cts.Token))
            .Throws<OperationCanceledException>();
    }
}
