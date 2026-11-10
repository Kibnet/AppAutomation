using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class SingleSelectControlTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LocalSelectionDoesNotRepeatActivationWhenCommittedValueIsUnchanged(bool initiallyOpen)
    {
        var open = initiallyOpen;
        var opens = 0;
        var openLookups = 0;
        var activations = 0;
        const string input = "Target";
        const string committed = "Original";
        var budget = UiOperationTimeoutBudget.Start(300, "local selection");
        var button = new FakeButton("Open", () => { opens++; open = true; });
        var exception = Assert.Throws<TimeoutException>(() => LocalComboBoxSelection.Select(
            "local editor", input, budget,
            () => open,
            () => LocalComboBoxSelection.WaitForOpenButton("local editor",
                () => ++openLookups == 1 ? null : button, budget).Invoke(),
            () => open ? new LocalComboBoxSurface<string>(input) : throw new InvalidOperationException("Looked up a closed popup."),
            _ => { activations++; open = false; return []; },
            _ => !open && string.Equals(committed, input, StringComparison.Ordinal)));

        await Assert.That(exception.Message).Contains("did not apply");
        await Assert.That(opens).IsEqualTo(initiallyOpen ? 0 : 1);
        await Assert.That(openLookups).IsEqualTo(initiallyOpen ? 0 : 2);
        await Assert.That(activations).IsEqualTo(1);
    }

    [Test]
    public async Task LocalSelectionUsesWrappedResultsWithoutOpeningAgain()
    {
        var actions = new List<string>();
        var results = new FakeListBox(["Target"], actions);
        var open = false;
        var parts = new LocalComboBoxParts(
            static () => null, static () => null, () => open ? results : null, () => open, static () => null,
            list =>
            {
                list.SelectItem("Target");
                return list.Items.Select(item => item.Text ?? string.Empty).ToArray();
            });

        var captions = LocalComboBoxSelection.Select<object>("local editor", "Target",
            UiOperationTimeoutBudget.Start(1000, "local selection"), () => open,
            () => { actions.Add("Open"); open = true; },
            () => parts.ResolveSurface<object>(static _ => null),
            _ => throw new InvalidOperationException("The wrapper is not a native list."));

        await Assert.That(string.Join(" > ", actions)).IsEqualTo("Open > Select:Target");
        await Assert.That(captions).IsEquivalentTo(["Target"]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConfirmedListSelection_UsesConfiguredPrimitiveParts(bool useSearchPicker)
    {
        var fixture = new SingleSelectFixture("Item 42", "Search result");
        string? ReadSelection() => useSearchPicker
            ? fixture.Page.ItemPicker.SelectedItemText
            : fixture.Page.CategorySelector.SelectedItem?.Text;

        fixture.CommittedValue.Text = "Initial item";
        fixture.Input.Enter("Uncommitted query");
        await Assert.That(ReadSelection()).IsEqualTo("Initial item");
        await Assert.That(fixture.Actions).IsEmpty();

        if (useSearchPicker)
        {
            fixture.Page.SearchAndSelect(static page => page.ItemPicker, "Item", "Item 42");
        }
        else
        {
            fixture.Page.SelectComboItem(static page => page.CategorySelector, "Item 42");
        }

        using (Assert.Multiple())
        {
            await Assert.That(string.Join(" > ", fixture.Actions)).IsEqualTo("Open > Select:Item 42 > Confirm");
            await Assert.That(ReadSelection()).IsEqualTo("Item 42");
            await Assert.That(((FakeListBox)fixture.Results).ItemsReadCount).IsEqualTo(useSearchPicker ? 2 : 1);
        }

        fixture.Input.Enter("Another query");
        await Assert.That(ReadSelection()).IsEqualTo("Item 42");
        fixture.CommittedValue.Text = string.Empty;
        await Assert.That(ReadSelection()).IsNull();
        fixture.CommittedValue.Text = "Replacement item";
        await Assert.That(ReadSelection()).IsEqualTo("Replacement item");
        fixture.CommittedValue.IsAvailable = false;
        Assert.Throws<UiControlResolutionException>(() => ReadSelection());
        fixture.CommittedValue.IsAvailable = true;
        fixture.ValueAvailable = false;
        var unavailable = Assert.Throws<UiControlResolutionException>(() => ReadSelection());
        await Assert.That(unavailable.Message).Contains("CategorySelectorValue");
    }

    [Test]
    public async Task ConfirmedComboSelection_UsesExistingComboCapability()
    {
        var fixture = new SingleSelectFixture(
            SingleSelectResultsKind.ComboBox,
            ["Item 42", "Search result"]);

        fixture.Page.SelectComboItem(static page => page.CategorySelector, "Search result");

        using (Assert.Multiple())
        {
            await Assert.That(string.Join(" > ", fixture.Actions))
                .IsEqualTo("Open > Expand > Select:Search result > Confirm");
            await Assert.That(fixture.Page.CategorySelector.SelectedItem?.Text).IsEqualTo("Search result");
        }
    }

    [Test]
    public async Task ImmediateListSelection_WithoutSelectedValueLocator_UsesResultsSelection()
    {
        var fixture = new SingleSelectFixture(
            SingleSelectResultsKind.ListBox, ["Item 42", "Search result"], SingleSelectCommitMode.Immediate);

        fixture.Page.SelectComboItem(static page => page.CategorySelector, "Item 42");

        await Assert.That(string.Join(" > ", fixture.Actions)).IsEqualTo("Open > Select:Item 42");
        await Assert.That(fixture.Page.CategorySelector.SelectedItem?.Text).IsEqualTo("Item 42");
    }

    [Test]
    public async Task Expand_RejectsDetachedResultsAndReadsReplacedItems()
    {
        var fixture = new SingleSelectFixture(
            SingleSelectResultsKind.ListBox, ["First", "Removed"], SingleSelectCommitMode.Immediate);
        var detached = (FakeListBox)fixture.Results;
        detached.IsAvailable = false;
        fixture.DetachedResults = detached;
        fixture.Results = new FakeListBox(["First"], fixture.Actions);
        var combo = fixture.Page.CategorySelector;

        await Assert.That(combo.Items).IsEmpty();
        combo.Expand();
        await Assert.That(fixture.PopupRoot.IsAvailable).IsTrue();
        await Assert.That(combo.Items.Select(item => item.Text).ToArray()).IsEquivalentTo(["First"]);

        fixture.PopupRoot.IsAvailable = false;
        fixture.Results = new FakeListBox(["Replacement"], fixture.Actions);
        combo.Expand();
        combo.Expand();

        await Assert.That(fixture.PopupRoot.IsAvailable).IsTrue();
        await Assert.That(combo.Items.Select(item => item.Text).ToArray()).IsEquivalentTo(["Replacement"]);
        await Assert.That(fixture.Actions).IsEquivalentTo(["Open", "Open"]);
        await Assert.That(detached.ItemsReadCount).IsEqualTo(0);

        combo.Expand();
        fixture.PopupRoot.IsAvailable = false;
        var missing = Assert.Throws<UiControlResolutionException>(() => { _ = combo.Items; });
        await Assert.That(missing.Failure).IsEqualTo(UiControlResolutionFailure.Detached);
    }

    [Test]
    public async Task MissingItem_FailsBeforeChangingSelection()
    {
        var fixture = new SingleSelectFixture("Item 42", "Search result");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            fixture.Page.SelectComboItem(static page => page.CategorySelector, "Missing item", timeoutMs: 100));

        using (Assert.Multiple())
        {
            await Assert.That(exception.Message).Contains("was not found");
            await Assert.That(string.Join(" > ", fixture.Actions)).IsEqualTo("Open");
            await Assert.That(fixture.Page.CategorySelector.SelectedItem).IsNull();
        }
    }

    [Test]
    public async Task DuplicateDisplayText_FailsWithoutSelectingFirstMatch()
    {
        var fixture = new SingleSelectFixture("Item 42", "Item 42");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            fixture.Page.SelectComboItem(static page => page.CategorySelector, "Item 42", timeoutMs: 100));

        using (Assert.Multiple())
        {
            await Assert.That(exception.Message).Contains("ambiguous");
            await Assert.That(string.Join(" > ", fixture.Actions)).IsEqualTo("Open");
        }
    }

    [Test]
    public async Task UnavailableLogicalEditor_FailsBeforeOpeningPopup()
    {
        var fixture = new SingleSelectFixture("Item 42");
        fixture.Root.IsAvailable = false;

        var exception = Assert.Throws<TimeoutException>(() =>
            fixture.Page.SelectComboItem(static page => page.CategorySelector, "Item 42", timeoutMs: 100));

        using (Assert.Multiple())
        {
            await Assert.That(exception.Message).Contains("did not become available");
            await Assert.That(fixture.Actions).IsEmpty();
        }
    }

    [Test]
    public async Task ConfirmedSelection_FailsWhenCommittedValueDoesNotChange()
    {
        var fixture = new SingleSelectFixture("Item 42")
        {
            CommitSelection = false
        };

        var exception = Assert.Throws<TimeoutException>(() =>
            fixture.Page.SelectComboItem(static page => page.CategorySelector, "Item 42", timeoutMs: 100));

        using (Assert.Multiple())
        {
            await Assert.That(exception!.Message).Contains("did not commit");
            await Assert.That(fixture.Page.CategorySelector.SelectedItem).IsNull();
        }
    }

    private sealed class SingleSelectFixture : IUiControlResolver
    {
        private readonly IUiControlResolver _resolver;

        public SingleSelectFixture(params string[] items)
            : this(SingleSelectResultsKind.ListBox, items)
        {
        }

        public SingleSelectFixture(
            SingleSelectResultsKind resultsKind, string[] items, SingleSelectCommitMode commitMode = SingleSelectCommitMode.Confirm)
        {
            Root = new FakeLocalComboBoxRoot("CategorySelector") { IsAvailable = true };
            Results = resultsKind == SingleSelectResultsKind.ComboBox
                ? new FakeComboBox(items, Actions)
                : new FakeListBox(items, Actions);
            PopupRoot = new FakeAvailability("CategorySelectorPopup");
            OpenButton = new FakeButton("CategorySelectorOpen", () =>
            {
                Actions.Add("Open");
                PopupRoot.IsAvailable = !PopupRoot.IsAvailable;
            });
            CommittedValue = new FakeTextBox("CategorySelectorValue");
            ConfirmButton = new FakeButton("CategorySelectorConfirm", () =>
            {
                Actions.Add("Confirm");
                if (CommitSelection)
                {
                    CommittedValue.Text = Results switch
                    {
                        FakeListBox listBox => listBox.SelectedItemText ?? string.Empty,
                        FakeComboBox comboBox => comboBox.SelectedItem?.Text ?? string.Empty,
                        _ => string.Empty
                    };
                }
                PopupRoot.IsAvailable = false;
            });
            var requiresConfirmation = commitMode == SingleSelectCommitMode.Confirm;
            var parts = SingleSelectParts.ByAutomationIds(
                    "CategorySelector",
                    "CategorySelectorResults",
                    inputAutomationId: "CategorySelectorInput",
                    selectedValueAutomationId: requiresConfirmation ? "CategorySelectorValue" : null,
                    openButtonAutomationId: "CategorySelectorOpen",
                    popupRootAutomationId: requiresConfirmation ? "CategorySelectorPopup" : null,
                    confirmButtonAutomationId: requiresConfirmation ? "CategorySelectorConfirm" : null,
                    resultsKind: resultsKind,
                    commitMode: commitMode);
            _resolver = this.WithSingleSelect("CategorySelector", parts)
                .WithSingleSelect("ItemPickerResults", parts)
                .WithSearchPicker("ItemPicker", new SearchPickerParts(
                    "CategorySelectorInput", "CategorySelector"));
            Page = new SingleSelectPage(_resolver);
        }

        public List<string> Actions { get; } = [];

        public bool CommitSelection { get; init; } = true;

        public FakeTextBox CommittedValue { get; }

        public FakeTextBox Input { get; } = new("CategorySelectorInput");

        public bool ValueAvailable { get; set; } = true;

        public FakeButton OpenButton { get; }

        public FakeButton ConfirmButton { get; }

        public object Results { get; set; }

        public FakeListBox? DetachedResults { get; set; }

        public FakeAvailability PopupRoot { get; }

        public FakeLocalComboBoxRoot Root { get; }

        public SingleSelectPage Page { get; }

        public UiRuntimeCapabilities Capabilities { get; } = new("single-select-test");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            if ((definition.LocatorValue == "CategorySelectorValue" && !ValueAvailable)
                || (definition.LocatorValue == "CategorySelectorResults" && !PopupRoot.IsAvailable && DetachedResults is null))
            {
                throw new UiControlResolutionException(UiControlResolutionFailure.NotFound,
                    $"Part '{definition.LocatorValue}' is unavailable.");
            }

            object control = definition.LocatorValue switch
            {
                "CategorySelectorInput" when typeof(TControl) == typeof(ITextBoxControl) => Input,
                "CategorySelector" when typeof(TControl) == typeof(IUiControl) => Root,
                "CategorySelectorOpen" when typeof(TControl) == typeof(IButtonControl) => OpenButton,
                "CategorySelectorConfirm" when typeof(TControl) == typeof(IButtonControl) => ConfirmButton,
                "CategorySelectorValue" when typeof(TControl) == typeof(ITextBoxControl)
                    || typeof(TControl) == typeof(IUiControl) => CommittedValue,
                "CategorySelectorResults" when typeof(TControl) == typeof(ISelectableListBoxControl)
                    && Results is FakeListBox listBox => PopupRoot.IsAvailable ? listBox : DetachedResults!,
                "CategorySelectorResults" when typeof(TControl) == typeof(IComboBoxControl)
                    && Results is FakeComboBox comboBox => comboBox,
                "CategorySelectorPopup" when typeof(TControl) == typeof(IUiControl) => PopupRoot,
                _ => throw new InvalidOperationException(
                    $"Unexpected control '{typeof(TControl).Name}:{definition.LocatorValue}'.")
            };
            return (TControl)control;
        }
    }

    private sealed class SingleSelectPage : UiPage
    {
        private static readonly UiControlDefinition CategorySelectorDefinition = new(
            "CategorySelector",
            UiControlType.ComboBox,
            "CategorySelector");

        public SingleSelectPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IComboBoxControl CategorySelector => Resolve<IComboBoxControl>(CategorySelectorDefinition);

        public ISearchPickerControl ItemPicker => Resolve<ISearchPickerControl>(new UiControlDefinition(
            "ItemPicker", UiControlType.SearchPicker, "CategorySelector"));
    }

    private sealed class FakeButton : IButtonControl
    {
        private readonly Action _invoke;

        public FakeButton(string automationId, Action invoke)
        {
            AutomationId = automationId;
            _invoke = invoke;
        }

        public string AutomationId { get; }

        public string Name => AutomationId;

        public bool IsEnabled => true;

        public void Invoke() => _invoke();
    }

    private sealed class FakeTextBox(string automationId) : ITextBoxControl, IUiControlAvailability
    {
        public string AutomationId { get; } = automationId;

        public string Name => AutomationId;

        public bool IsEnabled => true;

        public string Text { get; set; } = string.Empty;

        public bool IsAvailable { get; set; } = true;

        public void Enter(string value) => Text = value;
    }

    private sealed class FakeListBox : ISelectableListBoxControl, IUiControlAvailability
    {
        private readonly List<string> _actions;
        private readonly IReadOnlyList<IListBoxItem> _items;

        public FakeListBox(IEnumerable<string> items, List<string> actions)
        {
            _items = items.Select(static item => (IListBoxItem)new FakeListItem(item)).ToArray();
            _actions = actions;
        }

        public string AutomationId => "CategorySelectorResults";

        public string Name => AutomationId;

        public bool IsEnabled => true;

        public bool IsAvailable { get; set; } = true;

        public int ItemsReadCount { get; private set; }

        public IReadOnlyList<IListBoxItem> Items
        {
            get
            {
                ItemsReadCount++;
                return _items;
            }
        }

        public string? SelectedItemText { get; private set; }

        public void SelectItem(string itemText)
        {
            SelectedItemText = itemText;
            _actions.Add($"Select:{itemText}");
        }
    }

    private sealed class FakeComboBox : IComboBoxControl
    {
        private readonly List<string> _actions;

        public FakeComboBox(IEnumerable<string> items, List<string> actions)
        {
            Items = items.Select(static item => (IComboBoxItem)new FakeComboItem(item)).ToArray();
            _actions = actions;
        }

        public string AutomationId => "CategorySelectorResults";

        public string Name => AutomationId;

        public bool IsEnabled => true;

        public IReadOnlyList<IComboBoxItem> Items { get; }

        public IComboBoxItem? SelectedItem => SelectedIndex >= 0 ? Items[SelectedIndex] : null;

        public int SelectedIndex { get; set; } = -1;

        public void SelectByIndex(int index)
        {
            SelectedIndex = index;
            _actions.Add($"Select:{Items[index].Text}");
        }

        public void Expand() => _actions.Add("Expand");
    }

    private sealed class FakeLocalComboBoxRoot(string automationId) : FakeAvailability(automationId), ILocalComboBoxEditor
    {
        public bool? ReadPopupOpen(Func<IButtonControl?> resolveOpenButton) => null;

        public IReadOnlyList<string> SelectLocalItem(string itemText, UiOperationTimeoutBudget budget, LocalComboBoxParts parts) =>
            throw new InvalidOperationException("The editor does not expose a committed value for provider-specific selection.");
    }

    private class FakeAvailability : IUiControlAvailability
    {
        public FakeAvailability(string automationId)
        {
            AutomationId = automationId;
        }

        public string AutomationId { get; }

        public string Name => AutomationId;

        public bool IsEnabled => true;

        public bool IsAvailable { get; set; }
    }

    private sealed record FakeListItem(string Text) : IListBoxItem
    {
        string? IListBoxItem.Name => Text;
    }

    private sealed record FakeComboItem(string Text) : IComboBoxItem
    {
        public string Name => Text;
    }
}
