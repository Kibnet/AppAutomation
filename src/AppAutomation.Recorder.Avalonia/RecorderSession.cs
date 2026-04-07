using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderSession : IAppAutomationRecorderSession
{
    private static readonly TimeSpan RecentInputWindow = TimeSpan.FromSeconds(1);

    private readonly Window _window;
    private readonly ILogger _logger;
    private readonly RecorderStepFactory _stepFactory;
    private readonly AuthoringCodeGenerator _codeGenerator;
    private readonly List<RecordedStep> _steps = new();
    private readonly List<Action> _detachActions = new();
    private readonly DispatcherTimer _textDebounceTimer;
    private readonly DispatcherTimer _sliderDebounceTimer;
    private readonly AppAutomationRecorderOptions _options;
    private readonly RecorderHotkeyMap _hotkeyMap;

    private RecorderSessionState _state;
    private TextBox? _pendingTextBox;
    private Slider? _pendingSlider;
    private Control? _lastHoveredControl;
    private Control? _recentPointerControl;
    private DateTimeOffset _recentPointerAt;
    private Control? _recentKeyboardControl;
    private DateTimeOffset _recentKeyboardAt;
    private string _lastFingerprint = string.Empty;
    private DateTimeOffset _lastRecordedAt;

    public RecorderSession(Window window, AppAutomationRecorderOptions options)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = options.Logger ?? NullLogger.Instance;
        _hotkeyMap = RecorderHotkeyMap.Create(options.Hotkeys);
        _stepFactory = new RecorderStepFactory(options, window);
        _codeGenerator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), _logger);

        _textDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _textDebounceTimer.Tick += (_, _) => FlushPendingText();

        _sliderDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _sliderDebounceTimer.Tick += (_, _) => FlushPendingSlider();

        LatestStatus = "Recorder attached. Use configured hotkeys or overlay controls to start.";
        AttachHandlers();
    }

    internal event EventHandler? OverlayToggleRequested;

    internal event EventHandler? ExportRequested;

    public RecorderSessionState State => _state;

    public int StepCount => _steps.Count;

    public int PersistableStepCount => _steps.Count(static step => step.CanPersist);

    public string LatestPreview { get; private set; } = string.Empty;

    public string LatestStatus { get; private set; } = string.Empty;

    public RecorderValidationStatus LatestValidationStatus { get; private set; } = RecorderValidationStatus.Valid;

    public void Start()
    {
        _state = RecorderSessionState.Recording;
        SetStatus("Recording.", RecorderValidationStatus.Valid);
    }

    public void Stop()
    {
        FlushPendingState();
        _state = RecorderSessionState.Off;
        SetStatus("Recording stopped.", RecorderValidationStatus.Valid);
    }

    public void Clear()
    {
        FlushPendingState();
        _steps.Clear();
        LatestPreview = string.Empty;
        SetStatus("Recorded steps cleared.", RecorderValidationStatus.Valid);
    }

    public string ExportPreview()
    {
        FlushPendingState();
        return _codeGenerator.GeneratePreview(_steps);
    }

    public async Task<RecorderSaveResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        FlushPendingState();
        var result = await _codeGenerator.SaveAsync(_window, _options, _steps, outputDirectoryOverride: null, cancellationToken);
        ApplySaveResult(result);
        return result;
    }

    public async Task<RecorderSaveResult> SaveToDirectoryAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        FlushPendingState();
        var result = await _codeGenerator.SaveAsync(_window, _options, _steps, outputDirectory, cancellationToken);
        ApplySaveResult(result);
        return result;
    }

    public void Dispose()
    {
        _textDebounceTimer.Stop();
        _sliderDebounceTimer.Stop();

        foreach (var detachAction in _detachActions)
        {
            detachAction();
        }

        _detachActions.Clear();
    }

    private void ApplySaveResult(RecorderSaveResult result)
    {
        var status = !result.Success
            ? RecorderValidationStatus.Invalid
            : result.SkippedStepCount > 0
                ? RecorderValidationStatus.Warning
                : RecorderValidationStatus.Valid;
        SetStatus(result.Message, status);
        if (result.Success && result.ScenarioFilePath is not null)
        {
            LatestPreview = result.SkippedStepCount > 0
                ? $"Saved: {Path.GetFileName(result.ScenarioFilePath)} ({result.PersistedStepCount} persisted, {result.SkippedStepCount} skipped)"
                : $"Saved: {Path.GetFileName(result.ScenarioFilePath)}";
        }
    }

    private void AttachHandlers()
    {
        _window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        _window.AddHandler(Button.ClickEvent, OnButtonClick, RoutingStrategies.Bubble);
        _window.Loaded += OnWindowLoaded;

        _detachActions.Add(() => _window.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed));
        _detachActions.Add(() => _window.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved));
        _detachActions.Add(() => _window.RemoveHandler(InputElement.TextInputEvent, OnTextInput));
        _detachActions.Add(() => _window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown));
        _detachActions.Add(() => _window.RemoveHandler(Button.ClickEvent, OnButtonClick));
        _detachActions.Add(() => _window.Loaded -= OnWindowLoaded);
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        if (_window.Content is not Control root)
        {
            return;
        }

        SubscribeControlHandlers(root);
    }

    private void SubscribeControlHandlers(Control root)
    {
        foreach (var control in root.GetVisualDescendants().OfType<Control>().Prepend(root))
        {
            switch (control)
            {
                case ComboBox comboBox:
                    comboBox.SelectionChanged += OnComboBoxSelectionChanged;
                    _detachActions.Add(() => comboBox.SelectionChanged -= OnComboBoxSelectionChanged);
                    break;
                case ListBox listBox:
                    listBox.SelectionChanged += OnListBoxSelectionChanged;
                    _detachActions.Add(() => listBox.SelectionChanged -= OnListBoxSelectionChanged);
                    break;
                case TabControl tabControl:
                    tabControl.SelectionChanged += OnTabControlSelectionChanged;
                    _detachActions.Add(() => tabControl.SelectionChanged -= OnTabControlSelectionChanged);
                    break;
                case TreeView treeView:
                    treeView.SelectionChanged += OnTreeViewSelectionChanged;
                    _detachActions.Add(() => treeView.SelectionChanged -= OnTreeViewSelectionChanged);
                    break;
                case Slider slider:
                    slider.PropertyChanged += OnSliderPropertyChanged;
                    _detachActions.Add(() => slider.PropertyChanged -= OnSliderPropertyChanged);
                    break;
                case DatePicker datePicker:
                    datePicker.PropertyChanged += OnDatePickerPropertyChanged;
                    _detachActions.Add(() => datePicker.PropertyChanged -= OnDatePickerPropertyChanged);
                    break;
                case Calendar calendar:
                    calendar.PropertyChanged += OnCalendarPropertyChanged;
                    _detachActions.Add(() => calendar.PropertyChanged -= OnCalendarPropertyChanged);
                    break;
            }
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording)
        {
            return;
        }

        FlushPendingText();
        RegisterPointerInput(FindOwningControl(e.Source as Control));
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        _lastHoveredControl = FindOwningControl(e.Source as Control);
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var textBox = FindAncestorOrSelf<TextBox>(e.Source as Control);
        if (textBox is null)
        {
            return;
        }

        _pendingTextBox = textBox;
        RegisterKeyboardInput(textBox);
        _textDebounceTimer.Stop();
        _textDebounceTimer.Start();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_hotkeyMap.TryGetCommand(e.Key, e.KeyModifiers, out var command))
        {
            HandleRecorderCommand(command);
            e.Handled = true;
            return;
        }

        if (_state != RecorderSessionState.Recording)
        {
            return;
        }

        var focused = TopLevel.GetTopLevel(_window)?.FocusManager?.GetFocusedElement() as Control;
        if (focused is not null)
        {
            RegisterKeyboardInput(focused);
        }
    }

    private void HandleRecorderCommand(RecorderCommandKind command)
    {
        switch (command)
        {
            case RecorderCommandKind.StartStop:
                if (_state == RecorderSessionState.Recording)
                {
                    Stop();
                }
                else
                {
                    Start();
                }
                break;
            case RecorderCommandKind.Save:
                _ = SaveAsync();
                break;
            case RecorderCommandKind.Export:
                ExportRequested?.Invoke(this, EventArgs.Empty);
                break;
            case RecorderCommandKind.Clear:
                Clear();
                break;
            case RecorderCommandKind.CaptureAssertAuto:
                CaptureAssertion(RecorderAssertionMode.Auto);
                break;
            case RecorderCommandKind.CaptureAssertText:
                CaptureAssertion(RecorderAssertionMode.Text);
                break;
            case RecorderCommandKind.CaptureAssertEnabled:
                CaptureAssertion(RecorderAssertionMode.Enabled);
                break;
            case RecorderCommandKind.CaptureAssertChecked:
                CaptureAssertion(RecorderAssertionMode.Checked);
                break;
            case RecorderCommandKind.ToggleOverlayMinimize:
                OverlayToggleRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording)
        {
            return;
        }

        AddStep(_stepFactory.TryCreateButtonStep(FindOwningControl(e.Source as Control)));
    }

    private void OnComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || sender is not ComboBox comboBox || !WasRecentlyTriggeredByUser(comboBox))
        {
            return;
        }

        AddStep(_stepFactory.TryCreateComboBoxStep(comboBox));
    }

    private void OnListBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || sender is not ListBox listBox || !WasRecentlyTriggeredByUser(listBox))
        {
            return;
        }

        AddStep(_stepFactory.TryCreateListBoxStep(listBox));
    }

    private void OnTabControlSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || sender is not TabControl tabControl || !WasRecentlyTriggeredByUser(tabControl))
        {
            return;
        }

        AddStep(_stepFactory.TryCreateTabSelectionStep(tabControl));
    }

    private void OnTreeViewSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || sender is not TreeView treeView || !WasRecentlyTriggeredByUser(treeView))
        {
            return;
        }

        AddStep(_stepFactory.TryCreateTreeSelectionStep(treeView));
    }

    private void OnSliderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || sender is not Slider slider || !WasRecentlyTriggeredByUser(slider))
        {
            return;
        }

        if (!string.Equals(e.Property.Name, nameof(Slider.Value), StringComparison.Ordinal))
        {
            return;
        }

        _pendingSlider = slider;
        _sliderDebounceTimer.Stop();
        _sliderDebounceTimer.Start();
    }

    private void OnDatePickerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || sender is not DatePicker datePicker || !WasRecentlyTriggeredByUser(datePicker))
        {
            return;
        }

        if (string.Equals(e.Property.Name, nameof(DatePicker.SelectedDate), StringComparison.Ordinal))
        {
            AddStep(_stepFactory.TryCreateDatePickerStep(datePicker));
        }
    }

    private void OnCalendarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || sender is not Calendar calendar || !WasRecentlyTriggeredByUser(calendar))
        {
            return;
        }

        if (string.Equals(e.Property.Name, nameof(Calendar.SelectedDate), StringComparison.Ordinal))
        {
            AddStep(_stepFactory.TryCreateCalendarStep(calendar));
        }
    }

    private void CaptureAssertion(RecorderAssertionMode mode)
    {
        var control = _lastHoveredControl ?? TopLevel.GetTopLevel(_window)?.FocusManager?.GetFocusedElement() as Control;
        AddStep(_stepFactory.TryCreateAssertionStep(control, mode));
    }

    private void AddStep(StepCreationResult result)
    {
        if (!result.Success || result.Step is null)
        {
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                SetStatus(result.Message, RecorderValidationStatus.Invalid);
            }

            return;
        }

        var preview = _codeGenerator.GeneratePreview(result.Step);
        if (!result.Step.CanPersist && !_options.Validation.CaptureInvalidSteps)
        {
            LatestPreview = preview;
            SetStatus(
                string.IsNullOrWhiteSpace(result.Step.ValidationMessage)
                    ? "Invalid recorder step was skipped."
                    : result.Step.ValidationMessage,
                RecorderValidationStatus.Invalid);
            return;
        }

        var fingerprint = CreateFingerprint(result.Step);
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(fingerprint, _lastFingerprint, StringComparison.Ordinal)
            && now - _lastRecordedAt < TimeSpan.FromMilliseconds(250))
        {
            return;
        }

        _steps.Add(result.Step);
        _lastFingerprint = fingerprint;
        _lastRecordedAt = now;
        LatestPreview = preview;
        SetStatus(ResolveStepStatusMessage(result), result.Step.ValidationStatus);
    }

    private static string CreateFingerprint(RecordedStep step)
    {
        return string.Join(
            "|",
            step.ActionKind,
            step.Control.LocatorKind,
            step.Control.LocatorValue,
            step.StringValue ?? string.Empty,
            step.BoolValue?.ToString() ?? string.Empty,
            step.DoubleValue?.ToString() ?? string.Empty,
            step.DateValue?.ToString("O") ?? string.Empty,
            step.CanPersist);
    }

    private static string ResolveStepStatusMessage(StepCreationResult result)
    {
        if (result.Step is null)
        {
            return result.Message;
        }

        if (!string.IsNullOrWhiteSpace(result.Step.ValidationMessage))
        {
            return result.Step.ValidationMessage!;
        }

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            return result.Message;
        }

        return result.Step.ValidationStatus switch
        {
            RecorderValidationStatus.Warning => "Step recorded with warning.",
            RecorderValidationStatus.Invalid => "Invalid step recorded for review only.",
            _ => "Step recorded."
        };
    }

    private void FlushPendingState()
    {
        FlushPendingText();
        FlushPendingSlider();
    }

    private void FlushPendingText()
    {
        _textDebounceTimer.Stop();
        if (_pendingTextBox is null)
        {
            return;
        }

        var textBox = _pendingTextBox;
        _pendingTextBox = null;
        AddStep(_stepFactory.TryCreateTextEntryStep(textBox));
    }

    private void FlushPendingSlider()
    {
        _sliderDebounceTimer.Stop();
        if (_pendingSlider is null)
        {
            return;
        }

        var slider = _pendingSlider;
        _pendingSlider = null;
        AddStep(_stepFactory.TryCreateSliderStep(slider));
    }

    private void RegisterPointerInput(Control? control)
    {
        _recentPointerControl = control;
        _recentPointerAt = DateTimeOffset.UtcNow;
    }

    private void RegisterKeyboardInput(Control control)
    {
        _recentKeyboardControl = control;
        _recentKeyboardAt = DateTimeOffset.UtcNow;
    }

    private bool WasRecentlyTriggeredByUser(Control control)
    {
        var now = DateTimeOffset.UtcNow;
        return (now - _recentPointerAt <= RecentInputWindow && AreRelated(control, _recentPointerControl))
            || (now - _recentKeyboardAt <= RecentInputWindow && AreRelated(control, _recentKeyboardControl));
    }

    private static bool AreRelated(Control control, Control? recentControl)
    {
        if (recentControl is null)
        {
            return false;
        }

        return IsAncestorOrSelf(control, recentControl) || IsAncestorOrSelf(recentControl, control);
    }

    private static bool IsAncestorOrSelf(Control ancestor, Control descendant)
    {
        if (ReferenceEquals(ancestor, descendant))
        {
            return true;
        }

        for (Visual? current = descendant; current is not null; current = current.GetVisualParent())
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            if (current is Window)
            {
                break;
            }
        }

        return false;
    }

    private static Control? FindOwningControl(Control? control)
    {
        return FindAncestorOrSelf<Control>(control);
    }

    private static TControl? FindAncestorOrSelf<TControl>(Control? control)
        where TControl : Control
    {
        for (Visual? current = control; current is not null; current = current.GetVisualParent())
        {
            if (current is TControl typed)
            {
                return typed;
            }

            if (current is Window)
            {
                break;
            }
        }

        return null;
    }

    private void SetStatus(string message, RecorderValidationStatus validationStatus)
    {
        LatestStatus = message;
        LatestValidationStatus = validationStatus;

        switch (validationStatus)
        {
            case RecorderValidationStatus.Invalid:
                _logger.LogWarning("{Message}", message);
                break;
            case RecorderValidationStatus.Warning:
                _logger.LogWarning("{Message}", message);
                break;
            default:
                _logger.LogInformation("{Message}", message);
                break;
        }
    }
}
