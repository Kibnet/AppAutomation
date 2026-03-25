namespace AppAutomation.Abstractions;

/// <summary>
/// Represents the result of a non-throwing <see cref="UiWait"/> operation.
/// </summary>
/// <typeparam name="T">The type of value that was polled.</typeparam>
/// <param name="Success">Indicates whether the condition was met before timeout.</param>
/// <param name="Value">The last value observed from the value factory.</param>
/// <param name="Elapsed">The total time spent waiting.</param>
public readonly record struct UiWaitResult<T>(bool Success, T Value, TimeSpan Elapsed);
