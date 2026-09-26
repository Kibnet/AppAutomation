using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Media.Imaging;
using DotnetDebug.AppAutomation.Authoring.Pages;
using AppAutomation.Abstractions;
using DotnetDebug.AppAutomation.TestHost;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

public sealed class HeadlessFailureDiagnosticsTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task WaitUntilNameEquals_OnTimeout_CollectsHeadlessArtifacts()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        HeadlessRuntime.Dispatch(session.MainWindow.Show);
        var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow));
        await Assert.That(page.Capabilities.SupportsScreenshots).IsTrue();

        UiOperationException? exception = null;
        try
        {
            page.WaitUntilNameEquals(static candidate => candidate.ResultText, "Never matches", timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            exception = ex;
        }

        UiOperationException? secondException = null;
        try
        {
            page.WaitUntilNameEquals(static candidate => candidate.ResultText, "Still never matches", timeoutMs: 60);
        }
        catch (UiOperationException ex)
        {
            secondException = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.FailureContext.AdapterId).IsEqualTo("avalonia-headless");
            await Assert.That(exception.FailureContext.ControlPropertyName).IsEqualTo("ResultText");
            await Assert.That(exception.FailureContext.LocatorValue).IsEqualTo("ResultText");
            await Assert.That(exception.FailureContext.LocatorKind).IsEqualTo(UiLocatorKind.AutomationId);
            await Assert.That(exception.FailureContext.Artifacts.Select(static artifact => artifact.Kind).ToArray()).Contains("logical-tree");
            await Assert.That(exception.FailureContext.Artifacts.Select(static artifact => artifact.Kind).ToArray()).Contains("control-state");
            var screenshot = exception.FailureContext.Artifacts.Single(static artifact => artifact.Kind == "screenshot");
            var screenshotPath = Path.GetFullPath(screenshot.RelativePath, AppContext.BaseDirectory);
            await Assert.That(File.Exists(screenshotPath)).IsTrue();
            await Assert.That(new FileInfo(screenshotPath).Length > 100).IsTrue();
            using var bitmap = new Bitmap(screenshotPath);
            await Assert.That(bitmap.PixelSize.Width > 200 && bitmap.PixelSize.Height > 200).IsTrue();
            await Assert.That(secondException).IsNotNull();
            var secondScreenshot = secondException!.FailureContext.Artifacts.Single(static artifact => artifact.Kind == "screenshot");
            var secondPath = Path.GetFullPath(secondScreenshot.RelativePath, AppContext.BaseDirectory);
            await Assert.That(secondPath).IsNotEqualTo(screenshotPath);
            await Assert.That(File.Exists(secondPath)).IsTrue();
            await Assert.That(exception.InnerException is TimeoutException).IsTrue();
        }

        HeadlessRuntime.Dispatch(session.MainWindow.Close);
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task FailureCapture_WhenPathIsBlocked_KeepsOriginalErrorAndTree()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var blockedRoot = Path.Combine(AppContext.BaseDirectory, "artifacts", "blocked-headless-root", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(blockedRoot)!);
        File.WriteAllText(blockedRoot, "This file blocks screenshot directory creation.");
        HeadlessRuntime.Dispatch(session.MainWindow.Show);
        try
        {
            var page = new MainWindowPage(new HeadlessControlResolver(session.MainWindow, new HeadlessScreenshotOptions
            {
                ArtifactDirectory = blockedRoot
            }));
            UiOperationException? exception = null;
            try
            {
                page.WaitUntilNameEquals(static candidate => candidate.ResultText, "Never matches", timeoutMs: 60);
            }
            catch (UiOperationException ex)
            {
                exception = ex;
            }

            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.InnerException is TimeoutException).IsTrue();
            await Assert.That(exception.FailureContext.Artifacts.Select(static artifact => artifact.Kind).ToArray())
                .Contains("screenshot-unavailable");
            await Assert.That(exception.FailureContext.Artifacts.Select(static artifact => artifact.Kind).ToArray())
                .Contains("logical-tree");
            await Assert.That(exception.FailureContext.Artifacts.Select(static artifact => artifact.Kind).ToArray())
                .Contains("control-state");
        }
        finally
        {
            HeadlessRuntime.Dispatch(session.MainWindow.Close);
            File.Delete(blockedRoot);
        }
    }
}
