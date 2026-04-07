using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace AppAutomation.Recorder.Avalonia.UI;

internal sealed partial class RecorderOverlay : UserControl
{
    private IAppAutomationRecorderSession? _session;
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
    private TextBlock? _shortcutText;
    private TextBlock? _validationBadgeText;
    private TextBlock? _minimizedStatusText;
    private Control? _expandedPanel;
    private Control? _minimizedPanel;

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
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.OverlayTheme == RecorderOverlayTheme.Dark)
        {
            Classes.Add("dark-theme");
        }

        if (_shortcutText is not null)
        {
            _shortcutText.IsVisible = options.Overlay.ShowShortcutLegend;
            _shortcutText.Text = RecorderHotkeyMap.Create(options.Hotkeys).BuildLegend();
        }

        if (_exportButton is not null)
        {
            _exportButton.IsVisible = options.Overlay.EnableExportButton;
        }

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
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
        _shortcutText = this.FindControl<TextBlock>("ShortcutText");
        _validationBadgeText = this.FindControl<TextBlock>("ValidationBadgeText");
        _minimizedStatusText = this.FindControl<TextBlock>("MinimizedStatusText");
        _expandedPanel = this.FindControl<Control>("ExpandedPanel");
        _minimizedPanel = this.FindControl<Control>("MinimizedPanel");

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
            _minimizedStatusText.Text = _session.LatestStatus;
        }

        if (_previewText is not null)
        {
            _previewText.Text = _session.LatestPreview;
        }

        UpdateValidationBadge(_session.LatestValidationStatus);
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
}
