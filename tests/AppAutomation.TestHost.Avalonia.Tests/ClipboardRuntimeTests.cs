using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.TestHost.Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Automation;
using Avalonia.Input.Platform;
using static AppAutomation.TestHost.Avalonia.Tests.HeadlessTestRuntime;

namespace AppAutomation.TestHost.Avalonia.Tests;

public sealed class ClipboardRuntimeTests
{
    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessRuntime_CopiesAndPastesThroughApplicationClipboard()
    {
        var pasteObserved = false;
        using var headless = StartHeadlessRuntime(AvaloniaTestIsolationLevel.PerAssembly);
        using var session = DesktopAppSession.Launch(AvaloniaHeadlessLaunchHost.Create(
            () =>
            {
                var input = new TextBox { Text = "Old value", MaxLength = 4 };
                AutomationProperties.SetAutomationId(input, "ClipboardInput");
                input.PastingFromClipboard += (_, _) => pasteObserved = true;
                return new Window
                {
                    Width = 80,
                    Height = 60,
                    Content = input
                };
            }));
        HeadlessRuntime.Dispatch(session.MainWindow.Show);
        var page = new ClipboardPage(new HeadlessControlResolver(session.MainWindow));

        await page.CopyTextToClipboardAsync("Item 42");
        await page.PasteTextFromClipboardAsync(
            static current => current.Input,
            "Item");
        var readTask = HeadlessRuntime.Dispatch(() =>
            session.MainWindow.Clipboard!.TryGetTextAsync());

        using (Assert.Multiple())
        {
            await Assert.That(await readTask).IsEqualTo("Item 42");
            await Assert.That(page.Input.Text).IsEqualTo("Item");
            await Assert.That(pasteObserved).IsTrue();
        }

        HeadlessRuntime.Dispatch(session.MainWindow.Close);
    }

    private sealed class ClipboardPage : UiPage
    {
        public ClipboardPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ITextBoxControl Input => Resolve<ITextBoxControl>(InputDefinition);

        private static UiControlDefinition InputDefinition { get; } = new(
            nameof(Input),
            UiControlType.TextBox,
            "ClipboardInput");
    }
}
