using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
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
    private Button? _minimizeButton;
    private Button? _restoreButton;
    private TextBlock? _stepCounter;
    private TextBlock? _statusText;
    private TextBlock? _previewText;
    private TextBlock? _sessionSummaryText;
    private TextBlock? _scenarioPathText;
    private TextBlock? _shortcutText;
    private TextBlock? _validationBadgeText;
    private TextBlock? _minimizedStatusText;
    private TextBlock? _journalEmptyText;
    private Control? _expandedPanel;
    private Control? _minimizedPanel;
    private Panel? _stepJournalPanel;
    private IRecorderScenarioPathDetails? _scenarioPathDetails;

    public RecorderOverlay()
    {
        AvaloniaXamlLoader.Load(this);
        InitializeControls();
    }

    public event EventHandler? ExportRequested;

    public event EventHandler? MinimizeRequested;

    public event EventHandler? RestoreRequested;

    public bool IsMinimized { get; private set; }

    public void Attach(IAppAutomationRecorderSession session, AppAutomationRecorderOptions options)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _sessionDetails = session as IAppAutomationRecorderSessionDetails;
        _scenarioPathDetails = session as IRecorderScenarioPathDetails;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ApplyThemeResources(ResolveOverlayTheme(options.OverlayTheme));

        if (_shortcutText is not null)
        {
            _shortcutText.IsVisible = options.Overlay.ShowShortcutLegend;
            _shortcutText.Text = RecorderHotkeyMap.Create(options.Hotkeys).BuildLegend();
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

        if (options.Overlay.StartMinimized)
        {
            Minimize();
        }

        Refresh();
    }

    public void ToggleMinimized()
    {
        if (IsMinimized)
        {
            Restore();
            return;
        }

        Minimize();
    }

    public void Minimize()
    {
        if (IsMinimized)
        {
            return;
        }

        IsMinimized = true;
        UpdatePanelVisibility();
        MinimizeRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Restore()
    {
        if (!IsMinimized)
        {
            return;
        }

        IsMinimized = false;
        UpdatePanelVisibility();
        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(Refresh);
    }

    private void InitializeControls()
    {
        _recordButton = this.FindControl<Button>("RecordButton");
        _clearButton = this.FindControl<Button>("ClearButton");
        _saveButton = this.FindControl<Button>("SaveButton");
        _exportButton = this.FindControl<Button>("ExportButton");
        _minimizeButton = this.FindControl<Button>("MinimizeButton");
        _restoreButton = this.FindControl<Button>("RestoreButton");
        _stepCounter = this.FindControl<TextBlock>("StepCounter");
        _statusText = this.FindControl<TextBlock>("StatusText");
        _previewText = this.FindControl<TextBlock>("PreviewText");
        _sessionSummaryText = this.FindControl<TextBlock>("SessionSummaryText");
        _scenarioPathText = this.FindControl<TextBlock>("ScenarioPathText");
        _shortcutText = this.FindControl<TextBlock>("ShortcutText");
        _validationBadgeText = this.FindControl<TextBlock>("ValidationBadgeText");
        _minimizedStatusText = this.FindControl<TextBlock>("MinimizedStatusText");
        _journalEmptyText = this.FindControl<TextBlock>("JournalEmptyText");
        _expandedPanel = this.FindControl<Control>("ExpandedPanel");
        _minimizedPanel = this.FindControl<Control>("MinimizedPanel");
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

        if (_minimizeButton is not null)
        {
            _minimizeButton.Click += (_, _) => Minimize();
        }

        if (_restoreButton is not null)
        {
            _restoreButton.Click += (_, _) => Restore();
        }

        UpdatePanelVisibility();
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
        if (_clearButton is not null)
        {
            _clearButton.IsEnabled = !isBusy;
        }

        if (_saveButton is not null)
        {
            _saveButton.IsEnabled = !isBusy;
            _saveButton.Content = isBusy ? "Saving..." : "Save";
        }

        if (_exportButton is not null)
        {
            _exportButton.IsEnabled = !isBusy;
            _exportButton.Content = isBusy ? "Busy..." : "Export...";
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
        }

        if (_minimizedStatusText is not null)
        {
            _minimizedStatusText.Text = _sessionDetails?.SessionSummary ?? _session.LatestStatus;
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

        RenderStepJournal();
        UpdateValidationBadge(_session.LatestValidationStatus);
    }

    private void RenderStepJournal()
    {
        if (_stepJournalPanel is null || _journalEmptyText is null)
        {
            return;
        }

        _stepJournalPanel.Children.Clear();
        var entries = _sessionDetails?.StepJournal
            ?.Reverse()
            .Take(12)
            .ToArray()
            ?? Array.Empty<RecorderStepJournalEntry>();

        _journalEmptyText.IsVisible = entries.Length == 0;
        if (entries.Length == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            _stepJournalPanel.Children.Add(CreateStepJournalItem(entry));
        }
    }

    private Control CreateStepJournalItem(RecorderStepJournalEntry entry)
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
            Text = entry.IsIgnored
                ? "IGNORED"
                : entry.ValidationStatus switch
                {
                    RecorderValidationStatus.Warning => "WARN",
                    RecorderValidationStatus.Invalid => "INVALID",
                    _ => "VALID"
                },
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
        bool isEnabled = true)
    {
        var button = new Button
        {
            Content = content,
            Tag = stepId,
            Padding = new Thickness(8, 3),
            IsEnabled = isEnabled
        };
        button.Click += handler;
        return button;
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

    private void UpdatePanelVisibility()
    {
        if (_expandedPanel is not null)
        {
            _expandedPanel.IsVisible = !IsMinimized;
        }

        if (_minimizedPanel is not null)
        {
            _minimizedPanel.IsVisible = IsMinimized;
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
