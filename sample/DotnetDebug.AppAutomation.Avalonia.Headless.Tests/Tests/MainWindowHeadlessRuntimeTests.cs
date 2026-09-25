using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TUnit;
using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.Authoring.Tests.UIAutomationTests;
using DotnetDebug.AppAutomation.TestHost;
using DotnetDebug.AppAutomation.Configuration;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

[InheritsTests]
public sealed class MainWindowHeadlessRuntimeTests : MainWindowScenariosBase<MainWindowHeadlessRuntimeTests.HeadlessRuntimeSession>
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task CaptureScreenshot_AfterNavigation_ShowsCurrentTab()
    {
        Page.SelectTabItem(static page => page.ArmDesktopTabItem);
        var path = Session.Inner.CaptureScreenshot(Path.Combine(
            AppContext.BaseDirectory,
            "artifacts", "headless-screenshots", Guid.NewGuid().ToString("N"), "arm-desktop.png"));
        using var bitmap = new global::Avalonia.Media.Imaging.Bitmap(path);
        await TUnit.Assertions.Assert.That(bitmap.PixelSize.Width > 200).IsTrue();
        Console.WriteLine($"Headless screenshot after navigation: {path}");
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task IntentionalFailure_CapturesScreenshotOnlyInFailureSmoke()
    {
        if (Environment.GetEnvironmentVariable("APPAUTOMATION_SCREENSHOT_FAILURE_SMOKE") != "1")
        {
            return;
        }

        await TUnit.Assertions.Assert.That("actual-state").IsEqualTo("expected-state");
    }

    protected override HeadlessRuntimeSession LaunchSession()
    {
        var inner = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        try
        {
            HeadlessRuntime.Dispatch(inner.MainWindow.Show);
            return new HeadlessRuntimeSession(inner);
        }
        catch
        {
            inner.Dispose();
            throw;
        }
    }

    protected override ValueTask<IReadOnlyList<UiFailureArtifact>> CollectFailureArtifactsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Environment.GetEnvironmentVariable("APPAUTOMATION_SCREENSHOT_CAPTURE_ERROR_SMOKE") == "1")
        {
            Session.Inner.CaptureScreenshot(AppContext.BaseDirectory);
        }

        var path = Session.Inner.CaptureScreenshot(Path.Combine(
            AppContext.BaseDirectory,
            "artifacts", "ui-failures", "avalonia-headless",
            Guid.NewGuid().ToString("N"), "test-failure.png"));
        return ValueTask.FromResult<IReadOnlyList<UiFailureArtifact>>(
        [new UiFailureArtifact(
            "screenshot", "test-failure",
            Path.GetRelativePath(AppContext.BaseDirectory, path),
            "image/png", false, path)]);
    }

    protected override MainWindowPage CreatePage(HeadlessRuntimeSession session)
    {
        return new MainWindowPage(
            new HeadlessControlResolver(session.Inner.MainWindow)
                .WithAdapters(new HeadlessMultiSelectControlAdapter(session.Inner.MainWindow))
                .WithAdapters(new HeadlessComboBoxFilterControlAdapter(session.Inner.MainWindow))
                .WithAdapters(new HeadlessSearchControlAdapter(session.Inner.MainWindow))
                .WithSearchPicker(
                    "HistoryOperationPicker",
                    SearchPickerParts.ByAutomationIds(
                        "HistoryFilterInput",
                        "OperationCombo",
                        applyButtonAutomationId: "ApplyFilterButton"))
                .WithSampleSearchPicker("ArmServerSearchPicker", "SamplePickerCommittedValue")
                .WithColorPicker(
                    "ArmAccentColorPicker",
                    ColorPickerParts.ByAutomationIds(
                        "ArmAccentColorPicker",
                        "ArmAccentColorValue",
                        openButtonAutomationId: "ArmAccentColorOpenButton",
                        popupRootAutomationId: "ArmAccentColorPopup",
                        customValueAutomationId: "ArmAccentColorCustomValue",
                        confirmButtonAutomationId: "ArmAccentColorConfirmButton",
                        cancelButtonAutomationId: "ArmAccentColorCancelButton",
                        commitMode: ColorPickerCommitMode.Confirm))
                .WithGridAutomation(SampleGridAutomation.CreateHeadlessCatalog())
                .WithMultiItemControls(SampleMultiItemAutomation.CreateCatalog())
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
            try
            {
                HeadlessRuntime.Dispatch(Inner.MainWindow.Close);
            }
            finally
            {
                Inner.Dispose();
                if (Environment.GetEnvironmentVariable("APPAUTOMATION_SCREENSHOT_FAILURE_SMOKE") == "1")
                {
                    Console.WriteLine("Headless failure smoke cleanup completed");
                }
            }
        }
    }
}
