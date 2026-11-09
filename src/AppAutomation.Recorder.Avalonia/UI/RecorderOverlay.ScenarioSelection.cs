using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AppAutomation.Recorder.Avalonia.UI;

internal sealed partial class RecorderOverlay
{
    private TextBox _scenarioEntityTextBox = null!;
    private Popup _scenarioEntityPopup = null!;
    private Border _scenarioEntityResults = null!;
    private ListBox _scenarioEntityList = null!;
    private TextBlock _scenarioEntityNoResults = null!;
    private ComboBox _scenarioDestinationComboBox = null!;
    private Button _resetScenarioTargetButton = null!;
    private IReadOnlyList<RecordedScenarioDestination>? _scenarioCatalog;
    private RecorderScenarioEntity[] _scenarioEntities = [];
    private RecorderScenarioEntity? _selectedScenarioEntity;

    private void InitializeScenarioTargetPickers()
    {
        _scenarioEntityTextBox = this.GetControl<TextBox>("ScenarioEntityTextBox");
        _scenarioEntityPopup = this.GetControl<Popup>("ScenarioEntityPopup");
        _scenarioEntityResults = this.GetControl<Border>("ScenarioEntityResults");
        _scenarioEntityList = this.GetControl<ListBox>("ScenarioEntityList");
        _scenarioEntityNoResults = this.GetControl<TextBlock>("ScenarioEntityNoResults");
        _scenarioDestinationComboBox = this.GetControl<ComboBox>("ScenarioDestinationComboBox");
        _resetScenarioTargetButton = this.GetControl<Button>("ResetScenarioTargetButton");
        _resetScenarioTargetButton.Click += OnResetScenarioTargetClick;
        _scenarioEntityPopup.PlacementTarget = _scenarioEntityTextBox;
        _scenarioEntityPopup.OverlayInputPassThroughElement = this.GetControl<Grid>("ScenarioEntityField");
        _scenarioEntityPopup.Closed += (_, _) => RestoreScenarioEntityText();
        _scenarioEntityTextBox.PropertyChanged += OnScenarioEntityTextChanged;
        _scenarioEntityTextBox.AddHandler(KeyDownEvent, OnScenarioEntityKeyDown, RoutingStrategies.Tunnel);
        this.GetControl<Button>("ScenarioEntityOpenButton").Click += (_, _) =>
        {
            if (_scenarioEntityPopup.IsOpen)
            {
                CloseScenarioEntitySearch();
                return;
            }

            _scenarioEntityTextBox.Focus();
            _scenarioEntityTextBox.SelectAll();
            ShowScenarioEntityResults(string.Empty);
        };
        _scenarioDestinationComboBox.SelectionChanged += OnScenarioDestinationSelectionChanged;
    }

    private void RefreshScenarioTargetPickers(IRecorderScenarioSelectionDetails selection)
    {
        if (!ReferenceEquals(_scenarioCatalog, selection.ScenarioDestinations))
        {
            _scenarioCatalog = selection.ScenarioDestinations;
            var previousEntity = _selectedScenarioEntity;
            _scenarioEntities = RecorderScenarioEntity.Group(_scenarioCatalog);
            _selectedScenarioEntity = previousEntity is null
                ? null
                : Array.Find(_scenarioEntities, entity => entity.Key == previousEntity.Key);
            if (_scenarioEntityPopup.IsOpen)
            {
                ShowScenarioEntityResults(_scenarioEntityTextBox.Text);
            }
        }

        RecorderScenarioOperation? selectedOperation = null;
        if (selection.SelectedScenarioDestination is { } destination)
        {
            var entityKey = RecorderScenarioEntity.KeyFor(destination);
            if (_selectedScenarioEntity is null || _selectedScenarioEntity.Key != entityKey)
            {
                _selectedScenarioEntity = Array.Find(_scenarioEntities, entity => entity.Key == entityKey);
            }

            selectedOperation = _selectedScenarioEntity?.Operations.FirstOrDefault(operation =>
                operation.Destination == destination);
        }

        _scenarioEntityTextBox.IsEnabled = selection.CanChangeScenarioTarget;
        _resetScenarioTargetButton.IsEnabled = selection.CanChangeScenarioTarget;
        if (!selection.CanChangeScenarioTarget && _scenarioEntityPopup.IsOpen)
        {
            CloseScenarioEntitySearch();
        }
        if (!_scenarioEntityPopup.IsOpen)
        {
            RestoreScenarioEntityText();
        }

        var operationBox = _scenarioDestinationComboBox;
        var operations = _selectedScenarioEntity?.Operations;
        if (!ReferenceEquals(operationBox.ItemsSource, operations))
        {
            operationBox.ItemsSource = operations;
        }
        operationBox.SelectedItem = selectedOperation;
        operationBox.IsEnabled = selection.CanChangeScenarioTarget && operations is { Count: > 0 };
    }

    private void OnScenarioEntityTextChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty && !_isRefreshingScenarioSelection)
        {
            ShowScenarioEntityResults(_scenarioEntityTextBox.Text);
        }
    }

    private void ShowScenarioEntityResults(string? query)
    {
        if (_scenarioSelectionDetails?.CanChangeScenarioTarget != true)
        {
            return;
        }

        var filter = query?.Trim() ?? string.Empty;
        var results = filter.Length == 0 ? _scenarioEntities : Array.FindAll(_scenarioEntities,
            entity => entity.Label.Contains(filter, StringComparison.OrdinalIgnoreCase));
        _scenarioEntityList.ItemsSource = results;
        _scenarioEntityList.SelectedIndex = -1;
        _scenarioEntityList.IsVisible = results.Length != 0;
        _scenarioEntityNoResults.IsVisible = results.Length == 0;
        _scenarioEntityResults.MinWidth = _scenarioEntityTextBox.Bounds.Width;
        _scenarioEntityPopup.IsOpen = true;
    }

    private void OnScenarioEntityKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up)
        {
            if (!_scenarioEntityPopup.IsOpen)
            {
                ShowScenarioEntityResults(string.Empty);
            }

            var last = _scenarioEntityList.ItemCount - 1;
            if (last >= 0)
            {
                var index = _scenarioEntityList.SelectedIndex;
                _scenarioEntityList.SelectedIndex = e.Key == Key.Down
                    ? Math.Min(index + 1, last)
                    : index < 0 ? last : Math.Max(index - 1, 0);
                _scenarioEntityList.ScrollIntoView(_scenarioEntityList.SelectedIndex);
            }
            e.Handled = true;
        }
        else if (_scenarioEntityPopup.IsOpen)
        {
            if (e.Key == Key.Enter)
            {
                var entity = _scenarioEntityList.SelectedItem as RecorderScenarioEntity
                    ?? (_scenarioEntityList.ItemCount == 1 ? _scenarioEntityList.Items[0] as RecorderScenarioEntity : null);
                if (entity is not null) SelectScenarioEntity(entity);
                e.Handled = true;
            }
            else if (e.Key is Key.Escape or Key.Tab)
            {
                CloseScenarioEntitySearch();
                e.Handled = e.Key == Key.Escape;
            }
        }
    }

    private void OnScenarioEntityItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RecorderScenarioEntity entity })
        {
            SelectScenarioEntity(entity);
        }
    }

    private void SelectScenarioEntity(RecorderScenarioEntity entity)
    {
        if (_scenarioSelectionDetails is not { CanChangeScenarioTarget: true } selection
            || !_scenarioEntities.Contains(entity))
        {
            return;
        }

        if (!ReferenceEquals(_selectedScenarioEntity, entity))
        {
            var previous = _selectedScenarioEntity;
            _selectedScenarioEntity = entity;
            if (!selection.TrySelectScenarioDestination(null, suggestedName: string.Empty))
            {
                _selectedScenarioEntity = previous;
            }
        }

        CloseScenarioEntitySearch();
        Refresh();
    }

    private void CloseScenarioEntitySearch()
    {
        _scenarioEntityPopup.IsOpen = false;
        RestoreScenarioEntityText();
    }

    private void RestoreScenarioEntityText()
    {
        var label = _selectedScenarioEntity?.Label ?? string.Empty;
        if (string.Equals(_scenarioEntityTextBox.Text, label, StringComparison.Ordinal)) return;
        var refreshing = _isRefreshingScenarioSelection;
        _isRefreshingScenarioSelection = true;
        try
        {
            _scenarioEntityTextBox.Text = label;
        }
        finally
        {
            _isRefreshingScenarioSelection = refreshing;
        }
    }

    private void OnScenarioDestinationSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingScenarioSelection || _scenarioSelectionDetails is not { } selection
            || _scenarioDestinationComboBox.SelectedItem is not RecorderScenarioOperation operation)
        {
            return;
        }

        selection.TrySelectScenarioDestination(operation.Destination, operation.Name);
        Refresh();
    }

    private void OnResetScenarioTargetClick(object? sender, RoutedEventArgs e)
    {
        if (_scenarioSelectionDetails is not { CanChangeScenarioTarget: true } selection)
        {
            return;
        }

        var previous = _selectedScenarioEntity;
        _selectedScenarioEntity = null;
        if (selection.TryResetScenarioTarget())
        {
            CloseScenarioEntitySearch();
        }
        else
        {
            _selectedScenarioEntity = previous;
        }

        Refresh();
    }
}

internal sealed record RecorderScenarioEntity(string? Key, string Label, IReadOnlyList<RecorderScenarioOperation> Operations)
{
    public static string? KeyFor(RecordedScenarioDestination destination)
    {
        var separator = destination.DisplayName.IndexOf('.');
        return separator < 0 ? null : destination.DisplayName[..separator];
    }

    public static RecorderScenarioEntity[] Group(IReadOnlyList<RecordedScenarioDestination> destinations) => destinations
        .GroupBy(KeyFor, StringComparer.Ordinal)
        .Select(group => new RecorderScenarioEntity(group.Key, group.Key ?? "Общее (корень)", CreateOperations(group)))
        .OrderBy(static entity => entity.Label, StringComparer.OrdinalIgnoreCase)
        .ThenBy(static entity => entity.Label, StringComparer.Ordinal)
        .ToArray();

    private static RecorderScenarioOperation[] CreateOperations(IEnumerable<RecordedScenarioDestination> destinations) =>
        destinations.GroupBy(static destination => OperationName(destination.ScenarioClassName), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var ambiguous = group.Skip(1).Any();
                return group.Select(destination =>
                {
                    var name = OperationName(destination.ScenarioClassName);
                    return new RecorderScenarioOperation(destination, name,
                        ambiguous ? $"{name} ({destination.DisplayName})" : name);
                });
            })
            .OrderBy(static operation => operation.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static operation => operation.Label, StringComparer.Ordinal)
            .ToArray();

    private static string OperationName(string className)
    {
        var suffixLength = className.EndsWith("Tests", StringComparison.Ordinal) ? 5
            : className.EndsWith("Test", StringComparison.Ordinal) ? 4 : 0;
        return className.Length > suffixLength ? className[..(className.Length - suffixLength)] : className;
    }
}

internal sealed record RecorderScenarioOperation(RecordedScenarioDestination Destination, string Name, string Label);
