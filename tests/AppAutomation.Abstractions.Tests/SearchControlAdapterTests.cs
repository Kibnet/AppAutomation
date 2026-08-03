using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class SearchControlAdapterTests
{
    [Test]
    public async Task SameControl_HandlesEmptyAndLaterPopulatedHistory()
    {
        var fixture = new SearchControlFixture();
        var search = fixture.Resolve();

        using (Assert.Multiple())
        {
            await Assert.That(search.HistoryItems).IsEmpty();
            await Assert.That(search.IsHistoryOpen).IsFalse();
        }

        fixture.ShowHistory("orders", "customers");
        search.ApplySearchFromHistory("orders");

        using (Assert.Multiple())
        {
            await Assert.That(search.Text).IsEqualTo("orders");
            await Assert.That(search.IsHistoryOpen).IsFalse();
            await Assert.That(fixture.AppliedHistoryItem).IsEqualTo("orders");
        }
    }

    [Test]
    public async Task EnterAndClearSearch_UseSameInput()
    {
        var fixture = new SearchControlFixture();
        var search = fixture.Resolve();

        search.EnterSearch("orders");
        search.ClearSearch();

        await Assert.That(search.Text).IsEmpty();
    }

    [Test]
    public async Task DisabledActionButtons_DoNotBlockEnteringSearch()
    {
        var fixture = new SearchControlFixture(configureActionButtons: true);
        fixture.SearchButton.IsEnabled = false;
        fixture.HistoryButton.IsEnabled = false;
        fixture.Input.OnEnter = _ => fixture.SearchButton.IsEnabled = true;
        var search = fixture.Resolve();

        var wasEnabledBeforeInput = search.IsEnabled;
        search.EnterSearch("orders");

        using (Assert.Multiple())
        {
            await Assert.That(wasEnabledBeforeInput).IsTrue();
            await Assert.That(fixture.SearchButton.InvokeCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task EnterSearch_WaitsForActionEnabledAfterInputChanges()
    {
        var fixture = new SearchControlFixture(configureActionButtons: true);
        fixture.SearchButton.IsEnabled = false;
        fixture.Input.OnEnter = _ => EnableAfterDelay(fixture.SearchButton);

        fixture.CreatePage().EnterSearch(
            static page => page.TableSearch,
            "orders",
            timeoutMs: 1000);

        using (Assert.Multiple())
        {
            await Assert.That(fixture.SearchButton.InvokeCount).IsEqualTo(1);
            await Assert.That(fixture.SearchButton.WasInvokedWhileDisabled).IsFalse();
        }
    }

    [Test]
    public async Task ApplySearchFromHistory_WaitsForHistoryButtonEnabled()
    {
        var fixture = new SearchControlFixture(configureActionButtons: true);
        fixture.HistoryButton.IsEnabled = false;
        fixture.HistoryButton.OnInvoke = () => fixture.ShowHistory("orders");
        EnableAfterDelay(fixture.HistoryButton);

        fixture.CreatePage().ApplySearchFromHistory(
            static page => page.TableSearch,
            "orders",
            timeoutMs: 1000);

        using (Assert.Multiple())
        {
            await Assert.That(fixture.HistoryButton.InvokeCount).IsEqualTo(1);
            await Assert.That(fixture.HistoryButton.WasInvokedWhileDisabled).IsFalse();
            await Assert.That(fixture.AppliedHistoryItem).IsEqualTo("orders");
        }
    }

    [Test]
    public async Task HistoryItems_AreScopedToConfiguredHistoryRoot()
    {
        var fixture = new SearchControlFixture(configureHistoryRoot: true);
        fixture.ShowHistory("orders");

        _ = fixture.Resolve().HistoryItems;

        using (Assert.Multiple())
        {
            await Assert.That(fixture.LastHistoryDefinition!.Scope!.LocatorValue)
                .IsEqualTo("TableSearchHistoryRoot");
            await Assert.That(fixture.LastHistoryDefinition.Scope.LocatorKind)
                .IsEqualTo(UiLocatorKind.AutomationId);
            await Assert.That(fixture.LastHistoryDefinition.Scope.AnchorLocatorValue)
                .IsEqualTo("TableSearchInput");
        }
    }

    [Test]
    public async Task ApplyHistory_DoesNotTogglePopupAfterOneOpenRequest()
    {
        var fixture = new SearchControlFixture(configureActionButtons: true);
        var search = fixture.Resolve();

        search.OpenHistory();
        fixture.ShowHistory("orders");
        fixture.SetHistoryAvailability(false);
        search.ApplySearchFromHistory("orders");

        using (Assert.Multiple())
        {
            await Assert.That(fixture.HistoryButton.InvokeCount).IsEqualTo(1);
            await Assert.That(fixture.AppliedHistoryItem).IsEqualTo("orders");
        }
    }

    private sealed class SearchControlFixture : IUiControlResolver
    {
        private readonly FakeAvailabilityControl _historyRoot = new("TableSearchHistoryRoot");
        private readonly FakeHistoryItems _history;
        private readonly IUiControlResolver _resolver;

        public SearchControlFixture(
            bool configureActionButtons = false,
            bool configureHistoryRoot = false)
        {
            _history = new FakeHistoryItems(ApplyHistoryItem);
            _resolver = this.WithSearchControl(
                "TableSearch",
                SearchControlParts.ByAutomationIds(
                    "TableSearchInput",
                    "SearchHistoryItemButton",
                    searchButtonAutomationId: configureActionButtons ? "TableSearchButton" : null,
                    historyOpenButtonAutomationId: configureActionButtons ? "TableSearchHistoryButton" : null,
                    historyRootAutomationId: configureHistoryRoot ? "TableSearchHistoryRoot" : null));
        }

        private void ApplyHistoryItem(string value)
        {
            AppliedHistoryItem = value;
            Input.Enter(value);
            _history.IsAvailable = false;
        }

        public string? AppliedHistoryItem { get; private set; }

        public FakeTextBox Input { get; } = new("TableSearchInput");

        public FakeButton SearchButton { get; } = new("TableSearchButton");

        public FakeButton HistoryButton { get; } = new("TableSearchHistoryButton");

        public UiControlDefinition? LastHistoryDefinition { get; private set; }

        public UiRuntimeCapabilities Capabilities { get; } = new("search-control-test");

        public ISearchControl Resolve()
        {
            return _resolver.Resolve<ISearchControl>(new UiControlDefinition(
                "TableSearch",
                UiControlType.Search,
                "TableSearch"));
        }

        public SearchControlPage CreatePage() => new(_resolver);

        public void ShowHistory(params string[] items)
        {
            _history.SetItems(items);
            _history.IsAvailable = true;
        }

        public void SetHistoryAvailability(bool isAvailable) => _history.IsAvailable = isAvailable;

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            if (typeof(TControl) == typeof(ITextBoxControl))
            {
                return (TControl)(object)Input;
            }

            if (typeof(TControl) == typeof(ISearchHistoryItemsControl))
            {
                LastHistoryDefinition = definition;
                return (TControl)(object)_history;
            }

            if (typeof(TControl) == typeof(IButtonControl))
            {
                var button = definition.LocatorValue switch
                {
                    "TableSearchButton" => SearchButton,
                    "TableSearchHistoryButton" => HistoryButton,
                    _ => throw new InvalidOperationException($"Unexpected button: {definition.LocatorValue}.")
                };
                return (TControl)(object)button;
            }

            if (typeof(TControl) == typeof(IUiControl)
                && definition.LocatorValue == "TableSearchHistoryRoot")
            {
                return (TControl)(object)_historyRoot;
            }

            throw new InvalidOperationException($"Unexpected control type: {typeof(TControl).Name}.");
        }
    }

    private abstract class FakeControl : IUiControl
    {
        protected FakeControl(string automationId)
        {
            AutomationId = automationId;
        }

        public string AutomationId { get; }

        public string Name => AutomationId;

        public bool IsEnabled { get; set; } = true;
    }

    private sealed class FakeTextBox : FakeControl, ITextBoxControl
    {
        public FakeTextBox(string automationId) : base(automationId)
        {
        }

        public string Text { get; set; } = string.Empty;

        public Action<string>? OnEnter { get; set; }

        public void Enter(string value)
        {
            Text = value;
            OnEnter?.Invoke(value);
        }
    }

    private sealed class FakeButton : FakeControl, IButtonControl
    {
        public FakeButton(string automationId) : base(automationId)
        {
        }

        public int InvokeCount { get; private set; }

        public bool WasInvokedWhileDisabled { get; private set; }

        public Action? OnInvoke { get; set; }

        public void Invoke()
        {
            if (!IsEnabled)
            {
                WasInvokedWhileDisabled = true;
                throw new InvalidOperationException($"Button '{AutomationId}' is disabled.");
            }

            InvokeCount++;
            OnInvoke?.Invoke();
        }
    }

    private sealed class SearchControlPage : UiPage
    {
        private static readonly UiControlDefinition TableSearchDefinition = new(
            "TableSearch",
            UiControlType.Search,
            "TableSearch");

        public SearchControlPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ISearchControl TableSearch => Resolve<ISearchControl>(TableSearchDefinition);
    }

    private static void EnableAfterDelay(FakeControl control)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            control.IsEnabled = true;
        });
    }

    private sealed class FakeAvailabilityControl : FakeControl, IUiControlAvailability
    {
        public FakeAvailabilityControl(string automationId) : base(automationId)
        {
        }

        public bool IsAvailable { get; set; } = true;
    }

    private sealed class FakeHistoryItems : FakeControl, ISearchHistoryItemsControl
    {
        private readonly Action<string> _apply;
        private string[] _items = [];

        public FakeHistoryItems(Action<string> apply) : base("SearchHistoryItemButton")
        {
            _apply = apply;
        }

        public bool IsAvailable { get; set; }

        public IReadOnlyList<string> Items => _items;

        public void SetItems(string[] items) => _items = items;

        public void Apply(string itemText)
        {
            if (!_items.Contains(itemText, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"History item '{itemText}' was not found.");
            }

            _apply(itemText);
        }
    }
}
