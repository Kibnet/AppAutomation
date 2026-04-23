using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.Authoring.Tests.UIAutomationTests;
using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using DotnetDebug.AppAutomation.TestHost;
using AppAutomation.Abstractions;
using AppAutomation.TUnit;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Session;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

[InheritsTests]
public sealed class MainWindowFlaUiRuntimeTests : MainWindowScenariosBase<MainWindowFlaUiRuntimeTests.FlaUiRuntimeSession>
{
    protected override FlaUiRuntimeSession LaunchSession()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        return new FlaUiRuntimeSession(DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions()));
    }

    protected override MainWindowPage CreatePage(FlaUiRuntimeSession session)
    {
        return new MainWindowPage(
            new FlaUiControlResolver(session.Inner.MainWindow, session.Inner.ConditionFactory)
                .WithSearchPicker(
                    "HistoryOperationPicker",
                    SearchPickerParts.ByAutomationIds(
                        "HistoryFilterInput",
                        "OperationCombo",
                        applyButtonAutomationId: "ApplyFilterButton"))
                .WithSearchPicker(
                    "ArmSearchPicker",
                    SearchPickerParts.ByAutomationIds(
                        "ArmSearchInput",
                        "ArmSearchResults",
                        applyButtonAutomationId: "ArmSearchApplyButton"))
                .WithSearchPicker(
                    "ArmServerSearchPicker",
                    SearchPickerParts.ByAutomationIds(
                        "ArmServerPickerInput",
                        "ArmServerPickerResults",
                        expandButtonAutomationId: "ArmServerPickerOpenButton"))
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

    public sealed class FlaUiRuntimeSession : IUiTestSession
    {
        public FlaUiRuntimeSession(DesktopAppSession inner)
        {
            Inner = inner;
        }

        public DesktopAppSession Inner { get; }

        public void Dispose()
        {
            Inner.Dispose();
        }
    }
}
