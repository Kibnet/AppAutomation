using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AppAutomation.Recorder.Avalonia.Tests;

internal sealed class ComboBoxFilterCaptureFixture : IDisposable
{
    private static readonly string[] AvailableItems = ["Open", "Pending", "Closed"];
    private readonly IReadOnlyDictionary<string, CheckBox> _items;

    private ComboBoxFilterCaptureFixture(
        RecorderSession session,
        Button openButton,
        Button? applyButton,
        Button? cancelButton,
        ListBox? immediateResults,
        IReadOnlyDictionary<string, CheckBox> items)
    {
        Session = session;
        OpenButton = openButton;
        ApplyButton = applyButton;
        CancelButton = cancelButton;
        ImmediateResults = immediateResults;
        _items = items;
    }

    public RecorderSession Session { get; }

    public Button OpenButton { get; }

    public Button? ApplyButton { get; }

    public Button? CancelButton { get; }

    public ListBox? ImmediateResults { get; }

    public static ComboBoxFilterCaptureFixture Create(params string[] selectedItems)
    {
        var options = CreateOptions(hasCommitButtons: true);
        var selected = selectedItems.ToHashSet(StringComparer.Ordinal);
        var items = AvailableItems.ToDictionary(
            static item => item,
            item => CreateCheckBox(item, selected.Contains(item)),
            StringComparer.Ordinal);
        var root = WithAutomationId(new Border(), "StatusFilterRoot");
        var openButton = CreateButton("StatusFilterOpenButton");
        var itemsContainer = WithAutomationId(new StackPanel(), "StatusFilterItems");
        var applyButton = CreateButton("StatusFilterApplyButton");
        var cancelButton = CreateButton("StatusFilterCancelButton");

        foreach (var item in items.Values)
        {
            itemsContainer.Children.Add(item);
        }

        var validationRoot = new StackPanel();
        validationRoot.Children.Add(root);
        validationRoot.Children.Add(openButton);
        validationRoot.Children.Add(itemsContainer);
        validationRoot.Children.Add(applyButton);
        validationRoot.Children.Add(cancelButton);

        return new ComboBoxFilterCaptureFixture(
            CreateSession(options, validationRoot),
            openButton,
            applyButton,
            cancelButton,
            immediateResults: null,
            items);
    }

    public static ComboBoxFilterCaptureFixture CreateImmediate(string selectedItem)
    {
        var options = CreateOptions(hasCommitButtons: false);
        var root = WithAutomationId(new Border(), "StatusFilterRoot");
        var openButton = CreateButton("StatusFilterOpenButton");
        var results = WithAutomationId(
            new ListBox
            {
                ItemsSource = AvailableItems,
                SelectedItem = selectedItem
            },
            "StatusFilterItems");
        var validationRoot = new StackPanel();
        validationRoot.Children.Add(root);
        validationRoot.Children.Add(openButton);
        validationRoot.Children.Add(results);

        return new ComboBoxFilterCaptureFixture(
            CreateSession(options, validationRoot),
            openButton,
            applyButton: null,
            cancelButton: null,
            results,
            new Dictionary<string, CheckBox>(StringComparer.Ordinal));
    }

    public CheckBox Item(string name) => _items[name];

    public void Start() => Session.Start();

    public void Capture(params Control[] controls)
    {
        foreach (var control in controls)
        {
            Session.CaptureButtonClickForTesting(control);
        }
    }

    public void CaptureImmediateSelection()
    {
        var results = ImmediateResults
            ?? throw new InvalidOperationException("This fixture does not expose immediate results.");
        Session.RegisterPointerInputForTesting(results);
        Session.CaptureListBoxSelectionForTesting(results);
    }

    public void Click(Button button)
    {
        Session.CaptureButtonPressForTesting(button);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    public void SelectOnly(params string[] selectedItems)
    {
        var selected = selectedItems.ToHashSet(StringComparer.Ordinal);
        foreach (var item in _items)
        {
            item.Value.IsChecked = selected.Contains(item.Key);
        }
    }

    public void Dispose() => Session.Dispose();

    private static AppAutomationRecorderOptions CreateOptions(bool hasCommitButtons)
    {
        var options = new AppAutomationRecorderOptions { ShowOverlay = false };
        options.ComboBoxFilterHints.Add(new RecorderComboBoxFilterHint(
            "StatusFilter",
            ComboBoxFilterParts.ByAutomationIds(
                "StatusFilterRoot",
                "StatusFilterOpenButton",
                "StatusFilterItems",
                applyButtonAutomationId: hasCommitButtons ? "StatusFilterApplyButton" : null,
                cancelButtonAutomationId: hasCommitButtons ? "StatusFilterCancelButton" : null)));
        return options;
    }

    private static RecorderSession CreateSession(AppAutomationRecorderOptions options, Control root)
    {
        var session = new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options,
            () => root,
            attachWindowHandlers: false);
        session.AttachInputHandlersForTesting();
        return session;
    }

    private static Button CreateButton(string automationId)
    {
        return WithAutomationId(new Button { Content = automationId }, automationId);
    }

    private static CheckBox CreateCheckBox(string text, bool isChecked)
    {
        var checkBox = WithAutomationId(
            new CheckBox
            {
                Content = text,
                IsChecked = isChecked
            },
            $"StatusFilter{text}");
        AutomationProperties.SetName(checkBox, text);
        return checkBox;
    }

    private static TControl WithAutomationId<TControl>(TControl control, string automationId)
        where TControl : Control
    {
        AutomationProperties.SetAutomationId(control, automationId);
        return control;
    }
}
