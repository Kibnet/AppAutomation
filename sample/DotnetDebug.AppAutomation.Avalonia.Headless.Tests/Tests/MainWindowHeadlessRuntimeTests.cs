using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.Abstractions;
using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.Authoring.Tests.UIAutomationTests;
using DotnetDebug.AppAutomation.TestHost;
using AppAutomation.TUnit;
using AppAutomation.Avalonia.Headless.Automation;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

[InheritsTests]
public sealed class MainWindowHeadlessRuntimeTests : MainWindowScenariosBase<MainWindowHeadlessRuntimeTests.HeadlessRuntimeSession>
{
    protected override HeadlessRuntimeSession LaunchSession()
    {
        return new HeadlessRuntimeSession(DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions()));
    }

    protected override MainWindowPage CreatePage(HeadlessRuntimeSession session)
    {
        return new MainWindowPage(
            new HeadlessControlResolver(session.Inner.MainWindow)
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
                        "ArmShellNavigationList",
                        paneTabsAutomationId: "ArmShellPaneTabs",
                        activePaneLabelAutomationId: "ArmShellActivePaneLabel",
                        navigationKind: ShellNavigationSourceKind.ListBox)));
    }

    public sealed class HeadlessRuntimeSession : IUiTestSession
    {
        public HeadlessRuntimeSession(DesktopAppSession inner)
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
