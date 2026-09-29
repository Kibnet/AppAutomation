using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TestHost.Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using static AppAutomation.TestHost.Avalonia.Tests.HeadlessTestRuntime;

namespace AppAutomation.TestHost.Avalonia.Tests;

public sealed class ClipboardRuntimeTests
{
    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessRuntime_CopiesTextToApplicationClipboard()
    {
        using var headless = StartHeadlessRuntime(AvaloniaTestIsolationLevel.PerAssembly);
        using var session = DesktopAppSession.Launch(AvaloniaHeadlessLaunchHost.Create(
            () => new Window { Width = 80, Height = 60 }));
        HeadlessRuntime.Dispatch(session.MainWindow.Show);
        var page = new ClipboardPage(new HeadlessControlResolver(session.MainWindow));

        await page.CopyTextToClipboardAsync("Item 42");
        var readTask = HeadlessRuntime.Dispatch(() =>
            session.MainWindow.Clipboard!.TryGetTextAsync());

        await Assert.That(await readTask).IsEqualTo("Item 42");
        HeadlessRuntime.Dispatch(session.MainWindow.Close);
    }

    private sealed class ClipboardPage : UiPage
    {
        public ClipboardPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }
    }
}
