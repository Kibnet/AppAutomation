using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class ReadOnlyStateReaderTests
{
    [Test]
    public async Task Read_DistinguishesReadOnlyFromEnabledAndReadsCurrentState()
    {
        var control = new ReadOnlyControl { IsReadOnly = false };
        await Assert.That(control.IsEnabled).IsTrue();
        await Assert.That(ReadOnlyStateReader.Read(control)).IsFalse();

        control.IsReadOnly = true;
        await Assert.That(control.IsEnabled).IsTrue();
        await Assert.That(ReadOnlyStateReader.Read(control)).IsTrue();

        control.IsReadOnly = false;
        await Assert.That(ReadOnlyStateReader.Read(control)).IsFalse();
    }

    [Test]
    public async Task Read_RejectsUnavailableStateInsteadOfAssumingEditable()
    {
        var control = new ReadOnlyControl { CanRead = false };
        await Assert.That(() => ReadOnlyStateReader.Read(control))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Read_RejectsControlWithoutReadOnlyCapability()
    {
        var control = new PlainControl();
        await Assert.That(() => ReadOnlyStateReader.Read(control))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TextInputProperty_RejectsUnavailableReadOnlyState()
    {
        ITextBoxControl control = new PlainTextInput();
        await Assert.That(() => control.IsReadOnly)
            .Throws<InvalidOperationException>();
    }

    private sealed class ReadOnlyControl : IReadOnlyStateControl
    {
        public string AutomationId => "ObservedInput";
        public string Name => "Observed input";
        public bool IsEnabled => true;
        public bool IsReadOnly { get; set; }
        public bool CanRead { get; set; } = true;

        public bool TryGetIsReadOnly(out bool isReadOnly)
        {
            isReadOnly = IsReadOnly;
            return CanRead;
        }
    }

    private sealed class PlainControl : IUiControl
    {
        public string AutomationId => "PlainControl";
        public string Name => "Plain control";
        public bool IsEnabled => true;
    }

    private sealed class PlainTextInput : ITextBoxControl
    {
        public string AutomationId => "PlainTextInput";
        public string Name => "Plain text input";
        public bool IsEnabled => true;
        public string Text { get; set; } = string.Empty;
        public void Enter(string value) => Text = value;
    }
}
