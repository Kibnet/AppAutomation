namespace AppAutomation.Session.Contracts;

/// <summary>
/// Configuration options for launching a desktop application for UI testing.
/// </summary>
/// <remarks>
/// <para>
/// Use this class to configure how a desktop application should be started.
/// The launcher will wait for the main window to appear before returning.
/// </para>
/// <para>
/// For Avalonia applications running in headless mode, use <see cref="HeadlessAppLaunchOptions"/> instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var options = new DesktopAppLaunchOptions
/// {
///     ExecutablePath = @"C:\MyApp\MyApp.exe",
///     Arguments = ["--config", "test"],
///     WorkingDirectory = @"C:\MyApp",
///     MainWindowTimeout = TimeSpan.FromSeconds(30)
/// };
/// </code>
/// </example>
public sealed class DesktopAppLaunchOptions
{
    /// <summary>
    /// Gets the path to the executable file to launch.
    /// </summary>
    /// <value>The absolute path to the application executable.</value>
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// Gets the working directory for the launched process.
    /// </summary>
    /// <value>The working directory path, or <see langword="null"/> to use the executable's directory.</value>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets the command-line arguments to pass to the application.
    /// </summary>
    /// <value>A read-only list of arguments. Defaults to an empty list.</value>
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets environment variables to set for the launched process.
    /// </summary>
    /// <value>A dictionary of environment variable names and values. Defaults to empty.</value>
    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>
    /// Gets a callback invoked when the launched session is disposed or launch fails before the session is created.
    /// </summary>
    public Action? DisposeCallback { get; init; }

    /// <summary>
    /// Gets the maximum time to wait for the main window to appear.
    /// </summary>
    /// <value>The timeout duration. Defaults to 20 seconds.</value>
    public TimeSpan MainWindowTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Gets the interval between checks for the main window.
    /// </summary>
    /// <value>The poll interval. Defaults to 200 milliseconds.</value>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets the optional window placement to apply after the main window is discovered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Placement is interpreted by desktop runtime adapters. The FlaUI adapter uses Windows desktop pixels
    /// and applies the final rectangle before the launched session is returned to test code.
    /// </para>
    /// <para>
    /// A <see langword="null"/> value preserves the adapter's existing launch behavior.
    /// </para>
    /// </remarks>
    public DesktopWindowPlacement? WindowPlacement { get; init; }
}
