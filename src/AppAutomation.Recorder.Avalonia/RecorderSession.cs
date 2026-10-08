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
    IRecorderGeneratedValueSessionDetails,
    IRecorderCopiedValueSessionDetails,
    IRecorderStepEditingSessionDetails,
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
    private readonly CancellationTokenSource _lifetimeCancellation = new();
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
    private SpatialCaptureTarget? _lastSpatialCaptureTarget;
    private RecorderTargetSelectionMode _targetSelectionMode;
    private Control? _pendingTargetSelectionControl;
    private IReadOnlyList<Control> _pendingTargetSelectionCandidates = Array.Empty<Control>();
    private Guid? _requestedGeneratedValueId;
    private TextBox? _generatedValueTextApplication;
    private TextBox? _completedGeneratedValueInput;
    private string? _completedGeneratedValueText;
    private RecordedValueSeries? _recordingGeneratedValueSeries;
    private int _lastGeneratedValueOrdinal;
    private RecorderCopiedValueOption? _activeCopiedValue;
    private TextBox? _pendingCopiedValuePasteTarget;
    private Guid? _pendingCopiedValuePasteId;
    private Control? _recentPointerControl;
    private DateTimeOffset _recentPointerAt;
    private int _pointerGestureSequence;
    private int? _activePointerGestureSequence;
    private PendingGridRowSelectionGesture? _pendingGridRowSelectionGesture;
    private Control? _recentKeyboardControl;
    private DateTimeOffset _recentKeyboardAt;
    private PendingCatalogGridEdit? _pendingCatalogGridEdit;
    private GridComboSelectionContextResolution? _pendingGridComboSelectionContext;
    private CompletedCompositeSelection? _completedCompositeSelection;
    private RoutedEventArgs? _lastMenuItemClickEvent;
    private ComboBoxFilterClickSnapshot? _comboBoxFilterClickSnapshot;
    private string _lastFingerprint = string.Empty;
    private DateTimeOffset _lastRecordedAt;
    private Task<RecorderSaveResult>? _activeOperationTask;
    private Task? _copiedValueCommitTask;
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
    private int _scenarioGraphRevision;
    private int _cachedJournalContextRevision = -1;
    private RecorderJournalContext? _cachedJournalContext;
    private string? _lastScenarioFilePath;
    private IReadOnlyList<RecordedScenarioDestination> _scenarioDestinations = Array.Empty<RecordedScenarioDestination>();
    private RecordedScenarioDestination? _selectedScenarioDestination;
    private string _scenarioName;
    private string? _scenarioDiscoveryError;
    private bool _isScanning;
    private bool _isRestoringAutosave;
    private string _autosaveDraftIdentity = Guid.NewGuid().ToString("N");
    private Task _scenarioDiscoveryTask = Task.CompletedTask;
    private volatile bool _isDisposed;

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
        _ = _options.FreezeGridHints();
        _validationRootProvider = validationRootProvider ?? (() => window.Content as Control);
        _logger = options.Logger ?? NullLogger.Instance;
        _hasConfiguredLogger = options.Logger is not null;
        string? hotkeySettingsLoadError = null;
        _hotkeySettings = initialHotkeySettings
            ?? LoadEffectiveHotkeySettings(options, hotkeySettingsStore ?? new RecorderHotkeySettingsStore(), out hotkeySettingsLoadError);
        _hotkeyMap = _hotkeySettings.ToMap();
        _stepFactory = new RecorderStepFactory(options, _validationRootProvider);
        _selectorResolver = new RecorderSelectorResolver(options, _validationRootProvider);
        _stepValidator = new RecorderStepValidator(options);
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

    public event EventHandler<RecorderCheckTargetSelectedEventArgs>? CheckTargetSelected;

    public event EventHandler<RecorderNumericOperandTargetSelectedEventArgs>? NumericOperandTargetSelected;

    public event EventHandler<RecorderGeneratedValueTargetSelectedEventArgs>? GeneratedValueTargetSelected;

    public event EventHandler<RecorderCopiedValueTargetSelectedEventArgs>? CopiedValueTargetSelected;

    internal event EventHandler? ExportRequested;

    internal event EventHandler? HotkeysChanged;

    public RecorderSessionState State => _state;

    public int StepCount => _steps.Count;

    public int PersistableStepCount => _steps.Count(static step => step.CanPersist && !step.IsIgnored);

    public IReadOnlyList<RecorderCheckpointOption> Checkpoints => CreateCheckpointOptions();

    public IReadOnlyList<RecorderGeneratedValueOption> GeneratedValues => CreateGeneratedValueOptions();

    public IReadOnlyList<RecorderCopiedValueOption> CopiedValues => CreateCopiedValueOptions();

    public bool IsCheckTargetSelectionActive => _targetSelectionMode == RecorderTargetSelectionMode.Check;

    public bool IsNumericOperandTargetSelectionActive => _targetSelectionMode == RecorderTargetSelectionMode.NumericOperand;

    public bool IsGeneratedValueTargetSelectionActive => _targetSelectionMode == RecorderTargetSelectionMode.GeneratedValue;

    public bool IsCopiedValueTargetSelectionActive => _targetSelectionMode == RecorderTargetSelectionMode.CopiedValue;

    public string LatestPreview { get; private set; } = string.Empty;

    public string LatestStatus { get; private set; } = string.Empty;

    public RecorderValidationStatus LatestValidationStatus { get; private set; } = RecorderValidationStatus.Valid;

    public bool IsBusy => _activeOperationTask is not null
        || _copiedValueCommitTask is not null
        || _isRestoringAutosave;

    public string BusyDescription => _busyDescription;

    public string SessionSummary => BuildSessionSummary();

    public bool IsDiagnosticLogFileEnabled => _isDiagnosticLogFileEnabled;

    public string DiagnosticLogFilePath => _diagnosticLogFilePath;

    public int DiagnosticLogEntryCount => _diagnosticLogEntryCount;

    public int WarningStepCount => _steps.Count(static step => !step.IsIgnored && step.ValidationStatus == RecorderValidationStatus.Warning);

    public int InvalidStepCount => _steps.Count(static step => !step.IsIgnored && (step.ValidationStatus == RecorderValidationStatus.Invalid || !step.CanPersist));

    public int IgnoredStepCount => _steps.Count(static step => step.IsIgnored);

    public IReadOnlyList<RecorderStepJournalEntry> StepJournal
    {
        get
        {
            var context = GetCurrentJournalContext();
            return _steps.Select(step => CreateJournalEntry(step, context)).ToArray();
        }
    }

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

    public bool CanRestoreAutosave => CanChangeScenarioTarget
        && ScenarioSelectionError is null
        && _selectedScenarioDestination is not null;

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
        _pendingCatalogGridEdit = null;
        _pendingGridComboSelectionContext = null;
        ResetPointerGesture();
        _completedCompositeSelection = null;
        _completedGeneratedValueInput = null;
        _completedGeneratedValueText = null;
        _lastSpatialCaptureTarget = null;
        SetStatus("Recording.", RecorderValidationStatus.Valid);
    }

    public void Stop()
    {
        if (_copiedValueCommitTask is not null)
        {
            SetStatus("Wait for the clipboard copy to finish before stopping recording.", RecorderValidationStatus.Warning);
            return;
        }

        CancelTargetSelectionCore();
        ClearPendingCopiedValuePaste();
        FlushPendingState();
        _pendingCatalogGridEdit = null;
        _pendingGridComboSelectionContext = null;
        ResetPointerGesture();
        _completedCompositeSelection = null;
        _completedGeneratedValueInput = null;
        _completedGeneratedValueText = null;
        _lastSpatialCaptureTarget = null;
        _state = RecorderSessionState.Off;
        SetStatus("Recording stopped.", RecorderValidationStatus.Valid);
    }

    public void Clear()
    {
        if (_copiedValueCommitTask is not null)
        {
            SetStatus("Wait for the clipboard copy to finish before clearing recorded steps.", RecorderValidationStatus.Warning);
            return;
        }

        if (IsScenarioSelectionEnabled && (_state != RecorderSessionState.Off || IsBusy))
        {
            SetStatus(
                "Clear is available only while recording is stopped and no save operation is running.",
                RecorderValidationStatus.Warning);
            return;
        }

        CancelTargetSelectionCore();
        ClearPendingCopiedValuePaste();
        FlushPendingState();
        _pendingCatalogGridEdit = null;
        _pendingGridComboSelectionContext = null;
        ResetPointerGesture();
        _completedCompositeSelection = null;
        _completedGeneratedValueInput = null;
        _completedGeneratedValueText = null;
        _steps.Clear();
        InvalidateScenarioGraphValidation();
        _activeCopiedValue = null;
        _recordingGeneratedValueSeries = null;
        _lastGeneratedValueOrdinal = 0;
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
        lock (_operationSync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _pendingAutosave = false;
        }

        _lifetimeCancellation.Cancel();
        CancelTargetSelectionCore();
        ClearPendingCopiedValuePaste();
        _activeCopiedValue = null;
        _completedGeneratedValueInput = null;
        _completedGeneratedValueText = null;
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
            ResolveJournalStatusMessage(revalidatedStep, GetCurrentJournalContext()),
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

    public async Task<bool> RestoreAutosaveAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRestoreAutosave)
        {
            SetStatus(
                ScenarioSelectionError
                    ?? "Stop recording and clear recorded steps before restoring an autosave.",
                RecorderValidationStatus.Warning);
            return false;
        }

        var saveContext = CreateScenarioSaveContext();
        if (saveContext is null)
        {
            SetStatus("Select a valid scenario destination before restoring an autosave.", RecorderValidationStatus.Warning);
            return false;
        }

        _isRestoringAutosave = true;
        _busyDescription = "Restoring autosave...";
        SetStatus("Restoring autosave...", RecorderValidationStatus.Valid);
        RecorderAutosaveRestoreResult result;
        try
        {
            result = await _codeGenerator.RestoreAutosaveAsync(
                _window,
                _options,
                outputDirectoryOverride: null,
                saveContext,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Autosave recovery was cancelled.", RecorderValidationStatus.Warning);
            throw;
        }
        finally
        {
            _isRestoringAutosave = false;
            _busyDescription = string.Empty;
        }

        if (!result.Success)
        {
            SetStatus(
                result.Message,
                result.Found ? RecorderValidationStatus.Invalid : RecorderValidationStatus.Warning);
            return false;
        }

        _steps.Clear();
        _steps.AddRange(result.Steps);
        _recordingGeneratedValueSeries = null;
        _lastGeneratedValueOrdinal = _steps
            .Where(static step => step.DefinesGeneratedValue)
            .Select(static step => step.GeneratedValueOrdinal ?? 0)
            .DefaultIfEmpty()
            .Max();
        if (!string.IsNullOrWhiteSpace(result.DraftIdentity))
        {
            _autosaveDraftIdentity = result.DraftIdentity;
        }

        var graphValidation = ApplyScenarioGraphValidation();
        UpdateLatestPreviewFromSteps();
        SetStatusAfterGraphValidation(
            graphValidation,
            result.Message,
            RecorderValidationStatus.Valid);
        return graphValidation.Success;
    }

    public bool TryCreateStepEditDraft(
        Guid stepId,
        out RecorderStepEditDraft? draft,
        out string? error)
    {
        var index = _steps.FindIndex(candidate => candidate.StepId == stepId);
        if (index < 0)
        {
            draft = null;
            error = "The recorded step no longer exists.";
            return false;
        }

        var step = _steps[index];
        if (step.IsIgnored)
        {
            draft = null;
            error = "Restore the recorded step before editing it.";
            return false;
        }

        var context = GetCurrentJournalContext();
        var precedingSteps = _steps
            .Take(index)
            .Where(static candidate => !candidate.IsIgnored && candidate.CanPersist)
            .ToArray();
        var precedingCheckpointIds = precedingSteps
            .Where(static candidate => candidate.CheckpointId.HasValue)
            .Select(static candidate => candidate.CheckpointId!.Value)
            .ToHashSet();
        var precedingGeneratedValueIds = precedingSteps
            .Where(static candidate => candidate.GeneratedValueId.HasValue)
            .Select(static candidate => candidate.GeneratedValueId!.Value)
            .ToHashSet();
        var precedingCopiedValueIds = precedingSteps
            .Where(static candidate => candidate.CopiedValueId.HasValue)
            .Select(static candidate => candidate.CopiedValueId!.Value)
            .ToHashSet();
        var generatedPreview = _codeGenerator.GeneratePreviewForStep(step, context.PreviewSteps);
        if (!RecorderStepEditService.TryCreateDraft(
                step,
                _scenarioGraphRevision,
                DescribeRecordedValue(step),
                generatedPreview,
                ResolveEditableVariableName(step, context),
                context.Checkpoints
                    .Where(option => precedingCheckpointIds.Contains(option.CheckpointId))
                    .ToArray(),
                context.GeneratedValues
                    .Where(option => precedingGeneratedValueIds.Contains(option.GeneratedValueId))
                    .ToArray(),
                context.CopiedValues
                    .Where(option => precedingCopiedValueIds.Contains(option.CopiedValueId))
                    .ToArray(),
                out draft))
        {
            error = "This recorded step does not contain editable data.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string? ResolveEditableVariableName(
        RecordedStep step,
        RecorderJournalContext context)
    {
        if (step.ActionKind == RecordedActionKind.CaptureCheckpoint
            && step.CheckpointId is { } checkpointId)
        {
            return context.CheckpointsById.TryGetValue(checkpointId, out var checkpoint)
                ? checkpoint.VariableName
                : step.CheckpointVariableName;
        }

        if (step.ActionKind == RecordedActionKind.CaptureCopiedValue
            && step.CopiedValueId is { } copiedValueId)
        {
            return context.CopiedValuesById.TryGetValue(copiedValueId, out var copiedValue)
                ? copiedValue.VariableName
                : step.CopiedValueVariableName;
        }

        if (step.DefinesGeneratedValue
            && step.GeneratedValueId is { } generatedValueId)
        {
            return context.GeneratedValuesById.TryGetValue(generatedValueId, out var generatedValue)
                ? generatedValue.VariableName
                : step.GeneratedValueVariableName;
        }

        return null;
    }

    private static string DescribeRecordedValue(RecordedStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.NumericInputText))
        {
            return step.NumericInputText!;
        }

        if (step.StringValues is not null)
        {
            return string.Join(", ", step.StringValues);
        }

        if (step.StringValue is not null)
        {
            return step.StringValue;
        }

        if (step.ItemValue is not null)
        {
            return step.ItemValue;
        }

        if (step.BoolValue.HasValue)
        {
            return step.BoolValue.Value ? "true" : "false";
        }

        if (step.DoubleValue.HasValue)
        {
            return step.DoubleValue.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (step.DateValue.HasValue)
        {
            return step.DateValue.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        return step.TimeValue?.ToString("c", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public RecorderStepEditResult ApplyStepEdit(RecorderStepEditDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (IsBusy)
        {
            return RecorderStepEditResult.Rejected(
                string.IsNullOrWhiteSpace(BusyDescription)
                    ? "Wait for the current recorder operation to finish."
                    : $"Wait for '{BusyDescription}' to finish.",
                draft);
        }

        if (!TryPrepareStepEdit(
                draft,
                out var index,
                out var candidate,
                out _,
                out var error))
        {
            return RecorderStepEditResult.Rejected(error, draft);
        }

        _steps[index] = candidate;
        var appliedGraphValidation = ApplyScenarioGraphValidation();
        UpdateLatestPreviewFromSteps(notify: false);
        LogRecordedStepDiagnostics("EditStep", null, _steps[index]);
        SetStatusAfterGraphValidation(
            appliedGraphValidation,
            "Recorded step updated.",
            _steps[index].ValidationStatus);
        RequestAutosaveIfRecording();
        return RecorderStepEditResult.Applied("Recorded step updated.");
    }

    public RecorderStepEditPreviewResult PreviewStepEdit(RecorderStepEditDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!TryPrepareStepEdit(
                draft,
                out _,
                out var candidate,
                out var graphSteps,
                out var error))
        {
            return new RecorderStepEditPreviewResult(false, draft.GeneratedPreview, error);
        }

        return new RecorderStepEditPreviewResult(
            true,
            _codeGenerator.GeneratePreviewForStep(candidate, graphSteps),
            string.Empty);
    }

    private bool TryPrepareStepEdit(
        RecorderStepEditDraft draft,
        out int index,
        out RecordedStep candidate,
        out RecordedStep[] graphSteps,
        out string error)
    {
        index = _steps.FindIndex(step => step.StepId == draft.StepId);
        if (index < 0)
        {
            candidate = null!;
            graphSteps = [];
            error = "The recorded step no longer exists.";
            return false;
        }

        var source = RestoreValidationBeforeGraphError(_steps[index]);
        if (source.IsIgnored)
        {
            candidate = null!;
            graphSteps = [];
            error = "Restore the recorded step before editing it.";
            return false;
        }

        if (draft.Revision != _scenarioGraphRevision)
        {
            candidate = null!;
            graphSteps = [];
            error = "The scenario changed after the editor was opened. Reopen the step and apply the edit again.";
            return false;
        }

        if (!RecorderStepEditService.TryCreateCandidate(source, draft, out var preparedCandidate, out error)
            || preparedCandidate is null)
        {
            candidate = null!;
            graphSteps = [];
            return false;
        }

        if (!TryValidateEditedVariableName(preparedCandidate, out error))
        {
            candidate = null!;
            graphSteps = [];
            return false;
        }

        candidate = preparedCandidate with
        {
            PreserveVariableName = DefinesEditableVariable(preparedCandidate)
        };
        var payloadValidation = _runtimeValidator.ValidatePayload(candidate with
        {
            ValidationStatus = RecorderValidationStatus.Valid,
            ValidationMessage = null,
            CanPersist = true,
            RuntimeValidationFindings = Array.Empty<RecorderRuntimeValidationFinding>()
        });
        if (payloadValidation.ValidationStatus == RecorderValidationStatus.Invalid
            || !payloadValidation.CanPersist)
        {
            graphSteps = [];
            error = payloadValidation.ValidationMessage
                ?? "The edited step is not valid and cannot be saved.";
            return false;
        }

        candidate = RestoreRepairedPayloadValidation(source, candidate, payloadValidation);
        if (candidate.ValidationStatus == RecorderValidationStatus.Invalid || !candidate.CanPersist)
        {
            graphSteps = [];
            error = candidate.ValidationMessage
                ?? "The edited step still has a validation error and cannot be saved.";
            return false;
        }

        var candidateSteps = _steps.Select(RestoreValidationBeforeGraphError).ToArray();
        candidateSteps[index] = candidate;
        graphSteps = candidateSteps
            .Where(static step => !step.IsIgnored && step.CanPersist)
            .ToArray();
        var graphValidation = RecorderScenarioGraphValidator.Validate(graphSteps);
        if (!graphValidation.Success)
        {
            error = graphValidation.StepErrors.TryGetValue(candidate.StepId, out var stepError)
                ? stepError
                : graphValidation.Error ?? "The edited scenario dependency graph is invalid.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private RecordedStep RestoreRepairedPayloadValidation(
        RecordedStep source,
        RecordedStep candidate,
        RecordedStep payloadValidation)
    {
        if (source.ValidationStatus != RecorderValidationStatus.Invalid || source.CanPersist)
        {
            return candidate;
        }

        var previousPayloadValidation = _runtimeValidator.ValidatePayload(source with
        {
            ValidationStatus = RecorderValidationStatus.Valid,
            ValidationMessage = null,
            CanPersist = true,
            RuntimeValidationFindings = Array.Empty<RecorderRuntimeValidationFinding>()
        });
        var payloadFindings = previousPayloadValidation.RuntimeValidationFindings;
        var sourceFindings = source.RuntimeValidationFindings ?? [];
        // Older autosaves can retain the payload diagnostic but lose its structured findings.
        var legacyPayloadOnly = sourceFindings.Count == 0
            && previousPayloadValidation.ValidationStatus == RecorderValidationStatus.Invalid
            && string.Equals(
                source.ValidationMessage,
                previousPayloadValidation.ValidationMessage,
                StringComparison.Ordinal);
        if (previousPayloadValidation.ValidationStatus != RecorderValidationStatus.Invalid
            || (!legacyPayloadOnly && !sourceFindings.Any(finding => payloadFindings.Any(payload =>
                IsSameRuntimeValidationIssue(finding, payload)))))
        {
            return candidate;
        }

        var remainingFindings = sourceFindings
            .Where(finding => !payloadFindings.Any(payload =>
                IsSameRuntimeValidationIssue(finding, payload)))
            .ToArray();
        var hasBlockingFinding = remainingFindings.Any(static finding => finding.BlocksTarget);
        var hasWarning = remainingFindings.Any(static finding => finding.ShouldSurface)
            || !string.IsNullOrWhiteSpace(candidate.Warning);
        var validationStatus = hasBlockingFinding
            ? RecorderValidationStatus.Invalid
            : hasWarning
                ? RecorderValidationStatus.Warning
                : RecorderValidationStatus.Valid;
        var runtimeMessage = RecorderCommandRuntimeValidator.BuildRuntimeValidationMessage(remainingFindings);
        var validationMessage = string.Join(
            " ",
            new[] { candidate.Warning, runtimeMessage }
                .Where(static message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal));
        return payloadValidation with
        {
            ValidationStatus = validationStatus,
            ValidationMessage = string.IsNullOrWhiteSpace(validationMessage) ? null : validationMessage,
            CanPersist = !hasBlockingFinding,
            ReviewState = hasBlockingFinding
                ? RecorderStepReviewState.NeedsReview
                : RecorderStepReviewState.Active,
            FailureCode = hasBlockingFinding
                ? "validation-invalid"
                : hasWarning
                    ? "validation-warning"
                    : null,
            LastValidationAt = DateTimeOffset.UtcNow,
            RuntimeValidationFindings = remainingFindings
        };
    }

    private static bool IsSameRuntimeValidationIssue(
        RecorderRuntimeValidationFinding candidate,
        RecorderRuntimeValidationFinding expected)
    {
        static string? WithoutTargetPrefix(RecorderRuntimeValidationFinding finding)
        {
            var prefix = finding.Target switch
            {
                RecorderRuntimeValidationTarget.Headless => "headless-",
                RecorderRuntimeValidationTarget.FlaUI => "flaui-",
                _ => string.Empty
            };
            return prefix.Length > 0
                && finding.Code.StartsWith(prefix, StringComparison.Ordinal)
                    ? finding.Code[prefix.Length..]
                    : null;
        }

        var candidateCode = WithoutTargetPrefix(candidate);
        var expectedCode = WithoutTargetPrefix(expected);
        return candidate.Severity == expected.Severity
            && candidate.BlocksTarget == expected.BlocksTarget
            && candidateCode is not null
            && expectedCode is not null
            && string.Equals(
                candidateCode,
                expectedCode,
                StringComparison.Ordinal);
    }

    private bool TryValidateEditedVariableName(RecordedStep candidate, out string error)
    {
        var variableName = candidate.ActionKind == RecordedActionKind.CaptureCheckpoint
            ? candidate.CheckpointVariableName
            : candidate.ActionKind == RecordedActionKind.CaptureCopiedValue
                ? candidate.CopiedValueVariableName
                : candidate.DefinesGeneratedValue
                    ? candidate.GeneratedValueVariableName
                    : null;
        if (variableName is null)
        {
            error = string.Empty;
            return true;
        }

        if (!RecorderNaming.TryValidateExactVariableName(variableName, out error))
        {
            return false;
        }

        var context = GetCurrentJournalContext();
        var reservedNames = context.Checkpoints
            .Where(option => option.CheckpointId != candidate.CheckpointId)
            .Select(static option => option.VariableName)
            .Concat(context.GeneratedValues
                .Where(option => option.GeneratedValueId != candidate.GeneratedValueId)
                .Select(static option => option.VariableName))
            .Concat(context.CopiedValues
                .Where(option => option.CopiedValueId != candidate.CopiedValueId)
                .Select(static option => option.VariableName))
            .ToHashSet(StringComparer.Ordinal);
        if (context.GraphValidation.GeneratedValueSeriesVariable is { } generatedSeries)
        {
            reservedNames.Add(generatedSeries);
        }

        if (reservedNames.Contains(variableName.Trim()))
        {
            error = $"Variable name '{variableName.Trim()}' is already used by another recorded value.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool DefinesEditableVariable(RecordedStep step) =>
        step.ActionKind is RecordedActionKind.CaptureCheckpoint or RecordedActionKind.CaptureCopiedValue
        || step.DefinesGeneratedValue;

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

    internal void CancelPendingGridCellEditForTesting()
    {
        FlushPendingText();
        FlushPendingSpinner();
        CancelPendingCatalogGridEdit();
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
        InvalidateScenarioGraphValidation();
        if (updatedStep.DefinesGeneratedValue && updatedStep.GeneratedValueOrdinal is { } ordinal)
        {
            _lastGeneratedValueOrdinal = Math.Max(_lastGeneratedValueOrdinal, ordinal);
        }
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

        if (_stepFactory.ShouldSuppressGridComboSelectionButton(source))
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
        CaptureAssertion(ResolveCheckTargetSelection(source, [source]), mode);
    }

    internal void CaptureAssertionForTesting(
        Control source,
        IReadOnlyList<Control> visualCandidates,
        RecorderAssertionMode mode)
    {
        CaptureAssertion(ResolveCheckTargetSelection(source, visualCandidates), mode);
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

    internal void CaptureCatalogGridRowGestureForTesting(Control? source, int clickCount = 1)
    {
        TryRecordCatalogGridRowGesture(source, clickCount);
    }

    internal void BeginPointerGestureForTesting()
    {
        BeginPointerGesture();
    }

    internal void EndPointerGestureForTesting()
    {
        ResetPointerGesture();
    }

    internal void SetLastHoveredControlForTesting(Control? source)
    {
        _lastHoveredControl = source;
    }

    internal void SetLastSpatialCaptureTargetForTesting(
        Control? eventTarget,
        Control positionRoot,
        Point position)
    {
        ArgumentNullException.ThrowIfNull(positionRoot);
        _lastSpatialCaptureTarget = new SpatialCaptureTarget(eventTarget, positionRoot, position);
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
        _inputRoot.AddHandler(
            InputElement.PointerReleasedEvent,
            OnPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _inputRoot.AddHandler(
            InputElement.PointerReleasedEvent,
            OnPointerGestureCompleted,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        _inputRoot.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        _inputRoot.AddHandler(InputElement.PointerExitedEvent, OnPointerExited, RoutingStrategies.Tunnel);
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
        _inputRoot.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        _inputRoot.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerGestureCompleted);
        _inputRoot.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        _inputRoot.RemoveHandler(InputElement.PointerExitedEvent, OnPointerExited);
        _inputRoot.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
        _inputRoot.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _inputRoot.RemoveHandler(Button.ClickEvent, OnButtonClick);
        _inputRoot = null;
        _lastSpatialCaptureTarget = null;
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
                textBox.PastingFromClipboard += OnTextBoxPastingFromClipboard;
                return () =>
                {
                    textBox.PropertyChanged -= OnTextBoxPropertyChanged;
                    textBox.LostFocus -= OnTextBoxLostFocus;
                    textBox.PastingFromClipboard -= OnTextBoxPastingFromClipboard;
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

        BeginPointerGesture();
        var source = e.Source as Control;
        RememberSpatialCaptureTarget(source, e);
        if (_targetSelectionMode != RecorderTargetSelectionMode.None)
        {
            var positionRoot = _inputRoot ?? _window;
            _pendingTargetSelectionCandidates = ResolveCheckTargetCandidates(
                source,
                positionRoot,
                e.GetPosition(positionRoot),
                preserveReadOnlyText: _targetSelectionMode == RecorderTargetSelectionMode.Check);
            _pendingTargetSelectionControl = _pendingTargetSelectionCandidates.Count > 0
                ? _pendingTargetSelectionCandidates[0]
                : _targetSelectionMode == RecorderTargetSelectionMode.Check && source is not null
                    ? ResolveCheckTargetOwner(source)
                    : ResolveInteractionOwner(source) ?? source;
            e.Handled = true;
            return;
        }

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
        CommitPendingCatalogGridEditIfSwitchingTo(control);
        RegisterPointerInput(control);

        if (FindAncestorOrSelf<Button>(source) is null)
        {
            var gestureSource = source ?? control;
            if (!TryRecordCatalogGridRowGesture(gestureSource, e.ClickCount))
            {
                TryRecordGridAction(gestureSource);
            }
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_targetSelectionMode == RecorderTargetSelectionMode.None
            || _pendingTargetSelectionControl is null)
        {
            return;
        }

        var mode = _targetSelectionMode;
        var target = _pendingTargetSelectionControl;
        var candidates = _pendingTargetSelectionCandidates;
        e.Handled = true;
        switch (mode)
        {
            case RecorderTargetSelectionMode.Check:
                CompleteCheckTargetSelection(target, candidates);
                break;
            case RecorderTargetSelectionMode.NumericOperand:
                CompleteNumericOperandTargetSelection(target, candidates);
                break;
            case RecorderTargetSelectionMode.GeneratedValue:
                CompleteGeneratedValueTargetSelection(target, candidates);
                break;
            case RecorderTargetSelectionMode.CopiedValue:
                CompleteCopiedValueTargetSelection(target, candidates);
                break;
        }
    }

    private void OnPointerGestureCompleted(object? sender, PointerReleasedEventArgs e)
    {
        ResetPointerGesture();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var source = e.Source as Control;
        _lastHoveredControl = ResolveInteractionOwner(source);
        RememberSpatialCaptureTarget(source, e);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _lastSpatialCaptureTarget = null;
        _lastHoveredControl = null;
    }

    private void RememberSpatialCaptureTarget(Control? eventTarget, PointerEventArgs e)
    {
        var positionRoot = _inputRoot ?? _validationRootProvider() ?? _window;
        var position = e.GetPosition(positionRoot);
        if (position.X < 0
            || position.Y < 0
            || position.X > positionRoot.Bounds.Width
            || position.Y > positionRoot.Bounds.Height)
        {
            _lastSpatialCaptureTarget = null;
            return;
        }

        _lastSpatialCaptureTarget = new SpatialCaptureTarget(eventTarget, positionRoot, position);
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

        if (ReferenceEquals(textBox, _pendingCopiedValuePasteTarget))
        {
            ClearPendingCopiedValuePaste();
        }

        if (ReferenceEquals(textBox, _completedGeneratedValueInput))
        {
            _completedGeneratedValueInput = null;
            _completedGeneratedValueText = null;
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
        if (_targetSelectionMode != RecorderTargetSelectionMode.None
            && e.Key == Key.Escape)
        {
            CancelTargetSelectionCore();
            e.Handled = true;
            return;
        }

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
                FlushPendingText();
                FlushPendingSpinner();
                if (CancelPendingCatalogGridEdit())
                {
                    return;
                }
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
                CommitPendingCatalogGridEdit();
            }
            else if (e.Key is Key.Enter)
            {
                FlushPendingSpinner();
                CommitPendingCatalogGridEdit();
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

        if (_stepFactory.ShouldSuppressGridComboSelectionButton(eventSource))
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
        if (control is CheckBox && TryRecordCatalogGridCellEdit(control, "GridCheckBoxEdit"))
        {
            return;
        }

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
        if (_state != RecorderSessionState.Recording)
        {
            return;
        }

        if (ShouldSuppressCompletedCompositeEvent(comboBox))
        {
            return;
        }

        if (!WasRecentlyTriggeredByUser(comboBox) && !HasPendingCompositeSelection(comboBox))
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

        if (TryRecordSearchPickerSelection(comboBox))
        {
            return;
        }

        if (_stepFactory.ShouldSuppressCompositeSelection(comboBox))
        {
            return;
        }

        if (TryRecordGridComboSelection(comboBox))
        {
            return;
        }

        if (TryRecordSingleSelectSelection(comboBox))
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

    private bool TryRecordGridComboSelection(ComboBox comboBox)
    {
        return CompleteGridComboSelection(
            _stepFactory.TryCreateGridComboSelectionStep(
                comboBox,
                _pendingGridComboSelectionContext,
                _pendingTextValue),
            comboBox);
    }

    private bool TryRecordGridComboSelection(ListBox listBox)
    {
        return CompleteGridComboSelection(
            _stepFactory.TryCreateGridComboSelectionStep(
                listBox,
                _pendingGridComboSelectionContext,
                _pendingTextValue),
            listBox);
    }

    private bool CompleteGridComboSelection(GridComboSelectionCaptureResult capture, Control source)
    {
        if (!capture.IsConfigured)
        {
            return false;
        }

        if (!capture.HasSelection)
        {
            _pendingGridComboSelectionContext = null;
            return true;
        }

        FlushPendingTextIfSwitchingTo(source);
        FlushPendingSliderIfSwitchingTo(source);
        FlushPendingSpinnerIfSwitchingTo(source);
        var previousStepCount = _steps.Count;
        AddStep(capture.StepResult, source, "GridComboSelection");
        if (capture.StepResult.Success
            && _steps.Count > previousStepCount
            && _steps[^1].ActionKind is RecordedActionKind.SelectGridCellComboItem
                or RecordedActionKind.SearchAndSelectGridCell
            && _steps[^1].CanPersist)
        {
            _completedCompositeSelection = new CompletedCompositeSelection([source]);
        }

        _pendingGridComboSelectionContext = null;
        return true;
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
        var previousStepCount = _steps.Count;
        AddStep(result, results, "SearchPickerSelection");
        if (searchInput is not null
            && result.Step?.ActionKind == RecordedActionKind.SearchAndSelectGridCell
            && _steps.Count > previousStepCount
            && _steps[^1].ActionKind == RecordedActionKind.SearchAndSelectGridCell
            && _steps[^1].CanPersist)
        {
            _completedCompositeSelection = new CompletedCompositeSelection([searchInput, results]);
        }
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
        if (_state != RecorderSessionState.Recording)
        {
            return;
        }

        if (ShouldSuppressCompletedCompositeEvent(listBox))
        {
            return;
        }

        if (!WasRecentlyTriggeredByUser(listBox) && !HasPendingCompositeSelection(listBox))
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

        if (TryRecordSearchPickerSelection(listBox))
        {
            return;
        }

        if (_stepFactory.ShouldSuppressCompositeSelection(listBox))
        {
            return;
        }

        if (TryRecordGridComboSelection(listBox))
        {
            return;
        }

        if (TryRecordSingleSelectSelection(listBox))
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

        var dialogCapture = _stepFactory.TryCreateDialogActionStep(source);
        if (dialogCapture.IsConfigured)
        {
            AddStep(dialogCapture.Result, source, "DialogAction");
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
        if (TryRecordCatalogGridCellEdit(spinner, "GridSpinnerEdit", deferUntilCommit: true))
        {
            _pendingSpinner = null;
            _spinnerDebounceTimer.Stop();
            return;
        }

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

        if (TryRecordCatalogGridCellEdit(timePicker, "GridTimeEdit"))
        {
            return;
        }

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
            if (TryRecordCatalogGridCellEdit(datePicker, "GridDateEdit"))
            {
                return;
            }

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
            if (TryRecordCatalogGridCellEdit(calendar, "GridCalendarEdit"))
            {
                return;
            }

            var selectedDate = e.GetNewValue<DateTime?>();
            var result = selectedDate is { } value
                ? _stepFactory.TryCreateCalendarStep(calendar, value)
                : StepCreationResult.Unsupported("Calendar does not have a selected date.");
            AddStep(result, calendar, "CalendarSelection");
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

        if (ReferenceEquals(sender, _generatedValueTextApplication))
        {
            return;
        }

        if (ReferenceEquals(textBox, _completedGeneratedValueInput))
        {
            if (string.Equals(textBox.Text, _completedGeneratedValueText, StringComparison.Ordinal))
            {
                return;
            }

            _completedGeneratedValueInput = null;
            _completedGeneratedValueText = null;
        }

        if (ShouldSuppressCompletedCompositeEvent(textBox))
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

    private void OnTextBoxPastingFromClipboard(object? sender, RoutedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording
            || sender is not TextBox textBox
            || _activeCopiedValue is null
            || ShouldSuppressTemplateTextEntry(textBox))
        {
            return;
        }

        _pendingCopiedValuePasteTarget = textBox;
        _pendingCopiedValuePasteId = _activeCopiedValue.CopiedValueId;
    }

    internal void RegisterCopiedValuePasteForTesting(TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        OnTextBoxPastingFromClipboard(textBox, new RoutedEventArgs(TextBox.PastingFromClipboardEvent));
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
            CommitPendingCatalogGridEdit();
            return;
        }

        if (sender is TextBox lostFocusTextBox
            && _pendingCatalogGridEdit is { } pending
            && AreRelated(pending.Source, lostFocusTextBox))
        {
            CommitPendingCatalogGridEdit();
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

        return IsInsideConfiguredGrid(textBox)
            && (!_stepFactory.IsCatalogGridCell(textBox)
                || _stepFactory.ShouldSuppressCatalogGridTextEntry(textBox));
    }

    private bool IsInsideConfiguredGrid(Control source)
    {
        if (!_options.EnumerateGridHints().Any())
        {
            return false;
        }

        foreach (var current in EnumerateRelatedControls(source))
        {
            foreach (var hint in _options.EnumerateGridHints())
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
        CaptureAssertion(PrepareSemanticCaptureSelection(), mode);
    }

    private void CaptureAssertion(
        RecorderCheckTargetSelection? selection,
        RecorderAssertionMode mode)
    {
        if (selection is null)
        {
            AddStep(
                StepCreationResult.Unsupported("No control is available for assertion capture."),
                captureAction: $"Assertion:{mode}");
            return;
        }

        if (!CanCaptureCheckSelection(selection))
        {
            return;
        }

        var result = mode is RecorderAssertionMode.Auto or RecorderAssertionMode.Text
            ? _stepFactory.TryCreateAssertionStep(selection.ValueSnapshot, mode)
            : mode == RecorderAssertionMode.Enabled
                ? _stepFactory.TryCreateEnabledAssertionStep(
                    selection.Target,
                    selection.ValueSnapshot,
                    selection.IsEnabled)
                : _stepFactory.TryCreateAssertionStep(selection.Target, mode);
        AddStep(result, selection.Target, $"Assertion:{mode}");
    }

    private bool TryDescribeCurrentValue(
        out RecorderSemanticValueDescription? description,
        out string? error)
    {
        var selection = PrepareSemanticCaptureSelection();
        if (selection is not null && !selection.CanCaptureAssertions)
        {
            description = null;
            error = selection.ValueDescriptionError;
            return false;
        }

        description = selection?.ValueDescription;
        error = selection?.ValueDescriptionError;
        return description is not null && string.IsNullOrWhiteSpace(error);
    }

    public void BeginCheckTargetSelection()
    {
        BeginTargetSelection(RecorderTargetSelectionMode.Check);
    }

    public void CancelCheckTargetSelection()
    {
        CancelTargetSelectionCore(RecorderTargetSelectionMode.Check);
    }

    public void BeginNumericOperandTargetSelection()
    {
        BeginTargetSelection(RecorderTargetSelectionMode.NumericOperand);
    }

    public void CancelNumericOperandTargetSelection()
    {
        CancelTargetSelectionCore(RecorderTargetSelectionMode.NumericOperand);
    }

    internal bool SelectNumericOperandTargetForTesting(Control source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!IsNumericOperandTargetSelectionActive)
        {
            return false;
        }

        CompleteNumericOperandTargetSelection(ResolveInteractionOwner(source) ?? source, [source]);
        return true;
    }

    internal bool SelectNumericOperandTargetForTesting(
        Control eventSource,
        IReadOnlyList<Control> visualCandidates)
    {
        ArgumentNullException.ThrowIfNull(eventSource);
        ArgumentNullException.ThrowIfNull(visualCandidates);
        if (!IsNumericOperandTargetSelectionActive)
        {
            return false;
        }

        CompleteNumericOperandTargetSelection(
            ResolveInteractionOwner(eventSource) ?? eventSource,
            visualCandidates);
        return true;
    }

    internal bool SelectCheckTargetForTesting(Control source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!IsCheckTargetSelectionActive)
        {
            return false;
        }

        CompleteCheckTargetSelection(ResolveCheckTargetOwner(source));
        return true;
    }

    internal bool SelectCheckTargetForTesting(
        Control eventSource,
        IReadOnlyList<Control> visualCandidates)
    {
        ArgumentNullException.ThrowIfNull(eventSource);
        ArgumentNullException.ThrowIfNull(visualCandidates);
        if (!IsCheckTargetSelectionActive)
        {
            return false;
        }

        CompleteCheckTargetSelection(
            ResolveCheckTargetOwner(eventSource),
            ResolveCheckTargetCandidates(eventSource, visualCandidates, visualCandidates));
        return true;
    }

    internal bool SelectCheckTargetForTesting(
        Control eventSource,
        IReadOnlyList<Control> inputCandidates,
        IReadOnlyList<Control> visualCandidates)
    {
        ArgumentNullException.ThrowIfNull(eventSource);
        ArgumentNullException.ThrowIfNull(inputCandidates);
        ArgumentNullException.ThrowIfNull(visualCandidates);
        if (!IsCheckTargetSelectionActive)
        {
            return false;
        }

        CompleteCheckTargetSelection(
            ResolveCheckTargetOwner(eventSource),
            ResolveCheckTargetCandidates(eventSource, inputCandidates, visualCandidates));
        return true;
    }

    internal bool SelectCheckTargetAtForTesting(
        Control eventSource,
        Control positionRoot,
        Point position)
    {
        ArgumentNullException.ThrowIfNull(eventSource);
        ArgumentNullException.ThrowIfNull(positionRoot);
        if (!IsCheckTargetSelectionActive)
        {
            return false;
        }

        var candidates = ResolveCheckTargetCandidates(eventSource, positionRoot, position);
        CompleteCheckTargetSelection(
            candidates.FirstOrDefault() ?? ResolveCheckTargetOwner(eventSource),
            candidates);
        return true;
    }

    public void CaptureCheckpoint(string? variableName = null)
    {
        var selection = PrepareSemanticCaptureSelection();
        if (selection is not null && !CanCaptureCheckSelection(selection))
        {
            return;
        }

        AddStep(
            _stepFactory.TryCreateCheckpointStep(selection?.ValueSnapshot, variableName),
            selection?.Target,
            "Checkpoint:Remember");
    }

    void IRecorderCheckpointSessionDetails.CaptureCheckpoint(
        RecorderCheckTargetSelection selection,
        string? variableName)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!CanCaptureCheckSelection(selection))
        {
            return;
        }

        AddStep(
            _stepFactory.TryCreateCheckpointStep(selection.ValueSnapshot, variableName),
            selection.Target,
            "Checkpoint:Remember");
    }

    public void CaptureCheckpointAssertion(Guid checkpointId)
    {
        var selection = PrepareSemanticCaptureSelection();
        if (selection is null)
        {
            SetStatus("No control is available for checkpoint comparison.", RecorderValidationStatus.Invalid);
            return;
        }

        if (!CanCaptureCheckSelection(selection))
        {
            return;
        }

        CaptureCheckpointAssertion(selection.ValueSnapshot, selection.Target, checkpointId);
    }

    void IRecorderCheckpointSessionDetails.CaptureCheckpointAssertion(
        RecorderCheckTargetSelection selection,
        Guid checkpointId,
        RecorderComparisonKind comparisonKind)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!CanCaptureCheckSelection(selection))
        {
            return;
        }

        CaptureCheckpointAssertion(
            selection.ValueSnapshot,
            selection.Target,
            checkpointId,
            comparisonKind);
    }

    private void CaptureCheckpointAssertion(
        RecorderSemanticValueSnapshot? snapshot,
        Control source,
        Guid checkpointId,
        RecorderComparisonKind comparisonKind = RecorderComparisonKind.Equal)
    {
        var checkpoint = CreateCheckpointOptions()
            .FirstOrDefault(candidate => candidate.CheckpointId == checkpointId);
        if (checkpoint is null)
        {
            SetStatus("Selected checkpoint is missing or ignored.", RecorderValidationStatus.Invalid);
            return;
        }

        AddStep(
            _stepFactory.TryCreateCheckpointAssertionStep(snapshot, checkpoint, comparisonKind),
            source,
            "Checkpoint:Compare");
    }

    void IRecorderCheckpointSessionDetails.CapturePresenceAssertion(
        RecorderCheckTargetSelection selection,
        bool expectEmpty)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!CanCaptureCheckSelection(selection))
        {
            return;
        }

        AddStep(
            _stepFactory.TryCreatePresenceAssertionStep(selection.ValueSnapshot, expectEmpty),
            selection.Target,
            expectEmpty ? "Assertion:IsEmpty" : "Assertion:HasValue");
    }

    void IRecorderCheckpointSessionDetails.CaptureEnabledAssertion(
        RecorderCheckTargetSelection selection,
        bool expectedEnabled)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!CanCaptureCheckSelection(selection))
        {
            return;
        }

        AddStep(
            _stepFactory.TryCreateEnabledAssertionStep(
                selection.Target,
                selection.ValueSnapshot,
                expectedEnabled),
            selection.Target,
            "Assertion:IsEnabled");
    }

    void IRecorderCheckpointSessionDetails.CaptureCalculatedAssertion(
        RecorderCheckTargetSelection selection,
        RecorderNumericExpectedExpression expression)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(expression);
        if (!CanCaptureCheckSelection(selection))
        {
            return;
        }

        AddStep(
            _stepFactory.TryCreateCalculatedAssertionStep(selection.ValueSnapshot, expression),
            selection.Target,
            "Assertion:Calculated");
    }

    public void CaptureLiteralAssertion(
        string expectedText,
        RecorderComparisonKind comparisonKind)
    {
        var selection = PrepareSemanticCaptureSelection();
        if (selection is not null && !CanCaptureCheckSelection(selection))
        {
            return;
        }

        AddStep(
            _stepFactory.TryCreateLiteralAssertionStep(
                selection?.ValueSnapshot,
                expectedText,
                comparisonKind),
            selection?.Target,
            "Assertion:Literal");
    }

    void IRecorderCheckpointSessionDetails.CaptureLiteralAssertion(
        RecorderCheckTargetSelection selection,
        string expectedText,
        RecorderComparisonKind comparisonKind,
        RecorderDateExpression? dateExpression)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!CanCaptureCheckSelection(selection))
        {
            return;
        }

        AddStep(
            _stepFactory.TryCreateLiteralAssertionStep(
                selection.ValueSnapshot,
                expectedText,
                comparisonKind,
                dateExpression),
            selection.Target,
            "Assertion:Literal");
    }

    public void BeginGeneratedValueTargetSelection(Guid? generatedValueId = null)
    {
        if (generatedValueId is not null
            && CreateGeneratedValueOptions().All(option => option.GeneratedValueId != generatedValueId.Value))
        {
            SetStatus("Selected generated value is missing or ignored.", RecorderValidationStatus.Invalid);
            return;
        }

        if (!BeginTargetSelection(RecorderTargetSelectionMode.GeneratedValue))
        {
            return;
        }

        _requestedGeneratedValueId = generatedValueId;
    }

    public void CancelGeneratedValueTargetSelection()
    {
        CancelTargetSelectionCore(RecorderTargetSelectionMode.GeneratedValue);
    }

    internal bool SelectGeneratedValueTargetForTesting(Control source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!IsGeneratedValueTargetSelectionActive)
        {
            return false;
        }

        CompleteGeneratedValueTargetSelection(
            ResolveInteractionOwner(source) ?? source,
            [source]);
        return true;
    }

    internal bool SelectGeneratedValueTargetForTesting(
        Control eventSource,
        IReadOnlyList<Control> visualCandidates)
    {
        ArgumentNullException.ThrowIfNull(eventSource);
        ArgumentNullException.ThrowIfNull(visualCandidates);
        if (!IsGeneratedValueTargetSelectionActive)
        {
            return false;
        }

        CompleteGeneratedValueTargetSelection(
            ResolveInteractionOwner(eventSource) ?? eventSource,
            visualCandidates);
        return true;
    }

    public void ApplyGeneratedValue(RecorderGeneratedValueTargetSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (_state != RecorderSessionState.Recording || IsBusy)
        {
            return;
        }

        if (!selection.Input.IsEffectivelyEnabled || selection.Input.IsReadOnly)
        {
            SetStatus("Generated values can only be entered into an enabled writable text field.", RecorderValidationStatus.Invalid);
            return;
        }

        var result = _stepFactory.TryCreateGeneratedTextEntryStep(
            selection.Input,
            selection.GeneratedValue.PreviewValue,
            selection.GeneratedValue,
            selection.DefinesGeneratedValue);
        if (!result.Success || result.Step is null)
        {
            AddStep(result, selection.Input, "GeneratedValue");
            return;
        }

        if (ReferenceEquals(_pendingTextBox, selection.Input))
        {
            DiscardPendingText();
        }

        try
        {
            _generatedValueTextApplication = selection.Input;
            selection.Input.Text = selection.GeneratedValue.PreviewValue;
        }
        finally
        {
            _generatedValueTextApplication = null;
        }

        if (!string.Equals(
                selection.Input.Text,
                selection.GeneratedValue.PreviewValue,
                StringComparison.Ordinal))
        {
            SetStatus(
                "The selected text field did not accept the generated value.",
                RecorderValidationStatus.Invalid);
            return;
        }

        if (!AddStep(result, selection.Input, "GeneratedValue"))
        {
            return;
        }

        _completedGeneratedValueInput = selection.Input;
        _completedGeneratedValueText = selection.GeneratedValue.PreviewValue;
        if (selection.DefinesGeneratedValue)
        {
            _lastGeneratedValueOrdinal = Math.Max(_lastGeneratedValueOrdinal, selection.GeneratedValue.Ordinal);
        }
    }

    public void BeginCopiedValueTargetSelection()
    {
        BeginTargetSelection(RecorderTargetSelectionMode.CopiedValue);
    }

    public void CancelCopiedValueTargetSelection()
    {
        CancelTargetSelectionCore(RecorderTargetSelectionMode.CopiedValue);
    }

    internal bool SelectCopiedValueTargetForTesting(Control source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!IsCopiedValueTargetSelectionActive)
        {
            return false;
        }

        CompleteCopiedValueTargetSelection(ResolveInteractionOwner(source) ?? source, [source]);
        return true;
    }

    public Task CommitCopiedValueAsync(
        RecorderCopiedValueTargetSelection selection,
        Func<string, CancellationToken, Task> clipboardWriter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(clipboardWriter);
        lock (_operationSync)
        {
            if (_isDisposed)
            {
                return Task.CompletedTask;
            }

            if (_state != RecorderSessionState.Recording || IsBusy)
            {
                SetStatus(
                    _copiedValueCommitTask is not null
                        ? "A clipboard copy is already in progress."
                        : "Clipboard values can only be copied while recording is active and idle.",
                    RecorderValidationStatus.Warning);
                return Task.CompletedTask;
            }

            var result = _stepFactory.TryCreateCopiedValueStep(
                selection.TargetSelection.ValueSnapshot,
                selection.CopiedValue);
            if (!result.Success || result.Step is null)
            {
                AddStep(result, selection.TargetSelection.Target, "CopiedValue");
                return Task.CompletedTask;
            }

            _busyDescription = "Copy value...";
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _copiedValueCommitTask = completion.Task;
            if (!AddStep(result, selection.TargetSelection.Target, "CopiedValue"))
            {
                _copiedValueCommitTask = null;
                _busyDescription = string.Empty;
                completion.TrySetResult();
                NotifySessionChanged();
                return completion.Task;
            }

            _ = ExecuteCopiedValueCommitAsync(
                selection,
                result.Step.StepId,
                clipboardWriter,
                completion,
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _lifetimeCancellation.Token));
            NotifySessionChanged();
            return completion.Task;
        }
    }

    private async Task ExecuteCopiedValueCommitAsync(
        RecorderCopiedValueTargetSelection selection,
        Guid reservedStepId,
        Func<string, CancellationToken, Task> clipboardWriter,
        TaskCompletionSource completion,
        CancellationTokenSource commitCancellation)
    {
        var startPendingAutosave = false;
        string? failureMessage = null;
        try
        {
            await clipboardWriter(selection.CopiedValue.PreviewValue, commitCancellation.Token);
            commitCancellation.Token.ThrowIfCancellationRequested();
            lock (_operationSync)
            {
                if (!_isDisposed)
                {
                    _activeCopiedValue = selection.CopiedValue;
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (!_isDisposed)
            {
                RollbackCopiedValueStep(reservedStepId);
                failureMessage = "Copying the selected value to the clipboard was cancelled.";
            }
        }
        catch (Exception exception)
        {
            if (!_isDisposed)
            {
                RollbackCopiedValueStep(reservedStepId);
                failureMessage = $"The selected value could not be copied to the clipboard: {exception.Message}";
            }
        }
        finally
        {
            commitCancellation.Dispose();
            lock (_operationSync)
            {
                if (!_isDisposed)
                {
                    _copiedValueCommitTask = null;
                    _busyDescription = string.Empty;
                    if (_pendingAutosave)
                    {
                        _pendingAutosave = false;
                        startPendingAutosave = _state == RecorderSessionState.Recording;
                    }
                }
            }

            if (!_isDisposed)
            {
                NotifySessionChanged();
                if (startPendingAutosave)
                {
                    StartAutosaveOrQueue();
                }

                if (failureMessage is not null)
                {
                    RejectCopiedValue(failureMessage);
                }
            }

            completion.TrySetResult();
        }
    }

    private void RollbackCopiedValueStep(Guid stepId)
    {
        var index = _steps.FindIndex(step => step.StepId == stepId);
        if (index < 0)
        {
            return;
        }

        _steps.RemoveAt(index);
        ApplyScenarioGraphValidation();
        UpdateLatestPreviewFromSteps();
    }

    private void RejectCopiedValue(string message)
    {
        SetStatus(
            string.IsNullOrWhiteSpace(message)
                ? "The selected value could not be copied to the clipboard."
                : message,
            RecorderValidationStatus.Invalid);
    }

    public void CaptureGeneratedValueAssertion(
        RecorderCheckTargetSelection selection,
        Guid generatedValueId,
        RecorderComparisonKind comparisonKind = RecorderComparisonKind.Equal)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!CanCaptureCheckSelection(selection))
        {
            return;
        }

        var generatedValue = CreateGeneratedValueOptions()
            .FirstOrDefault(option => option.GeneratedValueId == generatedValueId);
        if (generatedValue is null)
        {
            SetStatus("Selected generated value is missing or ignored.", RecorderValidationStatus.Invalid);
            return;
        }

        AddStep(
            _stepFactory.TryCreateGeneratedValueAssertionStep(
                selection.ValueSnapshot,
                generatedValue,
                comparisonKind),
            selection.Target,
            "GeneratedValue:Compare");
    }

    private bool CanCaptureCheckSelection(RecorderCheckTargetSelection selection)
    {
        if (selection.CanCaptureAssertions)
        {
            return true;
        }

        SetStatus(
            selection.ValueDescriptionError ?? "Select one unambiguous control before adding a check.",
            RecorderValidationStatus.Invalid);
        return false;
    }

    private void CompleteGeneratedValueTargetSelection(
        Control target,
        IReadOnlyList<Control>? visualCandidates)
    {
        var requestedGeneratedValueId = _requestedGeneratedValueId;
        CancelTargetSelectionCore(RecorderTargetSelectionMode.GeneratedValue);

        var existingValue = requestedGeneratedValueId is { } generatedValueId
            ? CreateGeneratedValueOptions()
                .FirstOrDefault(option => option.GeneratedValueId == generatedValueId)
            : null;
        if (requestedGeneratedValueId is not null && existingValue is null)
        {
            SetStatus("Selected generated value is missing or ignored.", RecorderValidationStatus.Invalid);
            return;
        }

        var definesGeneratedValue = existingValue is null;
        var generatedValue = existingValue ?? CreateNextGeneratedValueOption();
        var candidates = (visualCandidates ?? [target])
            .Prepend(target)
            .OfType<TextBox>()
            .Distinct()
            .ToArray();
        var resolved = new List<(TextBox Input, RecordedControlDescriptor Control)>();
        string? firstFailure = null;
        foreach (var input in candidates)
        {
            if (!input.IsEffectivelyEnabled || input.IsReadOnly)
            {
                firstFailure ??= "Generated values can only be entered into an enabled writable text field.";
                continue;
            }

            var result = _stepFactory.TryCreateGeneratedTextEntryStep(
                input,
                generatedValue.PreviewValue,
                generatedValue,
                definesGeneratedValue);
            if (result.Success && result.Step is { } step)
            {
                resolved.Add((input, step.Control));
            }
            else if (!string.IsNullOrWhiteSpace(result.Message))
            {
                firstFailure ??= result.Message;
            }
        }

        var logicalTargets = resolved
            .GroupBy(
                candidate => (
                    candidate.Control.ControlType,
                    candidate.Control.LocatorKind,
                    candidate.Control.LocatorValue))
            .ToArray();
        if (logicalTargets.Length == 0)
        {
            SetStatus(
                firstFailure ?? "Select a writable text field for the generated value.",
                RecorderValidationStatus.Invalid);
            return;
        }

        if (logicalTargets.Length > 1)
        {
            SetStatus(
                "The selected point resolves to more than one writable text field.",
                RecorderValidationStatus.Invalid);
            return;
        }

        var selected = logicalTargets[0].First();
        GeneratedValueTargetSelected?.Invoke(
            this,
            new RecorderGeneratedValueTargetSelectedEventArgs(
                new RecorderGeneratedValueTargetSelection(
                    selected.Input,
                    generatedValue,
                    definesGeneratedValue,
                    selected.Control.ProposedPropertyName)));
    }

    private void CompleteCopiedValueTargetSelection(
        Control target,
        IReadOnlyList<Control>? visualCandidates)
    {
        CancelTargetSelectionCore(RecorderTargetSelectionMode.CopiedValue);
        var targetSelection = ResolveCheckTargetSelection(target, visualCandidates);
        if (!targetSelection.CanCaptureAssertions
            || targetSelection.ValueSnapshot is null
            || !string.IsNullOrWhiteSpace(targetSelection.ValueDescriptionError))
        {
            SetStatus(
                targetSelection.ValueDescriptionError
                    ?? "The selected control does not expose one readable text value.",
                RecorderValidationStatus.Invalid);
            return;
        }

        var description = targetSelection.ValueSnapshot.Description;
        if (description.ValueKind is not (RecorderValueKind.Text or RecorderValueKind.GridCellText))
        {
            SetStatus(
                "Copy value supports text-bearing controls and grid cells only.",
                RecorderValidationStatus.Invalid);
            return;
        }

        var copiedValue = CreateNextCopiedValueOption(
            targetSelection.ValueSnapshot,
            description.CurrentValueText);
        CopiedValueTargetSelected?.Invoke(
            this,
            new RecorderCopiedValueTargetSelectedEventArgs(
                new RecorderCopiedValueTargetSelection(targetSelection, copiedValue)));
    }

    private RecorderCopiedValueOption CreateNextCopiedValueOption(
        RecorderSemanticValueSnapshot snapshot,
        string previewValue)
    {
        var reservedNames = CreateReservedScenarioVariableNames(GetCurrentScenarioGraphValidation());
        var controlName = snapshot.Prototype.Control.ProposedPropertyName;
        var variableName = RecorderNaming.CreateCopiedValueVariableName(
            $"copied{controlName}",
            reservedNames);
        return new RecorderCopiedValueOption(
            Guid.NewGuid(),
            variableName,
            snapshot.Description.ValueKind,
            controlName,
            previewValue);
    }

    private RecorderGeneratedValueOption CreateNextGeneratedValueOption()
    {
        var ordinal = _lastGeneratedValueOrdinal + 1;
        var reservedNames = CreateReservedScenarioVariableNames(GetCurrentScenarioGraphValidation());
        var variableName = RecorderNaming.CreateGeneratedValueVariableName(
            $"generatedValue{ordinal}",
            reservedNames);
        _recordingGeneratedValueSeries ??= RecordedValueGenerator.Start();
        return new RecorderGeneratedValueOption(
            Guid.NewGuid(),
            variableName,
            ordinal,
            _recordingGeneratedValueSeries.Create(ordinal));
    }

    private static HashSet<string> CreateReservedScenarioVariableNames(
        RecorderScenarioGraphValidationResult graphValidation) =>
        graphValidation.CheckpointVariables.Values
            .Concat(graphValidation.GeneratedValueVariables.Values)
            .Concat(graphValidation.CopiedValueVariables.Values)
            .ToHashSet(StringComparer.Ordinal);

    private void CompleteCheckTargetSelection(
        Control target,
        IReadOnlyList<Control>? visualCandidates = null)
    {
        CancelTargetSelectionCore(RecorderTargetSelectionMode.Check);
        var selection = ResolveCheckTargetSelection(target, visualCandidates);
        CheckTargetSelected?.Invoke(
            this,
            new RecorderCheckTargetSelectedEventArgs(selection));
    }

    private void CompleteNumericOperandTargetSelection(
        Control target,
        IReadOnlyList<Control>? visualCandidates = null)
    {
        CancelTargetSelectionCore(RecorderTargetSelectionMode.NumericOperand);
        var selection = ResolveCheckTargetSelection(target, visualCandidates);
        var error = selection.ValueDescriptionError;
        RecorderNumericOperand? operand = null;
        if (selection.CanCaptureAssertions && string.IsNullOrWhiteSpace(error))
        {
            RecorderStepFactory.TryCreateNumericControlOperand(
                selection.ValueSnapshot,
                out operand,
                out error);
        }

        NumericOperandTargetSelected?.Invoke(
            this,
            new RecorderNumericOperandTargetSelectedEventArgs(
                new RecorderNumericOperandTargetSelection(
                    selection.Target,
                    operand,
                    operand?.Control?.ProposedPropertyName,
                    error)));
    }

    private RecorderCheckTargetSelection ResolveCheckTargetSelection(
        Control target,
        IReadOnlyList<Control>? visualCandidates)
    {
        var selectedTarget = target;
        Control? configuredTarget = null;
        RecorderSemanticValueSnapshot? snapshot = null;
        string? error = null;
        var canCaptureAssertions = true;
        var definitiveFailure = false;
        var configuredSnapshotCaptured = visualCandidates is { Count: > 0 }
            && _stepFactory.TryCaptureConfiguredSemanticValueSnapshot(
                visualCandidates,
                out configuredTarget,
                out snapshot,
                out error,
                out definitiveFailure);
        if (configuredSnapshotCaptured)
        {
            selectedTarget = configuredTarget ?? target;
        }
        else if (string.IsNullOrWhiteSpace(error))
        {
            _stepFactory.TryCaptureSemanticValueSnapshot(target, out snapshot, out error);
        }
        else
        {
            selectedTarget = configuredTarget ?? target;
            canCaptureAssertions = !definitiveFailure;
        }

        return new RecorderCheckTargetSelection(
            selectedTarget,
            snapshot,
            error,
            selectedTarget.IsEffectivelyEnabled,
            canCaptureAssertions);
    }

    private bool BeginTargetSelection(RecorderTargetSelectionMode mode)
    {
        if (_state != RecorderSessionState.Recording || IsBusy)
        {
            return false;
        }

        CancelTargetSelectionCore();
        _targetSelectionMode = mode;
        return true;
    }

    private void CancelTargetSelectionCore(RecorderTargetSelectionMode? expectedMode = null)
    {
        if (expectedMode is not null && _targetSelectionMode != expectedMode.Value)
        {
            return;
        }

        if (_targetSelectionMode == RecorderTargetSelectionMode.GeneratedValue)
        {
            _requestedGeneratedValueId = null;
        }

        _pendingTargetSelectionControl = null;
        _pendingTargetSelectionCandidates = Array.Empty<Control>();
        _targetSelectionMode = RecorderTargetSelectionMode.None;
    }

    private void ClearPendingCopiedValuePaste()
    {
        _pendingCopiedValuePasteTarget = null;
        _pendingCopiedValuePasteId = null;
    }

    private List<Control> ResolveCheckTargetCandidates(
        Control? eventTarget,
        Control positionRoot,
        Point position,
        bool preserveReadOnlyText = true)
    {
        var rootIsAttached = TopLevel.GetTopLevel(positionRoot) is not null;
        var configuredGridCells = EnumerateConfiguredGridCellCandidates(positionRoot, position)
            .ToArray();
        var nonHitTestCandidates = configuredGridCells
            .ToHashSet<Control>(ReferenceEqualityComparer.Instance);
        var visualCandidates = positionRoot
            .GetVisualsAt(position)
            .OfType<Control>()
            .Concat(configuredGridCells);
        return ResolveCheckTargetCandidatesCore(
            eventTarget,
            positionRoot
                .GetInputElementsAt(position, enabledElementsOnly: false)
                .OfType<Control>(),
            visualCandidates,
            requireAttachedVisual: rootIsAttached,
            nonHitTestCandidates,
            preserveReadOnlyText);
    }

    private IEnumerable<Control> EnumerateConfiguredGridCellCandidates(
        Control positionRoot,
        Point position)
    {
        var visited = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        foreach (var grid in _stepFactory.ReadConfiguredGridRoots())
        {
            if (!ContainsPosition(positionRoot, position, grid))
            {
                continue;
            }

            foreach (var cell in _stepFactory.ReadMaterializedGridCellControls(grid))
            {
                if (ContainsPosition(positionRoot, position, cell) && visited.Add(cell))
                {
                    yield return cell;
                }
            }
        }
    }

    private static bool ContainsPosition(
        Control positionRoot,
        Point position,
        Control candidate)
    {
        var localBounds = new Rect(0, 0, candidate.Bounds.Width, candidate.Bounds.Height);
        if (positionRoot.TranslatePoint(position, candidate) is { } localPosition)
        {
            return localBounds.Contains(localPosition);
        }

        if (TopLevel.GetTopLevel(positionRoot) is not null
            && TopLevel.GetTopLevel(candidate) is not null)
        {
            var screenPosition = positionRoot.PointToScreen(position);
            return localBounds.Contains(candidate.PointToClient(screenPosition));
        }

        var candidateVisualRoot = candidate;
        while (candidateVisualRoot.GetVisualParent() is Control visualParent)
        {
            candidateVisualRoot = visualParent;
        }

        if (!positionRoot.GetLogicalDescendants().OfType<Control>().Any(control =>
                ReferenceEquals(control, candidateVisualRoot)))
        {
            return false;
        }

        var visualRootPosition = new Point(
            position.X - candidateVisualRoot.Bounds.X,
            position.Y - candidateVisualRoot.Bounds.Y);
        if (ReferenceEquals(candidateVisualRoot, candidate))
        {
            return localBounds.Contains(visualRootPosition);
        }

        return candidateVisualRoot.TranslatePoint(visualRootPosition, candidate) is { } nestedPosition
            && localBounds.Contains(nestedPosition);
    }

    private List<Control> ResolveCheckTargetCandidates(
        Control? eventTarget,
        IEnumerable<Control> inputCandidates,
        IEnumerable<Control> visualCandidates)
    {
        var input = inputCandidates.ToArray();
        var visual = visualCandidates.ToArray();
        var nonHitTestCandidates = input
            .Concat(visual)
            .Where(_stepFactory.IsCatalogGridCell)
            .ToHashSet<Control>(ReferenceEqualityComparer.Instance);
        return ResolveCheckTargetCandidatesCore(
            eventTarget,
            input,
            visual,
            requireAttachedVisual: false,
            nonHitTestCandidates,
            preserveReadOnlyText: true);
    }

    private List<Control> ResolveCheckTargetCandidatesCore(
        Control? eventTarget,
        IEnumerable<Control> inputCandidates,
        IEnumerable<Control> visualCandidates,
        bool requireAttachedVisual,
        IReadOnlySet<Control> nonHitTestCandidates,
        bool preserveReadOnlyText)
    {
        var visitedSpatial = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var spatialCandidates = visualCandidates
            .Concat(inputCandidates)
            .Append(eventTarget)
            .Where(static candidate => candidate is not null)
            .Select(static candidate => candidate!)
            .Where(candidate => visitedSpatial.Add(candidate))
            .Where(candidate => IsCaptureVisualCandidate(
                candidate,
                requireAttachedVisual,
                nonHitTestCandidates.Contains(candidate)))
            .Where(candidate => !IsPlaybackOnlyGridSurface(candidate))
            .ToArray();
        var leafCandidates = spatialCandidates
            .Where(candidate => !spatialCandidates.Any(other =>
                !ReferenceEquals(candidate, other)
                && IsAncestorOrSelf(candidate, other)))
            .ToArray();
        var selectedPaths = leafCandidates.Length > 0
            ? leafCandidates
            : spatialCandidates.Take(1).ToArray();
        if (selectedPaths.Length == 0)
        {
            return [];
        }

        var candidates = new List<Control>();
        var visitedPath = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        foreach (var selected in selectedPaths)
        {
            var selectedOwner = ResolveCheckTargetOwner(selected, preserveReadOnlyText);
            AddCheckTargetAndRelations(
                selectedOwner,
                candidates,
                visitedPath);
        }

        return candidates;
    }

    private bool IsPlaybackOnlyGridSurface(Control candidate)
    {
        foreach (var definition in _options.GridAutomation)
        {
            if (definition.CaptureLocatorKind == definition.RuntimeLocatorKind
                && string.Equals(
                    definition.CaptureLocatorValue,
                    definition.RuntimeLocatorValue,
                    StringComparison.Ordinal))
            {
                continue;
            }

            for (Control? current = candidate; current is not null; current = current.GetVisualParent() as Control)
            {
                if (MatchesLocator(current, definition.RuntimeLocatorKind, definition.RuntimeLocatorValue))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsCaptureVisualCandidate(
        Control candidate,
        bool requireAttachedVisual,
        bool allowNonHitTestCandidate)
    {
        if (!candidate.IsVisible
            || (!candidate.IsHitTestVisible && !allowNonHitTestCandidate))
        {
            return false;
        }

        if (requireAttachedVisual && TopLevel.GetTopLevel(candidate) is null)
        {
            return false;
        }

        var effectiveOpacity = 1d;
        for (Visual? current = candidate; current is not null; current = current.GetVisualParent())
        {
            if (!current.IsVisible)
            {
                return false;
            }

            effectiveOpacity *= current.Opacity;
            if (effectiveOpacity <= 0.01d)
            {
                return false;
            }
        }

        return true;
    }

    private void AddCheckTargetAndRelations(
        Control? source,
        ICollection<Control> candidates,
        ISet<Control> visited)
    {
        if (source is null)
        {
            return;
        }

        var queue = new Queue<Control>();
        queue.Enqueue(source);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            candidates.Add(current);
            if (_stepFactory.IsCatalogGridCell(current)
                || _stepFactory.IsConfiguredGridRoot(current))
            {
                continue;
            }

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

            if (current is Popup { PlacementTarget: Control placementTarget })
            {
                queue.Enqueue(placementTarget);
            }
        }
    }

    private RecorderCheckTargetSelection? PrepareSemanticCaptureSelection()
    {
        Control? control;
        IReadOnlyList<Control>? candidates = null;
        if (TryResolveLastSpatialCaptureTarget(out var spatialTarget, out var spatialCandidates))
        {
            control = spatialTarget;
            candidates = spatialCandidates;
        }
        else
        {
            control = _lastHoveredControl ?? GetFocusedWindowControl();
        }

        FlushPendingTextIfSwitchingTo(control);
        FlushPendingSliderIfSwitchingTo(control);
        FlushPendingSpinnerIfSwitchingTo(control);
        return control is null ? null : ResolveCheckTargetSelection(control, candidates);
    }

    private bool TryResolveLastSpatialCaptureTarget(
        out Control? target,
        out IReadOnlyList<Control>? candidates)
    {
        target = null;
        candidates = null;
        var spatial = _lastSpatialCaptureTarget;
        if (spatial is null
            || (_inputRoot is not null && !ReferenceEquals(_inputRoot, spatial.PositionRoot))
            || !spatial.PositionRoot.IsVisible
            || spatial.Position.X < 0
            || spatial.Position.Y < 0
            || spatial.Position.X > spatial.PositionRoot.Bounds.Width
            || spatial.Position.Y > spatial.PositionRoot.Bounds.Height)
        {
            return false;
        }

        var resolvedCandidates = ResolveCheckTargetCandidates(
            spatial.EventTarget,
            spatial.PositionRoot,
            spatial.Position);
        if (resolvedCandidates.Count == 0)
        {
            return false;
        }

        candidates = resolvedCandidates;
        target = resolvedCandidates[0];
        return true;
    }

    private bool AddStep(StepCreationResult result, Control? source = null, string captureAction = "Unknown")
    {
        if (!result.Success || result.Step is null)
        {
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                LogCaptureFailure(captureAction, source, result.Message);
                SetStatus(result.Message, RecorderValidationStatus.Invalid);
            }

            return false;
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
            return false;
        }

        var fingerprint = CreateFingerprint(recordedStep);
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(fingerprint, _lastFingerprint, StringComparison.Ordinal)
            && now - _lastRecordedAt < TimeSpan.FromMilliseconds(250))
        {
            return false;
        }

        RemovePointerLinkedGridSelection(recordedStep);

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
        return effectiveStep.CanPersist;
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

    private bool TryRecordCatalogGridRowGesture(Control? source, int clickCount)
    {
        var capture = _stepFactory.TryCreateCatalogGridRowGestureStep(
            source,
            openRow: clickCount >= 2);
        if (!capture.IsConfigured)
        {
            return false;
        }

        if (clickCount >= 2 && capture.StepResult.Step is { } openStep)
        {
            RemoveImmediatelyPrecedingGridSelection(openStep);
        }

        var stepCount = _steps.Count;
        AddStep(capture.StepResult, source, clickCount >= 2 ? "GridRowOpen" : "GridRowSelect");
        if (clickCount < 2
            && _activePointerGestureSequence is { } pointerGestureSequence
            && _steps.Count > stepCount
            && _steps[^1] is { ActionKind: RecordedActionKind.SelectGridRow } selectionStep)
        {
            _pendingGridRowSelectionGesture = new PendingGridRowSelectionGesture(
                pointerGestureSequence,
                selectionStep.StepId);
        }

        return true;
    }

    private void RemovePointerLinkedGridSelection(RecordedStep recordedStep)
    {
        if (string.IsNullOrWhiteSpace(recordedStep.GridTargetColumnName)
            || _activePointerGestureSequence is not { } activeSequence
            || _pendingGridRowSelectionGesture is not { } pending
            || pending.PointerGestureSequence != activeSequence)
        {
            return;
        }

        _pendingGridRowSelectionGesture = null;
        if (_steps.Count == 0 || _steps[^1].StepId != pending.SelectionStepId)
        {
            return;
        }

        RemoveImmediatelyPrecedingGridSelection(recordedStep);
    }

    private void RemoveImmediatelyPrecedingGridSelection(RecordedStep openStep)
    {
        if (_steps.Count == 0)
        {
            return;
        }

        var previous = _steps[^1];
        if (previous.ActionKind != RecordedActionKind.SelectGridRow
            || !string.Equals(
                previous.Control.ProposedPropertyName,
                openStep.Control.ProposedPropertyName,
                StringComparison.Ordinal)
            || !GridRowConditionsEqual(previous.GridRowConditions, openStep.GridRowConditions))
        {
            return;
        }

        _steps.RemoveAt(_steps.Count - 1);
        _lastFingerprint = null;
    }

    private static bool GridRowConditionsEqual(
        IReadOnlyList<RecordedGridRowCondition>? left,
        IReadOnlyList<RecordedGridRowCondition>? right)
    {
        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        return left.Zip(right).All(pair =>
            string.Equals(pair.First.ColumnName, pair.Second.ColumnName, StringComparison.Ordinal)
            && string.Equals(pair.First.Value, pair.Second.Value, StringComparison.Ordinal)
            && pair.First.ValueReference == pair.Second.ValueReference);
    }

    private void BeginPointerGesture()
    {
        _pointerGestureSequence = _pointerGestureSequence == int.MaxValue
            ? 1
            : _pointerGestureSequence + 1;
        _activePointerGestureSequence = _pointerGestureSequence;
        _pendingGridRowSelectionGesture = null;
    }

    private void ResetPointerGesture()
    {
        _activePointerGestureSequence = null;
        _pendingGridRowSelectionGesture = null;
    }

    private bool TryRecordCatalogGridCellEdit(
        Control source,
        string diagnosticContext,
        GridCellEditCommitMode commitMode = GridCellEditCommitMode.Commit,
        bool deferUntilCommit = false)
    {
        var capture = _stepFactory.TryCreateGridCellEditStep(source, commitMode);
        if (!capture.IsConfigured)
        {
            return false;
        }

        if (deferUntilCommit && capture.StepResult.Success)
        {
            if (_pendingCatalogGridEdit is { } pending
                && !AreRelated(pending.Source, source))
            {
                CommitPendingCatalogGridEdit();
            }

            _pendingCatalogGridEdit = new PendingCatalogGridEdit(
                source,
                capture.StepResult,
                diagnosticContext);
            return true;
        }

        AddStep(capture.StepResult, source, diagnosticContext);
        return true;
    }

    private bool CommitPendingCatalogGridEdit()
    {
        if (_pendingCatalogGridEdit is not { } pending)
        {
            return false;
        }

        _pendingCatalogGridEdit = null;
        AddStep(pending.StepResult, pending.Source, pending.DiagnosticContext);
        return true;
    }

    private void CommitPendingCatalogGridEditIfSwitchingTo(Control? source)
    {
        if (_pendingCatalogGridEdit is { } pending
            && (source is null || !AreRelated(pending.Source, source)))
        {
            CommitPendingCatalogGridEdit();
        }
    }

    private bool CancelPendingCatalogGridEdit()
    {
        if (_pendingCatalogGridEdit is not { } pending)
        {
            return false;
        }

        _pendingCatalogGridEdit = null;
        var stepResult = pending.StepResult.Step is { } step
            ? pending.StepResult with
            {
                Step = step with { GridCellEditCommitMode = GridCellEditCommitMode.Cancel }
            }
            : pending.StepResult;
        AddStep(stepResult, pending.Source, $"{pending.DiagnosticContext}Cancel");
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
            step.DateExpression?.ReferenceKind.ToString() ?? string.Empty,
            step.DateExpression?.DayOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            step.SecondDoubleValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            step.SecondDateValue?.ToString("O") ?? string.Empty,
            step.SecondDateExpression?.ReferenceKind.ToString() ?? string.Empty,
            step.SecondDateExpression?.DayOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
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
            step.GeneratedValueId?.ToString("N") ?? string.Empty,
            step.GeneratedValueVariableName ?? string.Empty,
            step.GeneratedValueOrdinal?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            step.DefinesGeneratedValue,
            step.ExpectedGeneratedValueId?.ToString("N") ?? string.Empty,
            step.CopiedValueId?.ToString("N") ?? string.Empty,
            step.CopiedValueVariableName ?? string.Empty,
            step.InputCopiedValueId?.ToString("N") ?? string.Empty,
            CreateNumericExpressionFingerprint(step.NumericExpectedExpression),
            step.HasExpectedLiteral,
            step.CanPersist);
    }

    private static string CreateNumericExpressionFingerprint(RecorderNumericExpectedExpression? expression)
    {
        return expression is null
            ? string.Empty
            : string.Join(
                ":",
                expression.Operation,
                CreateNumericOperandFingerprint(expression.Left),
                CreateNumericOperandFingerprint(expression.Right));
    }

    private static string CreateNumericOperandFingerprint(RecorderNumericOperand operand)
    {
        return string.Join(
            ",",
            operand.Kind,
            operand.LiteralValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            operand.CheckpointId?.ToString("N") ?? string.Empty,
            operand.Control?.LocatorKind.ToString() ?? string.Empty,
            operand.Control?.LocatorValue ?? string.Empty,
            operand.ValueAccessorKind?.ToString() ?? string.Empty,
            operand.GridValueReference?.TargetColumnName ?? string.Empty,
            operand.GridValueReference is null
                ? string.Empty
                : string.Join(
                    ";",
                    operand.GridValueReference.RowConditions.Select(static condition =>
                        $"{condition.ColumnName}={condition.Value}")));
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
        CommitPendingCatalogGridEdit();
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
        var copiedValueId = ReferenceEquals(textBox, _pendingCopiedValuePasteTarget)
            ? _pendingCopiedValuePasteId
            : null;
        ClearPendingCopiedValuePaste();
        _pendingTextBox = null;
        _pendingTextValue = null;
        if (ShouldSuppressTemplateTextEntry(textBox))
        {
            return;
        }

        if (TryRecordCatalogGridCellEdit(textBox, "GridTextEdit", deferUntilCommit: true))
        {
            return;
        }

        var multiItemCapture = _stepFactory.TryCreateMultiItemSpinnerStep(textBox);
        if (multiItemCapture.IsConfigured)
        {
            DiscardPendingSpinnerIfRelated(textBox);
            AddStep(multiItemCapture.StepResult, textBox, "MultiItemSpinnerValue");
            return;
        }

        if (copiedValueId is { } copiedId)
        {
            var copiedValue = CreateCopiedValueOptions()
                .FirstOrDefault(option => option.CopiedValueId == copiedId);
            if (copiedValue is not null
                && string.Equals(textBox.Text, copiedValue.PreviewValue, StringComparison.Ordinal))
            {
                AddStep(
                    _stepFactory.TryCreateCopiedTextEntryStep(textBox, copiedValue),
                    textBox,
                    "CopiedValueTextEntry");
                return;
            }
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
        var multiItemCapture = _stepFactory.TryCreateMultiItemSpinnerStep(spinner);
        if (multiItemCapture.IsConfigured)
        {
            DiscardPendingTextIfRelated(spinner);
            AddStep(multiItemCapture.StepResult, spinner, "MultiItemSpinnerValue");
            return;
        }

        AddStep(_stepFactory.TryCreateSpinnerStep(spinner), spinner, "SpinnerValue");
    }

    private void DiscardPendingSpinnerIfRelated(Control source)
    {
        if (_pendingSpinner is null
            || (!AreRelated(_pendingSpinner, source)
                && !_stepFactory.AreSameMultiItemSpinner(_pendingSpinner, source)))
        {
            return;
        }

        _spinnerDebounceTimer.Stop();
        _pendingSpinner = null;
    }

    private void DiscardPendingTextIfRelated(Control source)
    {
        if (_pendingTextBox is null
            || (!AreRelated(_pendingTextBox, source)
                && !_stepFactory.AreSameMultiItemSpinner(_pendingTextBox, source)))
        {
            return;
        }

        DiscardPendingText();
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

        if (control is not null && _stepFactory.AreSameMultiItemSpinner(_pendingTextBox, control))
        {
            return;
        }

        if (control is not null && _stepFactory.IsCompositeSelectionPair(_pendingTextBox, control))
        {
            return;
        }

        FlushPendingText();
        CommitPendingCatalogGridEditIfSwitchingTo(control);
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

        if (control is not null && _stepFactory.AreSameMultiItemSpinner(_pendingSpinner, control))
        {
            return;
        }

        FlushPendingSpinner();
        CommitPendingCatalogGridEditIfSwitchingTo(control);
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
        CommitPendingCatalogGridEditIfSwitchingTo(control);
        BeginNewCompositeInteraction(control);
        UpdatePendingGridCellContext(control);
        _recentPointerControl = control;
        _recentPointerAt = DateTimeOffset.UtcNow;
    }

    private void RegisterKeyboardInput(Control control)
    {
        CommitPendingCatalogGridEditIfSwitchingTo(control);
        BeginNewCompositeInteraction(control);
        UpdatePendingGridCellContext(control);
        _recentKeyboardControl = control;
        _recentKeyboardAt = DateTimeOffset.UtcNow;
    }

    private void UpdatePendingGridCellContext(Control? control)
    {
        if (control is null)
        {
            _pendingGridComboSelectionContext = null;
            return;
        }

        var resolution = _stepFactory.ResolveGridComboSelectionContext(control);
        if (resolution.IsConfigured)
        {
            _pendingGridComboSelectionContext = resolution;
            return;
        }

        if (_pendingGridComboSelectionContext?.Context is not { } pending
            || !_stepFactory.IsGridCellContextPart(control, pending))
        {
            _pendingGridComboSelectionContext = null;
        }
    }

    private void BeginNewCompositeInteraction(Control? control)
    {
        if (_completedCompositeSelection?.Sources.Any(source => ReferenceEquals(control, source)) == true)
        {
            _completedCompositeSelection = null;
        }
    }

    private bool ShouldSuppressCompletedCompositeEvent(Control source)
    {
        return _completedCompositeSelection?.Sources.Any(candidate => ReferenceEquals(source, candidate)) == true;
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

    private Control ResolveCheckTargetOwner(Control selected, bool preserveReadOnlyText = true)
    {
        if (_stepFactory.IsCatalogGridCell(selected))
        {
            return selected;
        }

        var owner = ResolveInteractionOwner(selected);
        return preserveReadOnlyText && selected is (TextBlock or Label) && owner is TabControl
            ? selected
            : owner ?? selected;
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

    private sealed record CompletedCompositeSelection(IReadOnlyList<Control> Sources);

    private sealed record PendingCatalogGridEdit(
        Control Source,
        StepCreationResult StepResult,
        string DiagnosticContext);

    private sealed record PendingGridRowSelectionGesture(
        int PointerGestureSequence,
        Guid SelectionStepId);

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
            if (_copiedValueCommitTask is not null)
            {
                SetStatus(
                    $"{operationName} ignored while a clipboard copy is in progress.",
                    RecorderValidationStatus.Warning);
                return Task.FromResult(RecorderSaveResult.Failed("Clipboard copy is already in progress."));
            }

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
            if (_copiedValueCommitTask is not null)
            {
                _pendingAutosave = true;
                return;
            }

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
        if (step.ActionKind == RecordedActionKind.SetMultiItemSpinnerValue
            && step.ValidationStatus == RecorderValidationStatus.Invalid
            && !step.CanPersist)
        {
            return step with
            {
                LastValidationAt = DateTimeOffset.UtcNow,
                ReviewState = ResolveReviewState(step),
                FailureCode = ResolveFailureCode(step),
                RuntimeValidationFindings = Array.Empty<RecorderRuntimeValidationFinding>()
            };
        }

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

    private RecorderStepJournalEntry CreateJournalEntry(
        RecordedStep step,
        RecorderJournalContext context)
    {
        return new RecorderStepJournalEntry(
            step.StepId,
            _codeGenerator.GeneratePreviewForStep(
                step,
                context.PreviewSteps),
            ResolveJournalStatusMessage(step, context),
            step.ValidationStatus,
            step.CanPersist,
            step.IsIgnored,
            step.ReviewState,
            step.FailureCode,
            step.LastValidationAt,
            !step.IsIgnored && RecorderStepEditService.CanEdit(step));
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

    private static string ResolveJournalStatusMessage(
        RecordedStep step,
        RecorderJournalContext context)
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
                && context.CheckpointsById.TryGetValue(checkpointId, out var checkpointOption)
                    ? checkpointOption
                    : null;
            return $"Remember {step.Control.ProposedPropertyName}.{DescribeValueAccessor(step.ValueAccessorKind)} as "
                + (checkpoint?.VariableName ?? step.CheckpointVariableName ?? "checkpointValue");
        }

        if (step.ActionKind == RecordedActionKind.CaptureCopiedValue)
        {
            var copiedValue = step.CopiedValueId is { } copiedValueId
                && context.CopiedValuesById.TryGetValue(copiedValueId, out var copiedValueOption)
                    ? copiedValueOption
                    : null;
            return $"Copy {step.Control.ProposedPropertyName}.{DescribeValueAccessor(step.ValueAccessorKind)} to the clipboard as "
                + (copiedValue?.VariableName ?? step.CopiedValueVariableName ?? "copiedValue");
        }

        if (step.ActionKind == RecordedActionKind.AssertValue)
        {
            var target = $"{step.Control.ProposedPropertyName}.{DescribeValueAccessor(step.ValueAccessorKind)}";
            if (step.ComparisonKind == RecorderComparisonKind.HasValue)
            {
                return $"Assert {target} has value";
            }

            if (step.ComparisonKind == RecorderComparisonKind.IsEmpty)
            {
                return $"Assert {target} is empty";
            }

            var comparison = step.ComparisonKind switch
            {
                RecorderComparisonKind.NotEqual => "does not equal",
                RecorderComparisonKind.Contains => "contains",
                RecorderComparisonKind.Equivalent => "has the same items as",
                _ => "equals"
            };
            var expected = step.ExpectedCheckpointId is { } checkpointId
                ? "checkpoint " + (context.CheckpointsById.TryGetValue(checkpointId, out var checkpoint)
                    ? checkpoint.VariableName
                    : checkpointId.ToString("N"))
                : step.ExpectedGeneratedValueId is { } generatedValueId
                    ? "generated value " + (context.GeneratedValuesById.TryGetValue(generatedValueId, out var generatedValue)
                        ? generatedValue.VariableName
                        : generatedValueId.ToString("N"))
                    : step.NumericExpectedExpression is not null
                        ? "calculated value"
                        : "expected literal";
            return $"Assert {target} {comparison} {expected}";
        }

        if (step.ActionKind == RecordedActionKind.EnterText
            && step.GeneratedValueId is { } valueId)
        {
            context.GeneratedValuesById.TryGetValue(valueId, out var generatedValue);
            return step.DefinesGeneratedValue
                ? $"Generate {generatedValue?.VariableName ?? step.GeneratedValueVariableName ?? "value"} and enter it into {step.Control.ProposedPropertyName}"
                : $"Enter generated value {generatedValue?.VariableName ?? step.GeneratedValueVariableName ?? "value"} into {step.Control.ProposedPropertyName}";
        }

        if (step.ActionKind is (RecordedActionKind.EnterText or RecordedActionKind.EnterSearch)
            && step.InputCopiedValueId is { } inputCopiedValueId)
        {
            context.CopiedValuesById.TryGetValue(inputCopiedValueId, out var copiedValue);
            return $"Enter copied value {copiedValue?.VariableName ?? "copiedValue"} into {step.Control.ProposedPropertyName}";
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

    private void UpdateLatestPreviewFromSteps(bool notify = true)
    {
        var context = GetCurrentJournalContext();
        var latestStep = context.PreviewSteps.Length == 0
            ? null
            : context.PreviewSteps[^1];
        LatestPreview = latestStep is null
            ? string.Empty
            : _codeGenerator.GeneratePreviewForStep(
                latestStep,
                context.PreviewSteps);
        if (notify)
        {
            NotifySessionChanged();
        }
    }

    private IReadOnlyList<RecorderCheckpointOption> CreateCheckpointOptions() =>
        GetCurrentJournalContext().Checkpoints;

    private IReadOnlyList<RecorderGeneratedValueOption> CreateGeneratedValueOptions() =>
        GetCurrentJournalContext().GeneratedValues;

    private IReadOnlyList<RecorderCopiedValueOption> CreateCopiedValueOptions() =>
        GetCurrentJournalContext().CopiedValues;

    private void RefreshActiveCopiedValue()
    {
        if (_copiedValueCommitTask is not null)
        {
            _activeCopiedValue = null;
            ClearPendingCopiedValuePaste();
            return;
        }

        var copiedValues = GetCurrentJournalContext().CopiedValues;
        _activeCopiedValue = copiedValues.Length == 0
            ? null
            : copiedValues[^1];
        if (_activeCopiedValue is null)
        {
            ClearPendingCopiedValuePaste();
        }
    }

    private RecorderScenarioGraphValidationResult GetCurrentScenarioGraphValidation() =>
        GetCurrentJournalContext().GraphValidation;

    private RecorderJournalContext GetCurrentJournalContext()
    {
        if (_cachedJournalContext is not null
            && _cachedJournalContextRevision == _scenarioGraphRevision)
        {
            return _cachedJournalContext;
        }

        _cachedJournalContext = CreateJournalContext();
        _cachedJournalContextRevision = _scenarioGraphRevision;
        return _cachedJournalContext;
    }

    private RecorderJournalContext CreateJournalContext(
        RecorderScenarioGraphValidationResult? knownGraphValidation = null)
    {
        var previewSteps = _steps
            .Where(static step => !step.IsIgnored)
            .ToArray();
        var graphSteps = previewSteps
            .Where(static step => step.CanPersist)
            .ToArray();
        var graphValidation = knownGraphValidation
            ?? RecorderScenarioGraphValidator.Validate(graphSteps);
        var checkpoints = graphSteps
            .Where(static step => step.ActionKind == RecordedActionKind.CaptureCheckpoint
                && step.CheckpointId is not null
                && step.ValueKind is not null)
            .Select(step => new RecorderCheckpointOption(
                step.CheckpointId!.Value,
                graphValidation.CheckpointVariables.TryGetValue(step.CheckpointId.Value, out var variableName)
                    ? variableName
                    : step.CheckpointVariableName ?? "checkpointValue",
                step.ValueKind!.Value,
                step.Control.ProposedPropertyName))
            .ToArray();
        var generatedValues = graphSteps
            .Where(static step => step.ActionKind == RecordedActionKind.EnterText
                && step.DefinesGeneratedValue
                && step.GeneratedValueId is not null
                && step.GeneratedValueOrdinal is > 0)
            .Select(step => new RecorderGeneratedValueOption(
                step.GeneratedValueId!.Value,
                graphValidation.GeneratedValueVariables.TryGetValue(step.GeneratedValueId.Value, out var variableName)
                    ? variableName
                    : step.GeneratedValueVariableName ?? $"generatedValue{step.GeneratedValueOrdinal}",
                step.GeneratedValueOrdinal!.Value,
                step.StringValue ?? string.Empty))
            .ToArray();
        var copiedValues = graphSteps
            .Where(static step => step.ActionKind == RecordedActionKind.CaptureCopiedValue
                && step.CopiedValueId is not null
                && step.ValueKind is RecorderValueKind.Text or RecorderValueKind.GridCellText)
            .Select(step => new RecorderCopiedValueOption(
                step.CopiedValueId!.Value,
                graphValidation.CopiedValueVariables.TryGetValue(step.CopiedValueId.Value, out var variableName)
                    ? variableName
                    : step.CopiedValueVariableName ?? "copiedValue",
                step.ValueKind!.Value,
                step.Control.ProposedPropertyName,
                step.StringValue ?? string.Empty))
            .ToArray();

        return new RecorderJournalContext(
            previewSteps,
            graphValidation,
            checkpoints,
            generatedValues,
            copiedValues,
            checkpoints.ToDictionary(static option => option.CheckpointId),
            generatedValues.ToDictionary(static option => option.GeneratedValueId),
            copiedValues.ToDictionary(static option => option.CopiedValueId));
    }

    private void InvalidateScenarioGraphValidation()
    {
        _scenarioGraphRevision++;
        _cachedJournalContext = null;
        _cachedJournalContextRevision = -1;
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

        InvalidateScenarioGraphValidation();
        var graphValidation = GetCurrentScenarioGraphValidation();
        if (graphValidation.Success)
        {
            RefreshActiveCopiedValue();
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

        InvalidateScenarioGraphValidation();
        _cachedJournalContext = CreateJournalContext(graphValidation);
        _cachedJournalContextRevision = _scenarioGraphRevision;
        RefreshActiveCopiedValue();
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
                : graphValidation.Error ?? "Scenario dependency graph is invalid.",
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
        if (!_isDisposed)
        {
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed record RecorderJournalContext(
        RecordedStep[] PreviewSteps,
        RecorderScenarioGraphValidationResult GraphValidation,
        RecorderCheckpointOption[] Checkpoints,
        RecorderGeneratedValueOption[] GeneratedValues,
        RecorderCopiedValueOption[] CopiedValues,
        IReadOnlyDictionary<Guid, RecorderCheckpointOption> CheckpointsById,
        IReadOnlyDictionary<Guid, RecorderGeneratedValueOption> GeneratedValuesById,
        IReadOnlyDictionary<Guid, RecorderCopiedValueOption> CopiedValuesById);

    private sealed record SpatialCaptureTarget(
        Control? EventTarget,
        Control PositionRoot,
        Point Position);

    private enum RecorderTargetSelectionMode
    {
        None = 0,
        Check = 1,
        NumericOperand = 2,
        GeneratedValue = 3,
        CopiedValue = 4
    }

    private sealed record QueuedManagedOperation(
        string OperationName,
        Func<CancellationToken, Task<RecorderSaveResult>> Operation,
        CancellationToken CancellationToken,
        TaskCompletionSource<RecorderSaveResult> Completion);

}
