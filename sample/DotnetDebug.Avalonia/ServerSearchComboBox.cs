using System.Collections;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Eremex.AvaloniaUI.Controls.Editors;

namespace DotnetDebug.Avalonia;

/// <summary>
/// Visible sample of the ARM ServerSearchComboBox automation contract.
/// Business-specific server loading remains in the consumer; this control preserves
/// the real PopupEditor, text input, open button and detached ListBox topology.
/// </summary>
public sealed class ServerSearchComboBox : PopupEditor
{
    public static readonly StyledProperty<IEnumerable?> ItemListProperty =
        AvaloniaProperty.Register<ServerSearchComboBox, IEnumerable?>(nameof(ItemList));

    public static readonly StyledProperty<string?> SearchTextProperty =
        AvaloniaProperty.Register<ServerSearchComboBox, string?>(nameof(SearchText));

    public static readonly StyledProperty<object?> CurrentSelectedProperty =
        AvaloniaProperty.Register<ServerSearchComboBox, object?>(nameof(CurrentSelected));

    public static readonly StyledProperty<bool> DataGridControlModeProperty =
        AvaloniaProperty.Register<ServerSearchComboBox, bool>(nameof(DataGridControlMode));

    private readonly ListBox _results;
    private TextBox? _input;
    private ToggleButton? _openButton;
    private bool _synchronizing;

    public ServerSearchComboBox()
    {
        _results = new ListBox
        {
            SelectionMode = SelectionMode.Single
        };
        _results.SelectionChanged += OnResultsSelectionChanged;
        PopupContent = _results;
        Loaded += OnLoaded;
        PopupOpened += OnPopupOpened;
    }

    public IEnumerable? ItemList
    {
        get => GetValue(ItemListProperty);
        set => SetValue(ItemListProperty, value);
    }

    public string? SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public object? CurrentSelected
    {
        get => GetValue(CurrentSelectedProperty);
        set => SetValue(CurrentSelectedProperty, value);
    }

    public bool DataGridControlMode
    {
        get => GetValue(DataGridControlModeProperty);
        set => SetValue(DataGridControlModeProperty, value);
    }

    public event EventHandler<object?>? CurrentSelectedChanged;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_input is not null)
        {
            _input.TextChanged -= OnInputTextChanged;
        }

        base.OnApplyTemplate(e);
        _input = RealEditor as TextBox
            ?? e.NameScope.Find<TextBox>("PART_RealEditor")
            ?? this.GetVisualDescendants()
                .OfType<TextBox>()
                .FirstOrDefault(static candidate => candidate.Name == "PART_RealEditor");
        _input?.ApplyTemplate();
        _openButton = e.NameScope.Find<ToggleButton>("PART_PopupOpenButton")
            ?? this.GetVisualDescendants()
                .OfType<ToggleButton>()
                .FirstOrDefault(static candidate => candidate.Name == "PART_PopupOpenButton")
            ?? this.GetLogicalDescendants()
                .OfType<ToggleButton>()
                .FirstOrDefault(static candidate => candidate.Name == "PART_PopupOpenButton");

        if (_input is not null)
        {
            _input.Text = SearchText ?? CurrentSelected?.ToString() ?? string.Empty;
            _input.TextChanged += OnInputTextChanged;
        }

        ApplyAutomationPartIds();
        RefreshResults();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemListProperty
            || (change.Property == SearchTextProperty && !_synchronizing))
        {
            RefreshResults();
        }

        if (change.Property == SearchTextProperty && !_synchronizing && _input is not null)
        {
            var value = SearchText ?? string.Empty;
            if (!string.Equals(_input.Text, value, StringComparison.Ordinal))
            {
                _input.Text = value;
            }
        }

        if (change.Property == CurrentSelectedProperty && !_synchronizing)
        {
            SynchronizeSelectedValue(CurrentSelected);
        }

        if (change.Property == AutomationProperties.AutomationIdProperty)
        {
            ApplyAutomationPartIds();
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ApplyTemplate();
        ApplyAutomationPartIds();
    }

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        ApplyAutomationPartIds();
    }

    private void OnInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_synchronizing || _input is null)
        {
            return;
        }

        _synchronizing = true;
        try
        {
            SearchText = _input.Text ?? string.Empty;
            CurrentSelected = null;
        }
        finally
        {
            _synchronizing = false;
        }

        RefreshResults();
        if (this.IsAttachedToVisualTree())
        {
            IsPopupOpen = true;
        }
    }

    private void OnResultsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizing || _results.SelectedItem is null)
        {
            return;
        }

        SynchronizeSelectedValue(_results.SelectedItem);
    }

    private void SynchronizeSelectedValue(object? selected)
    {
        _synchronizing = true;
        try
        {
            CurrentSelected = selected;
            if (this.IsAttachedToVisualTree())
            {
                EditorValue = selected;
            }

            SearchText = selected?.ToString() ?? string.Empty;
            if (_input is not null)
            {
                _input.Text = SearchText;
            }

            _results.SelectedItem = selected;
            if (this.IsAttachedToVisualTree())
            {
                IsPopupOpen = false;
            }
        }
        finally
        {
            _synchronizing = false;
        }

        CurrentSelectedChanged?.Invoke(this, selected);
    }

    private void RefreshResults()
    {
        var searchText = SearchText?.Trim() ?? string.Empty;
        _results.ItemsSource = (ItemList ?? Array.Empty<object>())
            .Cast<object?>()
            .Where(static item => item is not null)
            .Where(item =>
                searchText.Length == 0
                || item!.ToString()?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        ApplyAutomationPartIds();
    }

    private void ApplyAutomationPartIds()
    {
        var automationId = AutomationProperties.GetAutomationId(this);
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return;
        }

        if (_input is not null)
        {
            AutomationProperties.SetAutomationId(_input, $"{automationId}_Input");
        }

        if (_openButton is not null)
        {
            AutomationProperties.SetAutomationId(_openButton, $"{automationId}_OpenButton");
        }

        AutomationProperties.SetAutomationId(_results, $"{automationId}_Results");
    }
}
