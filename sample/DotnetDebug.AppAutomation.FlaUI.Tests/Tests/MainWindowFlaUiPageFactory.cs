using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Session;
using DotnetDebug.AppAutomation.Authoring.Pages;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

internal static class MainWindowFlaUiPageFactory
{
    public static MainWindowPage Create(DesktopAppSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new MainWindowPage(
            new FlaUiControlResolver(session.MainWindow, session.ConditionFactory)
                .WithMultiSelect(
                    "MultiSelection",
                    MultiSelectParts.ByAutomationIds(
                        "MultiSelection",
                        "MultiSelection_OpenButton",
                        "MultiSelection_Results",
                        "MultiSelection_ApplyButton",
                        "MultiSelection_CancelButton"))
                .WithSearchPicker(
                    "HistoryOperationPicker",
                    SearchPickerParts.ByAutomationIds(
                        "HistoryFilterInput",
                        "OperationCombo",
                        applyButtonAutomationId: "ApplyFilterButton"))
                .WithSearchPicker(
                    "ArmServerSearchPicker",
                    SearchPickerParts.ByAutomationIds(
                        "ArmServerSearchPicker_Input",
                        "ArmServerSearchPicker_Results",
                        expandButtonAutomationId: "ArmServerSearchPicker_OpenButton",
                        resultsKind: SearchPickerResultsKind.ListBox,
                        opensOnSearch: true))
                .WithSearchControl(
                    "ArmTableSearch",
                    SearchControlParts.ByAutomationIds(
                        "ArmTableSearchInput",
                        "ArmTableSearchHistoryItemButton",
                        historyRootAutomationId: "ArmTableSearchHistoryRoot"))
                .WithComboBoxFilter(
                    "ArmStatusFilter",
                    ComboBoxFilterParts.ByAutomationIds(
                        "ArmStatusFilter",
                        "ArmStatusFilter_OpenButton",
                        "ArmStatusFilter_Results",
                        "ArmStatusFilter_ApplyButton",
                        "ArmStatusFilter_CancelButton"))
                .WithDateRangeFilter(
                    "ArmDateRangeFilter",
                    DateRangeFilterParts.ByAutomationIds(
                        "ArmDateRangeFrom",
                        "ArmDateRangeTo",
                        "ArmDateRangeApplyButton",
                        "ArmDateRangeCancelButton",
                        openButtonAutomationId: "ArmDateRangeOpenButton"))
                .WithNumericRangeFilter(
                    "ArmNumericRangeFilter",
                    NumericRangeFilterParts.ByAutomationIds(
                        "ArmNumericRangeFrom",
                        "ArmNumericRangeTo",
                        "ArmNumericRangeApplyButton",
                        "ArmNumericRangeCancelButton",
                        openButtonAutomationId: "ArmNumericRangeOpenButton",
                        editorKind: FilterValueEditorKind.TextBox))
                .WithDialog(
                    "ArmDialog",
                    DialogControlParts.ByAutomationIds(
                        "ArmDialogMessage",
                        "ArmDialogConfirmButton",
                        cancelButtonAutomationId: "ArmDialogCancelButton",
                        dismissButtonAutomationId: "ArmDialogDismissButton"))
                .WithNotification(
                    "ArmNotification",
                    NotificationControlParts.ByAutomationIds(
                        "ArmNotificationText",
                        dismissButtonAutomationId: "ArmNotificationDismissButton"))
                .WithFolderExport(
                    "ArmFolderExport",
                    FolderExportControlParts.ByAutomationIds(
                        "ArmFolderExportOpenButton",
                        "ArmFolderExportPathInput",
                        "ArmFolderExportSelectButton",
                        "ArmFolderExportCancelButton",
                        statusAutomationId: "ArmFolderExportStatusLabel"))
                .WithShellNavigation(
                    "ArmShellNavigation",
                    ShellNavigationParts.ByAutomationIds(
                        "ArmShellPaneTabs",
                        paneTabsAutomationId: "ArmShellPaneTabs",
                        activePaneLabelAutomationId: "ArmShellActivePaneLabel",
                        navigationKind: ShellNavigationSourceKind.Tab)));
    }
}
