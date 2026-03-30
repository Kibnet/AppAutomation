using System.Diagnostics;
using AppAutomation.Session.Contracts;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using System.Runtime.ExceptionServices;

namespace AppAutomation.FlaUI.Session;

public sealed class DesktopAppSession : IDisposable
{
    private readonly Application _application;
    private readonly UIA3Automation _automation;
    private readonly Action? _disposeCallback;
    private bool _disposed;

    private DesktopAppSession(
        Application application,
        UIA3Automation automation,
        Window mainWindow,
        ConditionFactory conditionFactory,
        Action? disposeCallback)
    {
        _application = application;
        _automation = automation;
        _disposeCallback = disposeCallback;
        MainWindow = mainWindow;
        ConditionFactory = conditionFactory;
    }

    public Window MainWindow { get; }

    public ConditionFactory ConditionFactory { get; }

    public static DesktopAppSession Launch(DesktopAppLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            throw new ArgumentException("ExecutablePath is required.", nameof(options));
        }

        if (options.MainWindowTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MainWindowTimeout must be greater than zero.");
        }

        if (options.PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "PollInterval must be greater than zero.");
        }

        var executablePath = Path.GetFullPath(options.ExecutablePath);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Desktop app executable was not found.", executablePath);
        }

        var workingDirectory = options.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = Path.GetDirectoryName(executablePath);
        }

        var startInfo = CreateProcessStartInfo(options, executablePath, workingDirectory);

        var application = Application.Launch(startInfo);
        var automation = new UIA3Automation();

        try
        {
            var mainWindowResult = Retry.WhileNull(
                () => application.GetMainWindow(automation),
                timeout: options.MainWindowTimeout,
                interval: options.PollInterval,
                throwOnTimeout: false);

            if (!mainWindowResult.Success || mainWindowResult.Result is null)
            {
                throw new TimeoutException("Main window was not found within timeout.");
            }

            WaitForAutomationTree(mainWindowResult.Result, options.MainWindowTimeout, options.PollInterval);

            var conditionFactory = new ConditionFactory(new UIA3PropertyLibrary());
            return new DesktopAppSession(application, automation, mainWindowResult.Result, conditionFactory, options.DisposeCallback);
        }
        catch (Exception launchException)
        {
            List<Exception>? cleanupExceptions = null;
            TryCleanup(automation.Dispose, cleanupExceptions ??= []);
            TryCleanup(() => TryTerminateApplication(application), cleanupExceptions ??= []);
            TryCleanup(application.Dispose, cleanupExceptions ??= []);
            TryCleanup(options.DisposeCallback, cleanupExceptions ??= []);
            AttachCleanupExceptions(launchException, cleanupExceptions);
            ExceptionDispatchInfo.Capture(launchException).Throw();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _automation.Dispose();
            TryTerminateApplication(_application);
            _application.Dispose();
        }
        finally
        {
            try
            {
                _disposeCallback?.Invoke();
            }
            finally
            {
                GC.SuppressFinalize(this);
            }
        }
    }

    internal static ProcessStartInfo CreateProcessStartInfo(
        DesktopAppLaunchOptions options,
        string executablePath,
        string? workingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (options.Arguments.Any(static argument => argument is null))
        {
            throw new ArgumentException("Arguments cannot contain null values.", nameof(options));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Path.GetDirectoryName(executablePath) ?? string.Empty
                : workingDirectory,
            UseShellExecute = false
        };

        foreach (var argument in options.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var environmentVariable in options.EnvironmentVariables)
        {
            if (string.IsNullOrWhiteSpace(environmentVariable.Key))
            {
                throw new ArgumentException("Environment variable names cannot be empty.", nameof(options));
            }

            if (environmentVariable.Value is null)
            {
                _ = startInfo.Environment.Remove(environmentVariable.Key);
            }
            else
            {
                startInfo.Environment[environmentVariable.Key] = environmentVariable.Value;
            }
        }

        return startInfo;
    }

    private static void WaitForAutomationTree(Window mainWindow, TimeSpan timeout, TimeSpan pollInterval)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                if (mainWindow.IsAvailable && mainWindow.FindAllDescendants().Length > 0)
                {
                    return;
                }
            }
            catch
            {
                // The automation tree can be transient while the window is initializing.
            }

            Thread.Sleep(pollInterval);
        }

        throw new TimeoutException("Main window automation tree was not ready within timeout.");
    }

    private static void TryTerminateApplication(Application application)
    {
        try
        {
            if (!application.HasExited)
            {
                application.Close();
            }
        }
        catch
        {
            // Best effort close.
        }

        try
        {
            if (!application.HasExited)
            {
                application.Kill();
            }
        }
        catch
        {
            // Best effort kill.
        }
    }

    private static void TryCleanup(Action? cleanupAction, List<Exception> exceptions)
    {
        if (cleanupAction is null)
        {
            return;
        }

        try
        {
            cleanupAction();
        }
        catch (Exception ex)
        {
            exceptions.Add(ex);
        }
    }

    private static void AttachCleanupExceptions(Exception launchException, List<Exception>? cleanupExceptions)
    {
        if (cleanupExceptions is null || cleanupExceptions.Count == 0)
        {
            return;
        }

        launchException.Data["AppAutomation.CleanupException"] = cleanupExceptions.Count == 1
            ? cleanupExceptions[0]
            : new AggregateException(cleanupExceptions);
    }
}
