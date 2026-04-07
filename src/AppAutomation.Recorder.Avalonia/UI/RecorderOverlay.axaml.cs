using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AppAutomation.Recorder.Avalonia.UI;

internal sealed partial class RecorderOverlay : UserControl
{
    private IAppAutomationRecorderSession? _session;
    private DispatcherTimer? _timer;
    private Button? _recordButton;
    private Button? _clearButton;
    private Button? _saveButton;
    private Button? _minimizeButton;
    private TextBlock? _stepCounter;
    private TextBlock? _statusText;
    private TextBlock? _previewText;

    public RecorderOverlay()
    {
        AvaloniaXamlLoader.Load(this);
        InitializeControls();
    }

    public event EventHandler? MinimizeRequested;

    public void Attach(IAppAutomationRecorderSession session, RecorderOverlayTheme? theme)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (theme == RecorderOverlayTheme.Dark)
        {
            Classes.Add("dark-theme");
        }

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    private void InitializeControls()
    {
        _recordButton = this.FindControl<Button>("RecordButton");
        _clearButton = this.FindControl<Button>("ClearButton");
        _saveButton = this.FindControl<Button>("SaveButton");
        _minimizeButton = this.FindControl<Button>("MinimizeButton");
        _stepCounter = this.FindControl<TextBlock>("StepCounter");
        _statusText = this.FindControl<TextBlock>("StatusText");
        _previewText = this.FindControl<TextBlock>("PreviewText");

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

        if (_minimizeButton is not null)
        {
            _minimizeButton.Click += (_, _) => MinimizeRequested?.Invoke(this, EventArgs.Empty);
        }
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
            _stepCounter.Text = $"{_session.StepCount} steps";
        }

        if (_statusText is not null)
        {
            _statusText.Text = _session.LatestStatus;
        }

        if (_previewText is not null)
        {
            _previewText.Text = _session.LatestPreview;
        }
    }
}
