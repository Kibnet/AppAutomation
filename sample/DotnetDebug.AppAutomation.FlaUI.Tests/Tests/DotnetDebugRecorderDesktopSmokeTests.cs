using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using AppAutomation.TestHost.Avalonia;
using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using DotnetDebug.AppAutomation.TestHost;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using System.Runtime.InteropServices;
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
    private const string RecorderSaveHotkeyEnvironmentVariable = "APPAUTOMATION_RECORDER_SAVE_HOTKEY";
    private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    [Test]
    public async Task RecorderLaunchOptionsMergeRecorderEnvironmentAndPreserveBaseOptions()
    {
        var disposeCalled = false;
        var windowPlacement = DesktopWindowPlacement.Centered(
            DesktopMonitorSelector.LastAvailable,
            width: 800,
            height: 600);
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
            PollInterval = TimeSpan.FromMilliseconds(123),
            WindowPlacement = windowPlacement
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
            await Assert.That(options.WindowPlacement).IsEqualTo(windowPlacement);
            await Assert.That(options.EnvironmentVariables["EXISTING_VALUE"]).IsEqualTo("42");
            await Assert.That(options.EnvironmentVariables[RecorderEnabledEnvironmentVariable]).IsEqualTo("1");
            await Assert.That(options.EnvironmentVariables[RecorderScenarioEnvironmentVariable]).IsEqualTo("ScenarioA");
            await Assert.That(options.EnvironmentVariables[RecorderOutputDirectoryEnvironmentVariable]).IsEqualTo(@"C:\Temp\RecorderOut");
            await Assert.That(options.EnvironmentVariables[RecorderAuthoringProjectEnvironmentVariable]).IsEqualTo(
                ResolveAuthoringProjectDirectory());
            await Assert.That(options.EnvironmentVariables[RecorderOverlayEnvironmentVariable]).IsEqualTo("0");
            await Assert.That(options.EnvironmentVariables[RecorderDiagnosticsEnvironmentVariable]).IsEqualTo("1");
            await Assert.That(options.EnvironmentVariables[RecorderSaveHotkeyEnvironmentVariable]).IsEqualTo("1");
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
        var page = MainWindowFlaUiPageFactory.Create(session);
        page.SelectTabItem(static candidate => candidate.ControlMixTabItem);
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
    public async Task RecorderInteractiveDestinationSelectsRecordsAndSaves()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        var scenarioName = CreateScenarioName("Interactive");
        using var outputDirectory = TemporaryDirectory.Create("DotnetDebugRecorderInteractive");
        using var session = DesktopAppSession.Launch(CreateInteractiveRecorderLaunchOptions(outputDirectory.FullPath));
        using var automation = new UIA3Automation();
        var overlayWindow = UiWait.Until(
            () => automation.GetDesktop()
                .FindAllChildren(session.ConditionFactory.ByControlType(ControlType.Window))
                .Select(static element => element.AsWindow())
                .FirstOrDefault(window =>
                    string.Equals(window.Name, "AppAutomation Recorder", StringComparison.Ordinal)
                    && window.Properties.ProcessId.Value == session.MainWindow.Properties.ProcessId.Value),
            static candidate => candidate is not null,
            new UiWaitOptions { Timeout = TimeSpan.FromSeconds(10), PollInterval = PollInterval },
            "Recorder overlay window was not found.")!;
        var appWindow = UiWait.Until(
            () => automation.GetDesktop()
                .FindAllChildren(session.ConditionFactory.ByControlType(ControlType.Window))
                .Select(static element => element.AsWindow())
                .FirstOrDefault(window =>
                    string.Equals(window.Name, "DotnetDebug - Math Operations Showcase", StringComparison.Ordinal)
                    && window.Properties.ProcessId.Value == overlayWindow.Properties.ProcessId.Value),
            static candidate => candidate is not null,
            new UiWaitOptions { Timeout = TimeSpan.FromSeconds(10), PollInterval = PollInterval },
            "DotnetDebug application window was not found.")!;
        var destinationCombo = FindElement(overlayWindow, session, "RecorderScenarioDestination").AsComboBox();
        var nameTextBox = FindElement(overlayWindow, session, "RecorderScenarioName").AsTextBox();
        var recordButton = FindElement(overlayWindow, session, "RecordButton").AsButton();
        var saveButton = FindElement(overlayWindow, session, "SaveButton").AsButton();

        UiWait.Until(
            () => destinationCombo.Items,
            static items => items.Any(item => string.Equals(
                item.Text,
                "UIAutomationTests.MainWindowScenariosBase",
                StringComparison.Ordinal)),
            new UiWaitOptions { Timeout = TimeSpan.FromSeconds(10), PollInterval = PollInterval },
            "Recorder scenario destinations were not loaded.");
        destinationCombo.Select("UIAutomationTests.MainWindowScenariosBase");
        destinationCombo.Collapse();
        nameTextBox.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(scenarioName);
        UiWait.Until(
            () => nameTextBox.Text,
            text => string.Equals(text, scenarioName, StringComparison.Ordinal),
            new UiWaitOptions { Timeout = TimeSpan.FromSeconds(5), PollInterval = PollInterval },
            "Recorder scenario name was not updated.");
        UiWait.Until(
            () => recordButton.IsEnabled,
            static enabled => enabled,
            new UiWaitOptions { Timeout = TimeSpan.FromSeconds(5), PollInterval = PollInterval },
            "Recorder Record button did not become enabled.");

        TryCaptureDesktopElement(overlayWindow, "recorder-destination-selection.png");
        recordButton.Invoke();
        var numbersInput = FindElement(appWindow, session, "NumbersInput");
        numbersInput.Focus();
        numbersInput.AsTextBox().Text = "4 2";
        recordButton.Invoke();
        saveButton.Invoke();

        var selectedOutputDirectory = Path.Combine(outputDirectory.FullPath, "UIAutomationTests");
        var scenarioPath = await WaitForScenarioFileAsync(
            selectedOutputDirectory,
            scenarioName,
            patternOverride: $"MainWindowScenariosBase.{scenarioName}.*.g.cs");
        var scenarioSource = await File.ReadAllTextAsync(scenarioPath);

        using (Assert.Multiple())
        {
            await Assert.That(scenarioSource).Contains(
                "namespace DotnetDebug.AppAutomation.Authoring.Tests.UIAutomationTests;");
            await Assert.That(scenarioSource).Contains(
                "partial class MainWindowScenariosBase<TSession>");
            await Assert.That(scenarioSource).Contains(
                "Page.EnterText(static page => page.NumbersInput, \"4 2\");");
        }
    }

    private static void TryCaptureDesktopElement(AutomationElement element, string fileName)
    {
        try
        {
            var screenshotDirectory = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "TestResults"));
            var screenshotPath = Path.Combine(screenshotDirectory.FullName, fileName);
            Capture.Element(element).ToFile(screenshotPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ExternalException)
        {
            Console.WriteLine($"Recorder overlay screenshot is unavailable: {exception.Message}");
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
        using var automation = new UIA3Automation();
        var page = MainWindowFlaUiPageFactory.Create(session);

        page.SelectTabItem(static candidate => candidate.ArmDesktopTabItem);
        var serverInput = FindElement(session, "ArmServerSearchPicker_Input");
        if (serverInput.Patterns.ScrollItem.IsSupported)
        {
            serverInput.Patterns.ScrollItem.Pattern.ScrollIntoView();
        }

        UiWait.Until(
            () => serverInput.BoundingRectangle,
            static bounds => bounds.Top >= 0,
            new UiWaitOptions { Timeout = TimeSpan.FromSeconds(5), PollInterval = PollInterval },
            "Server search picker input did not scroll into view.");
        page.ArmServerSearchPicker.Expand();
        var serverResults = UiWait.Until(
            () => automation.GetDesktop()
                .FindAllDescendants(session.ConditionFactory.ByAutomationId("ArmServerSearchPicker_Results"))
                .FirstOrDefault(element =>
                    element.Properties.ProcessId.Value == session.MainWindow.Properties.ProcessId.Value),
            static candidate => candidate is not null,
            new UiWaitOptions { Timeout = TimeSpan.FromSeconds(5), PollInterval = PollInterval },
            "Server search picker popup results were not found.")!;
        var popupVerticalGap = UiWait.Until(
            () => serverResults.BoundingRectangle.Top - serverInput.BoundingRectangle.Bottom,
            static gap => gap >= -1 && gap <= 8,
            new UiWaitOptions { Timeout = TimeSpan.FromSeconds(5), PollInterval = PollInterval },
            "Server search picker popup did not stay adjacent to its editor.");
        TryCaptureDesktopElement(session.MainWindow, "server-search-picker-popup.png");

        page
            .SearchAndSelect(static candidate => candidate.ArmServerSearchPicker, "product", "Product 42")
            .SelectTabItem(static candidate => candidate.DataGridTabItem)
            .SearchAndSelectGridCell(
                static candidate => candidate.SearchPickerGridAutomationBridge,
                0,
                1,
                "ga",
                "Gamma");

        var scenarioSource = await SaveAndReadScenarioSourceAsync(session, outputDirectory.FullPath, scenarioName);

        using (Assert.Multiple())
        {
            await Assert.That(popupVerticalGap >= -1 && popupVerticalGap <= 8).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.SearchAndSelect(static page => page.ArmServerSearchPicker, \"product\", \"Product 42\");",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains(
                "Page.SearchAndSelectGridCell(static page => page.SearchPickerGridAutomationBridge, 0, 1, \"ga\", \"Gamma\");",
                StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(scenarioSource.Contains("ArmServerSearchPicker_OpenButton", StringComparison.Ordinal)).IsEqualTo(false);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task RecorderSmokeMultiSelectCapturesConfirmedAndCanceledSelections()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        var scenarioName = CreateScenarioName("MultiSelect");
        using var outputDirectory = TemporaryDirectory.Create("DotnetDebugRecorderSmoke");
        using var session = DesktopAppSession.Launch(CreateRecorderLaunchOptions(scenarioName, outputDirectory.FullPath));
        var page = MainWindowFlaUiPageFactory.Create(session);

        page
            .SelectTabItem(static candidate => candidate.ControlMixTabItem)
            .SelectMultiItems(
                static candidate => candidate.MultiSelection,
                ["Alpha", "Omega"])
            .CancelMultiSelection(
                static candidate => candidate.MultiSelection,
                ["Beta", "Psi"]);

        var scenarioSource = await WaitForAutosaveScenarioSourceAsync(
            outputDirectory.FullPath,
            scenarioName);

        using (Assert.Multiple())
        {
            await Assert.That(scenarioSource.Contains(
                "Page.SelectMultiItems(static page => page.MultiSelection, new[] { \"Alpha\", \"Omega\" });",
                StringComparison.Ordinal)).IsTrue();
            await Assert.That(scenarioSource.Contains(
                "Page.CancelMultiSelection(static page => page.MultiSelection, new[] { \"Beta\", \"Psi\" });",
                StringComparison.Ordinal)).IsTrue();
            await Assert.That(scenarioSource.Contains("Page.SetChecked", StringComparison.Ordinal)).IsFalse();
            await Assert.That(scenarioSource.Contains("MultiSelection_OpenButton", StringComparison.Ordinal)).IsFalse();
            await Assert.That(scenarioSource.Contains("MultiSelection_ApplyButton", StringComparison.Ordinal)).IsFalse();
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

    private static DesktopAppLaunchOptions CreateInteractiveRecorderLaunchOptions(string outputDirectory)
    {
        var baseOptions = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(buildConfiguration: "Debug");
        var environmentVariables = new Dictionary<string, string?>(baseOptions.EnvironmentVariables, StringComparer.Ordinal)
        {
            [RecorderEnabledEnvironmentVariable] = "1",
            [RecorderScenarioEnvironmentVariable] = null,
            [RecorderOutputDirectoryEnvironmentVariable] = Path.GetFullPath(outputDirectory),
            [RecorderAuthoringProjectEnvironmentVariable] = ResolveAuthoringProjectDirectory(),
            [RecorderOverlayEnvironmentVariable] = "1",
            [RecorderDiagnosticsEnvironmentVariable] = "0"
        };

        return new DesktopAppLaunchOptions
        {
            ExecutablePath = baseOptions.ExecutablePath,
            WorkingDirectory = baseOptions.WorkingDirectory,
            Arguments = baseOptions.Arguments,
            EnvironmentVariables = environmentVariables,
            DisposeCallback = baseOptions.DisposeCallback,
            MainWindowTimeout = baseOptions.MainWindowTimeout,
            PollInterval = baseOptions.PollInterval,
            WindowPlacement = baseOptions.WindowPlacement
        };
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
            [RecorderDiagnosticsEnvironmentVariable] = "1",
            [RecorderSaveHotkeyEnvironmentVariable] = "1"
        };

        return new DesktopAppLaunchOptions
        {
            ExecutablePath = baseOptions.ExecutablePath,
            WorkingDirectory = baseOptions.WorkingDirectory,
            Arguments = baseOptions.Arguments,
            EnvironmentVariables = environmentVariables,
            DisposeCallback = baseOptions.DisposeCallback,
            MainWindowTimeout = baseOptions.MainWindowTimeout,
            PollInterval = baseOptions.PollInterval,
            WindowPlacement = baseOptions.WindowPlacement
        };
    }

    private static async Task<string> SaveAndReadScenarioSourceAsync(
        DesktopAppSession session,
        string outputDirectory,
        string scenarioName)
    {
        var previousScenarioWriteTime = GetLatestScenarioWriteTimeUtc(outputDirectory, scenarioName);

        var scenarioPath = await WaitForScenarioFileAsync(
            outputDirectory,
            scenarioName,
            previousScenarioWriteTime,
            () =>
            {
                session.MainWindow.SetForeground();
                SendSaveHotkey(session.MainWindow);
            });
        return await File.ReadAllTextAsync(scenarioPath);
    }

    private static async Task<string> WaitForAutosaveScenarioSourceAsync(
        string outputDirectory,
        string scenarioName)
    {
        var pattern = $"MainWindowScenariosBase.{scenarioName}.autosave.*.g.cs.autosave";
        var scenarioPath = await WaitForScenarioFileAsync(
            outputDirectory,
            scenarioName,
            patternOverride: pattern);
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
        element.AsTextBox().Text = value;
    }

    private static AutomationElement FindElement(DesktopAppSession session, string automationId)
    {
        return UiWait.Until(
            () => session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(automationId)),
            static candidate => candidate is not null,
            new UiWaitOptions { Timeout = TimeSpan.FromSeconds(5), PollInterval = PollInterval },
            $"Element '{automationId}' was not found.")!;
    }

    private static AutomationElement FindElement(
        AutomationElement root,
        DesktopAppSession session,
        string automationId)
    {
        return UiWait.Until(
            () => root.FindFirstDescendant(session.ConditionFactory.ByAutomationId(automationId)),
            static candidate => candidate is not null,
            new UiWaitOptions { Timeout = TimeSpan.FromSeconds(5), PollInterval = PollInterval },
            $"Element '{automationId}' was not found in the selected window.")!;
    }

    private static void SendSaveHotkey(Window window)
    {
        var windowHandle = new IntPtr(window.Properties.NativeWindowHandle.Value);
        SendMessage(windowHandle, 0x0100u, new IntPtr((int)VirtualKeyShort.KEY_1), new IntPtr(0x00020001));
        SendMessage(windowHandle, 0x0101u, new IntPtr((int)VirtualKeyShort.KEY_1), new IntPtr(unchecked((int)0xC0020001)));
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter);

    private static async Task<string> WaitForScenarioFileAsync(
        string outputDirectory,
        string scenarioName,
        DateTime? newerThanUtc = null,
        Action? retryAction = null,
        string? patternOverride = null)
    {
        var pattern = patternOverride
            ?? $"MainWindowScenariosBase.{scenarioName}.*.g.cs";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Exception? lastReadError = null;
        var nextRetryAt = TimeSpan.Zero;

        while (stopwatch.Elapsed < SaveTimeout)
        {
            if (retryAction is not null && stopwatch.Elapsed >= nextRetryAt)
            {
                retryAction();
                nextRetryAt = stopwatch.Elapsed.Add(TimeSpan.FromSeconds(1));
            }

            var candidate = Directory.Exists(outputDirectory)
                ? Directory.EnumerateFiles(outputDirectory, pattern, SearchOption.TopDirectoryOnly)
                    .Where(path => newerThanUtc is null || File.GetLastWriteTimeUtc(path) > newerThanUtc.Value)
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

    private static DateTime? GetLatestScenarioWriteTimeUtc(string outputDirectory, string scenarioName)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return null;
        }

        var pattern = $"MainWindowScenariosBase.{scenarioName}.*.g.cs";
        return Directory.EnumerateFiles(outputDirectory, pattern, SearchOption.TopDirectoryOnly)
            .Select(File.GetLastWriteTimeUtc)
            .OrderByDescending(static timestamp => timestamp)
            .Cast<DateTime?>()
            .FirstOrDefault();
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
