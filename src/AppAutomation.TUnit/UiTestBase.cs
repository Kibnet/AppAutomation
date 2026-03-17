using AppAutomation.Abstractions;
using TUnit.Core;

namespace AppAutomation.TUnit;

/// <summary>
/// Base class for UI automation tests using TUnit framework.
/// </summary>
/// <typeparam name="TSession">The session type that manages the application under test.</typeparam>
/// <typeparam name="TPage">The page object type representing the UI being tested.</typeparam>
/// <remarks>
/// <para>
/// This class provides automatic session lifecycle management. The session is launched before each test
/// and disposed after each test completes. Override <see cref="LaunchSession"/> to configure how the
/// application is started, and <see cref="CreatePage"/> to create the page object.
/// </para>
/// <para>
/// The class also provides helper methods for polling-based waits, which are essential for
/// UI automation where state changes may not be immediate.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class LoginTests : UiTestBase&lt;DesktopSession, LoginPage&gt;
/// {
///     protected override DesktopSession LaunchSession()
///         => DesktopSession.Launch(new DesktopAppLaunchOptions { ExecutablePath = "MyApp.exe" });
///     
///     protected override LoginPage CreatePage(DesktopSession session)
///         => new LoginPage(session.Resolver);
///     
///     [Test]
///     public void UserCanLogin()
///     {
///         Page.EnterText(p => p.UserName, "testuser")
///             .EnterText(p => p.Password, "secret")
///             .ClickButton(p => p.LoginButton);
///         
///         WaitUntil(() => Page.WelcomeLabel.Name.Contains("Welcome"));
///     }
/// }
/// </code>
/// </example>
public abstract class UiTestBase<TSession, TPage>
    where TSession : class, IUiTestSession
    where TPage : class
{
    /// <summary>
    /// Test constraint identifier for desktop UI tests.
    /// </summary>
    /// <remarks>
    /// Use this constant with TUnit's constraint attributes to mark tests that require desktop UI capabilities.
    /// </remarks>
    protected const string DesktopUiConstraint = "DesktopUi";

    private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

    private TSession? _session;
    private TPage? _page;

    /// <summary>
    /// Gets the current UI test session.
    /// </summary>
    /// <value>The active session instance.</value>
    /// <exception cref="InvalidOperationException">Thrown when accessed before the session is initialized.</exception>
    protected TSession Session =>
        _session ?? throw new InvalidOperationException("UI test session is not initialized.");

    /// <summary>
    /// Gets the current page object.
    /// </summary>
    /// <value>The active page instance.</value>
    /// <exception cref="InvalidOperationException">Thrown when accessed before the page is created.</exception>
    protected TPage Page =>
        _page ?? throw new InvalidOperationException("Page is not initialized.");

    /// <summary>
    /// Launches the UI test session. Override this method to configure application startup.
    /// </summary>
    /// <returns>The launched session instance.</returns>
    protected abstract TSession LaunchSession();

    /// <summary>
    /// Creates the page object for the test. Override this method to create your page object.
    /// </summary>
    /// <param name="session">The active session to use for control resolution.</param>
    /// <returns>The created page object instance.</returns>
    protected abstract TPage CreatePage(TSession session);

    /// <summary>
    /// Waits until a boolean condition becomes true.
    /// </summary>
    /// <param name="condition">The condition to wait for.</param>
    /// <param name="timeout">Maximum time to wait. Defaults to 5 seconds.</param>
    /// <param name="pollInterval">Interval between condition checks. Defaults to 100ms.</param>
    /// <param name="timeoutMessage">Custom message for timeout exception.</param>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    /// <exception cref="TimeoutException">Thrown when the condition is not met within the timeout.</exception>
    protected static void WaitUntil(
        Func<bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        _ = WaitUntil(
            () => condition(),
            static success => success,
            timeout,
            pollInterval,
            timeoutMessage,
            cancellationToken);
    }

    /// <summary>
    /// Waits until a value satisfies a condition.
    /// </summary>
    /// <typeparam name="T">The type of value being waited for.</typeparam>
    /// <param name="valueFactory">A function that produces the current value.</param>
    /// <param name="condition">A predicate that determines when the wait should complete.</param>
    /// <param name="timeout">Maximum time to wait. Defaults to 5 seconds.</param>
    /// <param name="pollInterval">Interval between value checks. Defaults to 100ms.</param>
    /// <param name="timeoutMessage">Custom message for timeout exception.</param>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    /// <returns>The value that satisfied the condition.</returns>
    /// <exception cref="TimeoutException">Thrown when the condition is not met within the timeout.</exception>
    protected static T WaitUntil<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        return UiWait.Until(
            valueFactory,
            condition,
            CreateWaitOptions(timeout, pollInterval),
            timeoutMessage,
            cancellationToken);
    }

    /// <summary>
    /// Asynchronously waits until a value satisfies a condition.
    /// </summary>
    /// <typeparam name="T">The type of value being waited for.</typeparam>
    /// <param name="valueFactory">A function that produces the current value.</param>
    /// <param name="condition">A predicate that determines when the wait should complete.</param>
    /// <param name="timeout">Maximum time to wait. Defaults to 5 seconds.</param>
    /// <param name="pollInterval">Interval between value checks. Defaults to 100ms.</param>
    /// <param name="timeoutMessage">Custom message for timeout exception.</param>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    /// <returns>A task that resolves to the value that satisfied the condition.</returns>
    /// <exception cref="TimeoutException">Thrown when the condition is not met within the timeout.</exception>
    protected static async Task<T> WaitUntilAsync<T>(
        Func<T> valueFactory,
        Predicate<T> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        return await UiWait.UntilAsync(
            valueFactory,
            condition,
            CreateWaitOptions(timeout, pollInterval),
            timeoutMessage,
            cancellationToken);
    }

    /// <summary>
    /// Retries an action until it returns true.
    /// </summary>
    /// <param name="attempt">A function that performs the action and returns success status.</param>
    /// <param name="timeout">Maximum time to retry. Defaults to 5 seconds.</param>
    /// <param name="pollInterval">Interval between retry attempts. Defaults to 100ms.</param>
    /// <param name="timeoutMessage">Custom message for timeout exception.</param>
    /// <param name="cancellationToken">Token to cancel the retry loop.</param>
    /// <exception cref="TimeoutException">Thrown when the action does not succeed within the timeout.</exception>
    protected static void RetryUntil(
        Func<bool> attempt,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? timeoutMessage = null,
        CancellationToken cancellationToken = default)
    {
        _ = WaitUntil(
            attempt,
            static success => success,
            timeout,
            pollInterval,
            timeoutMessage,
            cancellationToken);
    }

    /// <summary>
    /// Sets up the UI session before each test. Called automatically by TUnit.
    /// </summary>
    [Before(Test)]
    public void SetupUiSession()
    {
        _session = LaunchSession();
        _page = CreatePage(_session);
    }

    /// <summary>
    /// Cleans up the UI session after each test. Called automatically by TUnit.
    /// </summary>
    [After(Test)]
    public void CleanupUiSession()
    {
        _session?.Dispose();
        _session = null;
        _page = null;
    }

    private static UiWaitOptions CreateWaitOptions(TimeSpan? timeout, TimeSpan? pollInterval)
    {
        return new UiWaitOptions
        {
            Timeout = timeout ?? DefaultWaitTimeout,
            PollInterval = pollInterval ?? DefaultPollInterval
        };
    }
}
