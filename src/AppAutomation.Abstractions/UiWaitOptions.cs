namespace AppAutomation.Abstractions;

/// <summary>
/// Configuration options for <see cref="UiWait"/> polling operations.
/// </summary>
/// <remarks>
/// Use this record to customize timeout and poll interval for wait operations.
/// The <see cref="Default"/> instance provides sensible defaults for most scenarios.
/// </remarks>
public sealed record UiWaitOptions
{
    /// <summary>
    /// Gets the maximum duration to wait for a condition to be met.
    /// </summary>
    /// <value>The timeout duration. Defaults to 5 seconds.</value>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the interval between condition checks.
    /// </summary>
    /// <value>The poll interval. Defaults to 100 milliseconds.</value>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets the time provider used for async delay operations.
    /// </summary>
    /// <value>The <see cref="System.TimeProvider"/>. Defaults to <see cref="TimeProvider.System"/>.</value>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Gets the default wait options with standard timeout and poll interval.
    /// </summary>
    /// <value>A shared <see cref="UiWaitOptions"/> instance with default values.</value>
    public static UiWaitOptions Default { get; } = new();
}
