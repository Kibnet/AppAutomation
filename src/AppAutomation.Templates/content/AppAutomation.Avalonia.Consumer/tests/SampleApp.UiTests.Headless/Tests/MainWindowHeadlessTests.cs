using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.Abstractions;
using AppAutomation.TUnit;
using SampleApp.AppAutomation.TestHost;
using SampleApp.UiTests.Authoring.Pages;
using SampleApp.UiTests.Authoring.Tests;
using TUnit.Core;

namespace SampleApp.UiTests.Headless.Tests;

[InheritsTests]
public sealed class MainWindowHeadlessTests
    : MainWindowScenariosBase<MainWindowHeadlessTests.HeadlessRuntimeSession>
{
    [Test]
    [NotInParallel("DesktopUi")]
    public void CaptureScreenshotExample()
    {
        var path = Session.Inner.CaptureScreenshot(Path.Combine(
            AppContext.BaseDirectory,
            "artifacts", "headless-screenshots", Guid.NewGuid().ToString("N"), "main-window.png"));
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Headless screenshot was not saved: {path}");
        }

        Console.WriteLine($"Headless screenshot: {path}");
    }

    protected override HeadlessRuntimeSession LaunchSession()
    {
        var inner = DesktopAppSession.Launch(SampleAppAppLaunchHost.CreateHeadlessLaunchOptions());
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
        var path = Session.Inner.CaptureScreenshot(Path.Combine(
            AppContext.BaseDirectory,
            "artifacts", "ui-failures", "avalonia-headless",
            Guid.NewGuid().ToString("N"), "test-failure.png"));
        return ValueTask.FromResult<IReadOnlyList<UiFailureArtifact>>(
        [new UiFailureArtifact("screenshot", "test-failure",
            Path.GetRelativePath(AppContext.BaseDirectory, path),
            "image/png", false, path)]);
    }

    protected override MainWindowPage CreatePage(HeadlessRuntimeSession session)
    {
        return new MainWindowPage(new HeadlessControlResolver(session.Inner.MainWindow));
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
            }
        }
    }
}
