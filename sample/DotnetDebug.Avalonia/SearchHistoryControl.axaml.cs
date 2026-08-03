using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DotnetDebug.Avalonia;

public partial class SearchHistoryControl : UserControl
{
    private readonly ObservableCollection<string> _history = ["orders", "customers", "reports"];

    public SearchHistoryControl()
    {
        InitializeComponent();
        HistoryList.ItemsSource = _history;
    }

    public string SearchText
    {
        get => SearchInput.Text ?? string.Empty;
        set => SearchInput.Text = value;
    }

    public IReadOnlyList<string> HistoryItems => _history;

    public bool IsHistoryOpen => HistoryPopup.IsOpen;

    private void OnSearchInputGotFocus(object? sender, FocusChangedEventArgs e) => OpenHistoryIfAvailable();

    private void OnSearchInputPointerPressed(object? sender, PointerPressedEventArgs e) => OpenHistoryIfAvailable();

    private void OnSearchInputLostFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(CloseHistoryIfFocusLeftControl, DispatcherPriority.Background);
    }

    private void OnHistoryItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: string value })
        {
            return;
        }

        ApplyHistoryItem(value);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        HistoryPopup.IsOpen = false;
        base.OnDetachedFromVisualTree(e);
    }

    public void OpenHistoryIfAvailable()
    {
        HistoryPopup.IsOpen = _history.Count > 0;
    }

    public void ApplyHistoryItem(string value)
    {
        if (!_history.Contains(value, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Search history item '{value}' was not found.");
        }

        SearchInput.Text = value;
        HistoryPopup.IsOpen = false;
    }

    private void CloseHistoryIfFocusLeftControl()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        if (ReferenceEquals(focused, SearchInput)
            || focused is not null && HistoryRoot.GetVisualDescendants().Prepend(HistoryRoot).Contains(focused))
        {
            return;
        }

        HistoryPopup.IsOpen = false;
    }
}
