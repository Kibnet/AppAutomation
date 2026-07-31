using System;
using System.IO;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace DotnetDebug.Avalonia;

public partial class App : Application
{
    private const string RecorderEnabledEnvironmentVariable = "APPAUTOMATION_RECORDER";
    private const string RecorderScenarioEnvironmentVariable = "APPAUTOMATION_RECORDER_SCENARIO";
    private const string RecorderOutputDirectoryEnvironmentVariable = "APPAUTOMATION_RECORDER_OUTPUT_DIRECTORY";
    private const string RecorderAuthoringProjectEnvironmentVariable = "APPAUTOMATION_RECORDER_AUTHORING_PROJECT";
    private const string RecorderOverlayEnvironmentVariable = "APPAUTOMATION_RECORDER_OVERLAY";
    private const string RecorderDiagnosticsEnvironmentVariable = "APPAUTOMATION_RECORDER_DIAGNOSTICS";
    private const string RecorderSaveHotkeyEnvironmentVariable = "APPAUTOMATION_RECORDER_SAVE_HOTKEY";

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
        var outputDirectory = Environment.GetEnvironmentVariable(RecorderOutputDirectoryEnvironmentVariable);
        var authoringProjectDirectory = Environment.GetEnvironmentVariable(RecorderAuthoringProjectEnvironmentVariable);
        var saveHotkey = Environment.GetEnvironmentVariable(RecorderSaveHotkeyEnvironmentVariable);
        var useInteractiveScenarioSelection = string.IsNullOrWhiteSpace(scenarioName);
        var options = new AppAutomationRecorderOptions
        {
            ScenarioName = string.IsNullOrWhiteSpace(scenarioName) ? "RecordedSmoke" : scenarioName,
            AuthoringProjectDirectory = string.IsNullOrWhiteSpace(authoringProjectDirectory)
                ? ResolveAuthoringProjectDirectory()
                : Path.GetFullPath(authoringProjectDirectory),
            OutputSubdirectory = string.IsNullOrWhiteSpace(outputDirectory) ? "Recorded" : Path.GetFullPath(outputDirectory),
            PageNamespace = "DotnetDebug.AppAutomation.Authoring.Pages",
            PageClassName = "MainWindowPage",
            ScenarioNamespace = useInteractiveScenarioSelection
                ? null
                : "DotnetDebug.AppAutomation.Authoring.Tests.UIAutomationTests",
            ScenarioClassName = useInteractiveScenarioSelection ? null : "MainWindowScenariosBase",
            ScenarioSelection = new RecorderScenarioSelectionOptions
            {
                IsEnabled = useInteractiveScenarioSelection,
                ScenarioNamespaceRoot = "DotnetDebug.AppAutomation.Authoring.Tests",
                OutputSubdirectoryRoot = string.IsNullOrWhiteSpace(outputDirectory)
                    ? "Recorded"
                    : Path.GetFullPath(outputDirectory)
            },
            OverlayTheme = RecorderOverlayTheme.Dark,
            ShowOverlay = !string.Equals(Environment.GetEnvironmentVariable(RecorderOverlayEnvironmentVariable), "0", StringComparison.Ordinal),
            DiagnosticLog = new RecorderDiagnosticLogOptions
            {
                WriteToFile = ShouldWriteRecorderDiagnosticsToFile(
                    Environment.GetEnvironmentVariable(RecorderDiagnosticsEnvironmentVariable))
            },
            Hotkeys = string.IsNullOrWhiteSpace(saveHotkey)
                ? RecorderHotkeys.Default
                : new RecorderHotkeys { Save = saveHotkey },
            AllowNameLocators = false
        };
        options.ControlHints.Add(new RecorderControlHint("MixCountSpinner", RecorderActionHint.SpinnerTextBox));
        options.MultiSelectHints.Add(new RecorderMultiSelectHint(
            "MultiSelection",
            MultiSelectParts.ByAutomationIds(
                "MultiSelection",
                "MultiSelection_OpenButton",
                "MultiSelection_Results",
                "MultiSelection_ApplyButton",
                "MultiSelection_CancelButton")));
        options.LocatorAliases.Add(new RecorderLocatorAlias(
            "EremexDemoDataGridControl",
            "EremexDemoDataGridAutomationBridge",
            UiControlType.Grid));
        options.LocatorAliases.Add(new RecorderLocatorAlias(
            "ArmEremexDataGridControl",
            "ArmGridAutomationBridge",
            UiControlType.Grid));
        options.GridHints.Add(new RecorderGridHint(
            "EremexDemoDataGridControl",
            "EremexDemoDataGridAutomationBridge",
            ["EremexRow", "EremexValue", "EremexParity"]));
        options.GridHints.Add(new RecorderGridHint(
            "ArmEremexDataGridControl",
            "ArmGridAutomationBridge",
            ["Key", "Value", "State"]));
        options.GridHints.Add(new RecorderGridHint(
            "SearchPickerGridAutomationBridge",
            "SearchPickerGridAutomationBridge",
            ["Key", "SelectedValue"]));
        options.GridActionHints.Add(new RecorderGridActionHint(
            "ArmGridOpenButton",
            "ArmGridAutomationBridge",
            RecorderGridUserActionKind.OpenRow,
            RowIndex: 0));
        options.GridActionHints.Add(new RecorderGridActionHint(
            "ArmGridSortButton",
            "ArmGridAutomationBridge",
            RecorderGridUserActionKind.SortByColumn,
            ColumnName: "Value"));
        options.GridActionHints.Add(new RecorderGridActionHint(
            "ArmGridLoadMoreButton",
            "ArmGridAutomationBridge",
            RecorderGridUserActionKind.ScrollToEnd));
        options.GridActionHints.Add(new RecorderGridActionHint(
            "ArmGridCopyButton",
            "ArmGridAutomationBridge",
            RecorderGridUserActionKind.CopyCell,
            RowIndex: 0,
            ColumnIndex: 1));
        options.GridActionHints.Add(new RecorderGridActionHint(
            "ArmGridExportButton",
            "ArmGridAutomationBridge",
            RecorderGridUserActionKind.Export));
        options.GridEditHints.Add(new RecorderGridEditHint(
            "ArmGridCommitEditButton",
            "ArmGridAutomationBridge",
            "ArmGridEditValueInput",
            0,
            1));
        options.GridEditHints.Add(new RecorderGridEditHint(
            "ArmGridCancelEditButton",
            "ArmGridAutomationBridge",
            "ArmGridEditValueInput",
            0,
            1,
            CommitMode: GridCellEditCommitMode.Cancel));
        options.SearchPickerHints.Add(new RecorderSearchPickerHint(
            "ArmServerSearchPicker",
            SearchPickerParts.ByAutomationIds(
                "ArmServerSearchPicker_Input",
                "ArmServerSearchPicker_Results",
                expandButtonAutomationId: "ArmServerSearchPicker_OpenButton",
                resultsKind: SearchPickerResultsKind.ListBox,
                opensOnSearch: true)));
        options.GridSearchPickerHints.Add(new RecorderGridSearchPickerHint(
            "SearchPickerGridEditor",
            "SearchPickerGridAutomationBridge",
            SearchPickerParts.ByAutomationIds(
                "SearchPickerGridEditor_Input",
                "SearchPickerGridEditor_Results",
                expandButtonAutomationId: "SearchPickerGridEditor_OpenButton",
                resultsKind: SearchPickerResultsKind.ListBox,
                opensOnSearch: true),
            ColumnName: "SelectedValue"));
        options.DateRangeFilterHints.Add(new RecorderDateRangeFilterHint(
            "ArmDateRangeFilter",
            DateRangeFilterParts.ByAutomationIds(
                "ArmDateRangeFrom",
                "ArmDateRangeTo",
                "ArmDateRangeApplyButton",
                "ArmDateRangeCancelButton",
                openButtonAutomationId: "ArmDateRangeOpenButton")));
        options.NumericRangeFilterHints.Add(new RecorderNumericRangeFilterHint(
            "ArmNumericRangeFilter",
            NumericRangeFilterParts.ByAutomationIds(
                "ArmNumericRangeFrom",
                "ArmNumericRangeTo",
                "ArmNumericRangeApplyButton",
                "ArmNumericRangeCancelButton",
                openButtonAutomationId: "ArmNumericRangeOpenButton",
                editorKind: FilterValueEditorKind.TextBox)));
        options.DialogHints.Add(new RecorderDialogHint(
            "ArmDialog",
            DialogControlParts.ByAutomationIds(
                "ArmDialogMessage",
                "ArmDialogConfirmButton",
                cancelButtonAutomationId: "ArmDialogCancelButton",
                dismissButtonAutomationId: "ArmDialogDismissButton")));
        options.NotificationHints.Add(new RecorderNotificationHint(
            "ArmNotification",
            NotificationControlParts.ByAutomationIds(
                "ArmNotificationText",
                dismissButtonAutomationId: "ArmNotificationDismissButton")));
        options.FolderExportHints.Add(new RecorderFolderExportHint(
            "ArmFolderExport",
            FolderExportControlParts.ByAutomationIds(
                "ArmFolderExportOpenButton",
                "ArmFolderExportPathInput",
                "ArmFolderExportSelectButton",
                "ArmFolderExportCancelButton",
                statusAutomationId: "ArmFolderExportStatusLabel")));
        options.ShellNavigationHints.Add(new RecorderShellNavigationHint(
            "ArmShellNavigation",
            ShellNavigationParts.ByAutomationIds(
                "ArmShellNavigationList",
                paneTabsAutomationId: "ArmShellPaneTabs",
                activePaneLabelAutomationId: "ArmShellActivePaneLabel",
                navigationKind: ShellNavigationSourceKind.ListBox)));

        var session = AppAutomationRecorder.Attach(mainWindow, options);
        if (!useInteractiveScenarioSelection)
        {
            session.Start();
        }
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

    internal static bool ShouldWriteRecorderDiagnosticsToFile(string? environmentValue)
    {
        return !string.Equals(environmentValue, "0", StringComparison.Ordinal)
            && !string.Equals(environmentValue, "false", StringComparison.OrdinalIgnoreCase);
    }
}
