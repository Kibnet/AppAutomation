using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.Recorder.Avalonia;
using AppAutomation.TUnit;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.Authoring.Tests.UIAutomationTests;
using DotnetDebug.AppAutomation.TestHost;
using DotnetDebug.AppAutomation.Configuration;
using DotnetDebug.Avalonia;
using TUnit.Assertions;
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

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task RecorderCheck_RemembersReadOnlyServerSearchGridCell()
    {
        var row = GridRowSelector.ByCell("Key", "ARM-01");
        Page.SelectTabItem(static page => page.DataGridTabItem);
        Page.SelectGridRow(static page => page.ArmComplexDataGridControl, row);
        Page.ClickButton(static page => page.SaveArmGridProductButton);

        var result = HeadlessRuntime.Dispatch(() =>
        {
            var window = Session.Inner.MainWindow;
            var productEditor = window.GetVisualDescendants()
                .OfType<ServerSearchComboBox>()
                .Single(control =>
                    string.Equals(
                        AutomationProperties.GetAutomationId(control),
                        "ArmGridProductEditor",
                        StringComparison.Ordinal)
                    && control.DataContext is ArmDesktopGridRowViewModel { Key: "ARM-01" });
            var center = productEditor.TranslatePoint(
                new Point(productEditor.Bounds.Width / 2d, productEditor.Bounds.Height / 2d),
                window);
            if (center is null)
            {
                throw new InvalidOperationException("The read-only Product cell is not positioned in the sample window.");
            }

            var options = new AppAutomationRecorderOptions
            {
                ScenarioName = "ReadOnlyProductCheck",
                ShowOverlay = false,
                GridAutomation = SampleGridAutomation.CreateRecorderCatalog(),
                Validation = new RecorderValidationOptions
                {
                    ValidateSelectors = true,
                    ValidateRuntimeTargets = false,
                    CaptureInvalidSteps = true
                }
            };
            using var recorder = new RecorderSession(window, options);
            var details = (IRecorderCheckpointSessionDetails)recorder;
            RecorderCheckTargetSelection? selection = null;
            details.CheckTargetSelected += (_, eventArgs) => selection = eventArgs.Selection;
            recorder.Start();
            details.BeginCheckTargetSelection();

            recorder.SelectCheckTargetAtForTesting(window, window, center.Value);
            if (selection is null)
            {
                throw new InvalidOperationException("Check mode did not select the read-only Product cell.");
            }

            details.CaptureCheckpoint(selection, "savedProduct");
            return new
            {
                productEditor.IsEffectivelyEnabled,
                selection.CanCaptureAssertions,
                selection.ValueDescriptionError,
                selection.ValueDescription?.CurrentValueText,
                recorder.StepCount,
                recorder.PersistableStepCount,
                Preview = recorder.ExportPreview()
            };
        });

        using (Assert.Multiple())
        {
            await Assert.That(result.IsEffectivelyEnabled).IsFalse();
            await Assert.That(result.CanCaptureAssertions).IsTrue();
            await Assert.That(result.ValueDescriptionError).IsNullOrEmpty();
            await Assert.That(result.CurrentValueText).IsEqualTo("Product 42");
            await Assert.That(result.StepCount).IsEqualTo(1);
            await Assert.That(result.PersistableStepCount).IsEqualTo(1);
            await Assert.That(result.Preview).Contains("var savedProduct =");
            await Assert.That(result.Preview).Contains("GridRowKeyReader.Capture(Page.ArmComplexDataGridControl, 0)");
            await Assert.That(result.Preview).Contains("GridValueReader.ReadCellText(Page.ArmComplexDataGridControl, gridRow, \"Product\")");
            await Assert.That(result.Preview).DoesNotContain("GridRowSelector.ByCell(\"Key\", \"ARM-01\")");
            await Assert.That(result.Preview).Contains("\"Product\"");
            await Assert.That(result.Preview).DoesNotContain("ArmGridProductEditor");
        }
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
