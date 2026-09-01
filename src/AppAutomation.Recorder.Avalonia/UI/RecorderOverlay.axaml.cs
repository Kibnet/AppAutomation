using Avalonia;
using Avalonia.Controls;
using System.Globalization;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace AppAutomation.Recorder.Avalonia.UI;

internal sealed partial class RecorderOverlay : UserControl
{
    private IAppAutomationRecorderSession? _session;
    private IAppAutomationRecorderSessionDetails? _sessionDetails;
    private AppAutomationRecorderOptions? _options;
    private DispatcherTimer? _timer;
    private Button? _recordButton;
    private Button? _clearButton;
    private Button? _saveButton;
    private Button? _exportButton;
    private Button? _settingsButton;
    private Button? _checkButton;
    private Button? _generateValueButton;
    private Button? _copyDiagnosticLogPathButton;
    private CheckBox? _diagnosticLogCheckBox;
    private TextBlock? _stepCounter;
    private TextBlock? _statusText;
    private TextBlock? _previewText;
    private TextBlock? _sessionSummaryText;
    private TextBlock? _scenarioPathText;
    private TextBlock? _diagnosticLogPathText;
    private TextBlock? _shortcutText;
    private TextBlock? _validationBadgeText;
    private TextBlock? _journalEmptyText;
    private Control? _scenarioSelectionPanel;
    private ProgressBar? _scenarioScanProgress;
    private TextBlock? _scenarioScanStatus;
    private ComboBox? _scenarioDestinationComboBox;
    private TextBox? _scenarioNameTextBox;
    private Button? _restoreAutosaveButton;
    private TextBlock? _scenarioSelectionErrorText;
    private ScrollViewer? _stepJournalScrollViewer;
    private Panel? _stepJournalPanel;
    private IRecorderScenarioPathDetails? _scenarioPathDetails;
    private IRecorderStepReorderSessionDetails? _stepReorderDetails;
    private IRecorderScenarioSelectionDetails? _scenarioSelectionDetails;
    private IRecorderCheckpointSessionDetails? _checkpointDetails;
    private IRecorderGeneratedValueSessionDetails? _generatedValueDetails;
    private IRecorderRelativeDateSessionDetails? _relativeDateDetails;
    private int _renderedJournalEntryCount;
    private bool _isRefreshingScenarioSelection;

    public RecorderOverlay()
    {
        AvaloniaXamlLoader.Load(this);
        InitializeControls();
    }

    public event EventHandler? ExportRequested;

    internal Action<ScrollViewer>? ScrollToEndForTesting { get; set; }

    internal Control? LastDateExpressionEditorForTesting { get; private set; }

    internal void RefreshForTesting()
    {
        Refresh();
    }

    public void Attach(IAppAutomationRecorderSession session, AppAutomationRecorderOptions options)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _sessionDetails = session as IAppAutomationRecorderSessionDetails;
        _scenarioPathDetails = session as IRecorderScenarioPathDetails;
        _stepReorderDetails = session as IRecorderStepReorderSessionDetails;
        _scenarioSelectionDetails = session as IRecorderScenarioSelectionDetails;
        _checkpointDetails = session as IRecorderCheckpointSessionDetails;
        _generatedValueDetails = session as IRecorderGeneratedValueSessionDetails;
        _relativeDateDetails = session as IRecorderRelativeDateSessionDetails;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ApplyThemeResources(ResolveOverlayTheme(options.OverlayTheme));

        if (_shortcutText is not null)
        {
            _shortcutText.IsVisible = options.Overlay.ShowShortcutLegend;
            RefreshShortcutLegend();
        }

        if (_settingsButton is not null)
        {
            _settingsButton.IsEnabled = session is RecorderSession;
        }

        if (session is RecorderSession recorderSession)
        {
            recorderSession.HotkeysChanged += OnHotkeysChanged;
        }

        if (_exportButton is not null)
        {
            _exportButton.IsVisible = options.Overlay.EnableExportButton;
        }

        if (_sessionDetails is not null)
        {
            _sessionDetails.SessionChanged += OnSessionChanged;
        }
        else
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
        }

        if (_checkpointDetails is not null)
        {
            _checkpointDetails.CheckTargetSelected += OnCheckTargetSelected;
        }

        if (_generatedValueDetails is not null)
        {
            _generatedValueDetails.GeneratedValueTargetSelected += OnGeneratedValueTargetSelected;
        }

        Refresh();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(Refresh);
    }

    private void OnHotkeysChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(RefreshShortcutLegend);
    }

    private void RunOnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess() || TopLevel.GetTopLevel(this) is null)
        {
            action();
            return;
        }

        Dispatcher.Post(action);
    }

    private void InitializeControls()
    {
        _recordButton = this.FindControl<Button>("RecordButton");
        _clearButton = this.FindControl<Button>("ClearButton");
        _saveButton = this.FindControl<Button>("SaveButton");
        _exportButton = this.FindControl<Button>("ExportButton");
        _settingsButton = this.FindControl<Button>("SettingsButton");
        _checkButton = this.FindControl<Button>("CheckButton");
        _generateValueButton = this.FindControl<Button>("GenerateValueButton");
        _copyDiagnosticLogPathButton = this.FindControl<Button>("CopyDiagnosticLogPathButton");
        _diagnosticLogCheckBox = this.FindControl<CheckBox>("DiagnosticLogCheckBox");
        _stepCounter = this.FindControl<TextBlock>("StepCounter");
        _statusText = this.FindControl<TextBlock>("StatusText");
        _previewText = this.FindControl<TextBlock>("PreviewText");
        _sessionSummaryText = this.FindControl<TextBlock>("SessionSummaryText");
        _scenarioPathText = this.FindControl<TextBlock>("ScenarioPathText");
        _diagnosticLogPathText = this.FindControl<TextBlock>("DiagnosticLogPathText");
        _shortcutText = this.FindControl<TextBlock>("ShortcutText");
        _validationBadgeText = this.FindControl<TextBlock>("ValidationBadgeText");
        _journalEmptyText = this.FindControl<TextBlock>("JournalEmptyText");
        _scenarioSelectionPanel = this.FindControl<Control>("ScenarioSelectionPanel");
        _scenarioScanProgress = this.FindControl<ProgressBar>("ScenarioScanProgress");
        _scenarioScanStatus = this.FindControl<TextBlock>("ScenarioScanStatus");
        _scenarioDestinationComboBox = this.FindControl<ComboBox>("ScenarioDestinationComboBox");
        _scenarioNameTextBox = this.FindControl<TextBox>("ScenarioNameTextBox");
        _restoreAutosaveButton = this.FindControl<Button>("RestoreAutosaveButton");
        _scenarioSelectionErrorText = this.FindControl<TextBlock>("ScenarioSelectionErrorText");
        _stepJournalScrollViewer = this.FindControl<ScrollViewer>("StepJournalScrollViewer");
        _stepJournalPanel = this.FindControl<Panel>("StepJournalPanel");

        if (_recordButton is not null)
        {
            _recordButton.Click += OnRecordClick;
        }

        if (_clearButton is not null)
        {
            _clearButton.Click += (_, _) => _session?.Clear();
        }

        if (_saveButton is not null)
        {
            _saveButton.Click += OnSaveClick;
        }

        if (_exportButton is not null)
        {
            _exportButton.Click += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
        }

        if (_settingsButton is not null)
        {
            _settingsButton.Click += OnSettingsClick;
        }

        if (_checkButton is not null)
        {
            _checkButton.Click += OnCheckClick;
        }

        if (_generateValueButton is not null)
        {
            _generateValueButton.Click += OnGenerateValueClick;
        }

        if (_diagnosticLogCheckBox is not null)
        {
            _diagnosticLogCheckBox.Click += OnDiagnosticLogToggleClick;
        }

        if (_copyDiagnosticLogPathButton is not null)
        {
            _copyDiagnosticLogPathButton.Click += OnCopyDiagnosticLogPathClick;
        }

        if (_scenarioDestinationComboBox is not null)
        {
            _scenarioDestinationComboBox.SelectionChanged += OnScenarioDestinationSelectionChanged;
        }

        if (_scenarioNameTextBox is not null)
        {
            _scenarioNameTextBox.TextChanged += OnScenarioNameTextChanged;
        }

        if (_restoreAutosaveButton is not null)
        {
            _restoreAutosaveButton.Click += OnRestoreAutosaveClick;
        }

        AddHandler(
            InputElement.KeyDownEvent,
            OnOverlayKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void OnOverlayKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (_checkpointDetails?.IsCheckTargetSelectionActive == true)
        {
            _checkpointDetails.CancelCheckTargetSelection();
            e.Handled = true;
        }
        else if (_generatedValueDetails?.IsGeneratedValueTargetSelectionActive == true)
        {
            _generatedValueDetails.CancelGeneratedValueTargetSelection();
            e.Handled = true;
        }
    }

    private void OnScenarioDestinationSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingScenarioSelection || _scenarioSelectionDetails is null)
        {
            return;
        }

        _scenarioSelectionDetails.TrySelectScenarioDestination(
            _scenarioDestinationComboBox?.SelectedItem as RecordedScenarioDestination);
        Refresh();
    }

    private void OnScenarioNameTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isRefreshingScenarioSelection || _scenarioSelectionDetails is null)
        {
            return;
        }

        _scenarioSelectionDetails.TrySetScenarioName(_scenarioNameTextBox?.Text);
        Refresh();
    }

    private void OnRecordClick(object? sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        if (_session.State == RecorderSessionState.Recording)
        {
            _session.Stop();
        }
        else
        {
            _session.Start();
        }

        Refresh();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        _ = await _session.SaveAsync();
        Refresh();
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_session is not RecorderSession recorderSession || _options is null)
        {
            return;
        }

        var dialog = new RecorderHotkeySettingsWindow(recorderSession.HotkeySettings, _options.Hotkeys);
        var owner = TopLevel.GetTopLevel(this) as Window;
        var result = owner is not null
            ? await dialog.ShowDialog<RecorderHotkeySettingsWindowResult?>(owner)
            : await ShowStandaloneDialogAsync(dialog);
        if (result is null)
        {
            return;
        }

        var validation = result.Settings.Validate();
        if (!validation.IsValid)
        {
            ShowSettingsError(validation.ErrorMessage);
            return;
        }

        var store = new RecorderHotkeySettingsStore();
        try
        {
            if (result.ResetToDefaults)
            {
                store.Reset();
            }
            else
            {
                await store.SaveAsync(result.Settings.CreateOverridesAgainst(_options.Hotkeys));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowSettingsError($"Hotkey settings were not saved: {exception.Message}");
            return;
        }

        if (!recorderSession.TryApplyHotkeySettings(result.Settings, out var error))
        {
            ShowSettingsError(error ?? "Hotkey settings were not applied.");
            return;
        }

        RefreshShortcutLegend();
        Refresh();
    }

    private async void OnRestoreAutosaveClick(object? sender, RoutedEventArgs e)
    {
        if (_scenarioSelectionDetails is null)
        {
            return;
        }

        await _scenarioSelectionDetails.RestoreAutosaveAsync();
        Refresh();
    }

    private void OnCheckClick(object? sender, RoutedEventArgs e)
    {
        _checkpointDetails?.BeginCheckTargetSelection();
    }

    private void OnGenerateValueClick(object? sender, RoutedEventArgs e)
    {
        if (_generateValueButton is null || _generatedValueDetails is null)
        {
            return;
        }

        CreateGeneratedValueMenu().ShowAt(_generateValueButton);
    }

    private MenuFlyout CreateGeneratedValueMenu()
    {
        if (_generatedValueDetails is null)
        {
            throw new InvalidOperationException("Recorder generated-value details are not attached.");
        }

        var menu = new MenuFlyout();
        var create = new MenuItem { Header = "New value" };
        create.Click += (_, _) => _generatedValueDetails.BeginGeneratedValueTargetSelection();
        menu.Items.Add(create);

        var existingValues = _generatedValueDetails.GeneratedValues;
        if (existingValues.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        foreach (var generatedValue in existingValues)
        {
            var reuse = new MenuItem
            {
                Header = $"{generatedValue.VariableName} ({generatedValue.PreviewValue})",
                Tag = generatedValue.GeneratedValueId
            };
            reuse.Click += (_, _) =>
                _generatedValueDetails.BeginGeneratedValueTargetSelection(generatedValue.GeneratedValueId);
            menu.Items.Add(reuse);
        }

        return menu;
    }

    internal MenuFlyout CreateGeneratedValueMenuForTesting() => CreateGeneratedValueMenu();

    private void OnGeneratedValueTargetSelected(
        object? sender,
        RecorderGeneratedValueTargetSelectedEventArgs e)
    {
        RunOnUiThread(() => ShowGeneratedValueConfirmation(e.Selection));
    }

    private void ShowGeneratedValueConfirmation(RecorderGeneratedValueTargetSelection selection)
    {
        if (_generateValueButton is null)
        {
            return;
        }

        var flyout = new Flyout();
        flyout.Content = CreateGeneratedValueConfirmation(selection, flyout.Hide);
        flyout.ShowAt(_generateValueButton);
    }

    internal Control CreateGeneratedValueConfirmationForTesting(
        RecorderGeneratedValueTargetSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return CreateGeneratedValueConfirmation(selection, static () => { });
    }

    private StackPanel CreateGeneratedValueConfirmation(
        RecorderGeneratedValueTargetSelection selection,
        Action close)
    {
        if (_generatedValueDetails is null)
        {
            throw new InvalidOperationException("Recorder generated-value details are not attached.");
        }

        var add = new Button { Content = "Add", Padding = new Thickness(10, 4) };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(10, 4) };
        add.Click += (_, _) =>
        {
            _generatedValueDetails.ApplyGeneratedValue(selection);
            close();
        };
        cancel.Click += (_, _) => close();

        return new StackPanel
        {
            Width = 300,
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = selection.DefinesGeneratedValue
                        ? "Generate and enter value"
                        : "Enter generated value",
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = selection.GeneratedValue.PreviewValue,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = $"Target: {selection.ControlName}",
                    Foreground = GetBrush("RecorderMuted")
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { add, cancel }
                }
            }
        };
    }

    private void OnCheckTargetSelected(object? sender, RecorderCheckTargetSelectedEventArgs e)
    {
        RunOnUiThread(() => ShowCheckMenu(e.Selection));
    }

    private void ShowCheckMenu(RecorderCheckTargetSelection selection)
    {
        if (_checkButton is null || _checkpointDetails is null)
        {
            return;
        }

        var menu = CreateCheckMenu(selection);
        menu.ShowAt(_checkButton);
    }

    internal MenuFlyout CreateCheckMenuForTesting(RecorderCheckTargetSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return CreateCheckMenu(selection);
    }

    private MenuFlyout CreateCheckMenu(RecorderCheckTargetSelection selection)
    {
        if (_checkpointDetails is null)
        {
            throw new InvalidOperationException("Recorder checkpoint details are not attached.");
        }

        var menu = new MenuFlyout();
        var currentValue = selection.ValueDescription;
        var hasReadableValue = currentValue is not null;
        var remember = new MenuItem
        {
            Header = "Remember value…",
            IsEnabled = hasReadableValue
        };
        remember.Click += (_, _) => ShowRememberValueEditor(selection);
        menu.Items.Add(remember);

        var compare = new MenuItem { Header = "Compare with checkpoint" };
        var checkpoints = _checkpointDetails.Checkpoints
            .Where(checkpoint => checkpoint.ValueKind == currentValue?.ValueKind)
            .ToArray();
        compare.IsEnabled = checkpoints.Length > 0;
        foreach (var checkpoint in checkpoints)
        {
            var checkpointItem = new MenuItem
            {
                Header = $"{checkpoint.VariableName} ({checkpoint.ControlName})",
                Tag = checkpoint.CheckpointId
            };
            if (checkpoint.ValueKind == RecorderValueKind.StringSet)
            {
                checkpointItem.Click += (_, _) =>
                    _checkpointDetails.CaptureCheckpointAssertion(
                        selection,
                        checkpoint.CheckpointId,
                        RecorderComparisonKind.Equivalent);
            }
            else
            {
                foreach (var comparisonKind in new[]
                         {
                             RecorderComparisonKind.Equal,
                             RecorderComparisonKind.NotEqual
                         })
                {
                    var comparisonItem = new MenuItem
                    {
                        Header = DescribeComparison(comparisonKind)
                    };
                    comparisonItem.Click += (_, _) =>
                        _checkpointDetails.CaptureCheckpointAssertion(
                            selection,
                            checkpoint.CheckpointId,
                            comparisonKind);
                    checkpointItem.Items.Add(comparisonItem);
                }
            }

            compare.Items.Add(checkpointItem);
        }

        menu.Items.Add(compare);

        var compareWithGenerated = new MenuItem { Header = "Compare with generated value" };
        var generatedValues = currentValue?.ValueKind is RecorderValueKind.Text or RecorderValueKind.GridCellText
            ? _generatedValueDetails?.GeneratedValues ?? Array.Empty<RecorderGeneratedValueOption>()
            : Array.Empty<RecorderGeneratedValueOption>();
        compareWithGenerated.IsEnabled = generatedValues.Count > 0;
        foreach (var generatedValue in generatedValues)
        {
            var generatedValueItem = new MenuItem
            {
                Header = generatedValue.VariableName,
                Tag = generatedValue.GeneratedValueId
            };
            foreach (var comparisonKind in new[]
                     {
                         RecorderComparisonKind.Equal,
                         RecorderComparisonKind.NotEqual
                     })
            {
                var comparisonItem = new MenuItem { Header = DescribeComparison(comparisonKind) };
                comparisonItem.Click += (_, _) =>
                    _generatedValueDetails?.CaptureGeneratedValueAssertion(
                        selection,
                        generatedValue.GeneratedValueId,
                        comparisonKind);
                generatedValueItem.Items.Add(comparisonItem);
            }

            compareWithGenerated.Items.Add(generatedValueItem);
        }

        menu.Items.Add(compareWithGenerated);

        if (currentValue is not null
            && RecorderValueAssertions.TryGetHasValueAssertionKind(currentValue.ValueKind, out _))
        {
            menu.Items.Add(CreatePresenceAssertionMenu(selection, currentValue.ValueKind));
        }

        var assertExpected = new MenuItem
        {
            Header = "Assert expected value…",
            IsEnabled = hasReadableValue
        };
        assertExpected.Click += (_, _) => ShowLiteralAssertionEditor(selection);
        menu.Items.Add(assertExpected);

        var assertEnabled = new MenuItem
        {
            Header = "Enable…"
        };
        assertEnabled.Click += (_, _) => ShowEnabledAssertionEditor(selection);
        menu.Items.Add(assertEnabled);

        if (!hasReadableValue && !string.IsNullOrWhiteSpace(selection.ValueDescriptionError))
        {
            menu.Items.Add(new MenuItem
            {
                Header = selection.ValueDescriptionError,
                IsEnabled = false
            });
        }

        return menu;
    }

    private MenuItem CreatePresenceAssertionMenu(
        RecorderCheckTargetSelection selection,
        RecorderValueKind valueKind)
    {
        if (_checkpointDetails is null)
        {
            throw new InvalidOperationException("Recorder checkpoint details are not attached.");
        }

        if (!RecorderValueAssertions.TryGetHasValueAssertionKind(valueKind, out var assertionKind))
        {
            throw new InvalidOperationException(
                $"A presence assertion is not meaningful for {valueKind} values.");
        }

        var assertPresence = new MenuItem { Header = "Has Value" };
        var hasValue = new MenuItem
        {
            Header = assertionKind == RecorderHasValueAssertionKind.NotNull
                ? "IsNotNull"
                : "IsNotEmpty"
        };
        var empty = new MenuItem
        {
            Header = assertionKind == RecorderHasValueAssertionKind.NotNull
                ? "IsNull"
                : "IsEmpty"
        };
        hasValue.Click += (_, _) =>
            _checkpointDetails.CapturePresenceAssertion(selection, expectEmpty: false);
        empty.Click += (_, _) =>
            _checkpointDetails.CapturePresenceAssertion(selection, expectEmpty: true);
        assertPresence.ItemsSource = new[] { hasValue, empty };
        return assertPresence;
    }

    private void ShowEnabledAssertionEditor(RecorderCheckTargetSelection selection)
    {
        if (_checkButton is null || _checkpointDetails is null)
        {
            return;
        }

        var flyout = new Flyout();
        flyout.Content = CreateEnabledAssertionEditor(selection, flyout.Hide);
        flyout.ShowAt(_checkButton);
    }

    internal Control CreateEnabledAssertionEditorForTesting(RecorderCheckTargetSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return CreateEnabledAssertionEditor(selection, static () => { });
    }

    private StackPanel CreateEnabledAssertionEditor(
        RecorderCheckTargetSelection selection,
        Action close)
    {
        if (_checkpointDetails is null)
        {
            throw new InvalidOperationException("Recorder checkpoint details are not attached.");
        }

        var expected = new ComboBox
        {
            Name = "RecorderExpectedEnabled",
            ItemsSource = new[] { "true", "false" },
            SelectedIndex = selection.IsEnabled ? 0 : 1,
            MinWidth = 120
        };
        var add = new Button { Content = "Add", Padding = new Thickness(10, 4) };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(10, 4) };
        add.Click += (_, _) =>
        {
            _checkpointDetails.CaptureEnabledAssertion(selection, expected.SelectedIndex == 0);
            close();
        };
        cancel.Click += (_, _) => close();

        return new StackPanel
        {
            Width = 220,
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = "Enable",
                    FontWeight = FontWeight.SemiBold
                },
                expected,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { add, cancel }
                }
            }
        };
    }

    private void ShowRememberValueEditor(RecorderCheckTargetSelection selection)
    {
        var description = selection.ValueDescription;
        if (_checkButton is null || _checkpointDetails is null || description is null)
        {
            return;
        }

        var name = new TextBox
        {
            Text = description.SuggestedCheckpointName,
            MinWidth = 220,
            PlaceholderText = "Checkpoint name"
        };
        var add = new Button { Content = "Remember", Padding = new Thickness(10, 4) };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(10, 4) };
        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Width = 260,
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Remember runtime value",
                        FontWeight = FontWeight.SemiBold
                    },
                    name,
                    new TextBlock
                    {
                        Text = $"Current preview: {description.CurrentValueText}",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = GetBrush("RecorderMuted")
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children = { add, cancel }
                    }
                }
            }
        };
        add.Click += (_, _) =>
        {
            _checkpointDetails.CaptureCheckpoint(selection, name.Text);
            flyout.Hide();
        };
        cancel.Click += (_, _) => flyout.Hide();
        flyout.ShowAt(_checkButton);
        name.Focus();
        name.SelectAll();
    }

    private void ShowLiteralAssertionEditor(RecorderCheckTargetSelection selection)
    {
        if (_checkButton is null || _checkpointDetails is null)
        {
            return;
        }

        if (selection.ValueDescription is not { } description)
        {
            ShowSettingsError(
                selection.ValueDescriptionError
                ?? "The selected control does not expose a readable value.");
            return;
        }

        var flyout = new Flyout();
        var content = CreateLiteralAssertionEditor(selection, flyout.Hide);
        flyout.Content = content;
        flyout.ShowAt(_checkButton);
        var expected = content.GetLogicalDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(textBox => textBox.Name == "RecorderExpectedValue");
        expected?.Focus();
        expected?.SelectAll();
    }

    internal Control CreateLiteralAssertionEditorForTesting(RecorderCheckTargetSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return CreateLiteralAssertionEditor(selection, static () => { });
    }

    private StackPanel CreateLiteralAssertionEditor(
        RecorderCheckTargetSelection selection,
        Action close)
    {
        if (_checkpointDetails is null || selection.ValueDescription is not { } description)
        {
            throw new InvalidOperationException(
                selection.ValueDescriptionError
                ?? "The selected control does not expose a readable value.");
        }

        var comparisons = description.ValueKind switch
        {
            RecorderValueKind.StringSet => new[] { RecorderComparisonKind.Equivalent },
            RecorderValueKind.Text or RecorderValueKind.GridCellText =>
                new[]
                {
                    RecorderComparisonKind.Equal,
                    RecorderComparisonKind.NotEqual,
                    RecorderComparisonKind.Contains
                },
            _ => new[] { RecorderComparisonKind.Equal, RecorderComparisonKind.NotEqual }
        };
        var comparison = new ComboBox
        {
            Name = "RecorderExpectedComparison",
            ItemsSource = comparisons.Select(DescribeComparison).ToArray(),
            SelectedIndex = 0,
            MinWidth = 120
        };
        var expected = new TextBox
        {
            Name = "RecorderExpectedValue",
            Text = description.CurrentValueText,
            MinWidth = 220,
            PlaceholderText = "Expected value"
        };
        var dateEditor = description.ValueKind == RecorderValueKind.Date
            ? new RelativeDateOperandEditor(
                "Date mode",
                new RecorderDateOperandConfiguration(
                    selection.ValueSnapshot?.Prototype.DateValue,
                    RecorderDateReferenceKind.Exact,
                    DayOffset: 0),
                GetBrush("RecorderMuted"),
                showRecordedValue: false,
                controlNamePrefix: "RecorderLiteralDate")
            : null;
        var validation = new TextBlock
        {
            Foreground = GetBrush("RecorderDanger"),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        var add = new Button { Content = "Add", Padding = new Thickness(10, 4) };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(10, 4) };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { add, cancel }
        };
        var content = new StackPanel
        {
            Width = 260,
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = $"Assert {description.SuggestedCheckpointName}",
                    FontWeight = FontWeight.SemiBold
                },
                expected,
            }
        };
        if (dateEditor is not null)
        {
            content.Children.Add(dateEditor.Content);
        }

        content.Children.Add(comparison);
        content.Children.Add(validation);
        content.Children.Add(actions);

        void RefreshDateState()
        {
            expected.IsEnabled = dateEditor?.IsRelative != true;
            if (dateEditor is null)
            {
                add.IsEnabled = true;
                validation.IsVisible = false;
                return;
            }

            add.IsEnabled = dateEditor.TryGetExpression(out _, out var error);
            validation.Text = error;
            validation.IsVisible = !string.IsNullOrWhiteSpace(error);
        }

        if (dateEditor is not null)
        {
            dateEditor.Changed += (_, _) => RefreshDateState();
        }

        add.Click += (_, _) =>
        {
            var selectedIndex = Math.Clamp(comparison.SelectedIndex, 0, comparisons.Length - 1);
            RecorderDateExpression? dateExpression = null;
            if (dateEditor is not null
                && !dateEditor.TryGetExpression(out dateExpression, out var error))
            {
                validation.Text = error;
                validation.IsVisible = true;
                return;
            }

            _checkpointDetails.CaptureLiteralAssertion(
                selection,
                dateExpression is null ? expected.Text ?? string.Empty : description.CurrentValueText,
                comparisons[selectedIndex],
                dateExpression);
            close();
        };
        cancel.Click += (_, _) => close();
        RefreshDateState();
        return content;
    }

    private static string DescribeComparison(RecorderComparisonKind comparisonKind) =>
        comparisonKind switch
        {
            RecorderComparisonKind.Equal => "Equals",
            RecorderComparisonKind.NotEqual => "Not equals",
            RecorderComparisonKind.Contains => "Contains",
            RecorderComparisonKind.Equivalent => "Same items",
            _ => comparisonKind.ToString()
        };

    private void OnDiagnosticLogToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_sessionDetails is null || _diagnosticLogCheckBox is null)
        {
            return;
        }

        _sessionDetails.SetDiagnosticLogFileEnabled(_diagnosticLogCheckBox.IsChecked == true);
        Refresh();
    }

    private async void OnCopyDiagnosticLogPathClick(object? sender, RoutedEventArgs e)
    {
        if (_sessionDetails is null)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
        {
            return;
        }

        await topLevel.Clipboard.SetTextAsync(_sessionDetails.DiagnosticLogFilePath);
    }

    private void Refresh()
    {
        if (_session is null)
        {
            return;
        }

        if (_recordButton is not null)
        {
            _recordButton.Content = _session.State == RecorderSessionState.Recording ? "Stop" : "Record";
        }

        var isBusy = _sessionDetails?.IsBusy == true;
        if (_recordButton is not null)
        {
            _recordButton.IsEnabled = !isBusy
                && (_session.State == RecorderSessionState.Recording
                    || _scenarioSelectionDetails?.CanStartRecording != false);
        }
        if (_clearButton is not null)
        {
            _clearButton.IsEnabled = !isBusy
                && (_scenarioSelectionDetails?.IsScenarioSelectionEnabled != true
                    || _session.State == RecorderSessionState.Off);
        }

        if (_saveButton is not null)
        {
            _saveButton.IsEnabled = !isBusy && CanPersistSelectedScenario();
            _saveButton.Content = isBusy ? "Saving..." : "Save";
        }

        if (_exportButton is not null)
        {
            _exportButton.IsEnabled = !isBusy && CanPersistSelectedScenario();
            _exportButton.Content = isBusy ? "Busy..." : "Export...";
        }

        if (_checkButton is not null)
        {
            _checkButton.IsEnabled = !isBusy
                && _session.State == RecorderSessionState.Recording
                && _checkpointDetails is not null;
            var hotkeyMap = _session is RecorderSession recorderSession
                ? recorderSession.HotkeyMap
                : _options is null
                    ? null
                    : RecorderHotkeyMap.Create(_options.Hotkeys);
            if (hotkeyMap is not null)
            {
                ToolTip.SetTip(
                    _checkButton,
                    $"Remember: {hotkeyMap.GetDisplayText(RecorderCommandKind.CaptureCheckpoint)}; "
                    + $"compare: {hotkeyMap.GetDisplayText(RecorderCommandKind.CaptureCheckpointAssertion)}");
            }
        }

        if (_generateValueButton is not null)
        {
            _generateValueButton.IsEnabled = !isBusy
                && _session.State == RecorderSessionState.Recording
                && _generatedValueDetails is not null;
            ToolTip.SetTip(
                _generateValueButton,
                "Create a new runtime value or reuse one already generated in this scenario.");
        }

        if (_stepCounter is not null)
        {
            _stepCounter.Text = _session.PersistableStepCount == _session.StepCount
                ? $"{_session.StepCount} steps"
                : $"{_session.PersistableStepCount}/{_session.StepCount} steps";
        }

        if (_statusText is not null)
        {
            _statusText.Text = _session.LatestStatus;
            _statusText.Foreground = GetBrush("RecorderMuted");
        }

        if (_previewText is not null)
        {
            _previewText.Text = _session.LatestPreview;
        }

        if (_sessionSummaryText is not null)
        {
            _sessionSummaryText.Text = _sessionDetails?.SessionSummary ?? _session.LatestStatus;
        }

        if (_scenarioPathText is not null)
        {
            _scenarioPathText.Text = _scenarioPathDetails?.CurrentScenarioFilePath ?? "Scenario file path is unavailable.";
        }

        if (_diagnosticLogCheckBox is not null)
        {
            _diagnosticLogCheckBox.IsEnabled = _sessionDetails is not null;
            _diagnosticLogCheckBox.IsChecked = _sessionDetails?.IsDiagnosticLogFileEnabled == true;
        }

        if (_diagnosticLogPathText is not null)
        {
            _diagnosticLogPathText.Text = _sessionDetails is null
                ? "Diagnostic log file is unavailable."
                : _sessionDetails.IsDiagnosticLogFileEnabled
                    ? $"{_sessionDetails.DiagnosticLogFilePath} ({_sessionDetails.DiagnosticLogEntryCount} entries)"
                    : $"Off. File path when enabled: {_sessionDetails.DiagnosticLogFilePath}";
        }

        if (_copyDiagnosticLogPathButton is not null)
        {
            _copyDiagnosticLogPathButton.IsEnabled = _sessionDetails is not null;
        }

        RefreshScenarioSelection();

        RenderStepJournal();
        UpdateValidationBadge(_session.LatestValidationStatus);
    }

    private bool CanPersistSelectedScenario()
    {
        return _scenarioSelectionDetails is not { IsScenarioSelectionEnabled: true } selection
            || (!selection.IsScanning
                && selection.SelectedScenarioDestination is not null
                && selection.ScenarioSelectionError is null);
    }

    private void RefreshScenarioSelection()
    {
        if (_scenarioSelectionPanel is null)
        {
            return;
        }

        var selection = _scenarioSelectionDetails;
        var isVisible = selection?.IsScenarioSelectionEnabled == true;
        _scenarioSelectionPanel.IsVisible = isVisible;
        if (!isVisible || selection is null)
        {
            return;
        }

        _isRefreshingScenarioSelection = true;
        try
        {
            if (_scenarioScanProgress is not null)
            {
                _scenarioScanProgress.IsVisible = selection.IsScanning;
            }

            if (_scenarioScanStatus is not null)
            {
                _scenarioScanStatus.IsVisible = selection.IsScanning;
            }

            if (_scenarioDestinationComboBox is not null)
            {
                _scenarioDestinationComboBox.ItemsSource = selection.ScenarioDestinations;
                _scenarioDestinationComboBox.SelectedItem = selection.SelectedScenarioDestination;
                _scenarioDestinationComboBox.IsEnabled = selection.CanChangeScenarioTarget;
            }

            if (_scenarioNameTextBox is not null)
            {
                if (!string.Equals(_scenarioNameTextBox.Text, selection.ScenarioName, StringComparison.Ordinal))
                {
                    _scenarioNameTextBox.Text = selection.ScenarioName;
                }

                _scenarioNameTextBox.IsEnabled = selection.CanChangeScenarioTarget;
            }

            if (_restoreAutosaveButton is not null)
            {
                _restoreAutosaveButton.IsEnabled = selection.CanRestoreAutosave;
            }

            if (_scenarioSelectionErrorText is not null)
            {
                var error = selection.IsScanning ? null : selection.ScenarioSelectionError;
                _scenarioSelectionErrorText.Text = error ?? string.Empty;
                _scenarioSelectionErrorText.IsVisible = !string.IsNullOrWhiteSpace(error);
            }
        }
        finally
        {
            _isRefreshingScenarioSelection = false;
        }
    }

    private void RefreshShortcutLegend()
    {
        if (_shortcutText is null || _options is null)
        {
            return;
        }

        _shortcutText.IsVisible = _options.Overlay.ShowShortcutLegend;
        _shortcutText.Text = _session is RecorderSession recorderSession
            ? recorderSession.HotkeyMap.BuildLegend()
            : RecorderHotkeyMap.Create(_options.Hotkeys).BuildLegend();
    }

    private void ShowSettingsError(string message)
    {
        if (_statusText is not null)
        {
            _statusText.Text = message;
            _statusText.Foreground = GetBrush("RecorderDanger");
        }
    }

    private static Task<RecorderHotkeySettingsWindowResult?> ShowStandaloneDialogAsync(RecorderHotkeySettingsWindow dialog)
    {
        var completion = new TaskCompletionSource<RecorderHotkeySettingsWindowResult?>();
        dialog.Closed += (_, _) => completion.TrySetResult(dialog.Result);
        dialog.Show();
        return completion.Task;
    }

    private void RenderStepJournal()
    {
        if (_stepJournalPanel is null || _journalEmptyText is null)
        {
            return;
        }

        _stepJournalPanel.Children.Clear();
        var entries = _sessionDetails?.StepJournal
            ?.ToArray()
            ?? Array.Empty<RecorderStepJournalEntry>();
        var shouldScrollToEnd = entries.Length > _renderedJournalEntryCount;

        _journalEmptyText.IsVisible = entries.Length == 0;
        if (entries.Length == 0)
        {
            _renderedJournalEntryCount = 0;
            return;
        }

        for (var index = 0; index < entries.Length; index++)
        {
            _stepJournalPanel.Children.Add(CreateStepJournalItem(entries[index], index + 1));
        }

        _renderedJournalEntryCount = entries.Length;
        if (shouldScrollToEnd)
        {
            ScrollStepJournalToEnd();
        }
    }

    private Control CreateStepJournalItem(RecorderStepJournalEntry entry, int displayNumber)
    {
        var border = new Border
        {
            Background = GetBrush("RecorderSurfaceBackground"),
            BorderBrush = GetBrush("RecorderOverlayBorder"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6)
        };

        var container = new StackPanel
        {
            Spacing = 4
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };
        var badge = new TextBlock
        {
            Text = $"#{displayNumber} " + (entry.IsIgnored
                ? "IGNORED"
                : entry.ValidationStatus switch
                {
                    RecorderValidationStatus.Warning => "WARN",
                    RecorderValidationStatus.Invalid => "INVALID",
                    _ => "VALID"
                }),
            Foreground = entry.IsIgnored
                ? GetBrush("RecorderMuted")
                : entry.ValidationStatus switch
                {
                    RecorderValidationStatus.Warning => GetBrush("RecorderWarning"),
                    RecorderValidationStatus.Invalid => GetBrush("RecorderDanger"),
                    _ => GetBrush("RecorderAccent")
                },
            FontWeight = FontWeight.SemiBold
        };
        var status = new TextBlock
        {
            Text = entry.StatusMessage,
            Margin = new Thickness(10, 0, 0, 0),
            Foreground = GetBrush("RecorderMuted"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(status, 1);
        header.Children.Add(badge);
        header.Children.Add(status);

        var preview = new TextBlock
        {
            Text = entry.Preview,
            FontFamily = "Cascadia Mono, Consolas",
            Foreground = GetBrush("RecorderText"),
            TextWrapping = TextWrapping.Wrap
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };

        var canReorder = _stepReorderDetails is not null && !(_sessionDetails?.IsBusy ?? false);
        if (_relativeDateDetails?.TryGetDateConfiguration(entry.StepId, out var dateConfiguration) == true)
        {
            actions.Children.Add(CreateActionButton(
                DescribeDateConfiguration(dateConfiguration!),
                entry.StepId,
                OnEditDateExpressionClick,
                isEnabled: !(_sessionDetails?.IsBusy ?? false) && !entry.IsIgnored,
                toolTip: "Choose an exact or relative date"));
        }

        actions.Children.Add(CreateActionButton(
            "↑",
            entry.StepId,
            OnMoveStepEarlierClick,
            isEnabled: canReorder && _stepReorderDetails!.CanMoveStep(entry.StepId, RecorderStepMoveDirection.Earlier),
            toolTip: "Move earlier"));
        actions.Children.Add(CreateActionButton(
            "↓",
            entry.StepId,
            OnMoveStepLaterClick,
            isEnabled: canReorder && _stepReorderDetails!.CanMoveStep(entry.StepId, RecorderStepMoveDirection.Later),
            toolTip: "Move later"));
        actions.Children.Add(CreateActionButton("Remove", entry.StepId, OnRemoveStepClick, isEnabled: !(_sessionDetails?.IsBusy ?? false)));
        actions.Children.Add(CreateActionButton(entry.IsIgnored ? "Restore" : "Ignore", entry.StepId, OnIgnoreStepClick, isEnabled: !(_sessionDetails?.IsBusy ?? false)));
        actions.Children.Add(CreateActionButton("Retry", entry.StepId, OnRetryStepClick, isEnabled: !(_sessionDetails?.IsBusy ?? false)));
        actions.Children.Add(CreateActionButton("Copy", entry.StepId, OnCopyStepPreviewClick));

        container.Children.Add(header);
        container.Children.Add(preview);
        container.Children.Add(actions);
        border.Child = container;
        return border;
    }

    private static string DescribeDateConfiguration(RecorderStepDateConfiguration configuration)
    {
        var prefix = configuration.Secondary is null ? "Date" : "Dates";
        var primary = DescribeDateOperand(configuration.Primary);
        return configuration.Secondary is null
            ? $"{prefix}: {primary}"
            : $"{prefix}: {primary} / {DescribeDateOperand(configuration.Secondary)}";
    }

    private static string DescribeDateOperand(RecorderDateOperandConfiguration operand)
    {
        if (operand.ReferenceKind == RecorderDateReferenceKind.Exact)
        {
            return "Exact";
        }

        return operand.DayOffset switch
        {
            0 => "Today",
            > 0 => $"Today +{operand.DayOffset.ToString(CultureInfo.InvariantCulture)}d",
            _ => $"Today {operand.DayOffset.ToString(CultureInfo.InvariantCulture)}d"
        };
    }

    private void OnEditDateExpressionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid stepId } button
            || _relativeDateDetails?.TryGetDateConfiguration(stepId, out var configuration) != true)
        {
            return;
        }

        ShowDateExpressionEditor(button, configuration!);
    }

    private void ShowDateExpressionEditor(
        Button anchor,
        RecorderStepDateConfiguration configuration)
    {
        if (_relativeDateDetails is null)
        {
            return;
        }

        var primary = new RelativeDateOperandEditor(
            configuration.Secondary is null ? "Date" : "From",
            configuration.Primary,
            GetBrush("RecorderMuted"),
            controlNamePrefix: "RecorderJournalDate");
        var secondary = configuration.Secondary is null
            ? null
            : new RelativeDateOperandEditor(
                "To",
                configuration.Secondary,
                GetBrush("RecorderMuted"),
                controlNamePrefix: "RecorderJournalDateSecondary");
        var validation = new TextBlock
        {
            Name = "RecorderJournalDateValidation",
            Foreground = GetBrush("RecorderDanger"),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        var apply = new Button { Content = "Apply", Padding = new Thickness(10, 4) };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(10, 4) };
        var content = new StackPanel
        {
            Width = configuration.Secondary is null ? 300 : 340,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "Date value",
                    FontWeight = FontWeight.SemiBold
                },
                primary.Content
            }
        };
        if (secondary is not null)
        {
            content.Children.Add(secondary.Content);
        }

        content.Children.Add(validation);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { apply, cancel }
        });
        LastDateExpressionEditorForTesting = content;

        var flyout = new Flyout { Content = content };
        void RefreshValidation()
        {
            var primaryValid = primary.TryGetExpression(out _, out var primaryError);
            var secondaryValid = true;
            string? secondaryError = null;
            if (secondary is not null)
            {
                secondaryValid = secondary.TryGetExpression(out _, out secondaryError);
            }

            var error = primaryValid ? secondaryError : primaryError;
            apply.IsEnabled = primaryValid && secondaryValid;
            validation.Text = error;
            validation.IsVisible = !string.IsNullOrWhiteSpace(error);
        }

        primary.Changed += (_, _) => RefreshValidation();
        if (secondary is not null)
        {
            secondary.Changed += (_, _) => RefreshValidation();
        }

        apply.Click += (_, _) =>
        {
            if (!primary.TryGetExpression(out var primaryExpression, out var primaryError))
            {
                validation.Text = primaryError;
                validation.IsVisible = true;
                return;
            }

            RecorderDateExpression? secondaryExpression = null;
            if (secondary is not null
                && !secondary.TryGetExpression(out secondaryExpression, out var secondaryError))
            {
                validation.Text = secondaryError;
                validation.IsVisible = true;
                return;
            }

            if (!_relativeDateDetails.SetStepDateExpressions(
                    configuration.StepId,
                    primaryExpression,
                    secondaryExpression))
            {
                validation.Text = "The date expression could not be applied.";
                validation.IsVisible = true;
                return;
            }

            flyout.Hide();
        };
        cancel.Click += (_, _) => flyout.Hide();
        RefreshValidation();
        flyout.ShowAt(anchor);
    }

    private void ScrollStepJournalToEnd()
    {
        if (_stepJournalScrollViewer is null)
        {
            return;
        }

        if (ScrollToEndForTesting is { } scrollToEnd)
        {
            scrollToEnd(_stepJournalScrollViewer);
            return;
        }

        Dispatcher.Post(
            () =>
            {
                _stepJournalScrollViewer?.ScrollToEnd();
            },
            DispatcherPriority.Background);
    }

    internal static RecorderOverlayTheme ResolveOverlayTheme(RecorderOverlayTheme? requestedTheme)
    {
        if (requestedTheme.HasValue)
        {
            return requestedTheme.Value;
        }

        return Application.Current?.ActualThemeVariant == ThemeVariant.Dark
            ? RecorderOverlayTheme.Dark
            : RecorderOverlayTheme.Light;
    }

    internal static RecorderOverlayPalette GetPalette(RecorderOverlayTheme theme) =>
        theme == RecorderOverlayTheme.Dark
            ? new RecorderOverlayPalette(
                OverlayBackground: Color.Parse("#18212B"),
                SurfaceBackground: Color.Parse("#0F172A"),
                Border: Color.Parse("#334155"),
                Text: Color.Parse("#E2E8F0"),
                Accent: Color.Parse("#2DD4BF"),
                Muted: Color.Parse("#CBD5E1"),
                Warning: Color.Parse("#F59E0B"),
                Danger: Color.Parse("#F87171"))
            : new RecorderOverlayPalette(
                OverlayBackground: Color.Parse("#F4F6F8"),
                SurfaceBackground: Color.Parse("#FFFFFF"),
                Border: Color.Parse("#CBD5E1"),
                Text: Color.Parse("#0F172A"),
                Accent: Color.Parse("#0F766E"),
                Muted: Color.Parse("#475569"),
                Warning: Color.Parse("#B45309"),
                Danger: Color.Parse("#B91C1C"));

    private void ApplyThemeResources(RecorderOverlayTheme theme)
    {
        var palette = GetPalette(theme);
        Resources["RecorderOverlayBackground"] = new SolidColorBrush(palette.OverlayBackground);
        Resources["RecorderSurfaceBackground"] = new SolidColorBrush(palette.SurfaceBackground);
        Resources["RecorderOverlayBorder"] = new SolidColorBrush(palette.Border);
        Resources["RecorderText"] = new SolidColorBrush(palette.Text);
        Resources["RecorderAccent"] = new SolidColorBrush(palette.Accent);
        Resources["RecorderMuted"] = new SolidColorBrush(palette.Muted);
        Resources["RecorderWarning"] = new SolidColorBrush(palette.Warning);
        Resources["RecorderDanger"] = new SolidColorBrush(palette.Danger);
    }

    private Button CreateActionButton(
        string content,
        Guid stepId,
        EventHandler<RoutedEventArgs> handler,
        bool isEnabled = true,
        string? toolTip = null)
    {
        var button = new Button
        {
            Content = content,
            Tag = stepId,
            Padding = new Thickness(8, 3),
            IsEnabled = isEnabled
        };
        if (!string.IsNullOrWhiteSpace(toolTip))
        {
            ToolTip.SetTip(button, toolTip);
        }

        button.Click += handler;
        return button;
    }

    private void OnMoveStepEarlierClick(object? sender, RoutedEventArgs e)
    {
        MoveStep(sender, RecorderStepMoveDirection.Earlier);
    }

    private void OnMoveStepLaterClick(object? sender, RoutedEventArgs e)
    {
        MoveStep(sender, RecorderStepMoveDirection.Later);
    }

    private void MoveStep(object? sender, RecorderStepMoveDirection direction)
    {
        if (sender is not Button { Tag: Guid stepId } || _stepReorderDetails is null)
        {
            return;
        }

        if (_stepReorderDetails.MoveStep(stepId, direction))
        {
            Refresh();
        }
    }

    private void OnRemoveStepClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid stepId } || _sessionDetails is null)
        {
            return;
        }

        _sessionDetails.RemoveStep(stepId);
    }

    private void OnIgnoreStepClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid stepId } button || _sessionDetails is null)
        {
            return;
        }

        var currentEntry = _sessionDetails.StepJournal.FirstOrDefault(entry => entry.StepId == stepId);
        _sessionDetails.SetStepIgnored(stepId, !(currentEntry?.IsIgnored ?? false));
        button.Content = currentEntry?.IsIgnored == true ? "Ignore" : "Restore";
    }

    private void OnRetryStepClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid stepId } || _sessionDetails is null)
        {
            return;
        }

        _sessionDetails.RetryStepValidation(stepId);
    }

    private async void OnCopyStepPreviewClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid stepId } || _sessionDetails is null)
        {
            return;
        }

        var entry = _sessionDetails.StepJournal.FirstOrDefault(candidate => candidate.StepId == stepId);
        if (entry is null)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
        {
            return;
        }

        await topLevel.Clipboard.SetTextAsync(entry.Preview);
    }

    private void UpdateValidationBadge(RecorderValidationStatus status)
    {
        if (_validationBadgeText is null)
        {
            return;
        }

        _validationBadgeText.Text = status switch
        {
            RecorderValidationStatus.Warning => "WARN",
            RecorderValidationStatus.Invalid => "INVALID",
            _ => "VALID"
        };
        _validationBadgeText.Foreground = status switch
        {
            RecorderValidationStatus.Warning => GetBrush("RecorderWarning"),
            RecorderValidationStatus.Invalid => GetBrush("RecorderDanger"),
            _ => GetBrush("RecorderAccent")
        };
    }

    private IBrush GetBrush(string key)
    {
        return this.TryFindResource(key, out var value) && value is IBrush brush
            ? brush
            : Brushes.Gray;
    }

    private sealed class RelativeDateOperandEditor
    {
        private readonly DateTime? _exactDate;
        private readonly ComboBox _mode;
        private readonly TextBox _dayOffset;

        public RelativeDateOperandEditor(
            string label,
            RecorderDateOperandConfiguration configuration,
            IBrush mutedBrush,
            bool showRecordedValue = true,
            string? controlNamePrefix = null)
        {
            _exactDate = configuration.ExactDate;
            _mode = new ComboBox
            {
                Name = controlNamePrefix is null ? null : $"{controlNamePrefix}Mode",
                ItemsSource = new[] { "Exact date", "Today ± days" },
                SelectedIndex = configuration.ReferenceKind == RecorderDateReferenceKind.RelativeToToday ? 1 : 0,
                MinWidth = 145,
                IsEnabled = configuration.ExactDate.HasValue
            };
            _dayOffset = new TextBox
            {
                Name = controlNamePrefix is null ? null : $"{controlNamePrefix}Offset",
                Text = configuration.DayOffset.ToString(CultureInfo.InvariantCulture),
                Width = 74,
                HorizontalContentAlignment = HorizontalAlignment.Right
            };
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 6
            };
            row.Children.Add(_mode);
            Grid.SetColumn(_dayOffset, 1);
            row.Children.Add(_dayOffset);
            var content = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = label, FontWeight = FontWeight.SemiBold },
                    row
                }
            };
            if (showRecordedValue)
            {
                content.Children.Add(new TextBlock
                {
                    Text = configuration.ExactDate.HasValue
                        ? $"Recorded: {configuration.ExactDate.Value:yyyy-MM-dd}"
                        : "Recorded boundary is empty",
                    Foreground = mutedBrush
                });
            }

            Content = content;

            _mode.SelectionChanged += (_, _) =>
            {
                RefreshOffsetState();
                Changed?.Invoke(this, EventArgs.Empty);
            };
            _dayOffset.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            RefreshOffsetState();
        }

        public event EventHandler? Changed;

        public Control Content { get; }

        public bool IsRelative => _mode.SelectedIndex == 1;

        public bool TryGetExpression(
            out RecorderDateExpression? expression,
            out string? error)
        {
            expression = null;
            error = null;
            if (_mode.SelectedIndex != 1)
            {
                return true;
            }

            if (!_exactDate.HasValue)
            {
                error = "A relative expression cannot be used for an empty boundary.";
                return false;
            }

            if (!int.TryParse(
                    _dayOffset.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var dayOffset))
            {
                error = "Enter a whole number of days.";
                return false;
            }

            try
            {
                _ = DateTime.Today.AddDays(dayOffset);
            }
            catch (ArgumentOutOfRangeException)
            {
                error = "The relative date is outside the supported range.";
                return false;
            }

            expression = new RecorderDateExpression(
                RecorderDateReferenceKind.RelativeToToday,
                dayOffset);
            return true;
        }

        private void RefreshOffsetState()
        {
            _dayOffset.IsEnabled = _exactDate.HasValue && _mode.SelectedIndex == 1;
        }
    }

    internal readonly record struct RecorderOverlayPalette(
        Color OverlayBackground,
        Color SurfaceBackground,
        Color Border,
        Color Text,
        Color Accent,
        Color Muted,
        Color Warning,
        Color Danger);
}
