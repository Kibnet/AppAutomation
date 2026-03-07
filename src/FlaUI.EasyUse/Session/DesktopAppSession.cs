using System.Diagnostics;
using EasyUse.Session.Contracts;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace FlaUI.EasyUse.Session;

public sealed class DesktopAppSession : IDisposable
{
    private readonly Application _application;
    private readonly UIA3Automation _automation;
    private bool _disposed;

    private DesktopAppSession(Application application, UIA3Automation automation, Window mainWindow, ConditionFactory conditionFactory)
    {
        _application = application;
        _automation = automation;
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

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = true
        };

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
            return new DesktopAppSession(application, automation, mainWindowResult.Result, conditionFactory);
        }
        catch
        {
            automation.Dispose();
            TryTerminateApplication(application);
            application.Dispose();
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
        _automation.Dispose();
        TryTerminateApplication(_application);
        _application.Dispose();
        GC.SuppressFinalize(this);
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
}
