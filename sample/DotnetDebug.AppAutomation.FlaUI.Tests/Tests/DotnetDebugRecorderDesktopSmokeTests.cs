using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using AppAutomation.TestHost.Avalonia;
using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using DotnetDebug.AppAutomation.TestHost;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

public sealed class DotnetDebugRecorderDesktopSmokeTests
{
    private const string DesktopUiConstraint = "DesktopUi";
    private const string RecorderEnabledEnvironmentVariable = "APPAUTOMATION_RECORDER";
    private const string RecorderScenarioEnvironmentVariable = "APPAUTOMATION_RECORDER_SCENARIO";
    private const string RecorderOutputDirectoryEnvironmentVariable = "APPAUTOMATION_RECORDER_OUTPUT_DIRECTORY";
    private const string RecorderAuthoringProjectEnvironmentVariable = "APPAUTOMATION_RECORDER_AUTHORING_PROJECT";
    private const string RecorderOverlayEnvironmentVariable = "APPAUTOMATION_RECORDER_OVERLAY";
    private const string RecorderDiagnosticsEnvironmentVariable = "APPAUTOMATION_RECORDER_DIAGNOSTICS";
    private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    [Test]
    public async Task RecorderLaunchOptionsMergeRecorderEnvironmentAndPreserveBaseOptions()
    {
        var disposeCalled = false;
        var baseOptions = new DesktopAppLaunchOptions
        {
            ExecutablePath = Path.Combine(Path.GetTempPath(), "DotnetDebug.Avalonia.exe"),
            WorkingDirectory = Path.GetTempPath(),
            Arguments = ["--smoke"],
            EnvironmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["EXISTING_VALUE"] = "42"
            },
            DisposeCallback = () => disposeCalled = true,
            MainWindowTimeout = TimeSpan.FromSeconds(11),
            PollInterval = TimeSpan.FromMilliseconds(123)
        };

        var options = CreateRecorderLaunchOptions(baseOptions, "ScenarioA", @"C:\Temp\RecorderOut");
        options.DisposeCallback!();

        using (Assert.Multiple())
        {
            await Assert.That(options.ExecutablePath).IsEqualTo(baseOptions.ExecutablePath);
            await Assert.That(options.WorkingDirectory).IsEqualTo(baseOptions.WorkingDirectory);
            await Assert.That(options.Arguments).IsEqualTo(baseOptions.Arguments);
            await Assert.That(options.MainWindowTimeout).IsEqualTo(baseOptions.MainWindowTimeout);
            await Assert.That(options.PollInterval).IsEqualTo(baseOptions.PollInterval);
            await Assert.That(options.EnvironmentVariables["EXISTING_VALUE"]).IsEqualTo("42");
            await Assert.That(options.EnvironmentVariables[RecorderEnabledEnvironmentVariable]).IsEqualTo("1");
            await Assert.That(options.EnvironmentVariables[RecorderScenarioEnvironmentVariable]).IsEqualTo("ScenarioA");
            await Assert.That(options.EnvironmentVariables[RecorderOutputDirectoryEnvironmentVariable]).IsEqualTo(@"C:\Temp\RecorderOut");
            await Assert.That(options.EnvironmentVariables[RecorderAuthoringProjectEnvironmentVariable]).IsEqualTo(
                ResolveAuthoringProjectDirectory());
            await Assert.That(options.EnvironmentVariables[RecorderOverlayEnvironmentVariable]).IsEqualTo("0");
            await Assert.That(options.EnvironmentVariables[RecorderDiagnosticsEnvironmentVariable]).IsEqualTo("1");
            await Assert.That(disposeCalled).IsEqualTo(true);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task RecorderSmokeSpinnerSavesTypedSpinnerStep()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        var scenarioName = CreateScenarioName("Spinner");
        using var outputDirectory = TemporaryDirectory.Create("DotnetDebugRecorderSmoke");
        using var session = DesktopAppSession.Launch(CreateRecorderLaunchOptions(scenarioName, outputDirectory.FullPath));
        ClickElement(session, "ControlMixTabItem");
        ReplaceText(session, "MixCountSpinner", "7");

        var scenarioSource = await SaveAndReadScenarioSourceAsync(session, outputDirectory.FullPath, scenarioName);

        using (Assert.Multiple())
        {
            await Assert.That(scenarioSource.Contains(
                "Page.SetSpinnerValue(static page => page.MixCountSpinner, 7);",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.EnterText(static page => page.MixCountSpinner",
                StringComparison.Ordinal)).IsEqualTo(false);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task RecorderSmokeSearchPickersSaveCompositeSearchSteps()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        var scenarioName = CreateScenarioName("SearchPickers");
        using var outputDirectory = TemporaryDirectory.Create("DotnetDebugRecorderSmoke");
        using var session = DesktopAppSession.Launch(CreateRecorderLaunchOptions(scenarioName, outputDirectory.FullPath));
        var page = MainWindowFlaUiPageFactory.Create(session);

        page
            .SelectTabItem(static candidate => candidate.ArmDesktopTabItem)
            .SetChecked(static candidate => candidate.ArmSearchFuzzyToggle, true)
            .SearchAndSelect(static candidate => candidate.ArmSearchPicker, "customer", "Customer Alpha")
            .SearchAndSelect(static candidate => candidate.ArmServerSearchPicker, "product", "Product 42");

        var scenarioSource = await SaveAndReadScenarioSourceAsync(session, outputDirectory.FullPath, scenarioName);

        using (Assert.Multiple())
        {
            await Assert.That(scenarioSource.Contains(
                "Page.SearchAndSelect(static page => page.ArmSearchPicker, \"customer\", \"Customer Alpha\");",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.SearchAndSelect(static page => page.ArmServerSearchPicker, \"product\", \"Product 42\");",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("ArmSearchInput", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmSearchApplyButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmServerPickerOpenButton", StringComparison.Ordinal)).IsEqualTo(false);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task RecorderSmokeRangeAndFolderSaveCompositeSteps()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        var scenarioName = CreateScenarioName("RangeFolder");
        using var outputDirectory = TemporaryDirectory.Create("DotnetDebugRecorderSmoke");
        using var session = DesktopAppSession.Launch(CreateRecorderLaunchOptions(scenarioName, outputDirectory.FullPath));
        var page = MainWindowFlaUiPageFactory.Create(session);

        ClickElement(session, "ArmDesktopTabItem");
        page.SetDateRangeFilter(
            static candidate => candidate.ArmDateRangeFilter,
            new DateTime(2026, 4, 1),
            new DateTime(2026, 4, 30));
        ClickElement(session, "ArmDateRangeApplyButton");
        ReplaceText(session, "ArmNumericRangeFrom", "10.5");
        ReplaceText(session, "ArmNumericRangeTo", "42.25");
        ClickElement(session, "ArmNumericRangeApplyButton");
        ReplaceText(session, "ArmFolderExportPathInput", @"C:\Exports\Arm");
        ClickElement(session, "ArmFolderExportSelectButton");

        var scenarioSource = await SaveAndReadScenarioSourceAsync(session, outputDirectory.FullPath, scenarioName);

        using (Assert.Multiple())
        {
            await Assert.That(scenarioSource.Contains(
                "Page.SetDateRangeFilter(static page => page.ArmDateRangeFilter, new global::System.DateTime(2026, 4, 1), new global::System.DateTime(2026, 4, 30));",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.SetNumericRangeFilter(static page => page.ArmNumericRangeFilter, 10.5, 42.25);",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.SelectExportFolder(static page => page.ArmFolderExport, \"C:\\\\Exports\\\\Arm\");",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("ArmDateRangeOpenButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmDateRangeApplyButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmNumericRangeOpenButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmNumericRangeApplyButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmFolderExportOpenButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmFolderExportSelectButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains(
                "Page.EnterText(static page => page.ArmFolderExportPathInput",
                StringComparison.Ordinal)).IsEqualTo(false);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task RecorderSmokeGridEditAndUserActionsSaveGridSteps()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        var scenarioName = CreateScenarioName("GridActions");
        using var outputDirectory = TemporaryDirectory.Create("DotnetDebugRecorderSmoke");
        using var session = DesktopAppSession.Launch(CreateRecorderLaunchOptions(scenarioName, outputDirectory.FullPath));
        var page = MainWindowFlaUiPageFactory.Create(session);

        page
            .SelectTabItem(static candidate => candidate.ArmDesktopTabItem)
            .EnterText(static candidate => candidate.ArmGridEditValueInput, "Edited-42")
            .ClickButton(static candidate => candidate.ArmGridCommitEditButton)
            .ClickButton(static candidate => candidate.ArmGridOpenButton)
            .ClickButton(static candidate => candidate.ArmGridLoadMoreButton)
            .ClickButton(static candidate => candidate.ArmGridSortButton)
            .ClickButton(static candidate => candidate.ArmGridCopyButton)
            .ClickButton(static candidate => candidate.ArmGridExportButton);

        var scenarioSource = await SaveAndReadScenarioSourceAsync(session, outputDirectory.FullPath, scenarioName);

        using (Assert.Multiple())
        {
            await Assert.That(scenarioSource.Contains(
                "Page.EditGridCellText(static page => page.ArmGridAutomationBridge, 0, 1, \"Edited-42\");",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.OpenGridRow(static page => page.ArmGridAutomationBridge, 0);",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.ScrollGridToEnd(static page => page.ArmGridAutomationBridge);",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.SortGridByColumn(static page => page.ArmGridAutomationBridge, \"Value\");",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.CopyGridCell(static page => page.ArmGridAutomationBridge, 0, 1);",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.ExportGrid(static page => page.ArmGridAutomationBridge);",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.EnterText(static page => page.ArmGridEditValueInput",
                StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmGridCommitEditButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmGridOpenButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmGridLoadMoreButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmGridSortButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmGridCopyButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmGridExportButton", StringComparison.Ordinal)).IsEqualTo(false);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task RecorderSmokeDialogNotificationAndShellSaveCompositeSteps()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        var scenarioName = CreateScenarioName("Composite");
        using var outputDirectory = TemporaryDirectory.Create("DotnetDebugRecorderSmoke");
        using var session = DesktopAppSession.Launch(CreateRecorderLaunchOptions(scenarioName, outputDirectory.FullPath));
        var page = MainWindowFlaUiPageFactory.Create(session);

        page
            .SelectTabItem(static candidate => candidate.ArmDesktopTabItem)
            .ConfirmDialog(static candidate => candidate.ArmDialog, "Delete selected")
            .WaitUntilNameEquals(static candidate => candidate.ArmDialogResultLabel, "Dialog confirmed")
            .DismissNotification(static candidate => candidate.ArmNotification)
            .WaitUntilNameEquals(static candidate => candidate.ArmNotificationStatusLabel, "Notification dismissed")
            .ActivateShellPane(static candidate => candidate.ArmShellNavigation, "Reports")
            .WaitUntilNameEquals(static candidate => candidate.ArmShellActivePaneLabel, "Reports");

        var scenarioSource = await SaveAndReadScenarioSourceAsync(session, outputDirectory.FullPath, scenarioName);
        var hasShellCompositeStep = scenarioSource.Contains(
                "Page.OpenOrActivateShellPane(static page => page.ArmShellNavigation, \"Reports\");",
                StringComparison.Ordinal)
            || scenarioSource.Contains(
                "Page.ActivateShellPane(static page => page.ArmShellNavigation, \"Reports\");",
                StringComparison.Ordinal);

        using (Assert.Multiple())
        {
            await Assert.That(scenarioSource.Contains(
                "Page.ConfirmDialog(static page => page.ArmDialog);",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.DismissNotification(static page => page.ArmNotification);",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(hasShellCompositeStep).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("ArmDialogConfirmButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmNotificationDismissButton", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmShellNavigationList", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmShellPaneTabs", StringComparison.Ordinal)).IsEqualTo(false);
            await Assert.That(scenarioSource.Contains("ArmShellPaneReports", StringComparison.Ordinal)).IsEqualTo(false);
        }
    }

    private static DesktopAppLaunchOptions CreateRecorderLaunchOptions(string scenarioName, string outputDirectory)
    {
        var baseOptions = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(buildConfiguration: "Debug");
        return CreateRecorderLaunchOptions(baseOptions, scenarioName, outputDirectory);
    }

    private static DesktopAppLaunchOptions CreateRecorderLaunchOptions(
        DesktopAppLaunchOptions baseOptions,
        string scenarioName,
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var environmentVariables = new Dictionary<string, string?>(baseOptions.EnvironmentVariables, StringComparer.Ordinal)
        {
            [RecorderEnabledEnvironmentVariable] = "1",
            [RecorderScenarioEnvironmentVariable] = scenarioName,
            [RecorderOutputDirectoryEnvironmentVariable] = Path.GetFullPath(outputDirectory),
            [RecorderAuthoringProjectEnvironmentVariable] = ResolveAuthoringProjectDirectory(),
            [RecorderOverlayEnvironmentVariable] = "0",
            [RecorderDiagnosticsEnvironmentVariable] = "1"
        };

        return new DesktopAppLaunchOptions
        {
            ExecutablePath = baseOptions.ExecutablePath,
            WorkingDirectory = baseOptions.WorkingDirectory,
            Arguments = baseOptions.Arguments,
            EnvironmentVariables = environmentVariables,
            DisposeCallback = baseOptions.DisposeCallback,
            MainWindowTimeout = baseOptions.MainWindowTimeout,
            PollInterval = baseOptions.PollInterval
        };
    }

    private static async Task<string> SaveAndReadScenarioSourceAsync(
        DesktopAppSession session,
        string outputDirectory,
        string scenarioName)
    {
        session.MainWindow.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_S);

        var scenarioPath = await WaitForScenarioFileAsync(outputDirectory, scenarioName);
        return await File.ReadAllTextAsync(scenarioPath);
    }

    private static void ClickElement(DesktopAppSession session, string automationId)
    {
        var element = FindElement(session, automationId);
        element.Focus();
        element.Click();
    }

    private static void ReplaceText(DesktopAppSession session, string automationId, string value)
    {
        var element = FindElement(session, automationId);
        element.Focus();
        element.Click();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(value);
    }

    private static AutomationElement FindElement(DesktopAppSession session, string automationId)
    {
        return UiWait.Until(
            () => session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(automationId)),
            static candidate => candidate is not null,
            new UiWaitOptions { Timeout = TimeSpan.FromSeconds(5), PollInterval = PollInterval },
            $"Element '{automationId}' was not found.")!;
    }

    private static async Task<string> WaitForScenarioFileAsync(string outputDirectory, string scenarioName)
    {
        var pattern = $"MainWindowScenariosBase.{scenarioName}.*.g.cs";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Exception? lastReadError = null;

        while (stopwatch.Elapsed < SaveTimeout)
        {
            var candidate = Directory.Exists(outputDirectory)
                ? Directory.EnumerateFiles(outputDirectory, pattern, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;

            if (candidate is not null)
            {
                try
                {
                    _ = await File.ReadAllTextAsync(candidate);
                    return candidate;
                }
                catch (IOException ex)
                {
                    lastReadError = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastReadError = ex;
                }
            }

            await Task.Delay(PollInterval);
        }

        var existingFiles = Directory.Exists(outputDirectory)
            ? string.Join(", ", Directory.EnumerateFiles(outputDirectory).Select(Path.GetFileName))
            : "<missing output directory>";
        var diagnostics = ReadRecorderDiagnostics(outputDirectory);
        var message = $"Recorder scenario file '{pattern}' was not created in '{outputDirectory}'. Existing files: {existingFiles}. {diagnostics}";
        throw lastReadError is null ? new TimeoutException(message) : new TimeoutException(message, lastReadError);
    }

    private static string ReadRecorderDiagnostics(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return "Recorder diagnostics: <output directory missing>.";
        }

        var diagnosticFile = Directory.EnumerateFiles(outputDirectory, "*.recorder-diagnostics.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (diagnosticFile is null)
        {
            return "Recorder diagnostics: <none>.";
        }

        try
        {
            var content = File.ReadAllText(diagnosticFile);
            return $"Recorder diagnostics from '{Path.GetFileName(diagnosticFile)}': {content}";
        }
        catch (IOException ex)
        {
            return $"Recorder diagnostics file '{Path.GetFileName(diagnosticFile)}' could not be read: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"Recorder diagnostics file '{Path.GetFileName(diagnosticFile)}' could not be read: {ex.Message}";
        }
    }

    private static string CreateScenarioName(string prefix)
    {
        return $"{prefix}_{Guid.NewGuid():N}";
    }

    private static string ResolveAuthoringProjectDirectory()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "DotnetDebug.AppAutomation.Authoring"));
    }
}
