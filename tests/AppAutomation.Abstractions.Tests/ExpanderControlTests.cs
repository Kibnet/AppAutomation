using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class ExpanderControlTests
{
    [Test]
    public async Task SetExpanded_ChangesStateOnlyWhenNeeded()
    {
        var control = new FakeExpanderControl();
        var page = new ExpanderPage(control);

        page.SetExpanded(static candidate => candidate.DetailsExpander, true)
            .SetExpanded(static candidate => candidate.DetailsExpander, true)
            .SetExpanded(static candidate => candidate.DetailsExpander, false)
            .WaitUntilIsExpanded(static candidate => candidate.DetailsExpander, false);

        using (Assert.Multiple())
        {
            await Assert.That(control.IsExpanded).IsFalse();
            await Assert.That(control.ExpandCount).IsEqualTo(1);
            await Assert.That(control.CollapseCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task DisabledExpander_FailsBeforeChangingState()
    {
        var control = new FakeExpanderControl { IsEnabled = false };
        var page = new ExpanderPage(control);

        await Assert.That(() => page.SetExpanded(
                static candidate => candidate.DetailsExpander,
                true,
                timeoutMs: 100))
            .Throws<UiOperationException>();
        await Assert.That(control.ExpandCount).IsEqualTo(0);
    }

    private sealed class ExpanderPage : UiPage
    {
        private static readonly UiControlDefinition Definition = new(
            "DetailsExpander",
            UiControlType.Expander,
            "DetailsExpander");

        public ExpanderPage(IExpanderControl control)
            : base(new FakeResolver(control))
        {
        }

        public IExpanderControl DetailsExpander => Resolve<IExpanderControl>(Definition);
    }

    private sealed class FakeResolver(IExpanderControl control) : IUiControlResolver
    {
        public UiRuntimeCapabilities Capabilities { get; } = new("expander-test");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            return definition.LocatorValue == "DetailsExpander" && control is TControl typed
                ? typed
                : throw new InvalidOperationException($"Unexpected control '{definition.LocatorValue}'.");
        }
    }

    private sealed class FakeExpanderControl : IExpanderControl
    {
        public string AutomationId => "DetailsExpander";

        public string Name => "Details";

        public bool IsEnabled { get; set; } = true;

        public bool IsExpanded { get; private set; }

        public int ExpandCount { get; private set; }

        public int CollapseCount { get; private set; }

        public void Expand()
        {
            ExpandCount++;
            IsExpanded = true;
        }

        public void Collapse()
        {
            CollapseCount++;
            IsExpanded = false;
        }
    }
}
