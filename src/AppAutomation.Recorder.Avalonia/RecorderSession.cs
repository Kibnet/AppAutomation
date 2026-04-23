using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderSession : IAppAutomationRecorderSession, IAppAutomationRecorderSessionDetails, IRecorderScenarioPathDetails
{
    private static readonly TimeSpan RecentInputWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ObservationRefreshInterval = TimeSpan.FromMilliseconds(200);

    private readonly Window _window;
    private readonly ILogger _logger;
    private readonly RecorderStepFactory _stepFactory;
    private readonly RecorderSelectorResolver _selectorResolver;
    private readonly RecorderStepValidator _stepValidator;
    private readonly RecorderCommandRuntimeValidator _runtimeValidator;
    private readonly AuthoringCodeGenerator _codeGenerator;
    private readonly Func<IReadOnlyList<RecordedStep>, string?, CancellationToken, Task<RecorderSaveResult>> _saveOperation;
    private readonly List<RecordedStep> _steps = new();
    private readonly List<Action> _detachActions = new();
    private readonly Dictionary<Control, Action> _observedControlDetachers = new(ReferenceEqualityComparer.Instance);
    private readonly DispatcherTimer _textDebounceTimer;
    private readonly DispatcherTimer _sliderDebounceTimer;
    private readonly DispatcherTimer? _observationTimer;
    private readonly AppAutomationRecorderOptions _options;
    private readonly RecorderHotkeyMap _hotkeyMap;
    private readonly Func<Control?> _validationRootProvider;
    private readonly object _operationSync = new();

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
    private Task<RecorderSaveResult>? _activeOperationTask;
    private string _busyDescription = string.Empty;
    private readonly RecorderOutputDescription _defaultOutputDescription;
    private string? _lastScenarioFilePath;

    public RecorderSession(Window window, AppAutomationRecorderOptions options)
        : this(window, options, validationRootProvider: () => window.Content as Control, attachWindowHandlers: true)
    {
    }

    internal RecorderSession(
        Window window,
        AppAutomationRecorderOptions options,
        Func<Control?>? validationRootProvider,
        bool attachWindowHandlers,
        Func<IReadOnlyList<RecordedStep>, string?, CancellationToken, Task<RecorderSaveResult>>? saveOperation = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _validationRootProvider = validationRootProvider ?? (() => window.Content as Control);
        _logger = options.Logger ?? NullLogger.Instance;
        _hotkeyMap = RecorderHotkeyMap.Create(options.Hotkeys);
        _stepFactory = new RecorderStepFactory(options, _validationRootProvider);
        _selectorResolver = new RecorderSelectorResolver(options, _validationRootProvider);
        _stepValidator = new RecorderStepValidator();
        _runtimeValidator = new RecorderCommandRuntimeValidator(options);
        _codeGenerator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), _logger);
        _saveOperation = saveOperation ?? ((steps, outputDirectory, cancellationToken) =>
            _codeGenerator.SaveAsync(_window, _options, steps, outputDirectory, cancellationToken));
        _defaultOutputDescription = _codeGenerator.DescribeOutput(_window, _options, outputDirectoryOverride: null);

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
        if (attachWindowHandlers)
        {
            AttachHandlers();
            _observationTimer = new DispatcherTimer
            {
                Interval = ObservationRefreshInterval
            };
            _observationTimer.Tick += (_, _) => RefreshObservedControls();
            _observationTimer.Start();
            RefreshObservedControls();
        }
    }

    public event EventHandler? SessionChanged;

    internal event EventHandler? OverlayToggleRequested;

    internal event EventHandler? ExportRequested;

    public RecorderSessionState State => _state;

    public int StepCount => _steps.Count;

    public int PersistableStepCount => _steps.Count(static step => step.CanPersist && !step.IsIgnored);

    public string LatestPreview { get; private set; } = string.Empty;

    public string LatestStatus { get; private set; } = string.Empty;

    public RecorderValidationStatus LatestValidationStatus { get; private set; } = RecorderValidationStatus.Valid;

    public bool IsBusy => _activeOperationTask is not null;

    public string BusyDescription => _busyDescription;

    public string SessionSummary => BuildSessionSummary();

    public int WarningStepCount => _steps.Count(static step => !step.IsIgnored && step.ValidationStatus == RecorderValidationStatus.Warning);

    public int InvalidStepCount => _steps.Count(static step => !step.IsIgnored && (step.ValidationStatus == RecorderValidationStatus.Invalid || !step.CanPersist));

    public int IgnoredStepCount => _steps.Count(static step => step.IsIgnored);

    public IReadOnlyList<RecorderStepJournalEntry> StepJournal => _steps.Select(CreateJournalEntry).ToArray();

    public string CurrentScenarioFilePath => _lastScenarioFilePath ?? _defaultOutputDescription.ScenarioFilePathDisplay;

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
        var activeSteps = _steps.Where(static step => !step.IsIgnored).ToArray();
        return activeSteps.Length == 0
            ? string.Empty
            : _codeGenerator.GeneratePreview(activeSteps);
    }

    public Task<RecorderSaveResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        return RunManagedOperationAsync("Save", outputDirectory: null, cancellationToken);
    }

    public Task<RecorderSaveResult> SaveToDirectoryAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        return RunManagedOperationAsync("Export", outputDirectory, cancellationToken);
    }

    public void Dispose()
    {
        _observationTimer?.Stop();
        _textDebounceTimer.Stop();
        _sliderDebounceTimer.Stop();

        foreach (var detachAction in _observedControlDetachers.Values)
        {
            detachAction();
        }

        _observedControlDetachers.Clear();

        foreach (var detachAction in _detachActions)
        {
            detachAction();
        }

        _detachActions.Clear();
    }

    public void RemoveStep(Guid stepId)
    {
        var index = _steps.FindIndex(step => step.StepId == stepId);
        if (index < 0)
        {
            return;
        }

        _steps.RemoveAt(index);
        UpdateLatestPreviewFromSteps();
        SetStatus("Recorded step removed.", RecorderValidationStatus.Valid);
    }

    public void SetStepIgnored(Guid stepId, bool isIgnored)
    {
        var index = _steps.FindIndex(step => step.StepId == stepId);
        if (index < 0)
        {
            return;
        }

        var step = _steps[index];
        var updatedStep = step with
        {
            IsIgnored = isIgnored,
            ReviewState = ResolveReviewState(step with { IsIgnored = isIgnored }),
            FailureCode = ResolveFailureCode(step with { IsIgnored = isIgnored })
        };
        _steps[index] = updatedStep;
        UpdateLatestPreviewFromSteps();
        SetStatus(
            isIgnored ? "Recorded step ignored." : "Recorded step restored.",
            isIgnored ? RecorderValidationStatus.Warning : updatedStep.ValidationStatus);
    }

    public bool RetryStepValidation(Guid stepId)
    {
        var index = _steps.FindIndex(step => step.StepId == stepId);
        if (index < 0)
        {
            return false;
        }

        var revalidatedStep = RevalidateStep(_steps[index]);
        LogRecordedStepDiagnostics("RetryStepValidation", null, revalidatedStep);
        _steps[index] = revalidatedStep;
        LatestPreview = _codeGenerator.GeneratePreview(revalidatedStep);
        SetStatus(ResolveJournalStatusMessage(revalidatedStep), revalidatedStep.ValidationStatus);
        return true;
    }

    internal Task<RecorderSaveResult> ExportWithDirectoryPickerAsync(
        Func<CancellationToken, Task<string?>> selectOutputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectOutputDirectory);

        return RunManagedOperationAsync(
            "Export",
            async operationCancellationToken =>
            {
                var selectedOutputDirectory = await selectOutputDirectory(operationCancellationToken);
                if (string.IsNullOrWhiteSpace(selectedOutputDirectory))
                {
                    throw new OperationCanceledException(operationCancellationToken);
                }

                return await SaveCoreAsync(selectedOutputDirectory, operationCancellationToken);
            },
            cancellationToken);
    }

    internal void RefreshObservedControlsForTesting()
    {
        RefreshObservedControls();
    }

    internal void RegisterKeyboardInputForTesting(Control control)
    {
        RegisterKeyboardInput(control);
    }

    internal void RegisterPointerInputForTesting(Control? control)
    {
        RegisterPointerInput(control);
    }

    internal void RegisterPointerInputFromSourceForTesting(Control? source)
    {
        RegisterPointerInput(ResolveInteractionOwner(source));
    }

    internal void FlushPendingStateForTesting()
    {
        FlushPendingState();
    }

    internal void AddRecordedStepForTesting(RecordedStep step)
    {
        var updatedStep = step.StepId == Guid.Empty
            ? step with
            {
                StepId = Guid.NewGuid(),
                ReviewState = ResolveReviewState(step),
                FailureCode = ResolveFailureCode(step),
                LastValidationAt = DateTimeOffset.UtcNow
            }
            : step;
        _steps.Add(updatedStep);
        UpdateLatestPreviewFromSteps();
    }

    internal void CaptureButtonClickForTesting(Control? source)
    {
        var control = ResolveButtonActionOwner(source);
        FlushPendingTextIfSwitchingTo(control);
        FlushPendingSliderIfSwitchingTo(control);
        if (TryRecordGridAction(control))
        {
            return;
        }

        AddStep(_stepFactory.TryCreateButtonStep(control), control ?? source, "ButtonClick");
    }

    internal void CaptureComboBoxSelectionForTesting(ComboBox comboBox)
    {
        RecordComboBoxSelection(comboBox);
    }

    internal void CaptureListBoxSelectionForTesting(ListBox listBox)
    {
        RecordListBoxSelection(listBox);
    }

    internal void CaptureGridActionForTesting(Control? source)
    {
        TryRecordGridAction(source);
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
            _lastScenarioFilePath = result.ScenarioFilePath;
            LatestPreview = result.SkippedStepCount > 0
                ? $"Saved: {Path.GetFileName(result.ScenarioFilePath)} ({result.PersistedStepCount} persisted, {result.SkippedStepCount} skipped)"
                : $"Saved: {Path.GetFileName(result.ScenarioFilePath)}";
            NotifySessionChanged();
        }
    }

    private void AttachHandlers()
    {
        _window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        _window.AddHandler(Button.ClickEvent, OnButtonClick, RoutingStrategies.Bubble);
        _window.PropertyChanged += OnWindowPropertyChanged;

        _detachActions.Add(() => _window.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed));
        _detachActions.Add(() => _window.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved));
        _detachActions.Add(() => _window.RemoveHandler(InputElement.TextInputEvent, OnTextInput));
        _detachActions.Add(() => _window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown));
        _detachActions.Add(() => _window.RemoveHandler(Button.ClickEvent, OnButtonClick));
        _detachActions.Add(() => _window.PropertyChanged -= OnWindowPropertyChanged);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (string.Equals(e.Property.Name, "Content", StringComparison.Ordinal))
        {
            RefreshObservedControls();
        }
    }

    private void RefreshObservedControls()
    {
        var currentControls = CollectObservableControls();

        if (_pendingTextBox is not null && !currentControls.Contains(_pendingTextBox))
        {
            FlushPendingText();
        }

        if (_pendingSlider is not null && !currentControls.Contains(_pendingSlider))
        {
            FlushPendingSlider();
        }

        foreach (var observedControl in _observedControlDetachers.Keys.ToArray())
        {
            if (currentControls.Contains(observedControl))
            {
                continue;
            }

            _observedControlDetachers[observedControl]();
            _observedControlDetachers.Remove(observedControl);
        }

        foreach (var control in currentControls)
        {
            if (_observedControlDetachers.ContainsKey(control))
            {
                continue;
            }

            _observedControlDetachers[control] = AttachObservedControl(control);
        }
    }

    private HashSet<Control> CollectObservableControls()
    {
        var controls = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var root = _validationRootProvider();
        if (root is null)
        {
            return controls;
        }

        foreach (var control in root.GetVisualDescendants().OfType<Control>().Prepend(root))
        {
            if (IsObservableControl(control))
            {
                controls.Add(control);
            }
        }

        return controls;
    }

    private Action AttachObservedControl(Control control)
    {
        switch (control)
        {
            case TextBox textBox:
                textBox.PropertyChanged += OnTextBoxPropertyChanged;
                textBox.LostFocus += OnTextBoxLostFocus;
                return () =>
                {
                    textBox.PropertyChanged -= OnTextBoxPropertyChanged;
                    textBox.LostFocus -= OnTextBoxLostFocus;
                };
            case ComboBox comboBox:
                comboBox.SelectionChanged += OnComboBoxSelectionChanged;
                return () => comboBox.SelectionChanged -= OnComboBoxSelectionChanged;
            case ListBox listBox:
                listBox.SelectionChanged += OnListBoxSelectionChanged;
                return () => listBox.SelectionChanged -= OnListBoxSelectionChanged;
            case TabControl tabControl:
                tabControl.SelectionChanged += OnTabControlSelectionChanged;
                return () => tabControl.SelectionChanged -= OnTabControlSelectionChanged;
            case TreeView treeView:
                treeView.SelectionChanged += OnTreeViewSelectionChanged;
                return () => treeView.SelectionChanged -= OnTreeViewSelectionChanged;
            case Slider slider:
                slider.PropertyChanged += OnSliderPropertyChanged;
                return () => slider.PropertyChanged -= OnSliderPropertyChanged;
            case DatePicker datePicker:
                datePicker.PropertyChanged += OnDatePickerPropertyChanged;
                return () => datePicker.PropertyChanged -= OnDatePickerPropertyChanged;
            case Calendar calendar:
                calendar.PropertyChanged += OnCalendarPropertyChanged;
                return () => calendar.PropertyChanged -= OnCalendarPropertyChanged;
            default:
                return static () => { };
        }
    }

    private static bool IsObservableControl(Control control)
    {
        return control is TextBox
            or ComboBox
            or ListBox
            or TabControl
            or TreeView
            or Slider
            or DatePicker
            or Calendar;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording)
        {
            return;
        }

        var control = ResolveInteractionOwner(e.Source as Control);
        FlushPendingTextIfSwitchingTo(control);
        FlushPendingSliderIfSwitchingTo(control);
        RegisterPointerInput(control);

        if (FindAncestorOrSelf<Button>(e.Source as Control) is null)
        {
            TryRecordGridAction(e.Source as Control ?? control);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        _lastHoveredControl = ResolveInteractionOwner(e.Source as Control);
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
        RestartTextDebounce();
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
            RegisterKeyboardInput(ResolveInteractionOwner(focused) ?? focused);
            if (focused is TextBox && e.Key is Key.Enter or Key.Tab)
            {
                FlushPendingText();
            }
            else if (e.Key is Key.Enter)
            {
                TryRecordGridAction(focused);
            }
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

        var eventSource = e.Source as Control;
        var control = ResolveButtonActionOwner(eventSource);
        FlushPendingTextIfSwitchingTo(control);
        FlushPendingSliderIfSwitchingTo(control);
        if (TryRecordGridAction(control))
        {
            return;
        }

        AddStep(_stepFactory.TryCreateButtonStep(control), control ?? eventSource, "ButtonClick");
    }

    private void OnComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            RecordComboBoxSelection(comboBox);
        }
    }

    private void RecordComboBoxSelection(ComboBox comboBox)
    {
        if (_state != RecorderSessionState.Recording || !WasRecentlyTriggeredByUser(comboBox))
        {
            return;
        }

        if (TryRecordSearchPickerSelection(comboBox))
        {
            return;
        }

        FlushPendingTextIfSwitchingTo(comboBox);
        FlushPendingSliderIfSwitchingTo(comboBox);
        AddStep(_stepFactory.TryCreateComboBoxStep(comboBox), comboBox, "ComboBoxSelection");
    }

    private bool TryRecordSearchPickerSelection(ComboBox comboBox)
    {
        if (_pendingTextBox is null)
        {
            return false;
        }

        var result = _stepFactory.TryCreateSearchPickerStep(_pendingTextBox, comboBox);
        if (!result.Success)
        {
            return false;
        }

        _textDebounceTimer.Stop();
        _pendingTextBox = null;
        FlushPendingSliderIfSwitchingTo(comboBox);
        AddStep(result, comboBox, "SearchPickerSelection");
        return true;
    }

    private void OnListBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            RecordListBoxSelection(listBox);
        }
    }

    private void RecordListBoxSelection(ListBox listBox)
    {
        if (_state != RecorderSessionState.Recording || !WasRecentlyTriggeredByUser(listBox))
        {
            return;
        }

        FlushPendingTextIfSwitchingTo(listBox);
        FlushPendingSliderIfSwitchingTo(listBox);
        AddStep(_stepFactory.TryCreateListBoxStep(listBox), listBox, "ListBoxSelection");
    }

    private void OnTabControlSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || sender is not TabControl tabControl || !WasRecentlyTriggeredByUser(tabControl))
        {
            return;
        }

        FlushPendingTextIfSwitchingTo(tabControl);
        FlushPendingSliderIfSwitchingTo(tabControl);
        AddStep(_stepFactory.TryCreateTabSelectionStep(tabControl), tabControl, "TabSelection");
    }

    private void OnTreeViewSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || sender is not TreeView treeView || !WasRecentlyTriggeredByUser(treeView))
        {
            return;
        }

        FlushPendingTextIfSwitchingTo(treeView);
        FlushPendingSliderIfSwitchingTo(treeView);
        AddStep(_stepFactory.TryCreateTreeSelectionStep(treeView), treeView, "TreeSelection");
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

        FlushPendingTextIfSwitchingTo(slider);
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
            FlushPendingTextIfSwitchingTo(datePicker);
            FlushPendingSliderIfSwitchingTo(datePicker);
            AddStep(_stepFactory.TryCreateDatePickerStep(datePicker), datePicker, "DatePickerSelection");
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
            FlushPendingTextIfSwitchingTo(calendar);
            FlushPendingSliderIfSwitchingTo(calendar);
            AddStep(_stepFactory.TryCreateCalendarStep(calendar), calendar, "CalendarSelection");
        }
    }

    private void OnTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || sender is not TextBox textBox)
        {
            return;
        }

        if (!string.Equals(e.Property.Name, nameof(TextBox.Text), StringComparison.Ordinal))
        {
            return;
        }

        if (!ShouldTrackTextChange(textBox))
        {
            return;
        }

        _pendingTextBox = textBox;
        RestartTextDebounce();
    }

    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && ReferenceEquals(textBox, _pendingTextBox))
        {
            FlushPendingText();
        }
    }

    private bool ShouldTrackTextChange(TextBox textBox)
    {
        if (WasRecentlyTriggeredByUser(textBox))
        {
            return true;
        }

        var focused = TopLevel.GetTopLevel(textBox)?.FocusManager?.GetFocusedElement() as Control;
        if (focused is null || !AreRelated(textBox, focused))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        return now - _recentKeyboardAt <= RecentInputWindow
            || now - _recentPointerAt <= RecentInputWindow;
    }

    private void RestartTextDebounce()
    {
        _textDebounceTimer.Stop();
        _textDebounceTimer.Start();
    }

    private void CaptureAssertion(RecorderAssertionMode mode)
    {
        var control = _lastHoveredControl ?? TopLevel.GetTopLevel(_window)?.FocusManager?.GetFocusedElement() as Control;
        FlushPendingTextIfSwitchingTo(control);
        FlushPendingSliderIfSwitchingTo(control);
        AddStep(_stepFactory.TryCreateAssertionStep(control, mode), control, $"Assertion:{mode}");
    }

    private void AddStep(StepCreationResult result, Control? source = null, string captureAction = "Unknown")
    {
        if (!result.Success || result.Step is null)
        {
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                LogCaptureFailure(captureAction, source, result.Message);
                SetStatus(result.Message, RecorderValidationStatus.Invalid);
            }

            return;
        }

        var recordedStep = RevalidateStep(result.Step);
        LogRecordedStepDiagnostics(captureAction, source, recordedStep);
        var preview = _codeGenerator.GeneratePreview(recordedStep);
        if (!recordedStep.CanPersist && !_options.Validation.CaptureInvalidSteps)
        {
            LatestPreview = preview;
            SetStatus(
                string.IsNullOrWhiteSpace(recordedStep.ValidationMessage)
                    ? "Invalid recorder step was skipped."
                    : recordedStep.ValidationMessage,
                RecorderValidationStatus.Invalid);
            return;
        }

        var fingerprint = CreateFingerprint(recordedStep);
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(fingerprint, _lastFingerprint, StringComparison.Ordinal)
            && now - _lastRecordedAt < TimeSpan.FromMilliseconds(250))
        {
            return;
        }

        _steps.Add(recordedStep);
        _lastFingerprint = fingerprint;
        _lastRecordedAt = now;
        LatestPreview = preview;
        SetStatus(ResolveStepStatusMessage(recordedStep, result.Message), recordedStep.ValidationStatus);
    }

    private bool TryRecordGridAction(Control? source)
    {
        var result = _stepFactory.TryCreateGridActionStep(source);
        if (result.Success)
        {
            AddStep(result, source, "GridAction");
            return true;
        }

        if (string.Equals(result.Message, RecorderStepFactory.NoGridActionHintMessage, StringComparison.Ordinal))
        {
            return false;
        }

        AddStep(result, source, "GridAction");
        return true;
    }

    private void LogCaptureFailure(string captureAction, Control? source, string message)
    {
        LogRecorderDiagnostic(
            RecorderDiagnosticsEventIds.CaptureFailed,
            captureAction,
            source,
            step: null,
            findings: Array.Empty<RecorderRuntimeValidationFinding>(),
            message);
    }

    private void LogRecordedStepDiagnostics(string captureAction, Control? source, RecordedStep step)
    {
        var runtimeFindings = step.RuntimeValidationFindings ?? Array.Empty<RecorderRuntimeValidationFinding>();
        var surfacedRuntimeFindings = runtimeFindings
            .Where(static finding => finding.ShouldSurface)
            .ToArray();
        if (surfacedRuntimeFindings.Length > 0)
        {
            LogRecorderDiagnostic(
                surfacedRuntimeFindings.Any(static finding => finding.BlocksTarget)
                    ? RecorderDiagnosticsEventIds.RuntimeValidationFailed
                    : RecorderDiagnosticsEventIds.RuntimeValidationWarning,
                captureAction,
                source,
                step,
                surfacedRuntimeFindings,
                step.ValidationMessage);
        }

        if (!step.CanPersist && !RuntimeFindingsBlockAllTargets(runtimeFindings))
        {
            LogRecorderDiagnostic(
                IsActionValidationFailure(step)
                    ? RecorderDiagnosticsEventIds.ActionValidationFailed
                    : RecorderDiagnosticsEventIds.SelectorValidationFailed,
                captureAction,
                source,
                step,
                runtimeFindings,
                step.ValidationMessage);
        }
    }

    private void LogRecorderDiagnostic(
        EventId eventId,
        string captureAction,
        Control? source,
        RecordedStep? step,
        IReadOnlyList<RecorderRuntimeValidationFinding> findings,
        string? message)
    {
        try
        {
            var diagnostic = RecorderCaptureDiagnostics.Build(
                _options.ScenarioName,
                _state,
                captureAction,
                source,
                step,
                findings,
                message);
            _logger.LogWarning(eventId, "{RecorderDiagnostic}", diagnostic);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                RecorderDiagnosticsEventIds.DiagnosticsSnapshotFailed,
                ex,
                "Failed to build recorder diagnostic for capture action '{CaptureAction}': {Message}",
                captureAction,
                ex.Message);
        }
    }

    private static bool RuntimeFindingsBlockAllTargets(IReadOnlyList<RecorderRuntimeValidationFinding> findings)
    {
        var targets = findings
            .Select(static finding => finding.Target)
            .Distinct()
            .ToArray();
        if (targets.Length == 0)
        {
            return false;
        }

        var blockedTargets = findings
            .Where(static finding => finding.BlocksTarget)
            .Select(static finding => finding.Target)
            .Distinct()
            .ToHashSet();
        return blockedTargets.Count > 0 && targets.All(blockedTargets.Contains);
    }

    private static bool IsActionValidationFailure(RecordedStep step)
    {
        return step.ValidationMessage?.Contains("not compatible", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string CreateFingerprint(RecordedStep step)
    {
        return string.Join(
            "|",
            step.ActionKind,
            step.Control.LocatorKind,
            step.Control.LocatorValue,
            step.StringValue ?? string.Empty,
            step.ItemValue ?? string.Empty,
            step.BoolValue?.ToString() ?? string.Empty,
            step.DoubleValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            step.DateValue?.ToString("O") ?? string.Empty,
            step.RowIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            step.ColumnIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            step.CanPersist);
    }

    private static string ResolveStepStatusMessage(RecordedStep step, string? fallbackMessage)
    {
        if (!string.IsNullOrWhiteSpace(step.ValidationMessage))
        {
            return step.ValidationMessage!;
        }

        if (!string.IsNullOrWhiteSpace(fallbackMessage))
        {
            return fallbackMessage;
        }

        return step.ValidationStatus switch
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
        AddStep(_stepFactory.TryCreateTextEntryStep(textBox), textBox, "TextEntry");
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
        AddStep(_stepFactory.TryCreateSliderStep(slider), slider, "SliderValue");
    }

    private void FlushPendingTextIfSwitchingTo(Control? control)
    {
        if (_pendingTextBox is null)
        {
            return;
        }

        if (control is not null && AreRelated(_pendingTextBox, control))
        {
            return;
        }

        FlushPendingText();
    }

    private void FlushPendingSliderIfSwitchingTo(Control? control)
    {
        if (_pendingSlider is null)
        {
            return;
        }

        if (control is not null && AreRelated(_pendingSlider, control))
        {
            return;
        }

        FlushPendingSlider();
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
        if ((now - _recentPointerAt <= RecentInputWindow && AreRelated(control, _recentPointerControl))
            || (now - _recentKeyboardAt <= RecentInputWindow && AreRelated(control, _recentKeyboardControl)))
        {
            return true;
        }

        var focused = TopLevel.GetTopLevel(control)?.FocusManager?.GetFocusedElement() as Control;
        return focused is not null && AreRelated(control, ResolveInteractionOwner(focused) ?? focused);
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
        foreach (var candidate in EnumerateRelatedControls(descendant))
        {
            if (ReferenceEquals(candidate, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static Control? ResolveInteractionOwner(Control? control)
    {
        foreach (var candidate in EnumerateRelatedControls(control))
        {
            switch (candidate)
            {
                case TextBox:
                case ComboBox:
                case ListBox:
                case TabControl:
                case TreeView:
                case Slider:
                case DatePicker:
                case Calendar:
                case CheckBox:
                case RadioButton:
                case ToggleButton:
                case Button:
                case TabItem:
                case TreeViewItem:
                    return candidate;
            }
        }

        return control;
    }

    private static Control? ResolveButtonActionOwner(Control? control)
    {
        foreach (var candidate in EnumerateRelatedControls(control))
        {
            if (candidate is CheckBox or RadioButton or ToggleButton or Button)
            {
                return candidate;
            }
        }

        return null;
    }

    private static TControl? FindAncestorOrSelf<TControl>(Control? control)
        where TControl : Control
    {
        foreach (var candidate in EnumerateRelatedControls(control))
        {
            if (candidate is TControl typed)
            {
                return typed;
            }
        }

        return null;
    }

    private static IEnumerable<Control> EnumerateRelatedControls(Control? control)
    {
        if (control is null)
        {
            yield break;
        }

        var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<Control>();
        queue.Enqueue(control);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;

            if (current.GetVisualParent() is Control visualParent)
            {
                queue.Enqueue(visualParent);
            }

            if (current is ILogical { LogicalParent: Control logicalParent })
            {
                queue.Enqueue(logicalParent);
            }

            if (current is StyledElement { TemplatedParent: Control templatedParent })
            {
                queue.Enqueue(templatedParent);
            }
        }
    }

    private Task<RecorderSaveResult> RunManagedOperationAsync(
        string operationName,
        string? outputDirectory,
        CancellationToken cancellationToken)
    {
        return RunManagedOperationAsync(
            operationName,
            operationCancellationToken => SaveCoreAsync(outputDirectory, operationCancellationToken),
            cancellationToken);
    }

    private Task<RecorderSaveResult> RunManagedOperationAsync(
        string operationName,
        Func<CancellationToken, Task<RecorderSaveResult>> operation,
        CancellationToken cancellationToken)
    {
        lock (_operationSync)
        {
            if (_activeOperationTask is not null)
            {
                SetStatus(
                    $"{operationName} ignored while '{_busyDescription}' is in progress.",
                    RecorderValidationStatus.Warning);
                return Task.FromResult(RecorderSaveResult.Failed($"{_busyDescription} is already in progress."));
            }

            _busyDescription = $"{operationName}...";
            SetStatus($"{operationName} in progress...", LatestValidationStatus);
            _activeOperationTask = ExecuteManagedOperationAsync(operationName, operation, cancellationToken);
            return _activeOperationTask;
        }
    }

    private async Task<RecorderSaveResult> ExecuteManagedOperationAsync(
        string operationName,
        Func<CancellationToken, Task<RecorderSaveResult>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            SetStatus($"{operationName} cancelled.", LatestValidationStatus);
            return RecorderSaveResult.Failed($"{operationName} cancelled.");
        }
        catch (Exception ex)
        {
            var message = $"{operationName} failed: {ex.Message}";
            SetStatus(message, RecorderValidationStatus.Invalid);
            return RecorderSaveResult.Failed(message, ex.ToString());
        }
        finally
        {
            lock (_operationSync)
            {
                _activeOperationTask = null;
                _busyDescription = string.Empty;
            }

            NotifySessionChanged();
        }
    }

    private async Task<RecorderSaveResult> SaveCoreAsync(string? outputDirectory, CancellationToken cancellationToken)
    {
        FlushPendingState();
        var stepsToPersist = _steps.Where(static step => !step.IsIgnored).ToArray();
        var result = await _saveOperation(stepsToPersist, outputDirectory, cancellationToken);
        ApplySaveResult(result);
        return result;
    }

    private RecordedStep RevalidateStep(RecordedStep step)
    {
        if (!_options.Validation.ValidateSelectors)
        {
            var selectorValidationDisabledStep = _runtimeValidator.Validate(step with
            {
                LastValidationAt = DateTimeOffset.UtcNow,
                ReviewState = ResolveReviewState(step),
                FailureCode = ResolveFailureCode(step)
            });

            return selectorValidationDisabledStep with
            {
                ReviewState = ResolveReviewState(selectorValidationDisabledStep),
                FailureCode = ResolveFailureCode(selectorValidationDisabledStep)
            };
        }

        var validation = _selectorResolver.ResolveExisting(step.Control);
        var revalidated = step with
        {
            ValidationStatus = validation.ValidationStatus,
            ValidationMessage = validation.ValidationMessage,
            CanPersist = validation.CanPersist,
            LastValidationAt = DateTimeOffset.UtcNow
        };

        if (validation.MatchedControl is not null)
        {
            revalidated = _stepValidator.Validate(revalidated, validation.MatchedControl);
        }

        revalidated = _runtimeValidator.Validate(revalidated);

        return revalidated with
        {
            ReviewState = ResolveReviewState(revalidated),
            FailureCode = ResolveFailureCode(revalidated)
        };
    }

    private RecorderStepJournalEntry CreateJournalEntry(RecordedStep step)
    {
        return new RecorderStepJournalEntry(
            step.StepId,
            _codeGenerator.GeneratePreview(step),
            ResolveJournalStatusMessage(step),
            step.ValidationStatus,
            step.CanPersist,
            step.IsIgnored,
            step.ReviewState,
            step.FailureCode,
            step.LastValidationAt);
    }

    private static RecorderStepReviewState ResolveReviewState(RecordedStep step)
    {
        if (step.IsIgnored)
        {
            return RecorderStepReviewState.Ignored;
        }

        return step.ValidationStatus == RecorderValidationStatus.Valid && step.CanPersist
            ? RecorderStepReviewState.Active
            : RecorderStepReviewState.NeedsReview;
    }

    private static string? ResolveFailureCode(RecordedStep step)
    {
        if (step.IsIgnored)
        {
            return "ignored";
        }

        return step.ValidationStatus switch
        {
            RecorderValidationStatus.Invalid when !step.CanPersist => "validation-invalid",
            RecorderValidationStatus.Warning => "validation-warning",
            _ => null
        };
    }

    private static string ResolveJournalStatusMessage(RecordedStep step)
    {
        if (step.IsIgnored)
        {
            return "Ignored for save/export.";
        }

        if (!string.IsNullOrWhiteSpace(step.ValidationMessage))
        {
            return step.ValidationMessage!;
        }

        return step.ValidationStatus switch
        {
            RecorderValidationStatus.Warning => "Recorded with warning.",
            RecorderValidationStatus.Invalid => "Recorded for review only.",
            _ => "Ready to persist."
        };
    }

    private string BuildSessionSummary()
    {
        var parts = new List<string>
        {
            PersistableStepCount == StepCount
                ? $"{StepCount} steps"
                : $"{PersistableStepCount}/{StepCount} steps"
        };

        if (WarningStepCount > 0)
        {
            parts.Add($"{WarningStepCount} warnings");
        }

        if (InvalidStepCount > 0)
        {
            parts.Add($"{InvalidStepCount} invalid");
        }

        if (IgnoredStepCount > 0)
        {
            parts.Add($"{IgnoredStepCount} ignored");
        }

        if (IsBusy)
        {
            parts.Add(_busyDescription.ToLowerInvariant());
        }

        return string.Join(" | ", parts);
    }

    private void UpdateLatestPreviewFromSteps()
    {
        var latestStep = _steps.LastOrDefault(static step => !step.IsIgnored);
        LatestPreview = latestStep is null
            ? string.Empty
            : _codeGenerator.GeneratePreview(latestStep);
        NotifySessionChanged();
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

        NotifySessionChanged();
    }

    private void NotifySessionChanged()
    {
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}
