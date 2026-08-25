using System.Reflection;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using Avalonia;
using Avalonia.Automation;
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

internal sealed class RecorderSession :
    IAppAutomationRecorderSession,
    IAppAutomationRecorderSessionDetails,
    IRecorderStepReorderSessionDetails,
    IRecorderCheckpointSessionDetails,
    IRecorderScenarioPathDetails,
    IRecorderScenarioSelectionDetails
{
    private static readonly TimeSpan RecentInputWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ObservationRefreshInterval = TimeSpan.FromMilliseconds(200);
    private static readonly string[] DetachedContentPropertyNames = ["PopupContent", "Child", "Content"];

    private readonly Window _window;
    private readonly ILogger _logger;
    private readonly RecorderStepFactory _stepFactory;
    private readonly RecorderSelectorResolver _selectorResolver;
    private readonly RecorderStepValidator _stepValidator;
    private readonly RecorderCommandRuntimeValidator _runtimeValidator;
    private readonly AuthoringCodeGenerator _codeGenerator;
    private readonly AuthoringProjectScanner _authoringProjectScanner;
    private readonly Func<IReadOnlyList<RecordedStep>, string?, CancellationToken, Task<RecorderSaveResult>> _saveOperation;
    private readonly Func<IReadOnlyList<RecordedStep>, string?, CancellationToken, Task<RecorderSaveResult>> _autosaveOperation;
    private readonly List<RecordedStep> _steps = new();
    private readonly List<Action> _detachActions = new();
    private readonly Dictionary<Control, Action> _observedControlDetachers = new(ReferenceEqualityComparer.Instance);
    private readonly DispatcherTimer _textDebounceTimer;
    private readonly DispatcherTimer _sliderDebounceTimer;
    private readonly DispatcherTimer _spinnerDebounceTimer;
    private readonly DispatcherTimer? _observationTimer;
    private readonly AppAutomationRecorderOptions _options;
    private RecorderHotkeyMap _hotkeyMap;
    private RecorderHotkeySettings _hotkeySettings;
    private readonly Func<Control?> _validationRootProvider;
    private readonly object _operationSync = new();
    private Control? _inputRoot;

    private RecorderSessionState _state;
    private TextBox? _pendingTextBox;
    private string? _pendingTextValue;
    private Slider? _pendingSlider;
    private NumericUpDown? _pendingSpinner;
    private TimePicker? _pendingTimePicker;
    private RecorderTimePickerHint? _pendingTimePickerHint;
    private StepCreationResult? _pendingSingleSelectStep;
    private RecorderSingleSelectHint? _pendingSingleSelectHint;
    private Control? _pendingSingleSelectSource;
    private StepCreationResult? _pendingColorPickerStep;
    private RecorderColorPickerHint? _pendingColorPickerHint;
    private Control? _pendingColorPickerSource;
    private Control? _pendingContextMenuOwner;
    private Control? _lastHoveredControl;
    private Control? _recentPointerControl;
    private DateTimeOffset _recentPointerAt;
    private Control? _recentKeyboardControl;
    private DateTimeOffset _recentKeyboardAt;
    private RoutedEventArgs? _lastMenuItemClickEvent;
    private ComboBoxFilterClickSnapshot? _comboBoxFilterClickSnapshot;
    private string _lastFingerprint = string.Empty;
    private DateTimeOffset _lastRecordedAt;
    private Task<RecorderSaveResult>? _activeOperationTask;
    private QueuedManagedOperation? _queuedManagedOperation;
    private string _busyDescription = string.Empty;
    private bool _activeOperationIsAutosave;
    private readonly RecorderOutputDescription _defaultOutputDescription;
    private readonly bool _hasConfiguredLogger;
    private readonly string _diagnosticLogFilePath;
    private bool _isDiagnosticLogFileEnabled;
    private bool _pendingAutosave;
    private bool _isCapturingPersistenceSnapshot;
    private int _diagnosticLogEntryCount;
    private string? _lastScenarioFilePath;
    private IReadOnlyList<RecordedScenarioDestination> _scenarioDestinations = Array.Empty<RecordedScenarioDestination>();
    private RecordedScenarioDestination? _selectedScenarioDestination;
    private string _scenarioName;
    private string? _scenarioDiscoveryError;
    private bool _isScanning;
    private string _autosaveDraftIdentity = Guid.NewGuid().ToString("N");
    private Task _scenarioDiscoveryTask = Task.CompletedTask;

    public RecorderSession(Window window, AppAutomationRecorderOptions options)
        : this(window, options, validationRootProvider: () => window.Content as Control, attachWindowHandlers: true)
    {
    }

    internal RecorderSession(
        Window window,
        AppAutomationRecorderOptions options,
        Func<Control?>? validationRootProvider,
        bool attachWindowHandlers,
        Func<IReadOnlyList<RecordedStep>, string?, CancellationToken, Task<RecorderSaveResult>>? saveOperation = null,
        Func<IReadOnlyList<RecordedStep>, string?, CancellationToken, Task<RecorderSaveResult>>? autosaveOperation = null,
        RecorderHotkeySettings? initialHotkeySettings = null,
        RecorderHotkeySettingsStore? hotkeySettingsStore = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _validationRootProvider = validationRootProvider ?? (() => window.Content as Control);
        _logger = options.Logger ?? NullLogger.Instance;
        _hasConfiguredLogger = options.Logger is not null;
        string? hotkeySettingsLoadError = null;
        _hotkeySettings = initialHotkeySettings
            ?? LoadEffectiveHotkeySettings(options, hotkeySettingsStore ?? new RecorderHotkeySettingsStore(), out hotkeySettingsLoadError);
        _hotkeyMap = _hotkeySettings.ToMap();
        _stepFactory = new RecorderStepFactory(options, _validationRootProvider);
        _selectorResolver = new RecorderSelectorResolver(options, _validationRootProvider);
        _stepValidator = new RecorderStepValidator();
        _runtimeValidator = new RecorderCommandRuntimeValidator(options);
        _authoringProjectScanner = new AuthoringProjectScanner();
        _codeGenerator = new AuthoringCodeGenerator(_authoringProjectScanner, _logger);
        _scenarioName = options.ScenarioName?.Trim() ?? string.Empty;
        _saveOperation = saveOperation ?? ((steps, outputDirectory, cancellationToken) =>
        {
            var saveContext = CreateScenarioSaveContext();
            return IsScenarioSelectionEnabled && saveContext is null
                ? Task.FromResult(RecorderSaveResult.Failed(ScenarioSelectionError ?? "Scenario destination is not ready."))
                : saveContext is null
                    ? _codeGenerator.SaveAsync(_window, _options, steps, outputDirectory, cancellationToken)
                    : _codeGenerator.SaveAsync(_window, _options, steps, outputDirectory, saveContext, cancellationToken);
        });
        _autosaveOperation = autosaveOperation
            ?? saveOperation
            ?? ((steps, outputDirectory, cancellationToken) =>
            {
                var saveContext = CreateScenarioSaveContext();
                return IsScenarioSelectionEnabled && saveContext is null
                    ? Task.FromResult(RecorderSaveResult.Failed(ScenarioSelectionError ?? "Scenario destination is not ready."))
                    : saveContext is null
                        ? _codeGenerator.AutosaveAsync(_window, _options, steps, outputDirectory, cancellationToken)
                        : _codeGenerator.AutosaveAsync(_window, _options, steps, outputDirectory, saveContext, cancellationToken);
            });
        _defaultOutputDescription = _codeGenerator.DescribeOutput(_window, _options, outputDirectoryOverride: null);
        _diagnosticLogFilePath = ResolveDiagnosticLogFilePath(options, _defaultOutputDescription);
        _isDiagnosticLogFileEnabled = options.DiagnosticLog.WriteToFile;
        if (_isDiagnosticLogFileEnabled)
        {
            EnsureDiagnosticLogFileHeader();
        }

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

        _spinnerDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _spinnerDebounceTimer.Tick += (_, _) => FlushPendingSpinner();

        AttachSearchPickerSelectionSources();
        AttachColorPickerSelectionSources();

        LatestStatus = hotkeySettingsLoadError is null
            ? "Recorder attached. Use configured hotkeys or overlay controls to start."
            : $"Recorder attached. User hotkey settings were ignored: {hotkeySettingsLoadError}";
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

        if (IsScenarioSelectionEnabled)
        {
            _isScanning = true;
            _scenarioDiscoveryTask = DiscoverScenarioDestinationsAsync();
        }
    }

    private static RecorderHotkeySettings LoadEffectiveHotkeySettings(
        AppAutomationRecorderOptions options,
        RecorderHotkeySettingsStore store,
        out string? loadError)
    {
        if (!store.TryLoad(out var overrides, out loadError))
        {
            return RecorderHotkeySettings.CreateEffective(options.Hotkeys, overrides: null);
        }

        var effective = RecorderHotkeySettings.CreateEffective(options.Hotkeys, overrides);
        var validation = effective.Validate();
        if (validation.IsValid)
        {
            return effective;
        }

        loadError = $"Invalid hotkey settings: {validation.ErrorMessage}";
        return RecorderHotkeySettings.CreateEffective(options.Hotkeys, overrides: null);
    }

    public event EventHandler? SessionChanged;

    internal event EventHandler? ExportRequested;

    internal event EventHandler? HotkeysChanged;

    public RecorderSessionState State => _state;

    public int StepCount => _steps.Count;

    public int PersistableStepCount => _steps.Count(static step => step.CanPersist && !step.IsIgnored);

    public IReadOnlyList<RecorderCheckpointOption> Checkpoints => CreateCheckpointOptions();

    public string LatestPreview { get; private set; } = string.Empty;

    public string LatestStatus { get; private set; } = string.Empty;

    public RecorderValidationStatus LatestValidationStatus { get; private set; } = RecorderValidationStatus.Valid;

    public bool IsBusy => _activeOperationTask is not null;

    public string BusyDescription => _busyDescription;

    public string SessionSummary => BuildSessionSummary();

    public bool IsDiagnosticLogFileEnabled => _isDiagnosticLogFileEnabled;

    public string DiagnosticLogFilePath => _diagnosticLogFilePath;

    public int DiagnosticLogEntryCount => _diagnosticLogEntryCount;

    public int WarningStepCount => _steps.Count(static step => !step.IsIgnored && step.ValidationStatus == RecorderValidationStatus.Warning);

    public int InvalidStepCount => _steps.Count(static step => !step.IsIgnored && (step.ValidationStatus == RecorderValidationStatus.Invalid || !step.CanPersist));

    public int IgnoredStepCount => _steps.Count(static step => step.IsIgnored);

    public IReadOnlyList<RecorderStepJournalEntry> StepJournal => _steps.Select(CreateJournalEntry).ToArray();

    public string CurrentScenarioFilePath
    {
        get
        {
            if (_lastScenarioFilePath is not null)
            {
                return _lastScenarioFilePath;
            }

            if (!IsScenarioSelectionEnabled)
            {
                return _defaultOutputDescription.ScenarioFilePathDisplay;
            }

            var saveContext = CreateScenarioSaveContext();
            return saveContext is null
                ? "Select a scenario destination and enter a scenario name."
                : _codeGenerator.DescribeOutput(_window, _options, outputDirectoryOverride: null, saveContext).ScenarioFilePathDisplay;
        }
    }

    public bool IsScenarioSelectionEnabled => _options.ScenarioSelection.IsEnabled;

    public bool IsScanning => _isScanning;

    public string? ScenarioSelectionError => _scenarioDiscoveryError ?? ValidateScenarioName(_scenarioName);

    public IReadOnlyList<RecordedScenarioDestination> ScenarioDestinations => _scenarioDestinations;

    public RecordedScenarioDestination? SelectedScenarioDestination => _selectedScenarioDestination;

    public string ScenarioName => _scenarioName;

    public bool CanStartRecording => !IsScenarioSelectionEnabled
        || (_state == RecorderSessionState.Off
            && !IsBusy
            && !_isScanning
            && ScenarioSelectionError is null
            && _selectedScenarioDestination is not null);

    public bool CanChangeScenarioTarget => IsScenarioSelectionEnabled
        && _state == RecorderSessionState.Off
        && _steps.Count == 0
        && !IsBusy
        && !_isScanning;

    internal Task ScenarioDiscoveryTaskForTesting => _scenarioDiscoveryTask;

    internal RecorderHotkeySettings HotkeySettings => _hotkeySettings;

    internal RecorderHotkeyMap HotkeyMap => _hotkeyMap;

    public void Start()
    {
        if (!CanStartRecording)
        {
            SetStatus(
                ScenarioSelectionError
                    ?? (_isScanning ? "Scenario destinations are still being scanned." : "Select a scenario destination."),
                RecorderValidationStatus.Warning);
            return;
        }

        if (IsScenarioSelectionEnabled)
        {
            _scenarioName = _scenarioName.Trim();
        }

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
        if (IsScenarioSelectionEnabled && (_state != RecorderSessionState.Off || IsBusy))
        {
            SetStatus(
                "Clear is available only while recording is stopped and no save operation is running.",
                RecorderValidationStatus.Warning);
            return;
        }

        FlushPendingState();
        _steps.Clear();
        LatestPreview = string.Empty;
        _lastScenarioFilePath = null;
        _autosaveDraftIdentity = Guid.NewGuid().ToString("N");
        SetStatus("Recorded steps cleared.", RecorderValidationStatus.Valid);
    }

    public bool TrySelectScenarioDestination(RecordedScenarioDestination? destination)
    {
        if (!CanChangeScenarioTarget)
        {
            SetStatus("Stop recording and clear recorded steps before changing the scenario destination.", RecorderValidationStatus.Warning);
            return false;
        }

        if (destination is not null && !_scenarioDestinations.Contains(destination))
        {
            SetStatus("The selected scenario destination is not available.", RecorderValidationStatus.Warning);
            return false;
        }

        _selectedScenarioDestination = destination;
        _lastScenarioFilePath = null;
        _autosaveDraftIdentity = Guid.NewGuid().ToString("N");
        SetStatus(
            destination is null ? "Scenario destination cleared." : $"Scenario destination: {destination.DisplayName}",
            RecorderValidationStatus.Valid);
        return true;
    }

    public bool TrySetScenarioName(string? scenarioName)
    {
        if (!CanChangeScenarioTarget)
        {
            SetStatus("Stop recording and clear recorded steps before changing the scenario name.", RecorderValidationStatus.Warning);
            return false;
        }

        _scenarioName = scenarioName ?? string.Empty;
        _lastScenarioFilePath = null;
        _autosaveDraftIdentity = Guid.NewGuid().ToString("N");
        var validationError = ValidateScenarioName(_scenarioName);
        SetStatus(
            validationError ?? "Scenario name updated.",
            validationError is null ? RecorderValidationStatus.Valid : RecorderValidationStatus.Warning);
        return true;
    }

    public void SetDiagnosticLogFileEnabled(bool isEnabled)
    {
        if (_isDiagnosticLogFileEnabled == isEnabled)
        {
            return;
        }

        _isDiagnosticLogFileEnabled = isEnabled;
        if (isEnabled)
        {
            EnsureDiagnosticLogFileHeader();
        }

        SetStatus(
            isEnabled
                ? $"Diagnostic log file enabled: {_diagnosticLogFilePath}"
                : "Diagnostic log file disabled.",
            LatestValidationStatus);
    }

    internal bool TryApplyHotkeySettings(RecorderHotkeySettings hotkeySettings, out string? error)
    {
        ArgumentNullException.ThrowIfNull(hotkeySettings);

        var validation = hotkeySettings.Validate();
        if (!validation.IsValid)
        {
            error = validation.ErrorMessage;
            return false;
        }

        _hotkeySettings = hotkeySettings;
        _hotkeyMap = hotkeySettings.ToMap();
        error = null;
        SetStatus("Recorder hotkeys updated.", LatestValidationStatus);
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
        return true;
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
        _spinnerDebounceTimer.Stop();
        DiscardPendingTimePicker();

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
        var graphValidation = ApplyScenarioGraphValidation();
        UpdateLatestPreviewFromSteps();
        SetStatusAfterGraphValidation(
            graphValidation,
            "Recorded step removed.",
            RecorderValidationStatus.Valid);
        RequestAutosaveIfRecording();
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
        var graphValidation = ApplyScenarioGraphValidation();
        UpdateLatestPreviewFromSteps();
        SetStatusAfterGraphValidation(
            graphValidation,
            isIgnored ? "Recorded step ignored." : "Recorded step restored.",
            isIgnored ? RecorderValidationStatus.Warning : _steps[index].ValidationStatus);
        RequestAutosaveIfRecording();
    }

    public bool RetryStepValidation(Guid stepId)
    {
        var index = _steps.FindIndex(step => step.StepId == stepId);
        if (index < 0)
        {
            return false;
        }

        _steps[index] = RevalidateStep(_steps[index]);
        var graphValidation = ApplyScenarioGraphValidation();
        UpdateLatestPreviewFromSteps();
        var revalidatedStep = _steps[index];
        LogRecordedStepDiagnostics("RetryStepValidation", null, revalidatedStep);
        SetStatusAfterGraphValidation(
            graphValidation,
            ResolveJournalStatusMessage(revalidatedStep),
            revalidatedStep.ValidationStatus);
        RequestAutosaveIfRecording();
        return true;
    }

    public bool CanMoveStep(Guid stepId, RecorderStepMoveDirection direction)
    {
        var index = _steps.FindIndex(step => step.StepId == stepId);
        if (index < 0)
        {
            return false;
        }

        return direction switch
        {
            RecorderStepMoveDirection.Earlier => index > 0,
            RecorderStepMoveDirection.Later => index < _steps.Count - 1,
            _ => false
        };
    }

    public bool MoveStep(Guid stepId, RecorderStepMoveDirection direction)
    {
        if (!CanMoveStep(stepId, direction))
        {
            return false;
        }

        var index = _steps.FindIndex(step => step.StepId == stepId);
        var targetIndex = direction == RecorderStepMoveDirection.Earlier
            ? index - 1
            : index + 1;
        (_steps[index], _steps[targetIndex]) = (_steps[targetIndex], _steps[index]);
        var graphValidation = ApplyScenarioGraphValidation();
        UpdateLatestPreviewFromSteps();
        SetStatusAfterGraphValidation(
            graphValidation,
            direction == RecorderStepMoveDirection.Earlier
                ? "Recorded step moved earlier."
                : "Recorded step moved later.",
            _steps[targetIndex].ValidationStatus);
        RequestAutosaveIfRecording();
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

    internal void RegisterContextMenuOwnerForTesting(Control owner, bool keyboard = false)
    {
        _pendingContextMenuOwner = FindContextMenuOwner(owner);
        if (keyboard)
        {
            RegisterKeyboardInput(owner);
        }
        else
        {
            RegisterPointerInput(owner);
        }
    }

    internal void CancelContextMenuForTesting()
    {
        _pendingContextMenuOwner = null;
    }

    internal void RegisterContextMenuItemPointerForTesting(Control source)
    {
        DiscardPendingContextMenuOwnerIfSwitchingTo(source);
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
        DiscardPendingTimePickerIfSwitchingTo(source);
        DiscardPendingSingleSelectIfSwitchingTo(source);
        DiscardPendingColorPickerIfSwitchingTo(source);

        if (IsPickerTemplateButton(source))
        {
            return;
        }

        if (IsExpanderHeaderToggle(source))
        {
            return;
        }

        if (TryHandleTimePickerButton(source))
        {
            return;
        }

        if (TryHandleSingleSelectButton(source))
        {
            return;
        }

        if (TryHandleColorPickerButton(source))
        {
            return;
        }

        if (TryRecordSearchHistoryAction(source))
        {
            return;
        }

        if (TrySuppressSearchPickerButtonClick(source))
        {
            return;
        }

        if (_stepFactory.ShouldSuppressSingleSelectButton(source))
        {
            return;
        }

        if (_stepFactory.ShouldSuppressColorPickerButton(source))
        {
            return;
        }

        if (TrySuppressCompositeWorkflowButtonClick(source))
        {
            return;
        }

        var control = ResolveButtonActionOwner(source);
        if (TryRecordGridAction(control))
        {
            return;
        }

        if (TryRecordCompositeButtonAction(control ?? source))
        {
            return;
        }

        FlushPendingTextIfSwitchingTo(control);
        FlushPendingSliderIfSwitchingTo(control);
        FlushPendingSpinnerIfSwitchingTo(control);
        AddStep(_stepFactory.TryCreateButtonStep(control), control ?? source, "ButtonClick");
    }

    internal void CaptureAssertionForTesting(Control source, RecorderAssertionMode mode)
    {
        AddStep(_stepFactory.TryCreateAssertionStep(source, mode), source, $"Assertion:{mode}");
    }

    internal void AttachInputHandlersForTesting()
    {
        RebindInputHandlers();
        RefreshObservedControls();
    }

    internal void CaptureButtonPressForTesting(Control? source)
    {
        CaptureComboBoxFilterClickSnapshot(ResolveButtonActionOwner(source));
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

    internal void SetLastHoveredControlForTesting(Control? source)
    {
        _lastHoveredControl = source;
    }

    internal void HandleRecorderCommandForTesting(RecorderCommandKind command)
    {
        HandleRecorderCommand(command);
    }

    private void ApplySaveResult(RecorderSaveResult result)
    {
        var status = !result.Success
            ? RecorderValidationStatus.Invalid
            : result.SkippedStepCount > 0
                ? RecorderValidationStatus.Warning
                : RecorderValidationStatus.Valid;
        SetStatus(result.Message, status);
        if (!result.Success)
        {
            var message = result.Diagnostics.Count == 0
                ? result.Message
                : $"{result.Message} {string.Join(" ", result.Diagnostics)}";
            LogRecorderDiagnostic(
                RecorderDiagnosticsEventIds.SaveFailed,
                "Save",
                source: null,
                step: null,
                findings: Array.Empty<RecorderRuntimeValidationFinding>(),
                message);
        }

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
        RebindInputHandlers();
        _window.PropertyChanged += OnWindowPropertyChanged;

        _detachActions.Add(DetachInputHandlers);
        _detachActions.Add(() => _window.PropertyChanged -= OnWindowPropertyChanged);
    }

    private void AttachSearchPickerSelectionSources()
    {
        foreach (var source in _options.SearchPickerSelectionSources
                     .Distinct<IRecorderSearchPickerSelectionSource>(ReferenceEqualityComparer.Instance))
        {
            ArgumentNullException.ThrowIfNull(source);
            source.SelectionConfirmed += OnSearchPickerSelectionConfirmed;
            _detachActions.Add(() => source.SelectionConfirmed -= OnSearchPickerSelectionConfirmed);
        }
    }

    private void OnSearchPickerSelectionConfirmed(
        object? sender,
        RecorderSearchPickerSelectionConfirmedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording)
        {
            return;
        }

        var result = _stepFactory.TryCreateSearchPickerStep(
            e.SearchInput,
            e.ResultsRoot,
            e.SelectedValue,
            _pendingTextBox,
            _pendingTextValue);
        if (!result.Success)
        {
            AddStep(result, e.ResultsRoot, "SearchPickerSelectionSource");
            return;
        }

        CompleteSearchPickerSelection(result, e.SearchInput, e.ResultsRoot);
    }

    private void RebindInputHandlers()
    {
        var inputRoot = _validationRootProvider() ?? _window;
        if (ReferenceEquals(inputRoot, _inputRoot))
        {
            return;
        }

        DetachInputHandlers();
        _inputRoot = inputRoot;
        _inputRoot.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _inputRoot.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        _inputRoot.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        _inputRoot.AddHandler(
            InputElement.KeyDownEvent,
            OnKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _inputRoot.AddHandler(Button.ClickEvent, OnButtonClick, RoutingStrategies.Bubble);
    }

    private void DetachInputHandlers()
    {
        if (_inputRoot is null)
        {
            return;
        }

        _inputRoot.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _inputRoot.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        _inputRoot.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
        _inputRoot.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _inputRoot.RemoveHandler(Button.ClickEvent, OnButtonClick);
        _inputRoot = null;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (string.Equals(e.Property.Name, "Content", StringComparison.Ordinal))
        {
            RebindInputHandlers();
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

        if (_pendingSpinner is not null && !currentControls.Contains(_pendingSpinner))
        {
            FlushPendingSpinner();
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

        var visited = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        CollectObservableControls(root, controls, visited);

        return controls;
    }

    private static void CollectObservableControls(
        Control root,
        ISet<Control> controls,
        ISet<Control> visited)
    {
        foreach (var control in root.GetVisualDescendants().OfType<Control>().Prepend(root))
        {
            if (!visited.Add(control))
            {
                continue;
            }

            if (IsObservableControl(control))
            {
                controls.Add(control);
            }

            foreach (var detachedRoot in EnumerateDetachedContentRoots(control))
            {
                CollectObservableControls(detachedRoot, controls, visited);
            }

            if (control.ContextMenu is { } contextMenu)
            {
                CollectMenuItems(contextMenu.Items.OfType<MenuItem>(), controls, visited);
            }

            if (control.ContextFlyout is MenuFlyout menuFlyout)
            {
                CollectMenuItems(menuFlyout.Items.OfType<MenuItem>(), controls, visited);
            }
        }

        foreach (var menuItem in root.GetLogicalDescendants().OfType<MenuItem>())
        {
            if (visited.Add(menuItem))
            {
                controls.Add(menuItem);
            }
        }

        var menus = root.GetVisualDescendants().OfType<Menu>();
        if (root is Menu rootMenu)
        {
            menus = menus.Prepend(rootMenu);
        }

        foreach (var menu in menus)
        {
            CollectMenuItems(menu.Items.OfType<MenuItem>(), controls, visited);
        }
    }

    private static void CollectMenuItems(
        IEnumerable<MenuItem> items,
        ISet<Control> controls,
        ISet<Control> visited)
    {
        foreach (var item in items)
        {
            if (visited.Add(item))
            {
                controls.Add(item);
            }

            CollectMenuItems(item.Items.OfType<MenuItem>(), controls, visited);
        }
    }

    private static IEnumerable<Control> EnumerateDetachedContentRoots(Control control)
    {
        foreach (var propertyName in DetachedContentPropertyNames)
        {
            var property = control.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public);
            if (property is null || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (property.GetValue(control) is Control contentRoot)
            {
                yield return contentRoot;
            }
        }
    }

    private Action AttachObservedControl(Control control)
    {
        switch (control)
        {
            case NumericUpDown spinner:
                spinner.PropertyChanged += OnSpinnerPropertyChanged;
                return () => spinner.PropertyChanged -= OnSpinnerPropertyChanged;
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
            case TimePicker timePicker:
                timePicker.PropertyChanged += OnTimePickerPropertyChanged;
                return () => timePicker.PropertyChanged -= OnTimePickerPropertyChanged;
            case Expander expander:
                expander.PropertyChanged += OnExpanderPropertyChanged;
                return () => expander.PropertyChanged -= OnExpanderPropertyChanged;
            case MenuItem menuItem:
                menuItem.AddHandler(MenuItem.ClickEvent, OnMenuItemClick, RoutingStrategies.Bubble);
                return () => menuItem.RemoveHandler(MenuItem.ClickEvent, OnMenuItemClick);
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
        if (control is TextBox
            && (FindAncestorOrSelf<NumericUpDown>(control) is not null
                || FindAncestorOrSelf<TimePicker>(control) is not null))
        {
            return false;
        }

        return control is TextBox
            or ComboBox
            or ListBox
            or TabControl
            or TreeView
            or Slider
            or NumericUpDown
            or TimePicker
            or Expander
            or MenuItem
            or DatePicker
            or Calendar;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording)
        {
            return;
        }

        var source = e.Source as Control;
        var control = ResolveInteractionOwner(source);
        var isRightButtonPressed = e.GetCurrentPoint(_inputRoot ?? _window).Properties.IsRightButtonPressed;
        if (isRightButtonPressed)
        {
            _pendingContextMenuOwner = FindContextMenuOwner(source);
            FlushPendingTextIfSwitchingTo(_pendingContextMenuOwner);
            FlushPendingSliderIfSwitchingTo(_pendingContextMenuOwner);
            FlushPendingSpinnerIfSwitchingTo(_pendingContextMenuOwner);
            RegisterPointerInput(_pendingContextMenuOwner ?? control);
            return;
        }

        DiscardPendingContextMenuOwnerIfSwitchingTo(source);
        DiscardPendingTimePickerIfSwitchingTo(source);
        DiscardPendingSingleSelectIfSwitchingTo(source);
        DiscardPendingColorPickerIfSwitchingTo(source);
        CaptureComboBoxFilterClickSnapshot(ResolveButtonActionOwner(source));
        FlushPendingTextIfSwitchingTo(control);
        FlushPendingSliderIfSwitchingTo(control);
        FlushPendingSpinnerIfSwitchingTo(control);
        RegisterPointerInput(control);

        if (FindAncestorOrSelf<Button>(source) is null)
        {
            TryRecordGridAction(source ?? control);
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

        if (ShouldSuppressTemplateTextEntry(textBox))
        {
            return;
        }

        _pendingTextBox = textBox;
        _pendingTextValue = textBox.Text;
        RegisterKeyboardInput(textBox);
        RestartTextDebounceUnlessCompositeSelection(textBox);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_hotkeyMap.TryGetCommand(e.Key, e.PhysicalKey, e.KeyModifiers, out var command))
        {
            HandleRecorderCommand(command);
            e.Handled = true;
            return;
        }

        if (_state != RecorderSessionState.Recording)
        {
            return;
        }

        var focused = GetFocusedWindowControl();
        if (focused is not null)
        {
            if (e.Key == Key.Escape)
            {
                _pendingContextMenuOwner = null;
            }
            else if (e.Key == Key.Apps
                     || (e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
            {
                _pendingContextMenuOwner = FindContextMenuOwner(focused);
                RegisterKeyboardInput(_pendingContextMenuOwner ?? focused);
                return;
            }

            DiscardPendingTimePickerIfSwitchingTo(focused);
            DiscardPendingSingleSelectIfSwitchingTo(focused);
            DiscardPendingColorPickerIfSwitchingTo(focused);
            if (e.Key is Key.Enter or Key.Space)
            {
                CaptureComboBoxFilterClickSnapshot(ResolveButtonActionOwner(focused));
            }

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
        var focused = GetFocusedWindowControl();
        LogRecorderDiagnostic(
            RecorderDiagnosticsEventIds.CommandHandled,
            $"Command:{command}",
            focused,
            step: null,
            findings: Array.Empty<RecorderRuntimeValidationFinding>(),
            message: null);

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
            case RecorderCommandKind.CaptureAssertExists:
                CaptureAssertion(RecorderAssertionMode.Exists);
                break;
            case RecorderCommandKind.CaptureCheckpoint:
                CaptureCheckpoint();
                break;
            case RecorderCommandKind.CaptureCheckpointAssertion:
                if (!TryDescribeCurrentValue(out var currentValue, out var valueError)
                    || currentValue is null)
                {
                    SetStatus(
                        valueError ?? "The selected control does not expose a readable value.",
                        RecorderValidationStatus.Invalid);
                    break;
                }

                var checkpoint = CreateCheckpointOptions()
                    .LastOrDefault(candidate => candidate.ValueKind == currentValue.ValueKind);
                if (checkpoint is null)
                {
                    SetStatus(
                        $"No active {currentValue.ValueKind} checkpoint is available to compare.",
                        RecorderValidationStatus.Invalid);
                }
                else
                {
                    CaptureCheckpointAssertion(checkpoint.CheckpointId);
                }
                break;
        }
    }

    private void CaptureComboBoxFilterClickSnapshot(Control? actionSource)
    {
        _comboBoxFilterClickSnapshot = null;
        if (_state == RecorderSessionState.Recording
            && actionSource is not null
            && _stepFactory.TryCaptureComboBoxFilterSelection(actionSource, out var selectedValues))
        {
            _comboBoxFilterClickSnapshot = new ComboBoxFilterClickSnapshot(
                actionSource,
                selectedValues,
                DateTimeOffset.UtcNow);
        }
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        var clickSnapshot = _comboBoxFilterClickSnapshot;
        _comboBoxFilterClickSnapshot = null;
        if (_state != RecorderSessionState.Recording)
        {
            return;
        }

        var eventSource = e.Source as Control;
        DiscardPendingTimePickerIfSwitchingTo(eventSource);
        DiscardPendingSingleSelectIfSwitchingTo(eventSource);
        DiscardPendingColorPickerIfSwitchingTo(eventSource);
        if (IsPickerTemplateButton(eventSource))
        {
            return;
        }

        if (IsExpanderHeaderToggle(eventSource))
        {
            return;
        }

        if (TryHandleTimePickerButton(eventSource))
        {
            return;
        }

        if (TryHandleSingleSelectButton(eventSource))
        {
            return;
        }

        if (TryHandleColorPickerButton(eventSource))
        {
            return;
        }

        if (TryRecordSearchHistoryAction(eventSource))
        {
            return;
        }

        if (TrySuppressSearchPickerButtonClick(eventSource))
        {
            return;
        }

        if (_stepFactory.ShouldSuppressSingleSelectButton(eventSource))
        {
            return;
        }

        if (_stepFactory.ShouldSuppressColorPickerButton(eventSource))
        {
            return;
        }

        if (TrySuppressCompositeWorkflowButtonClick(eventSource))
        {
            return;
        }

        var control = ResolveButtonActionOwner(eventSource);
        if (TryRecordGridAction(control))
        {
            return;
        }

        if (TryRecordCompositeButtonAction(control ?? eventSource, clickSnapshot))
        {
            return;
        }

        FlushPendingTextIfSwitchingTo(control);
        FlushPendingSliderIfSwitchingTo(control);
        FlushPendingSpinnerIfSwitchingTo(control);
        AddStep(_stepFactory.TryCreateButtonStep(control), control ?? eventSource, "ButtonClick");
    }

    private void OnMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording
            || e.Source is not MenuItem { Items.Count: 0 } item
            || ReferenceEquals(_lastMenuItemClickEvent, e))
        {
            return;
        }

        _lastMenuItemClickEvent = e;
        var contextMenuOwner = _pendingContextMenuOwner;
        _pendingContextMenuOwner = null;
        FlushPendingState();
        if (contextMenuOwner is not null)
        {
            var contextResult = _stepFactory.TryCreateContextMenuItemStep(
                item,
                contextMenuOwner,
                out var belongsToOwner);
            if (belongsToOwner)
            {
                AddStep(contextResult, item, "ContextMenuItemClick");
                return;
            }
        }

        AddStep(_stepFactory.TryCreateMenuItemStep(item), item, "MenuItemClick");
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
        if (_state != RecorderSessionState.Recording
            || (!WasRecentlyTriggeredByUser(comboBox) && !HasPendingCompositeSelection(comboBox)))
        {
            return;
        }

        if (_stepFactory.ShouldSuppressCompositeSelection(comboBox))
        {
            return;
        }

        if (TryRecordComboBoxFilterSelection(comboBox))
        {
            return;
        }

        if (TryRecordColorPickerSelection(comboBox))
        {
            return;
        }

        if (TryRecordSingleSelectSelection(comboBox))
        {
            return;
        }

        if (TryRecordSearchPickerSelection(comboBox))
        {
            return;
        }

        FlushPendingTextIfSwitchingTo(comboBox);
        FlushPendingSliderIfSwitchingTo(comboBox);
        FlushPendingSpinnerIfSwitchingTo(comboBox);
        AddStep(_stepFactory.TryCreateComboBoxStep(comboBox), comboBox, "ComboBoxSelection");
    }

    private bool TryRecordSearchPickerSelection(ComboBox comboBox)
    {
        if (_pendingTextBox is not null)
        {
            var pendingResult = _stepFactory.TryCreateSearchPickerStep(_pendingTextBox, comboBox, _pendingTextValue);
            if (pendingResult.Success)
            {
                CompleteSearchPickerSelection(pendingResult, _pendingTextBox, comboBox);
                return true;
            }
        }

        var capture = _stepFactory.TryCreateSearchPickerStep(comboBox, _pendingTextBox, _pendingTextValue);
        if (!capture.IsConfigured)
        {
            return false;
        }

        if (capture.HasSelection)
        {
            CompleteSearchPickerSelection(capture.StepResult, capture.SearchInput, comboBox);
        }

        return true;
    }

    private bool TryRecordSingleSelectSelection(ComboBox comboBox)
    {
        return CompleteSingleSelectSelection(_stepFactory.TryCreateSingleSelectStep(comboBox), comboBox);
    }

    private bool TryRecordSingleSelectSelection(ListBox listBox)
    {
        return CompleteSingleSelectSelection(_stepFactory.TryCreateSingleSelectStep(listBox), listBox);
    }

    private bool TryRecordColorPickerSelection(ComboBox palette)
    {
        return CompleteColorPickerSelection(_stepFactory.TryCreateColorPickerStep(palette), palette);
    }

    private bool TryRecordColorPickerSelection(ListBox palette)
    {
        return CompleteColorPickerSelection(_stepFactory.TryCreateColorPickerStep(palette), palette);
    }

    private bool CompleteColorPickerSelection(ColorPickerCaptureResult capture, Control source)
    {
        if (!capture.IsConfigured)
        {
            return false;
        }

        if (!capture.HasCandidateValue)
        {
            return true;
        }

        if (capture.Hint is null || !capture.StepResult.Success)
        {
            LogSemanticCaptureFailure("ColorPickerSelection", source, capture.StepResult);
            return false;
        }

        DiscardPendingColorPicker();
        if (capture.Hint.Parts.CommitMode == ColorPickerCommitMode.Confirm)
        {
            _pendingColorPickerStep = capture.StepResult;
            _pendingColorPickerHint = capture.Hint;
            _pendingColorPickerSource = source;
            return true;
        }

        AddStep(capture.StepResult, source, "ColorPickerSelection");
        return true;
    }

    private bool CompleteSingleSelectSelection(SingleSelectCaptureResult capture, Control source)
    {
        if (!capture.IsConfigured)
        {
            return false;
        }

        if (!capture.HasSelection)
        {
            return true;
        }

        if (capture.Hint is null || !capture.StepResult.Success)
        {
            LogSemanticCaptureFailure("SingleSelectSelection", source, capture.StepResult);
            return false;
        }

        DiscardPendingSingleSelectText(capture.Hint);
        FlushPendingSliderIfSwitchingTo(source);
        FlushPendingSpinnerIfSwitchingTo(source);
        DiscardPendingSingleSelect();
        if (capture.Hint.Parts.CommitMode == SingleSelectCommitMode.Confirm)
        {
            _pendingSingleSelectStep = capture.StepResult;
            _pendingSingleSelectHint = capture.Hint;
            _pendingSingleSelectSource = source;
            return true;
        }

        AddStep(capture.StepResult, source, "SingleSelectSelection");
        return true;
    }

    private bool TryRecordSearchPickerSelection(ListBox listBox)
    {
        if (_pendingTextBox is not null)
        {
            var pendingResult = _stepFactory.TryCreateSearchPickerStep(_pendingTextBox, listBox, _pendingTextValue);
            if (pendingResult.Success)
            {
                CompleteSearchPickerSelection(pendingResult, _pendingTextBox, listBox);
                return true;
            }
        }

        var capture = _stepFactory.TryCreateSearchPickerStep(listBox, _pendingTextBox, _pendingTextValue);
        if (!capture.IsConfigured)
        {
            return false;
        }

        if (capture.HasSelection)
        {
            CompleteSearchPickerSelection(capture.StepResult, capture.SearchInput, listBox);
        }

        return true;
    }

    private void CompleteSearchPickerSelection(
        StepCreationResult result,
        TextBox? searchInput,
        Control results)
    {
        if (ReferenceEquals(_pendingTextBox, searchInput))
        {
            DiscardPendingText();
        }
        else
        {
            FlushPendingTextIfSwitchingTo(results);
        }

        FlushPendingSliderIfSwitchingTo(results);
        FlushPendingSpinnerIfSwitchingTo(results);
        AddStep(result, results, "SearchPickerSelection");
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
        if (_state != RecorderSessionState.Recording
            || (!WasRecentlyTriggeredByUser(listBox) && !HasPendingCompositeSelection(listBox)))
        {
            return;
        }

        if (TryRecordComboBoxFilterSelection(listBox))
        {
            return;
        }

        if (TryRecordColorPickerSelection(listBox))
        {
            return;
        }

        if (TryRecordSingleSelectSelection(listBox))
        {
            return;
        }

        if (_stepFactory.ShouldSuppressCompositeSelection(listBox))
        {
            return;
        }

        if (TryRecordSearchPickerSelection(listBox))
        {
            return;
        }

        if (TryRecordShellNavigation(listBox))
        {
            return;
        }

        FlushPendingTextIfSwitchingTo(listBox);
        FlushPendingSliderIfSwitchingTo(listBox);
        FlushPendingSpinnerIfSwitchingTo(listBox);
        AddStep(_stepFactory.TryCreateListBoxStep(listBox), listBox, "ListBoxSelection");
    }

    private void OnTabControlSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || sender is not TabControl tabControl || !WasRecentlyTriggeredByUser(tabControl))
        {
            return;
        }

        if (TryRecordShellNavigation(tabControl))
        {
            return;
        }

        FlushPendingTextIfSwitchingTo(tabControl);
        FlushPendingSliderIfSwitchingTo(tabControl);
        FlushPendingSpinnerIfSwitchingTo(tabControl);
        AddStep(_stepFactory.TryCreateTabSelectionStep(tabControl), tabControl, "TabSelection");
    }

    private void OnTreeViewSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || sender is not TreeView treeView || !WasRecentlyTriggeredByUser(treeView))
        {
            return;
        }

        if (TryRecordShellNavigation(treeView))
        {
            return;
        }

        FlushPendingTextIfSwitchingTo(treeView);
        FlushPendingSliderIfSwitchingTo(treeView);
        FlushPendingSpinnerIfSwitchingTo(treeView);
        AddStep(_stepFactory.TryCreateTreeSelectionStep(treeView), treeView, "TreeSelection");
    }

    private bool TrySuppressSearchPickerButtonClick(Control? source)
    {
        return source is not null && _stepFactory.ShouldSuppressSearchPickerButton(source);
    }

    private bool TrySuppressCompositeWorkflowButtonClick(Control? source)
    {
        return source is not null && _stepFactory.ShouldSuppressCompositeWorkflowButton(source);
    }

    private bool TryRecordCompositeButtonAction(
        Control? source,
        ComboBoxFilterClickSnapshot? clickSnapshot = null)
    {
        if (source is null)
        {
            return false;
        }

        var isComboBoxFilterAction = _stepFactory.IsComboBoxFilterAction(source);
        var capturedFilterValues = ReferenceEquals(clickSnapshot?.ActionSource, source)
            && DateTimeOffset.UtcNow - clickSnapshot.CapturedAt <= RecentInputWindow
            ? clickSnapshot.SelectedValues
            : null;
        var comboBoxFilterResult = _stepFactory.TryCreateComboBoxFilterStep(source, capturedFilterValues);
        if (TryRecordCompositeStep(comboBoxFilterResult, source, "ComboBoxFilter"))
        {
            return true;
        }

        if (isComboBoxFilterAction)
        {
            AddStep(comboBoxFilterResult, source, "ComboBoxFilter");
            return true;
        }

        var isMultiSelectCommit = _stepFactory.IsMultiSelectCommit(source);
        var multiSelectResult = _stepFactory.TryCreateMultiSelectStep(source);
        if (TryRecordCompositeStep(multiSelectResult, source, "MultiSelect"))
        {
            return true;
        }

        if (isMultiSelectCommit)
        {
            AddStep(multiSelectResult, source, "MultiSelect");
            return true;
        }

        var gridEditResult = _stepFactory.TryCreateGridEditStep(source);
        if (TryRecordCompositeStep(gridEditResult, source, "GridEdit"))
        {
            return true;
        }

        var dateRangeResult = _stepFactory.TryCreateDateRangeFilterStep(source);
        if (TryRecordCompositeStep(dateRangeResult, source, "DateRangeFilter"))
        {
            return true;
        }

        var numericRangeResult = _stepFactory.TryCreateNumericRangeFilterStep(source);
        if (TryRecordCompositeStep(numericRangeResult, source, "NumericRangeFilter"))
        {
            return true;
        }

        var folderExportResult = _stepFactory.TryCreateFolderExportStep(source);
        if (TryRecordCompositeStep(folderExportResult, source, "FolderExport"))
        {
            return true;
        }

        var dialogResult = _stepFactory.TryCreateDialogActionStep(source);
        if (TryRecordCompositeStep(dialogResult, source, "DialogAction", clearPendingInput: false))
        {
            return true;
        }

        var notificationResult = _stepFactory.TryCreateNotificationActionStep(source);
        if (TryRecordCompositeStep(notificationResult, source, "NotificationAction", clearPendingInput: false))
        {
            return true;
        }

        return false;
    }

    private bool TryRecordCompositeStep(
        StepCreationResult result,
        Control source,
        string diagnosticContext,
        bool clearPendingInput = true)
    {
        if (!result.Success)
        {
            return false;
        }

        if (clearPendingInput)
        {
            DiscardPendingText();
            FlushPendingSliderIfSwitchingTo(source);
            FlushPendingSpinnerIfSwitchingTo(source);
        }

        AddStep(result, source, diagnosticContext);
        return true;
    }

    private bool TryRecordComboBoxFilterSelection(Control source)
    {
        if (!_stepFactory.IsComboBoxFilterAction(source))
        {
            return false;
        }

        var result = _stepFactory.TryCreateComboBoxFilterStep(source);
        DiscardPendingText();
        FlushPendingSliderIfSwitchingTo(source);
        FlushPendingSpinnerIfSwitchingTo(source);
        AddStep(result, source, "ComboBoxFilter");
        return true;
    }

    private bool TryRecordShellNavigation(Control source)
    {
        var result = _stepFactory.TryCreateShellNavigationStep(source);
        if (!result.Success)
        {
            return false;
        }

        FlushPendingTextIfSwitchingTo(source);
        FlushPendingSliderIfSwitchingTo(source);
        FlushPendingSpinnerIfSwitchingTo(source);
        AddStep(result, source, "ShellNavigation");
        return true;
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

    private void OnSpinnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording
            || sender is not NumericUpDown spinner
            || !WasRecentlyTriggeredByUser(spinner)
            || !string.Equals(e.Property.Name, nameof(NumericUpDown.Value), StringComparison.Ordinal))
        {
            return;
        }

        FlushPendingTextIfSwitchingTo(spinner);
        FlushPendingSliderIfSwitchingTo(spinner);
        _pendingSpinner = spinner;
        _spinnerDebounceTimer.Stop();
        _spinnerDebounceTimer.Start();
    }

    private void OnTimePickerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording
            || sender is not TimePicker timePicker
            || !WasRecentlyTriggeredByUser(timePicker)
            || !string.Equals(e.Property.Name, nameof(TimePicker.SelectedTime), StringComparison.Ordinal)
            || timePicker.SelectedTime is null)
        {
            return;
        }

        if (_stepFactory.ShouldSuppressCompositeTimeSelection(timePicker))
        {
            return;
        }

        FlushPendingSliderIfSwitchingTo(timePicker);
        FlushPendingSpinnerIfSwitchingTo(timePicker);

        if (_stepFactory.TryResolveTimePickerHint(timePicker, out var hint))
        {
            DiscardPendingTimePickerText(hint);
            if (hint.Parts.CommitMode == TimePickerCommitMode.Confirm)
            {
                _pendingTimePicker = timePicker;
                _pendingTimePickerHint = hint;
                return;
            }
        }
        else
        {
            FlushPendingTextIfSwitchingTo(timePicker);
        }

        DiscardPendingTimePicker();
        AddStep(_stepFactory.TryCreateTimePickerStep(timePicker), timePicker, "TimePickerSelection");
    }

    private void OnExpanderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording
            || sender is not Expander expander
            || !WasRecentlyTriggeredByUser(expander)
            || e.Property != Expander.IsExpandedProperty)
        {
            return;
        }

        FlushPendingTextIfSwitchingTo(expander);
        FlushPendingSliderIfSwitchingTo(expander);
        FlushPendingSpinnerIfSwitchingTo(expander);
        AddStep(_stepFactory.TryCreateExpanderStep(expander), expander, "ExpanderState");
    }

    private void OnDatePickerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || sender is not DatePicker datePicker || !WasRecentlyTriggeredByUser(datePicker))
        {
            return;
        }

        if (_stepFactory.ShouldSuppressCompositeDateSelection(datePicker))
        {
            return;
        }

        if (string.Equals(e.Property.Name, nameof(DatePicker.SelectedDate), StringComparison.Ordinal))
        {
            FlushPendingTextIfSwitchingTo(datePicker);
            FlushPendingSliderIfSwitchingTo(datePicker);
            FlushPendingSpinnerIfSwitchingTo(datePicker);
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
            FlushPendingSpinnerIfSwitchingTo(calendar);
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

        if (CapturePendingColorPickerInput(textBox))
        {
            return;
        }

        if (ShouldSuppressTemplateTextEntry(textBox))
        {
            return;
        }

        var currentText = textBox.Text ?? string.Empty;
        var preserveCapturedSearchText =
            ReferenceEquals(_pendingTextBox, textBox)
            && !string.IsNullOrWhiteSpace(_pendingTextValue)
            && !string.Equals(_pendingTextValue, currentText, StringComparison.Ordinal)
            && IsCompositeSelectedValue(textBox, currentText);

        _pendingTextBox = textBox;
        if (!preserveCapturedSearchText)
        {
            _pendingTextValue = currentText;
        }

        RestartTextDebounceUnlessCompositeSelection(textBox);
    }

    private bool CapturePendingColorPickerInput(TextBox textBox)
    {
        var matchingHints = _options.ColorPickerHints
            .Where(hint => _stepFactory.IsColorPickerInput(textBox, hint))
            .ToArray();
        if (matchingHints.Length == 0)
        {
            return false;
        }

        DiscardPendingColorPicker();
        var capture = _stepFactory.TryCreateColorPickerStep(textBox, textBox.Text ?? string.Empty);
        if (!capture.HasCandidateValue)
        {
            return true;
        }

        if (capture.Hint is null || !capture.StepResult.Success)
        {
            LogSemanticCaptureFailure("ColorPickerInput", textBox, capture.StepResult);
            return false;
        }

        if (capture.HasColor)
        {
            _pendingColorPickerStep = capture.StepResult;
            _pendingColorPickerHint = capture.Hint;
            _pendingColorPickerSource = textBox;
        }

        return true;
    }

    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && ReferenceEquals(textBox, _pendingTextBox))
        {
            if (_stepFactory.ShouldRetainPendingTextForCompositeSelection(textBox))
            {
                return;
            }

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

    private bool ShouldSuppressTemplateTextEntry(TextBox textBox)
    {
        if (FindAncestorOrSelf<TimePicker>(textBox) is not null)
        {
            return true;
        }

        if (IsComboBoxTemplateTextBox(textBox))
        {
            return true;
        }

        if (IsConfiguredGridSearchPickerTextBox(textBox))
        {
            return false;
        }

        if (_stepFactory.ShouldSuppressCompositeTextEntry(textBox))
        {
            return true;
        }

        return IsInsideConfiguredGrid(textBox);
    }

    private bool IsInsideConfiguredGrid(Control source)
    {
        if (_options.GridHints.Count == 0)
        {
            return false;
        }

        foreach (var current in EnumerateRelatedControls(source))
        {
            foreach (var hint in _options.GridHints)
            {
                if (TryGetLocator(current, hint.SourceLocatorKind, out var locator)
                    && string.Equals(hint.SourceLocatorValue.Trim(), locator, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsConfiguredGridSearchPickerTextBox(TextBox textBox)
    {
        foreach (var hint in _options.GridSearchPickerHints)
        {
            if (TryGetLocator(textBox, hint.Parts.LocatorKind, out var locator)
                && string.Equals(hint.Parts.SearchInputLocator.Trim(), locator, StringComparison.Ordinal)
                && MatchesLocator(textBox, hint.SourceLocatorKind, hint.SourceLocatorValue))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsComboBoxTemplateTextBox(TextBox textBox)
    {
        if (!string.Equals(textBox.Name, "PART_EditableTextBox", StringComparison.Ordinal))
        {
            return false;
        }

        if (TryGetLocator(textBox, UiLocatorKind.AutomationId, out _))
        {
            return false;
        }

        var hasComboBoxOwner = false;
        foreach (var current in EnumerateRelatedControls(textBox))
        {
            if (ReferenceEquals(current, textBox))
            {
                continue;
            }

            if (current is ComboBox)
            {
                hasComboBoxOwner = true;
                break;
            }
        }

        return hasComboBoxOwner;
    }

    private static bool TryGetLocator(Control control, UiLocatorKind locatorKind, out string locator)
    {
        locator = locatorKind switch
        {
            UiLocatorKind.AutomationId => AutomationProperties.GetAutomationId(control) ?? string.Empty,
            UiLocatorKind.Name => AutomationProperties.GetName(control) ?? control.Name ?? string.Empty,
            _ => string.Empty
        };

        locator = locator.Trim();
        return !string.IsNullOrWhiteSpace(locator);
    }

    private static bool MatchesLocator(Control source, UiLocatorKind locatorKind, string locatorValue)
    {
        if (string.IsNullOrWhiteSpace(locatorValue))
        {
            return false;
        }

        foreach (var current in EnumerateRelatedControls(source))
        {
            if (TryGetLocator(current, locatorKind, out var currentLocator)
                && string.Equals(currentLocator, locatorValue.Trim(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void RestartTextDebounce()
    {
        _textDebounceTimer.Stop();
        _textDebounceTimer.Start();
    }

    private void RestartTextDebounceUnlessCompositeSelection(TextBox textBox)
    {
        _textDebounceTimer.Stop();
        if (!_stepFactory.ShouldRetainPendingTextForCompositeSelection(textBox))
        {
            _textDebounceTimer.Start();
        }
    }

    private void DiscardPendingText()
    {
        _textDebounceTimer.Stop();
        _pendingTextBox = null;
        _pendingTextValue = null;
    }

    private Control? GetFocusedWindowControl()
    {
        if (!_window.IsInitialized)
        {
            return null;
        }

        return TopLevel.GetTopLevel(_window)?.FocusManager?.GetFocusedElement() as Control;
    }

    private void CaptureAssertion(RecorderAssertionMode mode)
    {
        var control = _lastHoveredControl ?? GetFocusedWindowControl();
        FlushPendingTextIfSwitchingTo(control);
        FlushPendingSliderIfSwitchingTo(control);
        FlushPendingSpinnerIfSwitchingTo(control);
        AddStep(_stepFactory.TryCreateAssertionStep(control, mode), control, $"Assertion:{mode}");
    }

    public bool TryDescribeCurrentValue(
        out RecorderSemanticValueDescription? description,
        out string? error)
    {
        var control = _lastHoveredControl ?? GetFocusedWindowControl();
        return _stepFactory.TryDescribeSemanticValue(control, out description, out error);
    }

    public void CaptureCheckpoint(string? variableName = null)
    {
        var control = PrepareSemanticCaptureTarget();
        AddStep(
            _stepFactory.TryCreateCheckpointStep(control, variableName),
            control,
            "Checkpoint:Remember");
    }

    public void CaptureCheckpointAssertion(Guid checkpointId)
    {
        var checkpoint = CreateCheckpointOptions()
            .FirstOrDefault(candidate => candidate.CheckpointId == checkpointId);
        if (checkpoint is null)
        {
            SetStatus("Selected checkpoint is missing or ignored.", RecorderValidationStatus.Invalid);
            return;
        }

        var control = PrepareSemanticCaptureTarget();
        AddStep(
            _stepFactory.TryCreateCheckpointAssertionStep(control, checkpoint),
            control,
            "Checkpoint:Compare");
    }

    public void CaptureLiteralAssertion(
        string expectedText,
        RecorderComparisonKind comparisonKind)
    {
        var control = PrepareSemanticCaptureTarget();
        AddStep(
            _stepFactory.TryCreateLiteralAssertionStep(control, expectedText, comparisonKind),
            control,
            "Assertion:Literal");
    }

    private Control? PrepareSemanticCaptureTarget()
    {
        var control = _lastHoveredControl ?? GetFocusedWindowControl();
        FlushPendingTextIfSwitchingTo(control);
        FlushPendingSliderIfSwitchingTo(control);
        FlushPendingSpinnerIfSwitchingTo(control);
        return control;
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
        var tentativeSteps = _steps
            .Where(static step => !step.IsIgnored)
            .Append(recordedStep)
            .ToArray();
        var preview = _codeGenerator.GeneratePreviewForStep(recordedStep, tentativeSteps);
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
        var graphValidation = ApplyScenarioGraphValidation();
        _lastFingerprint = fingerprint;
        _lastRecordedAt = now;
        var effectiveStep = _steps[^1];
        LatestPreview = _codeGenerator.GeneratePreviewForStep(
            effectiveStep,
            _steps.Where(static step => !step.IsIgnored).ToArray());
        SetStatusAfterGraphValidation(
            graphValidation,
            ResolveStepStatusMessage(effectiveStep, result.Message),
            effectiveStep.ValidationStatus);
        RequestAutosaveIfRecording();
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

    private void LogSemanticCaptureFailure(
        string captureAction,
        Control source,
        StepCreationResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            LogCaptureFailure(captureAction, source, result.Message);
        }
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
        if (!_hasConfiguredLogger && !_isDiagnosticLogFileEnabled)
        {
            return;
        }

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
            AppendDiagnosticLogFile(eventId, diagnostic);

            if (_hasConfiguredLogger)
            {
                _logger.LogWarning(eventId, "{RecorderDiagnostic}", diagnostic);
            }
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

    private void AppendDiagnosticLogFile(EventId eventId, string diagnostic)
    {
        if (!_isDiagnosticLogFileEnabled)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_diagnosticLogFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(
                _diagnosticLogFilePath,
                string.Join(
                    Environment.NewLine,
                    $"[{DateTimeOffset.UtcNow:O}] EventId={eventId.Id} EventName={eventId.Name}",
                    diagnostic,
                    string.Empty,
                    new string('-', 80),
                    string.Empty));
            _diagnosticLogEntryCount++;
            NotifySessionChanged();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                RecorderDiagnosticsEventIds.DiagnosticsSnapshotFailed,
                ex,
                "Failed to append recorder diagnostic log file '{DiagnosticLogFilePath}': {Message}",
                _diagnosticLogFilePath,
                ex.Message);
        }
    }

    private void EnsureDiagnosticLogFileHeader()
    {
        try
        {
            var directory = Path.GetDirectoryName(_diagnosticLogFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(_diagnosticLogFilePath))
            {
                return;
            }

            File.WriteAllText(
                _diagnosticLogFilePath,
                string.Join(
                    Environment.NewLine,
                    "AppAutomation recorder diagnostic log",
                    $"ScenarioName: {_options.ScenarioName}",
                    $"CreatedUtc: {DateTimeOffset.UtcNow:O}",
                    string.Empty));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                RecorderDiagnosticsEventIds.DiagnosticsSnapshotFailed,
                ex,
                "Failed to initialize recorder diagnostic log file '{DiagnosticLogFilePath}': {Message}",
                _diagnosticLogFilePath,
                ex.Message);
        }
    }

    private static string ResolveDiagnosticLogFilePath(
        AppAutomationRecorderOptions options,
        RecorderOutputDescription outputDescription)
    {
        if (!string.IsNullOrWhiteSpace(options.DiagnosticLog.FilePath))
        {
            return Path.GetFullPath(options.DiagnosticLog.FilePath);
        }

        var directory = !string.IsNullOrWhiteSpace(outputDescription.OutputDirectory)
            ? outputDescription.OutputDirectory
            : Path.Combine(Path.GetTempPath(), "AppAutomation", "Recorder");
        var scenarioName = RecorderNaming.CreateFileSafeName(options.ScenarioName, "scenario");
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(directory, $"{scenarioName}.{timestamp}.recorder-diagnostics.log");
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
            step.SecondDoubleValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            step.SecondDateValue?.ToString("O") ?? string.Empty,
            step.RowIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            step.ColumnIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            step.IntValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            step.FilterCommitMode?.ToString() ?? string.Empty,
            step.FolderExportCommitMode?.ToString() ?? string.Empty,
            step.GridCellEditCommitMode?.ToString() ?? string.Empty,
            step.TimeValue?.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            step.ValueKind?.ToString() ?? string.Empty,
            step.ValueAccessorKind?.ToString() ?? string.Empty,
            step.ComparisonKind?.ToString() ?? string.Empty,
            step.CheckpointId?.ToString("N") ?? string.Empty,
            step.ExpectedCheckpointId?.ToString("N") ?? string.Empty,
            step.CheckpointVariableName ?? string.Empty,
            step.HasExpectedLiteral,
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
        FlushPendingSpinner();
        DiscardPendingTimePicker();
        DiscardPendingSingleSelect();
        DiscardPendingColorPicker();
        _pendingContextMenuOwner = null;
    }

    private bool TryHandleTimePickerButton(Control? source)
    {
        if (!_stepFactory.TryResolveTimePickerButton(source, out var hint, out var isConfirm))
        {
            return false;
        }

        var pendingTimePicker = _pendingTimePicker;
        var isPendingSelectionForHint = pendingTimePicker is not null
            && Equals(_pendingTimePickerHint, hint);
        DiscardPendingTimePickerText(hint);
        DiscardPendingTimePicker();

        if (isConfirm && isPendingSelectionForHint)
        {
            AddStep(
                _stepFactory.TryCreateTimePickerStep(pendingTimePicker!, hint),
                pendingTimePicker,
                "TimePickerSelection");
        }

        return true;
    }

    private void DiscardPendingTimePicker()
    {
        _pendingTimePicker = null;
        _pendingTimePickerHint = null;
    }

    private void DiscardPendingTimePickerIfSwitchingTo(Control? source)
    {
        var hint = _pendingTimePickerHint;
        if (hint is null || _stepFactory.IsTimePickerPart(source, hint))
        {
            return;
        }

        DiscardPendingTimePickerText(hint);
        DiscardPendingTimePicker();
    }

    private void DiscardPendingTimePickerText(RecorderTimePickerHint hint)
    {
        if (_pendingTextBox is not null && _stepFactory.IsTimePickerInput(_pendingTextBox, hint))
        {
            DiscardPendingText();
        }
    }

    private void AttachColorPickerSelectionSources()
    {
        foreach (var source in _options.ColorPickerSelectionSources
                     .Distinct<IRecorderColorPickerSelectionSource>(ReferenceEqualityComparer.Instance))
        {
            ArgumentNullException.ThrowIfNull(source);
            source.SelectionConfirmed += OnColorPickerSelectionConfirmed;
            _detachActions.Add(() => source.SelectionConfirmed -= OnColorPickerSelectionConfirmed);
        }
    }

    private void OnColorPickerSelectionConfirmed(
        object? sender,
        RecorderColorPickerSelectionConfirmedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording)
        {
            return;
        }

        var capture = _stepFactory.TryCreateColorPickerStep(e.LogicalRoot, e.Color);
        if (!capture.IsConfigured || capture.Hint is null || !capture.StepResult.Success)
        {
            AddStep(capture.StepResult, e.LogicalRoot, "ColorPickerSelectionSource");
            return;
        }

        DiscardPendingColorPicker();
        AddStep(capture.StepResult, e.LogicalRoot, "ColorPickerSelectionSource");
    }

    private bool TryHandleSingleSelectButton(Control? source)
    {
        if (!_stepFactory.TryResolveSingleSelectButton(source, out var hint, out var isConfirm))
        {
            return false;
        }

        var pendingStep = _pendingSingleSelectStep;
        var pendingSource = _pendingSingleSelectSource;
        var hasPendingSelection = pendingStep is not null
            && pendingSource is not null
            && Equals(_pendingSingleSelectHint, hint);
        DiscardPendingSingleSelectText(hint);
        DiscardPendingSingleSelect();

        if (isConfirm && hasPendingSelection)
        {
            AddStep(pendingStep!, pendingSource, "SingleSelectSelection");
        }

        return true;
    }

    private void DiscardPendingSingleSelect()
    {
        _pendingSingleSelectStep = null;
        _pendingSingleSelectHint = null;
        _pendingSingleSelectSource = null;
    }

    private void DiscardPendingSingleSelectIfSwitchingTo(Control? source)
    {
        var hint = _pendingSingleSelectHint;
        if (hint is null || _stepFactory.IsSingleSelectPart(source, hint))
        {
            return;
        }

        DiscardPendingSingleSelectText(hint);
        DiscardPendingSingleSelect();
    }

    private void DiscardPendingSingleSelectText(RecorderSingleSelectHint hint)
    {
        if (_pendingTextBox is not null && _stepFactory.IsSingleSelectInput(_pendingTextBox, hint))
        {
            DiscardPendingText();
        }
    }

    private bool TryHandleColorPickerButton(Control? source)
    {
        if (!_stepFactory.TryResolveColorPickerButton(source, out var hint, out var isConfirm))
        {
            return false;
        }

        var pendingStep = _pendingColorPickerStep;
        var pendingSource = _pendingColorPickerSource;
        var hasPendingColor = pendingStep is not null
            && pendingSource is not null
            && Equals(_pendingColorPickerHint, hint);

        DiscardPendingColorPicker();
        if (isConfirm && hasPendingColor)
        {
            AddStep(pendingStep!, pendingSource!, "ColorPickerSelection");
        }

        return true;
    }

    private void DiscardPendingColorPicker()
    {
        _pendingColorPickerStep = null;
        _pendingColorPickerHint = null;
        _pendingColorPickerSource = null;
    }

    private void DiscardPendingColorPickerIfSwitchingTo(Control? source)
    {
        var hint = _pendingColorPickerHint;
        if (hint is null || _stepFactory.IsColorPickerPart(source, hint))
        {
            return;
        }

        DiscardPendingColorPicker();
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
        _pendingTextValue = null;
        if (ShouldSuppressTemplateTextEntry(textBox))
        {
            return;
        }

        AddStep(_stepFactory.TryCreateTextEntryStep(textBox), textBox, "TextEntry");
    }

    private bool TryRecordSearchHistoryAction(Control? source)
    {
        if (source is null || !_stepFactory.IsSearchHistoryAction(source))
        {
            return false;
        }

        if (_pendingTextBox is not null && _stepFactory.IsSearchHistoryPair(_pendingTextBox, source))
        {
            _textDebounceTimer.Stop();
            _pendingTextBox = null;
            _pendingTextValue = null;
        }

        AddStep(_stepFactory.TryCreateSearchHistoryStep(source), source, "SearchHistorySelection");
        return true;
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

    private void FlushPendingSpinner()
    {
        _spinnerDebounceTimer.Stop();
        if (_pendingSpinner is null)
        {
            return;
        }

        var spinner = _pendingSpinner;
        _pendingSpinner = null;
        AddStep(_stepFactory.TryCreateSpinnerStep(spinner), spinner, "SpinnerValue");
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

        if (control is not null && _stepFactory.IsCompositeSelectionPair(_pendingTextBox, control))
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

    private void FlushPendingSpinnerIfSwitchingTo(Control? control)
    {
        if (_pendingSpinner is null)
        {
            return;
        }

        if (control is not null && AreRelated(_pendingSpinner, control))
        {
            return;
        }

        FlushPendingSpinner();
    }

    private bool HasPendingCompositeSelection(Control results)
    {
        return _pendingTextBox is not null
            && _stepFactory.IsCompositeSelectionPair(_pendingTextBox, results);
    }

    private bool IsCompositeSelectedValue(TextBox searchInput, string text)
    {
        return _observedControlDetachers.Keys.Any(results =>
            (results is ComboBox or ListBox)
            && _stepFactory.IsCompositeSelectedValue(searchInput, results, text));
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
        var expander = ResolveHeaderExpander(control);
        if (expander is not null)
        {
            return expander;
        }

        var timePicker = FindAncestorOrSelf<TimePicker>(control);
        if (timePicker is not null)
        {
            return timePicker;
        }

        var spinner = FindAncestorOrSelf<NumericUpDown>(control);
        if (spinner is not null)
        {
            return spinner;
        }

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
                case NumericUpDown:
                case TimePicker:
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

    private static Control? FindContextMenuOwner(Control? control)
    {
        return EnumerateRelatedControls(control)
            .FirstOrDefault(static candidate =>
                candidate.ContextMenu is not null
                || candidate.ContextFlyout is MenuFlyout);
    }

    private void DiscardPendingContextMenuOwnerIfSwitchingTo(Control? source)
    {
        var contextMenuItem = FindAncestorOrSelf<MenuItem>(source);
        if (contextMenuItem is null
            || !_stepFactory.BelongsToContextMenuOwner(contextMenuItem, _pendingContextMenuOwner))
        {
            _pendingContextMenuOwner = null;
        }
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

    private sealed record ComboBoxFilterClickSnapshot(
        Control ActionSource,
        IReadOnlyList<string> SelectedValues,
        DateTimeOffset CapturedAt);

    private static bool IsPickerTemplateButton(Control? control)
    {
        var button = FindAncestorOrSelf<Button>(control);
        if (button is null || !IsKnownPickerTemplateButton(button))
        {
            return false;
        }

        foreach (var candidate in EnumerateRelatedControls(button))
        {
            if (ReferenceEquals(candidate, button))
            {
                continue;
            }

            if (candidate is DatePicker or Calendar or TimePicker)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExpanderHeaderToggle(Control? control)
    {
        return ResolveHeaderExpander(control) is not null;
    }

    private static Expander? ResolveHeaderExpander(Control? control)
    {
        if (control is Expander expander)
        {
            return expander;
        }

        var toggle = FindAncestorOrSelf<ToggleButton>(control);
        return toggle is StyledElement { TemplatedParent: Expander owner }
            ? owner
            : null;
    }

    private static bool IsKnownPickerTemplateButton(Button button)
    {
        return button.Name is "PART_FlyoutButton" or "PART_AcceptButton" or "PART_DismissButton";
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
                if (_activeOperationIsAutosave && _queuedManagedOperation is null)
                {
                    var queuedCompletion = new TaskCompletionSource<RecorderSaveResult>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _queuedManagedOperation = new QueuedManagedOperation(
                        operationName,
                        operation,
                        cancellationToken,
                        queuedCompletion);
                    _pendingAutosave = false;
                    SetStatus(
                        $"{operationName} queued until autosave completes.",
                        LatestValidationStatus);
                    return queuedCompletion.Task;
                }

                SetStatus(
                    $"{operationName} ignored while '{_busyDescription}' is in progress.",
                    RecorderValidationStatus.Warning);
                return Task.FromResult(RecorderSaveResult.Failed($"{_busyDescription} is already in progress."));
            }

            _busyDescription = $"{operationName}...";
            _activeOperationIsAutosave = false;
            SetStatus($"{operationName} in progress...", LatestValidationStatus);
            var operationCompletion = new TaskCompletionSource<RecorderSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeOperationTask = operationCompletion.Task;
            _ = ExecuteManagedOperationAsync(operationName, operation, cancellationToken, operationCompletion);
            return operationCompletion.Task;
        }
    }

    private void RequestAutosaveIfRecording()
    {
        if (_state != RecorderSessionState.Recording || _isCapturingPersistenceSnapshot)
        {
            return;
        }

        StartAutosaveOrQueue();
    }

    private void StartAutosaveOrQueue()
    {
        lock (_operationSync)
        {
            if (_activeOperationTask is not null)
            {
                _pendingAutosave = true;
                return;
            }

            _pendingAutosave = false;
            _busyDescription = "Autosave...";
            _activeOperationIsAutosave = true;
            SetStatus("Autosave in progress...", LatestValidationStatus);
            var operationCompletion = new TaskCompletionSource<RecorderSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeOperationTask = operationCompletion.Task;
            _ = ExecuteManagedOperationAsync(
                "Autosave",
                operationCancellationToken => AutosaveCoreAsync(outputDirectory: null, operationCancellationToken),
                CancellationToken.None,
                operationCompletion);
        }
    }

    private async Task ExecuteManagedOperationAsync(
        string operationName,
        Func<CancellationToken, Task<RecorderSaveResult>> operation,
        CancellationToken cancellationToken,
        TaskCompletionSource<RecorderSaveResult> completion)
    {
        var startPendingAutosave = false;
        QueuedManagedOperation? queuedManagedOperation = null;
        try
        {
            completion.TrySetResult(await operation(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            SetStatus($"{operationName} cancelled.", LatestValidationStatus);
            completion.TrySetResult(RecorderSaveResult.Failed($"{operationName} cancelled."));
        }
        catch (Exception ex)
        {
            var message = $"{operationName} failed: {ex.Message}";
            SetStatus(message, RecorderValidationStatus.Invalid);
            completion.TrySetResult(RecorderSaveResult.Failed(message, ex.ToString()));
        }
        finally
        {
            lock (_operationSync)
            {
                _activeOperationTask = null;
                _activeOperationIsAutosave = false;
                _busyDescription = string.Empty;
                if (_queuedManagedOperation is not null)
                {
                    queuedManagedOperation = _queuedManagedOperation;
                    _queuedManagedOperation = null;
                    _pendingAutosave = false;
                    _busyDescription = $"{queuedManagedOperation.OperationName}...";
                    _activeOperationTask = queuedManagedOperation.Completion.Task;
                }
                else if (_pendingAutosave)
                {
                    _pendingAutosave = false;
                    startPendingAutosave = _state == RecorderSessionState.Recording;
                }
            }

            NotifySessionChanged();
            if (queuedManagedOperation is not null)
            {
                SetStatus(
                    $"{queuedManagedOperation.OperationName} in progress...",
                    LatestValidationStatus);
                _ = ExecuteManagedOperationAsync(
                    queuedManagedOperation.OperationName,
                    queuedManagedOperation.Operation,
                    queuedManagedOperation.CancellationToken,
                    queuedManagedOperation.Completion);
            }
            else if (startPendingAutosave)
            {
                StartAutosaveOrQueue();
            }
        }
    }

    private async Task<RecorderSaveResult> SaveCoreAsync(string? outputDirectory, CancellationToken cancellationToken)
    {
        var stepsToPersist = CapturePersistenceSnapshot();
        var result = await _saveOperation(stepsToPersist, outputDirectory, cancellationToken);
        ApplySaveResult(result);
        if (result.Success)
        {
            lock (_operationSync)
            {
                _pendingAutosave = false;
            }
        }
        else
        {
            RequestAutosaveIfRecording();
        }

        return result;
    }

    private async Task<RecorderSaveResult> AutosaveCoreAsync(string? outputDirectory, CancellationToken cancellationToken)
    {
        var stepsToPersist = CapturePersistenceSnapshot();
        var result = await _autosaveOperation(stepsToPersist, outputDirectory, cancellationToken);
        ApplySaveResult(result);
        return result;
    }

    private RecordedStep[] CapturePersistenceSnapshot()
    {
        _isCapturingPersistenceSnapshot = true;
        try
        {
            FlushPendingState();
            return _steps.Where(static step => !step.IsIgnored).ToArray();
        }
        finally
        {
            _isCapturingPersistenceSnapshot = false;
        }
    }

    private RecordedStep RevalidateStep(RecordedStep step)
    {
        step = RestoreValidationBeforeGraphError(step);
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

        var validation = _selectorResolver.ResolveExisting(step);
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
            _codeGenerator.GeneratePreviewForStep(
                step,
                _steps.Where(static candidate => !candidate.IsIgnored).ToArray()),
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

    private string ResolveJournalStatusMessage(RecordedStep step)
    {
        if (step.IsIgnored)
        {
            return "Ignored for save/export.";
        }

        if (!string.IsNullOrWhiteSpace(step.ValidationMessage))
        {
            return step.ValidationMessage!;
        }

        if (step.ActionKind == RecordedActionKind.CaptureCheckpoint)
        {
            var checkpoint = step.CheckpointId is { } checkpointId
                ? CreateCheckpointOptions().FirstOrDefault(candidate => candidate.CheckpointId == checkpointId)
                : null;
            return $"Remember {step.Control.ProposedPropertyName}.{DescribeValueAccessor(step.ValueAccessorKind)} as "
                + (checkpoint?.VariableName ?? step.CheckpointVariableName ?? "checkpointValue");
        }

        if (step.ActionKind == RecordedActionKind.AssertValue)
        {
            var comparison = step.ComparisonKind switch
            {
                RecorderComparisonKind.Contains => "contains",
                RecorderComparisonKind.Equivalent => "has the same items as",
                _ => "equals"
            };
            var expected = step.ExpectedCheckpointId is { } checkpointId
                ? "checkpoint " + (CreateCheckpointOptions()
                    .FirstOrDefault(candidate => candidate.CheckpointId == checkpointId)?.VariableName
                    ?? checkpointId.ToString("N"))
                : "expected literal";
            return $"Assert {step.Control.ProposedPropertyName}.{DescribeValueAccessor(step.ValueAccessorKind)} {comparison} {expected}";
        }

        return step.ValidationStatus switch
        {
            RecorderValidationStatus.Warning => "Recorded with warning.",
            RecorderValidationStatus.Invalid => "Recorded for review only.",
            _ => "Ready to persist."
        };
    }

    private static string DescribeValueAccessor(RecorderValueAccessorKind? accessorKind) =>
        accessorKind switch
        {
            RecorderValueAccessorKind.SelectedItemText => "SelectedItemText",
            RecorderValueAccessorKind.SelectedItems => "SelectedItems",
            RecorderValueAccessorKind.NumericValue => "Value",
            RecorderValueAccessorKind.SelectedDate => "SelectedDate",
            RecorderValueAccessorKind.SelectedTime => "SelectedTime",
            RecorderValueAccessorKind.Color => "Color",
            RecorderValueAccessorKind.IsChecked => "IsChecked",
            RecorderValueAccessorKind.IsToggled => "IsToggled",
            RecorderValueAccessorKind.IsSelected => "IsSelected",
            RecorderValueAccessorKind.IsExpanded => "IsExpanded",
            RecorderValueAccessorKind.IsEnabled => "IsEnabled",
            RecorderValueAccessorKind.GridCellText => "CellText",
            _ => "Text"
        };

    private async Task DiscoverScenarioDestinationsAsync()
    {
        ScenarioDestinationDiscoveryResult result;
        try
        {
            result = await Task.Run(
                () => _authoringProjectScanner.DiscoverScenarioDestinations(
                    _options.AuthoringProjectDirectory,
                    _options.ScenarioSelection.ScenarioNamespaceRoot,
                    _options.ScenarioSelection.OutputSubdirectoryRoot));
        }
        catch (Exception ex)
        {
            result = ScenarioDestinationDiscoveryResult.Failed($"Scenario destination scan failed: {ex.Message}");
        }

        _scenarioDestinations = result.Destinations;
        _scenarioDiscoveryError = result.Error;

        if (result.Success
            && !string.IsNullOrWhiteSpace(_options.ScenarioNamespace)
            && !string.IsNullOrWhiteSpace(_options.ScenarioClassName))
        {
            _selectedScenarioDestination = result.Destinations.SingleOrDefault(destination =>
                string.Equals(destination.ScenarioNamespace, _options.ScenarioNamespace, StringComparison.Ordinal)
                && string.Equals(destination.ScenarioClassName, _options.ScenarioClassName, StringComparison.Ordinal));
            if (_selectedScenarioDestination is null)
            {
                _scenarioDiscoveryError =
                    $"Configured scenario destination '{_options.ScenarioNamespace}.{_options.ScenarioClassName}' was not found.";
            }
        }

        _isScanning = false;
        SetStatus(
            _scenarioDiscoveryError
                ?? $"Found {_scenarioDestinations.Count} scenario destination(s).",
            _scenarioDiscoveryError is null ? RecorderValidationStatus.Valid : RecorderValidationStatus.Invalid);
    }

    private RecorderScenarioSaveContext? CreateScenarioSaveContext()
    {
        if (!IsScenarioSelectionEnabled
            || _selectedScenarioDestination is null
            || _isScanning
            || _scenarioDiscoveryError is not null
            || ValidateScenarioName(_scenarioName) is not null)
        {
            return null;
        }

        return new RecorderScenarioSaveContext(
            _selectedScenarioDestination,
            _scenarioName.Trim(),
            _autosaveDraftIdentity);
    }

    private static string? ValidateScenarioName(string? scenarioName)
    {
        var value = scenarioName?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return "Scenario name is required.";
        }

        if (value.Any(static character => char.IsControl(character))
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar)
            || value.Contains("..", StringComparison.Ordinal))
        {
            return "Scenario name contains characters that cannot be used safely in a generated file.";
        }

        if (string.Equals(RecorderNaming.CreateFileSafeName(value, "scenario"), "scenario", StringComparison.Ordinal)
            && !string.Equals(value, "scenario", StringComparison.OrdinalIgnoreCase))
        {
            return "Scenario name cannot be converted to a generated method and file name.";
        }

        return null;
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

        if (_isDiagnosticLogFileEnabled)
        {
            parts.Add(_diagnosticLogEntryCount == 0
                ? "diagnostic log on"
                : $"{_diagnosticLogEntryCount} diagnostic log entries");
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
            : _codeGenerator.GeneratePreviewForStep(
                latestStep,
                _steps.Where(static step => !step.IsIgnored).ToArray());
        NotifySessionChanged();
    }

    private IReadOnlyList<RecorderCheckpointOption> CreateCheckpointOptions()
    {
        var reservedNames = new HashSet<string>(StringComparer.Ordinal);
        return _steps
            .Where(static step => !step.IsIgnored
                && step.CanPersist
                && step.ActionKind == RecordedActionKind.CaptureCheckpoint
                && step.CheckpointId is not null
                && step.ValueKind is not null)
            .Select(step => new RecorderCheckpointOption(
                step.CheckpointId!.Value,
                RecorderNaming.CreateCheckpointVariableName(step.CheckpointVariableName, reservedNames),
                step.ValueKind!.Value,
                step.Control.ProposedPropertyName))
            .ToArray();
    }

    private RecorderScenarioGraphValidationResult ApplyScenarioGraphValidation()
    {
        for (var index = 0; index < _steps.Count; index++)
        {
            var step = _steps[index];
            if (step.IsIgnored
                || !string.Equals(step.FailureCode, "checkpoint-graph-invalid", StringComparison.Ordinal))
            {
                continue;
            }

            _steps[index] = RestoreValidationBeforeGraphError(step);
        }

        var graphSteps = _steps
            .Where(static step => !step.IsIgnored && step.CanPersist)
            .ToArray();
        var graphValidation = RecorderScenarioGraphValidator.Validate(graphSteps);
        if (graphValidation.Success)
        {
            return graphValidation;
        }

        foreach (var entry in graphValidation.StepErrors)
        {
            var index = _steps.FindIndex(step => step.StepId == entry.Key);
            if (index < 0)
            {
                continue;
            }

            var step = _steps[index];
            var validationBeforeGraphError = step.ValidationBeforeGraphError
                ?? new RecorderStepValidationState(
                    step.ValidationStatus,
                    step.ValidationMessage,
                    step.CanPersist,
                    step.ReviewState,
                    step.FailureCode);
            _steps[index] = step with
            {
                ValidationStatus = RecorderValidationStatus.Invalid,
                ValidationMessage = entry.Value,
                CanPersist = false,
                ReviewState = RecorderStepReviewState.NeedsReview,
                FailureCode = "checkpoint-graph-invalid",
                ValidationBeforeGraphError = validationBeforeGraphError
            };
        }

        return graphValidation;
    }

    private static RecordedStep RestoreValidationBeforeGraphError(RecordedStep step)
    {
        if (step.ValidationBeforeGraphError is not { } validation)
        {
            return step;
        }

        return step with
        {
            ValidationStatus = validation.ValidationStatus,
            ValidationMessage = validation.ValidationMessage,
            CanPersist = validation.CanPersist,
            ReviewState = validation.ReviewState,
            FailureCode = validation.FailureCode,
            ValidationBeforeGraphError = null
        };
    }

    private void SetStatusAfterGraphValidation(
        RecorderScenarioGraphValidationResult graphValidation,
        string successMessage,
        RecorderValidationStatus successStatus)
    {
        SetStatus(
            graphValidation.Success
                ? successMessage
                : graphValidation.Error ?? "Checkpoint dependency graph is invalid.",
            graphValidation.Success ? successStatus : RecorderValidationStatus.Invalid);
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

    private sealed record QueuedManagedOperation(
        string OperationName,
        Func<CancellationToken, Task<RecorderSaveResult>> Operation,
        CancellationToken CancellationToken,
        TaskCompletionSource<RecorderSaveResult> Completion);

}
