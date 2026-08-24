using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class SingleSelectControlTests
{
    [Test]
    public async Task ConfirmedListSelection_UsesConfiguredPrimitiveParts()
    {
        var fixture = new SingleSelectFixture("Item 42", "Search result");

        fixture.Page.SelectComboItem(static page => page.CategorySelector, "Item 42");

        using (Assert.Multiple())
        {
            await Assert.That(string.Join(" > ", fixture.Actions)).IsEqualTo("Open > Select:Item 42 > Confirm");
            await Assert.That(fixture.Page.CategorySelector.SelectedItem?.Text).IsEqualTo("Item 42");
        }
    }

    [Test]
    public async Task ConfirmedComboSelection_UsesExistingComboCapability()
    {
        var fixture = new SingleSelectFixture(
            SingleSelectResultsKind.ComboBox,
            "Item 42",
            "Search result");

        fixture.Page.SelectComboItem(static page => page.CategorySelector, "Search result");

        using (Assert.Multiple())
        {
            await Assert.That(string.Join(" > ", fixture.Actions))
                .IsEqualTo("Open > Expand > Select:Search result > Confirm");
            await Assert.That(fixture.Page.CategorySelector.SelectedItem?.Text).IsEqualTo("Search result");
        }
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

        public SingleSelectFixture(SingleSelectResultsKind resultsKind, params string[] items)
        {
            Root = new FakeAvailability("CategorySelector") { IsAvailable = true };
            Results = resultsKind == SingleSelectResultsKind.ComboBox
                ? new FakeComboBox(items, Actions)
                : new FakeListBox(items, Actions);
            PopupRoot = new FakeAvailability("CategorySelectorPopup");
            OpenButton = new FakeButton("CategorySelectorOpen", () =>
            {
                Actions.Add("Open");
                PopupRoot.IsAvailable = true;
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
            _resolver = this.WithSingleSelect(
                "CategorySelector",
                SingleSelectParts.ByAutomationIds(
                    "CategorySelector",
                    "CategorySelectorResults",
                    selectedValueAutomationId: "CategorySelectorValue",
                    openButtonAutomationId: "CategorySelectorOpen",
                    popupRootAutomationId: "CategorySelectorPopup",
                    confirmButtonAutomationId: "CategorySelectorConfirm",
                    resultsKind: resultsKind,
                    commitMode: SingleSelectCommitMode.Confirm));
            Page = new SingleSelectPage(_resolver);
        }

        public List<string> Actions { get; } = [];

        public bool CommitSelection { get; init; } = true;

        public FakeTextBox CommittedValue { get; }

        public FakeButton OpenButton { get; }

        public FakeButton ConfirmButton { get; }

        public object Results { get; }

        public FakeAvailability PopupRoot { get; }

        public FakeAvailability Root { get; }

        public SingleSelectPage Page { get; }

        public UiRuntimeCapabilities Capabilities { get; } = new("single-select-test");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            object control = definition.LocatorValue switch
            {
                "CategorySelector" when typeof(TControl) == typeof(IUiControl) => Root,
                "CategorySelectorOpen" when typeof(TControl) == typeof(IButtonControl) => OpenButton,
                "CategorySelectorConfirm" when typeof(TControl) == typeof(IButtonControl) => ConfirmButton,
                "CategorySelectorValue" when typeof(TControl) == typeof(ITextBoxControl) => CommittedValue,
                "CategorySelectorResults" when typeof(TControl) == typeof(ISelectableListBoxControl)
                    && Results is FakeListBox listBox => listBox,
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

    private sealed class FakeTextBox(string automationId) : ITextBoxControl
    {
        public string AutomationId { get; } = automationId;

        public string Name => AutomationId;

        public bool IsEnabled => true;

        public string Text { get; set; } = string.Empty;

        public void Enter(string value) => Text = value;
    }

    private sealed class FakeListBox : ISelectableListBoxControl
    {
        private readonly List<string> _actions;

        public FakeListBox(IEnumerable<string> items, List<string> actions)
        {
            Items = items.Select(static item => (IListBoxItem)new FakeListItem(item)).ToArray();
            _actions = actions;
        }

        public string AutomationId => "CategorySelectorResults";

        public string Name => AutomationId;

        public bool IsEnabled => true;

        public IReadOnlyList<IListBoxItem> Items { get; }

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

    private sealed class FakeAvailability : IUiControlAvailability
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
