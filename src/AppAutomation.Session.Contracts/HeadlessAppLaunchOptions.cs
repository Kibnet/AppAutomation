namespace AppAutomation.Session.Contracts;

/// <summary>
/// Configuration options for launching an Avalonia application in headless mode for UI testing.
/// </summary>
/// <remarks>
/// <para>
/// Headless mode runs the application without a visible window, which is faster and works in
/// CI/CD environments that don't have a display. Use this for unit-style UI tests.
/// </para>
/// <para>
/// Provide either <see cref="CreateMainWindow"/> for synchronous window creation or
/// <see cref="CreateMainWindowAsync"/> for asynchronous creation. If both are provided,
/// the async version takes precedence.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var options = new HeadlessAppLaunchOptions
/// {
///     CreateMainWindow = () => new MainWindow(),
///     BeforeLaunchAsync = async (ct) =>
///     {
///         await InitializeServicesAsync(ct);
///     }
/// };
/// </code>
/// </example>
public sealed class HeadlessAppLaunchOptions
{
    /// <summary>
    /// Gets a factory function that creates the main window synchronously.
    /// </summary>
    /// <value>A function returning the main window instance, or <see langword="null"/> if using async creation.</value>
    public Func<object>? CreateMainWindow { get; init; }

    /// <summary>
    /// Gets a callback to execute before the application launches.
    /// </summary>
    /// <value>An async function for pre-launch setup, or <see langword="null"/> if not needed.</value>
    /// <remarks>Use this to initialize services, load configuration, or perform other setup tasks.</remarks>
    public Func<CancellationToken, ValueTask>? BeforeLaunchAsync { get; init; }

    /// <summary>
    /// Gets a callback invoked when the launched session is disposed or launch fails before the session is created.
    /// </summary>
    public Action? DisposeCallback { get; init; }

    /// <summary>
    /// Gets a factory function that creates the main window asynchronously.
    /// </summary>
    /// <value>An async function returning the main window instance, or <see langword="null"/> if using sync creation.</value>
    /// <remarks>If both sync and async factories are provided, the async factory takes precedence.</remarks>
    public Func<CancellationToken, ValueTask<object>>? CreateMainWindowAsync { get; init; }
}
