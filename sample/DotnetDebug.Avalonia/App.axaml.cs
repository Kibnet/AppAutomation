using AppAutomation.Recorder.Avalonia;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.IO;

namespace DotnetDebug.Avalonia;

public partial class App : Application
{
    private const string RecorderEnabledEnvironmentVariable = "APPAUTOMATION_RECORDER";
    private const string RecorderScenarioEnvironmentVariable = "APPAUTOMATION_RECORDER_SCENARIO";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;

#if DEBUG
            AttachRecorderIfRequested(mainWindow);
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }

#if DEBUG
    private static void AttachRecorderIfRequested(MainWindow mainWindow)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RecorderEnabledEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        var scenarioName = Environment.GetEnvironmentVariable(RecorderScenarioEnvironmentVariable);
        var options = new AppAutomationRecorderOptions
        {
            ScenarioName = string.IsNullOrWhiteSpace(scenarioName) ? "RecordedSmoke" : scenarioName,
            AuthoringProjectDirectory = ResolveAuthoringProjectDirectory(),
            OutputSubdirectory = "Recorded",
            PageNamespace = "DotnetDebug.AppAutomation.Authoring.Pages",
            PageClassName = "MainWindowPage",
            ScenarioNamespace = "DotnetDebug.AppAutomation.Authoring.Tests.UIAutomationTests",
            ScenarioClassName = "MainWindowScenariosBase",
            OverlayTheme = RecorderOverlayTheme.Dark,
            ShowOverlay = true,
            AllowNameLocators = false
        };
        options.ControlHints.Add(new RecorderControlHint("MixCountSpinner", RecorderActionHint.SpinnerTextBox));

        var session = AppAutomationRecorder.Attach(mainWindow, options);
        session.Start();
    }

    private static string ResolveAuthoringProjectDirectory()
    {
        var fallbackPath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "DotnetDebug.AppAutomation.Authoring"));

        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            var solutionPath = Path.Combine(current.FullName, "AppAutomation.sln");
            var authoringPath = Path.Combine(current.FullName, "sample", "DotnetDebug.AppAutomation.Authoring");
            if (File.Exists(solutionPath) && Directory.Exists(authoringPath))
            {
                return authoringPath;
            }
        }

        return fallbackPath;
    }
#endif
}
