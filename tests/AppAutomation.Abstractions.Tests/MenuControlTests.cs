using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class MenuControlTests
{
    [Test]
    public async Task InvokeMenuItem_UsesDirectItemAndOrderedMenuPath()
    {
        var resolver = new MenuResolver();
        var page = new MenuPage(resolver);

        page.InvokeMenuItem(static candidate => candidate.RefreshItem)
            .InvokeMenuItem(static candidate => candidate.MainMenu, ["Actions", "Export", "Snapshot"]);

        using (Assert.Multiple())
        {
            await Assert.That(resolver.DirectInvocations).IsEqualTo(1);
            await Assert.That(resolver.DirectTimeoutMs).IsEqualTo(5000);
            await Assert.That(resolver.MenuTimeoutMs).IsEqualTo(5000);
            await Assert.That(resolver.LastPath).IsEquivalentTo(["Actions", "Export", "Snapshot"]);
        }
    }

    [Test]
    public async Task InvokeMenuItem_RejectsEmptyPathBeforeRuntimeMutation()
    {
        var resolver = new MenuResolver();
        var page = new MenuPage(resolver);

        await Assert.That(() => page.InvokeMenuItem(static candidate => candidate.MainMenu, []))
            .Throws<ArgumentException>();
        await Assert.That(resolver.LastPath).IsNull();
    }

    private sealed class MenuPage : UiPage
    {
        private static readonly UiControlDefinition MenuDefinition = new(
            "MainMenu",
            UiControlType.Menu,
            "MainMenu");

        private static readonly UiControlDefinition ItemDefinition = new(
            "RefreshItem",
            UiControlType.MenuItem,
            "RefreshItem");

        public MenuPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IMenuControl MainMenu => Resolve<IMenuControl>(MenuDefinition);

        public IMenuItemControl RefreshItem => Resolve<IMenuItemControl>(ItemDefinition);
    }

    private sealed class MenuResolver : IUiControlResolver
    {
        private readonly FakeMenu _menu;
        private readonly FakeMenuItem _item;

        public MenuResolver()
        {
            _menu = new FakeMenu(this);
            _item = new FakeMenuItem(this);
        }

        public int DirectInvocations { get; private set; }

        public int DirectTimeoutMs { get; private set; }

        public int MenuTimeoutMs { get; private set; }

        public IReadOnlyList<string>? LastPath { get; private set; }

        public UiRuntimeCapabilities Capabilities { get; } = new("menu-test");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            object control = definition.ControlType switch
            {
                UiControlType.Menu => _menu,
                UiControlType.MenuItem => _item,
                _ => throw new InvalidOperationException($"Unexpected type '{definition.ControlType}'.")
            };
            return (TControl)control;
        }

        private sealed class FakeMenu(MenuResolver owner) : IMenuControl
        {
            public string AutomationId => "MainMenu";

            public string Name => "Main menu";

            public bool IsEnabled => true;

            public void InvokeItem(IReadOnlyList<string> path, int timeoutMs)
            {
                owner.LastPath = path.ToArray();
                owner.MenuTimeoutMs = timeoutMs;
            }
        }

        private sealed class FakeMenuItem(MenuResolver owner) : IMenuItemControl
        {
            public string AutomationId => "RefreshItem";

            public string Name => "Refresh";

            public bool IsEnabled => true;

            public void Invoke(int timeoutMs)
            {
                owner.DirectInvocations++;
                owner.DirectTimeoutMs = timeoutMs;
            }
        }
    }
}
