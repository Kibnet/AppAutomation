using EasyUse.TUnit.Core.Waiting;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace EasyUse.TUnit.Core;

public static class UiAssert
{
    private static readonly UiWaitOptions DefaultWaitOptions = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
        PollInterval = TimeSpan.FromMilliseconds(100)
    };

    public static async Task TextEqualsAsync(
        Func<string> actualFactory,
        string expected,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var actual = await UiWait.UntilAsync(
            actualFactory,
            value => string.Equals(value, expected, StringComparison.Ordinal),
            ResolveOptions(timeout),
            timeoutMessage: $"Text did not become '{expected}'.",
            cancellationToken: cancellationToken);

        await Assert.That(actual).IsEqualTo(expected);
    }

    public static async Task TextContainsAsync(
        Func<string> actualFactory,
        string expectedPart,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var actual = await UiWait.UntilAsync(
            actualFactory,
            value => value.Contains(expectedPart, StringComparison.Ordinal),
            ResolveOptions(timeout),
            timeoutMessage: $"Text did not contain '{expectedPart}'.",
            cancellationToken: cancellationToken);

        await Assert.That(actual.Contains(expectedPart, StringComparison.Ordinal)).IsEqualTo(true);
    }

    public static async Task NumberAtLeastAsync(
        Func<int> actualFactory,
        int expectedMin,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var actual = await UiWait.UntilAsync(
            actualFactory,
            value => value >= expectedMin,
            ResolveOptions(timeout),
            timeoutMessage: $"Number did not reach {expectedMin}.",
            cancellationToken: cancellationToken);

        await Assert.That(actual >= expectedMin).IsEqualTo(true);
    }

    private static UiWaitOptions ResolveOptions(TimeSpan? timeout)
    {
        return timeout is null
            ? DefaultWaitOptions
            : DefaultWaitOptions with { Timeout = timeout.Value };
    }
}
