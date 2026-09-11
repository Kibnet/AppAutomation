using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class SpinnerControlTests
{
    [Test]
    public async Task SpinnerTextBoxProxy_ReplaysLogicalSpinnerValue()
    {
        var textBox = new FakeTextBoxControl("QuantitySpinnerInput", "8");
        var resolver = new FakeResolver(textBox)
            .WithSpinnerTextBoxProxy("QuantitySpinner", "QuantitySpinnerInput");
        var page = new SpinnerPage(resolver);

        page.SetSpinnerValue(static candidate => candidate.QuantitySpinner, 12.5)
            .WaitUntilValueEquals(static candidate => candidate.QuantitySpinner, 12.5);

        using (Assert.Multiple())
        {
            await Assert.That(textBox.Text).IsEqualTo("12.5");
            await Assert.That(textBox.EnterCount).IsEqualTo(1);
            await Assert.That(page.QuantitySpinner.AutomationId).IsEqualTo("QuantitySpinner");
        }
    }

    private static class SpinnerPageDefinitions
    {
        public static readonly UiControlDefinition QuantitySpinner = new(
            "QuantitySpinner",
            UiControlType.Spinner,
            "QuantitySpinner");
    }

    private sealed class SpinnerPage : UiPage
    {
        public SpinnerPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ISpinnerControl QuantitySpinner => Resolve<ISpinnerControl>(SpinnerPageDefinitions.QuantitySpinner);
    }

    private sealed class FakeResolver(FakeTextBoxControl textBox) : IUiControlResolver
    {
        public UiRuntimeCapabilities Capabilities { get; } = new("fake-runtime");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            if (definition.ControlType == UiControlType.TextBox
                && string.Equals(definition.LocatorValue, textBox.AutomationId, StringComparison.Ordinal)
                && textBox is TControl typed)
            {
                return typed;
            }

            throw new InvalidOperationException($"Unknown control '{definition.PropertyName}'.");
        }
    }

    private sealed class FakeTextBoxControl(string automationId, string text) : ITextBoxControl, IUiControlAvailability
    {
        public string AutomationId { get; } = automationId;

        public string Name => AutomationId;

        public bool IsEnabled => true;

        public bool IsAvailable => true;

        public string Text { get; set; } = text;

        public int EnterCount { get; private set; }

        public void Enter(string value)
        {
            EnterCount++;
            Text = value;
        }
    }
}
