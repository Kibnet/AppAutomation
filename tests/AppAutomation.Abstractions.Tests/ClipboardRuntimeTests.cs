namespace AppAutomation.Abstractions.Tests;

public sealed class ClipboardRuntimeTests
{
    [Test]
    public async Task CopyAndPasteText_ForwardThroughConfiguredTextBoxAdapter()
    {
        var runtime = new ClipboardResolver(supportsClipboard: true);
        var page = new ClipboardPage(runtime.WithAdapters(new ClipboardTextBoxAdapter()));

        var returnedPage = await page
            .CopyTextToClipboardAsync("Item 42");
        await page.PasteTextFromClipboardAsync(
            static current => current.Input,
            "Item 42");

        using (Assert.Multiple())
        {
            await Assert.That(returnedPage).IsSameReferenceAs(page);
            await Assert.That(runtime.Text).IsEqualTo("Item 42");
            await Assert.That(runtime.PasteCount).IsEqualTo(1);
            await Assert.That(page.Input.Text).IsEqualTo("Item 42");
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

    [Test]
    public async Task PasteTextFromClipboardAsync_RejectsInvalidTimeoutBeforePaste()
    {
        var runtime = new ClipboardResolver(supportsClipboard: true);
        var page = new ClipboardPage(runtime);
        runtime.Text = "Item 42";

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            page.PasteTextFromClipboardAsync(
                static current => current.Input,
                "Item 42",
                timeoutMs: 0));

        await Assert.That(runtime.PasteCount).IsEqualTo(0);
    }

    [Test]
    public async Task PasteTextFromClipboardAsync_HonorsCancellationDuringPostconditionWait()
    {
        using var cancellation = new CancellationTokenSource();
        var runtime = new ClipboardResolver(supportsClipboard: true)
        {
            Text = "Item 42",
            ApplyPaste = false
        };
        runtime.CancelOnNextTextRead = cancellation.Cancel;
        var page = new ClipboardPage(runtime);

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            page.PasteTextFromClipboardAsync(
                static current => current.Input,
                "Item 42",
                timeoutMs: 5000,
                cancellationToken: cancellation.Token));

        await Assert.That(runtime.PasteCount).IsEqualTo(1);
    }

    private sealed class ClipboardPage(IUiControlResolver resolver) : UiPage(resolver)
    {
        public ITextBoxControl Input => Resolve<ITextBoxControl>(InputDefinition);

        private static UiControlDefinition InputDefinition { get; } = new(
            nameof(Input),
            UiControlType.TextBox,
            "Input");
    }

    private sealed class ClipboardResolver : IUiControlResolver, IUiClipboardRuntime
    {
        private readonly FakeTextBox _input;

        public ClipboardResolver(bool supportsClipboard)
        {
            Capabilities = new UiRuntimeCapabilities("clipboard-test")
            {
                SupportsClipboardText = supportsClipboard
            };
            _input = new FakeTextBox(this);
        }

        public string? Text { get; set; }

        public int PasteCount { get; private set; }

        public bool ApplyPaste { get; init; } = true;

        public Action? CancelOnNextTextRead { get; set; }

        public UiRuntimeCapabilities Capabilities { get; }

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class =>
            typeof(TControl) == typeof(ITextBoxControl)
                ? (TControl)(object)_input
                : throw new NotSupportedException();

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Text = text;
            return Task.CompletedTask;
        }

        public Task PasteAsync(FakeTextBox target, int timeoutMs, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            cancellationToken.ThrowIfCancellationRequested();
            PasteCount++;
            if (ApplyPaste)
            {
                target.Enter(Text ?? string.Empty);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeTextBox(ClipboardResolver owner) : IClipboardPasteTarget
    {
        private string _text = string.Empty;

        public string AutomationId => "Input";
        public string Name => "Input";
        public bool IsEnabled => true;

        public string Text
        {
            get
            {
                var callback = owner.CancelOnNextTextRead;
                owner.CancelOnNextTextRead = null;
                callback?.Invoke();
                return _text;
            }
            set => _text = value;
        }

        public void Enter(string value) => _text = value;

        public Task PasteFromClipboardAsync(int timeoutMs, CancellationToken cancellationToken = default) =>
            owner.PasteAsync(this, timeoutMs, cancellationToken);
    }

    private sealed class ClipboardTextBoxAdapter : IUiControlAdapter
    {
        public bool CanResolve(Type requestedType, UiControlDefinition definition) =>
            requestedType == typeof(ITextBoxControl)
            && string.Equals(definition.PropertyName, nameof(ClipboardPage.Input), StringComparison.Ordinal);

        public object Resolve(
            Type requestedType,
            UiControlDefinition definition,
            IUiControlResolver innerResolver)
        {
            var inner = innerResolver.Resolve<ITextBoxControl>(definition);
            return new WrappedClipboardTextBox((IClipboardPasteTarget)inner);
        }
    }

    private sealed class WrappedClipboardTextBox(IClipboardPasteTarget inner) : IClipboardPasteTarget
    {
        public string AutomationId => inner.AutomationId;
        public string Name => inner.Name;
        public bool IsEnabled => inner.IsEnabled;
        public string Text { get => inner.Text; set => inner.Text = value; }
        public void Enter(string value) => inner.Enter(value);

        public Task PasteFromClipboardAsync(int timeoutMs, CancellationToken cancellationToken = default) =>
            inner.PasteFromClipboardAsync(timeoutMs, cancellationToken);
    }
}
