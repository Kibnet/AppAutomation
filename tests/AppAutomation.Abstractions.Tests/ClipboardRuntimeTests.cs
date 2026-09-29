namespace AppAutomation.Abstractions.Tests;

public sealed class ClipboardRuntimeTests
{
    [Test]
    public async Task CopyTextToClipboardAsync_ForwardsThroughConfiguredAdapters()
    {
        var runtime = new ClipboardResolver(supportsClipboard: true);
        var page = new ClipboardPage(runtime.WithAdapters(new NoOpAdapter()));

        var returnedPage = await page.CopyTextToClipboardAsync("Item 42");

        using (Assert.Multiple())
        {
            await Assert.That(returnedPage).IsSameReferenceAs(page);
            await Assert.That(runtime.Text).IsEqualTo("Item 42");
        }
    }

    [Test]
    public async Task CopyTextToClipboardAsync_RejectsRuntimeWithoutClipboardCapability()
    {
        var page = new ClipboardPage(new ClipboardResolver(supportsClipboard: false));

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => page.CopyTextToClipboardAsync("Item 42"));

        await Assert.That(exception!.Message)
            .Contains("does not support writing text to the clipboard");
    }

    private sealed class ClipboardPage(IUiControlResolver resolver) : UiPage(resolver);

    private sealed class ClipboardResolver(bool supportsClipboard) : IUiControlResolver, IUiClipboardRuntime
    {
        public string? Text { get; private set; }

        public UiRuntimeCapabilities Capabilities { get; } = new(
            "clipboard-test",
            SupportsClipboardWrite: supportsClipboard);

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class =>
            throw new NotSupportedException();

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpAdapter : IUiControlAdapter
    {
        public bool CanResolve(Type requestedType, UiControlDefinition definition) => false;

        public object Resolve(
            Type requestedType,
            UiControlDefinition definition,
            IUiControlResolver innerResolver) =>
            throw new NotSupportedException();
    }
}
