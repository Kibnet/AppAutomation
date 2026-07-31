using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia.Tests;

internal sealed class MultiSelectCaptureFixture : IDisposable
{
    private static readonly string[] AvailableItems = ["Alpha", "Beta", "Gamma"];
    private readonly IReadOnlyDictionary<string, CheckBox> _items;

    private MultiSelectCaptureFixture(
        RecorderSession session,
        Button openButton,
        Button applyButton,
        Button cancelButton,
        IReadOnlyDictionary<string, CheckBox> items)
    {
        Session = session;
        OpenButton = openButton;
        ApplyButton = applyButton;
        CancelButton = cancelButton;
        _items = items;
    }

    public RecorderSession Session { get; }

    public Button OpenButton { get; }

    public Button ApplyButton { get; }

    public Button CancelButton { get; }

    public static MultiSelectCaptureFixture Create(params string[] selectedItems)
    {
        return Create(CreateOptions(), selectedItems);
    }

    public static MultiSelectCaptureFixture Create(
        AppAutomationRecorderOptions options,
        params string[] selectedItems)
    {
        var selected = selectedItems.ToHashSet(StringComparer.Ordinal);
        var items = AvailableItems.ToDictionary(
            static item => item,
            item => CreateCheckBox(item, selected.Contains(item)),
            StringComparer.Ordinal);
        var editor = WithAutomationId(new Border(), "CategoriesEditor");
        var openButton = CreateButton("CategoriesOpenButton");
        var itemsContainer = WithAutomationId(new StackPanel(), "CategoriesItems");
        var applyButton = CreateButton("CategoriesApplyButton");
        var cancelButton = CreateButton("CategoriesCancelButton");

        foreach (var item in items.Values)
        {
            itemsContainer.Children.Add(item);
        }

        var popup = new StackPanel();
        popup.Children.Add(itemsContainer);
        popup.Children.Add(applyButton);
        popup.Children.Add(cancelButton);

        var windowRoot = new StackPanel();
        windowRoot.Children.Add(editor);
        windowRoot.Children.Add(openButton);

        return new MultiSelectCaptureFixture(
            CreateSession(options, windowRoot),
            openButton,
            applyButton,
            cancelButton,
            items);
    }

    public static AppAutomationRecorderOptions CreateOptions()
    {
        var options = new AppAutomationRecorderOptions { ShowOverlay = false };
        options.MultiSelectHints.Add(new RecorderMultiSelectHint("Categories", CreateParts()));
        return options;
    }

    public static MultiSelectParts CreateParts()
    {
        return MultiSelectParts.ByAutomationIds(
            "CategoriesEditor",
            "CategoriesOpenButton",
            "CategoriesItems",
            "CategoriesApplyButton",
            "CategoriesCancelButton");
    }

    public static ListBox CreateItemsContainer(string selectedItem)
    {
        return WithAutomationId(
            new ListBox
            {
                ItemsSource = AvailableItems,
                SelectedItem = selectedItem
            },
            "CategoriesItems");
    }

    public static RecorderSession CreateSession(AppAutomationRecorderOptions options, Control root)
    {
        return new RecorderSession(
            RecorderTestWindow.CreateStub(),
            options,
            () => root,
            attachWindowHandlers: false);
    }

    public CheckBox Item(string name)
    {
        return _items[name];
    }

    public void Start()
    {
        Session.Start();
    }

    public void Capture(params Control[] controls)
    {
        foreach (var control in controls)
        {
            Session.CaptureButtonClickForTesting(control);
        }
    }

    public void Dispose()
    {
        Session.Dispose();
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
            $"Categories{text}");
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
