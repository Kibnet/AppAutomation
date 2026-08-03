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

    private sealed class SearchControlFixture : IUiControlResolver
    {
        private readonly FakeTextBox _input = new("TableSearchInput");
        private readonly FakeHistoryItems _history;
        private readonly IUiControlResolver _resolver;

        public SearchControlFixture()
        {
            _history = new FakeHistoryItems(value =>
            {
                AppliedHistoryItem = value;
                _input.Enter(value);
                _history.IsAvailable = false;
            });
            _resolver = this.WithSearchControl(
                "TableSearch",
                SearchControlParts.ByAutomationIds(
                    "TableSearchInput",
                    "SearchHistoryItemButton"));
        }

        public string? AppliedHistoryItem { get; private set; }

        public UiRuntimeCapabilities Capabilities { get; } = new("search-control-test");

        public ISearchControl Resolve()
        {
            return _resolver.Resolve<ISearchControl>(new UiControlDefinition(
                "TableSearch",
                UiControlType.Search,
                "TableSearch"));
        }

        public void ShowHistory(params string[] items)
        {
            _history.SetItems(items);
            _history.IsAvailable = true;
        }

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            if (typeof(TControl) == typeof(ITextBoxControl))
            {
                return (TControl)(object)_input;
            }

            if (typeof(TControl) == typeof(ISearchHistoryItemsControl))
            {
                return (TControl)(object)_history;
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

        public bool IsEnabled => true;
    }

    private sealed class FakeTextBox : FakeControl, ITextBoxControl
    {
        public FakeTextBox(string automationId) : base(automationId)
        {
        }

        public string Text { get; set; } = string.Empty;

        public void Enter(string value) => Text = value;
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
