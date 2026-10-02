namespace AppAutomation.Abstractions;

/// <summary>
/// Identifies a button used by an explicit pointer gesture.
/// </summary>
public enum PointerButton
{
    /// <summary>The primary, left mouse button.</summary>
    Left,

    /// <summary>The secondary, right mouse button.</summary>
    Right,

    /// <summary>The middle mouse button.</summary>
    Middle
}

/// <summary>
/// Options for a physical click that restores the cursor before completing.
/// </summary>
public sealed record PointerClickOptions
{
    private TimeSpan _timeout = TimeSpan.FromSeconds(5);
    private PointerButton _button;
    private int _clickCount = 1;

    /// <summary>Maximum time for queueing, target lookup and the gesture, excluding bounded cleanup.</summary>
    public TimeSpan Timeout
    {
        get => _timeout;
        init => _timeout = PointerOptionValidation.PositiveDuration(value, nameof(Timeout));
    }

    /// <summary>The button to click. The default is <see cref="PointerButton.Left"/>.</summary>
    public PointerButton Button
    {
        get => _button;
        init => _button = PointerOptionValidation.Button(value);
    }

    /// <summary>One for a single click or two for one double-click gesture.</summary>
    public int ClickCount
    {
        get => _clickCount;
        init
        {
            if (value is not (1 or 2))
            {
                throw new ArgumentOutOfRangeException(nameof(ClickCount), value, "ClickCount must be one or two.");
            }

            _clickCount = value;
        }
    }
}

/// <summary>
/// Options for a hover whose verification runs before the original cursor position is restored.
/// </summary>
public sealed record PointerHoverOptions
{
    private TimeSpan _timeout = TimeSpan.FromSeconds(5);

    /// <summary>Maximum time for queueing, target lookup and verification, excluding bounded cleanup.</summary>
    public TimeSpan Timeout
    {
        get => _timeout;
        init => _timeout = PointerOptionValidation.PositiveDuration(value, nameof(Timeout));
    }
}

/// <summary>
/// Options for a physical drag-and-drop that releases its button and restores the cursor.
/// </summary>
public sealed record PointerDragOptions
{
    private TimeSpan _timeout = TimeSpan.FromSeconds(5);
    private TimeSpan _duration = TimeSpan.FromMilliseconds(250);
    private PointerButton _button;

    /// <summary>Maximum time for queueing, target lookup and the gesture, excluding bounded cleanup.</summary>
    public TimeSpan Timeout
    {
        get => _timeout;
        init => _timeout = PointerOptionValidation.PositiveDuration(value, nameof(Timeout));
    }

    /// <summary>The button held during the drag. The default is <see cref="PointerButton.Left"/>.</summary>
    public PointerButton Button
    {
        get => _button;
        init => _button = PointerOptionValidation.Button(value);
    }

    /// <summary>
    /// Time spent moving from source to target. It must fit within the remaining operation timeout.
    /// </summary>
    public TimeSpan Duration
    {
        get => _duration;
        init => _duration = PointerOptionValidation.PositiveDuration(value, nameof(Duration));
    }
}

/// <summary>
/// Provides optional physical pointer gestures with exclusive desktop ownership and cursor restoration.
/// </summary>
/// <remarks>
/// Targets are resolved from their definitions after acquiring desktop ownership. Every operation
/// saves the current cursor position and completes bounded cleanup before its task completes,
/// including when the gesture fails or is cancelled. Cleanup failures must not be reported as success.
/// This capability is not provided by runtimes that cannot safely operate the physical desktop.
/// </remarks>
public interface IUiPointerRuntime
{
    /// <summary>Clicks the target and restores the previous cursor position.</summary>
    Task PointerClickAsync(
        UiControlDefinition target,
        PointerClickOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Moves directly to the target, verifies it while hovered, then restores the cursor.</summary>
    /// <remarks>
    /// Verification is called once and may only read or assert UI state. It must return a task
    /// promptly, observe its cancellation token and not start pointer operations, UI mutations or
    /// fire-and-forget work. The runtime can stop waiting for an incomplete task at its deadline;
    /// it cannot interrupt synchronous user code or a blocked native call. Disabled targets may
    /// be hovered. An already-hovered target need not receive an artificial leave and re-entry.
    /// </remarks>
    Task HoverAsync(
        UiControlDefinition target,
        Func<CancellationToken, Task> verifyWhileHovered,
        PointerHoverOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Drags from source to target, releases its button and restores the previous cursor position.</summary>
    Task DragAndDropAsync(
        UiControlDefinition source,
        UiControlDefinition target,
        PointerDragOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Explicit pointer gestures for page objects whose runtime supports physical pointer input.
/// </summary>
public static class PointerPageExtensions
{
    /// <summary>Clicks the target and returns the page after the cursor has been restored.</summary>
    public static async Task<TPage> PointerClickAsync<TPage>(
        this TPage page,
        UiControlDefinition target,
        PointerClickOptions? options = null,
        CancellationToken cancellationToken = default)
        where TPage : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(target);
        var runtime = GetRuntime(page);
        cancellationToken.ThrowIfCancellationRequested();

        await runtime.PointerClickAsync(target, options, cancellationToken).ConfigureAwait(false);
        return page;
    }

    /// <summary>Verifies the target while hovered and returns the page after cursor restoration.</summary>
    /// <remarks>
    /// The callback must return promptly, use its cancellation token and perform only reads and
    /// assertions. Nested pointer gestures and fire-and-forget work are prohibited. A disabled
    /// target or a tooltip that is already open may be verified without an artificial mouse move.
    /// </remarks>
    public static async Task<TPage> HoverAsync<TPage>(
        this TPage page,
        UiControlDefinition target,
        Func<CancellationToken, Task> verifyWhileHovered,
        PointerHoverOptions? options = null,
        CancellationToken cancellationToken = default)
        where TPage : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(verifyWhileHovered);
        var runtime = GetRuntime(page);
        cancellationToken.ThrowIfCancellationRequested();

        await runtime.HoverAsync(target, verifyWhileHovered, options, cancellationToken).ConfigureAwait(false);
        return page;
    }

    /// <summary>Drags source to target and returns the page after button release and cursor restoration.</summary>
    public static async Task<TPage> DragAndDropAsync<TPage>(
        this TPage page,
        UiControlDefinition source,
        UiControlDefinition target,
        PointerDragOptions? options = null,
        CancellationToken cancellationToken = default)
        where TPage : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var runtime = GetRuntime(page);
        if (options is not null && options.Duration > options.Timeout)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Drag duration must fit within the operation timeout.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await runtime.DragAndDropAsync(source, target, options, cancellationToken).ConfigureAwait(false);
        return page;
    }

    private static IUiPointerRuntime GetRuntime(UiPage page)
    {
        if (!page.Capabilities.SupportsPointerInput || page.ResolverInternal is not IUiPointerRuntime runtime)
        {
            throw new NotSupportedException(
                $"Runtime adapter '{page.Capabilities.AdapterId}' does not support physical pointer input.");
        }

        return runtime;
    }
}

internal static class PointerOptionValidation
{
    public static TimeSpan PositiveDuration(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The duration must be positive and finite.");
        }

        return value;
    }

    public static PointerButton Button(PointerButton value)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The pointer button is not supported.");
        }

        return value;
    }
}
