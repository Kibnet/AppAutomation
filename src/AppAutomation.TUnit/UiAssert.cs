using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace AppAutomation.TUnit;

/// <summary>
/// Provides UI-aware assertion methods that wait for conditions before asserting.
/// </summary>
/// <remarks>
/// <para>
/// These assertions combine polling-based waiting with TUnit assertions. They wait for
/// a value to reach the expected state before performing the assertion, making tests
/// more resilient to timing issues in UI automation.
/// </para>
/// <para>
/// If the expected condition is not met within the timeout, a <see cref="TimeoutException"/>
/// is thrown before the assertion is evaluated.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Wait for text to equal expected value, then assert
/// await UiAssert.TextEqualsAsync(() => statusLabel.Text, "Success");
/// 
/// // Wait for text to contain expected substring
/// await UiAssert.TextContainsAsync(() => messageLabel.Text, "completed");
/// 
/// // Wait for count to reach minimum
/// await UiAssert.NumberAtLeastAsync(() => listBox.Items.Count, 5);
/// </code>
/// </example>
public static class UiAssert
{
    private static readonly UiWaitOptions DefaultWaitOptions = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
        PollInterval = TimeSpan.FromMilliseconds(100)
    };

    /// <summary>
    /// Waits for a text value to equal the expected value, then asserts equality.
    /// </summary>
    /// <param name="actualFactory">A function that produces the current text value.</param>
    /// <param name="expected">The expected text value.</param>
    /// <param name="timeout">Maximum time to wait. Defaults to 5 seconds.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous assertion.</returns>
    /// <exception cref="TimeoutException">Thrown when the text does not equal the expected value within the timeout.</exception>
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

    /// <summary>
    /// Waits for a text value to contain the expected substring, then asserts containment.
    /// </summary>
    /// <param name="actualFactory">A function that produces the current text value.</param>
    /// <param name="expectedPart">The expected substring.</param>
    /// <param name="timeout">Maximum time to wait. Defaults to 5 seconds.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous assertion.</returns>
    /// <exception cref="TimeoutException">Thrown when the text does not contain the expected substring within the timeout.</exception>
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

    /// <summary>
    /// Waits for a numeric value to reach at least the specified minimum, then asserts.
    /// </summary>
    /// <param name="actualFactory">A function that produces the current numeric value.</param>
    /// <param name="expectedMin">The minimum expected value.</param>
    /// <param name="timeout">Maximum time to wait. Defaults to 5 seconds.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous assertion.</returns>
    /// <exception cref="TimeoutException">Thrown when the number does not reach the minimum within the timeout.</exception>
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
