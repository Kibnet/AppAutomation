using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Input;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;

namespace AppAutomation.FlaUI.Automation;

public sealed partial class FlaUiControlResolver
{
    private ITimePickerControl ResolvePresenterTimeControl(UiControlDefinition definition)
    {
        var intent = definition.TimePickerIntent!;
        if (intent.Part == TimePickerPartKind.CommittedRoot)
            return new FlaUiCommittedTimeControl(new FlaUiTimePickerBinding(this, intent, definition));
        if (intent.Part != TimePickerPartKind.Presenter)
            throw new NotSupportedException("Only the committed root and pending presenter expose a time value.");
        return new FlaUiPresenterTimeControl(this, definition, RequireTimePickerBinding(definition));
    }

    private FlaUiTimePickerBinding RequireTimePickerBinding(UiControlDefinition definition)
    {
        var intent = definition.TimePickerIntent!;
        if (intent.OwnerIdentity is not FlaUiTimePickerBinding binding || !ReferenceEquals(binding.Resolver, this)
            || !Equals(intent.Parts, binding.Parts))
            throw new InvalidOperationException("The time-picker part has no binding to this native owner instance.");
        binding.ValidateDefinitionScope(definition);
        return binding;
    }

    private AutomationElement[] TimePickerCandidates(UiControlDefinition definition, AutomationElement? container = null)
    {
        var roots = container is not null ? [container] : GetProcessSearchRoots();
        if (container is null && definition.Scope is { } scope) roots = Find(roots, scope);
        return Find(roots, new UiControlScope(definition.LocatorValue, definition.LocatorKind, definition.FallbackToName));

        AutomationElement[] Find(AutomationElement[] searchRoots, UiControlScope locator)
        {
            AutomationElement[] Search(UiLocatorKind kind) => DistinctElements(searchRoots.SelectMany(root =>
                    TryRead(() => root.FindAll(TreeScope.Subtree, CreatePropertyCondition(_conditionFactory, locator.LocatorValue, kind))) ?? [])
                .Where(IsAttachedAndAvailable)).ToArray();
            var primary = Search(locator.LocatorKind);
            return primary.Length > 0 || !locator.FallbackToName || locator.LocatorKind == UiLocatorKind.Name
                ? primary : Search(UiLocatorKind.Name);
        }
    }

    private static AutomationElement RequireSingleTimePart(AutomationElement[] matches, string locator)
    {
        if (matches.Length > 1)
            throw new UiControlResolutionException(UiControlResolutionFailure.Ambiguous,
                $"Time-picker part '{locator}' matched more than one live native instance.");
        return matches.SingleOrDefault()
            ?? throw new UiControlResolutionException(UiControlResolutionFailure.NotFound,
                $"Time-picker part '{locator}' was not found for the current owner and opening epoch.");
    }

    private AutomationElement FindTimePickerPresenter(FlaUiTimePickerBinding binding, TimePickerOwnerState state)
    {
        var definition = binding.Definition(binding.Parts.TimePickerLocator, UiControlType.TimePicker,
            TimePickerPartKind.Presenter) with { Scope = binding.Parts.PopupScope };
        var matches = TimePickerCandidates(definition).Where(candidate =>
        {
            var actual = TimePickerRuntimeMetadata.ReadPresenter(candidate.Properties.ItemStatus.Value);
            return actual.OwnerId == state.OwnerId && actual.Epoch == state.Epoch
                && candidate.Properties.ProcessId.Value == binding.ProcessId && !candidate.IsOffscreen;
        }).ToArray();
        var presenter = RequireSingleTimePart(matches, definition.LocatorValue);
        binding.CheckPresenter(presenter);
        return presenter;
    }

    private AutomationElement FindTimePickerPopup(FlaUiTimePickerBinding binding, AutomationElement presenter)
    {
        if (string.IsNullOrWhiteSpace(binding.Parts.PopupRootLocator)) return presenter;
        var definition = binding.Definition(binding.Parts.PopupRootLocator, UiControlType.AutomationElement,
            TimePickerPartKind.Popup) with { Scope = binding.Parts.PopupScope };
        var candidates = TimePickerCandidates(definition).Where(candidate =>
            candidate.Properties.ProcessId.Value == binding.ProcessId && !candidate.IsOffscreen
            && IsWithin(presenter, candidate)).ToArray();
        return RequireSingleTimePart(candidates, definition.LocatorValue);
    }

    private AutomationElement FindTimePickerPart(UiControlDefinition definition, FlaUiTimePickerBinding binding)
    {
        var part = definition.TimePickerIntent!.Part;
        if (part == TimePickerPartKind.CommittedRoot) return binding.ReadRoot();
        var (element, _) = ResolveTimePickerPart(definition, binding);
        _ = binding.ReadOwner(requireOpen: part != TimePickerPartKind.Input,
            requireEditable: part != TimePickerPartKind.Component);
        if (part == TimePickerPartKind.Confirm) binding.ValidateVerifiedPending();
        return element;
    }

    private (AutomationElement Element, TimePickerOwnerState Owner) ResolveTimePickerPart(
        UiControlDefinition definition, FlaUiTimePickerBinding binding)
    {
        var part = definition.TimePickerIntent!.Part;
        var owner = binding.ReadOwner(requireOpen: part != TimePickerPartKind.Input,
            bindOpening: part != TimePickerPartKind.Input, requireEditable: true);
        if (part == TimePickerPartKind.Input)
        {
            var input = RequireSingleTimePart(TimePickerCandidates(definition, binding.ReadRoot()), definition.LocatorValue);
            return (input, owner);
        }
        var presenter = FindTimePickerPresenter(binding, owner);
        if (part == TimePickerPartKind.Presenter) return (presenter, owner);
        var popup = FindTimePickerPopup(binding, presenter);
        if (part == TimePickerPartKind.Popup) return (popup, owner);
        var container = part == TimePickerPartKind.Component ? presenter : popup;
        var matches = TimePickerCandidates(definition, container);
        if (part == TimePickerPartKind.Component)
        {
            var kind = definition.TimePickerIntent.Component
                ?? throw new InvalidOperationException("A picker component requires its declared numeric role.");
            matches = matches.Where(candidate =>
            {
                var state = TimePickerRuntimeMetadata.ReadComponent(candidate.Properties.ItemStatus.Value, kind);
                return state.OwnerId == binding.OwnerId && state.Epoch == binding.Epoch;
            }).ToArray();
        }
        var result = RequireSingleTimePart(matches, definition.LocatorValue);
        if (result.Properties.ProcessId.Value != binding.ProcessId || result.IsOffscreen)
            throw new InvalidOperationException("The time-picker part is not available in its bound native surface.");
        return (result, owner);
    }

    private static int[] TimePickerRuntimeId(AutomationElement element) =>
        element.Properties.RuntimeId.Value is { Length: > 0 } id ? id.ToArray()
            : throw new NotSupportedException("The time-picker peer does not publish native instance identity.");

    private static void RequireTimePickerRuntimeId(AutomationElement element, int[] expected)
    {
        if (!TimePickerRuntimeId(element).AsSpan().SequenceEqual(expected))
            throw new InvalidOperationException("The time-picker native instance changed during the operation.");
    }

    private sealed class FlaUiTimePickerBinding
    {
        private readonly UiControlDefinition _rootDefinition;
        private readonly int[] _runtimeId;
        private readonly IntPtr _window;
        private readonly TimePickerOwnerState _initial;
        private int[]? _presenterId;
        private long? _openingEpoch;
        private PendingComponent[]? _verifiedPending;
        private bool _disposed;

        private sealed record PendingComponent(TimePickerComponentKind Kind, int Target, int[] RuntimeId,
            TimePickerComponentState Capabilities);

        internal FlaUiTimePickerBinding(FlaUiControlResolver resolver, TimePickerResolveDescriptor intent,
            UiControlDefinition actualDefinition)
        {
            Resolver = resolver;
            Parts = intent.Parts;
            _rootDefinition = intent.OwnerDefinition with
            {
                LocatorValue = Parts.RootLocator, LocatorKind = Parts.LocatorKind,
                FallbackToName = Parts.FallbackToName, ControlType = UiControlType.AutomationElement,
                Scope = actualDefinition.Scope ?? intent.OwnerDefinition.Scope, TimePickerIntent = null
            };
            var root = ResolveRoot();
            _runtimeId = TimePickerRuntimeId(root);
            ProcessId = root.Properties.ProcessId.Value;
            _window = WindowsPointerBackend.RootWindow(DesktopPointerTargetResolver.FindWindow(root));
            _initial = TimePickerRuntimeMetadata.ReadOwner(root.Properties.ItemStatus.Value);
            OwnerId = _initial.OwnerId;
            if (_initial.IsOpen) _openingEpoch = _initial.Epoch;
        }

        internal FlaUiControlResolver Resolver { get; }
        internal TimePickerParts Parts { get; }
        internal string OwnerId { get; }
        internal long Epoch => _openingEpoch ?? _initial.Epoch;
        internal int ProcessId { get; }

        internal void ValidateDefinitionScope(UiControlDefinition definition)
        {
            if (definition.Scope is { } scope && scope != _rootDefinition.Scope
                && (definition.TimePickerIntent!.Part == TimePickerPartKind.Input || scope != Parts.PopupScope))
                throw new InvalidOperationException("The current part scope no longer matches the bound time-editor owner.");
        }

        private AutomationElement ResolveRoot() => RequireSingleTimePart(Resolver.TimePickerCandidates(_rootDefinition), Parts.RootLocator);

        internal AutomationElement ReadRoot()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return CheckRoot(ResolveRoot());
        }

        private AutomationElement CheckRoot(AutomationElement root)
        {
            RequireTimePickerRuntimeId(root, _runtimeId);
            if (root.Properties.ProcessId.Value != ProcessId
                || WindowsPointerBackend.RootWindow(DesktopPointerTargetResolver.FindWindow(root)) != _window)
                throw new InvalidOperationException("The time editor moved to another native owner window.");
            return root;
        }

        internal TimePickerOwnerState ReadOwner(bool? requireOpen = null, bool bindOpening = false, bool requireEditable = false)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var candidates = Resolver.TimePickerCandidates(_rootDefinition with { Scope = null });
            var root = CheckRoot(_rootDefinition.Scope is null
                ? RequireSingleTimePart(candidates, Parts.RootLocator) : ResolveRoot());
            foreach (var candidate in candidates)
            {
                var metadata = candidate.Properties.ItemStatus.ValueOrDefault;
                if (metadata?.StartsWith("AppAutomation.TimePicker/", StringComparison.Ordinal) != true) continue;
                if (TimePickerRuntimeMetadata.ReadOwner(metadata).OwnerId == OwnerId
                    && !TimePickerRuntimeId(candidate).AsSpan().SequenceEqual(_runtimeId))
                    throw new UiControlResolutionException(UiControlResolutionFailure.Ambiguous,
                        "More than one live time-editor root publishes the same UI owner token.");
            }
            var state = TimePickerRuntimeMetadata.ReadOwner(root.Properties.ItemStatus.Value);
            if (state.OwnerId != OwnerId || state.Clock != _initial.Clock || state.UseSeconds != _initial.UseSeconds
                || state.MinuteStep != _initial.MinuteStep || state.SecondStep != _initial.SecondStep)
                throw new InvalidOperationException("The time editor's owner or capabilities changed during the operation.");
            if (bindOpening && _openingEpoch is null)
            {
                if (!state.IsOpen)
                    throw new UiControlResolutionException(UiControlResolutionFailure.NotFound,
                        "The time editor has not opened its owner-bound popup yet.");
                if (_initial.Epoch == long.MaxValue || state.Epoch != _initial.Epoch + 1)
                    throw new InvalidOperationException("The time editor did not publish a fresh opening epoch.");
                _openingEpoch = state.Epoch;
            }
            TimePickerRuntimeMetadata.RequireIdentity(OwnerId, Epoch, state.OwnerId, state.Epoch);
            if (requireOpen is { } open && state.IsOpen != open)
                throw new UiControlResolutionException(UiControlResolutionFailure.NotFound,
                    open ? "The bound time-picker popup is not open." : "The bound time-picker popup is still open.");
            if (requireEditable && (state.IsReadOnly || !root.IsEnabled))
                throw new InvalidOperationException("The time editor is disabled or read-only.");
            return state;
        }

        internal void CheckPresenter(AutomationElement presenter)
        {
            if (_presenterId is null)
            {
                _presenterId = TimePickerRuntimeId(presenter);
            }
            else RequireTimePickerRuntimeId(presenter, _presenterId);
        }

        internal void ValidateKeyboardForeground(AutomationElement panel)
        {
            var panelWindow = WindowsPointerBackend.RootWindow(DesktopPointerTargetResolver.FindWindow(panel));
            if (panelWindow == IntPtr.Zero || WindowsPointerBackend.WindowProcess(panelWindow) != ProcessId)
                throw new PointerInterferenceException("The verified picker panel has no matching native process window.");
            var foreground = WindowsPointerBackend.ForegroundWindow;
            if (foreground != _window && foreground != panelWindow)
                throw new PointerInterferenceException("Another native window owns the keyboard instead of the bound time editor.");
        }

        internal void ClearVerifiedPending() => _verifiedPending = null;

        internal void CaptureVerifiedPending(IReadOnlyList<(TimePickerComponentKind Kind, int Value)> targets,
            IReadOnlyDictionary<TimePickerComponentKind, int[]> identities,
            IReadOnlyDictionary<TimePickerComponentKind, TimePickerComponentState> capabilities) =>
            _verifiedPending = targets.Select(target => new PendingComponent(target.Kind, target.Value,
                identities[target.Kind].ToArray(), capabilities[target.Kind])).ToArray();

        internal void ValidateVerifiedPending()
        {
            var pending = _verifiedPending
                ?? throw new InvalidOperationException("The time presenter has no verified pending selection to confirm.");
            _ = ReadOwner(requireOpen: true, requireEditable: true);
            foreach (var expected in pending)
            {
                var parts = Parts.PresenterParts!.Get(expected.Kind);
                var definition = Definition(parts.SelectorLocator, UiControlType.AutomationElement,
                    TimePickerPartKind.Component, expected.Kind);
                var panel = Resolver.FindTimePickerPart(definition, this);
                RequireTimePickerRuntimeId(panel, expected.RuntimeId);
                if (!panel.IsEnabled) throw new InvalidOperationException("A pending time component became disabled before confirmation.");
                var actual = TimePickerRuntimeMetadata.ReadComponent(panel.Properties.ItemStatus.Value, expected.Kind);
                TimePickerRuntimeMetadata.RequireIdentity(OwnerId, Epoch, actual.OwnerId, actual.Epoch);
                if (actual.SelectedValue != expected.Target || actual.Minimum != expected.Capabilities.Minimum
                    || actual.Maximum != expected.Capabilities.Maximum || actual.Step != expected.Capabilities.Step)
                    throw new InvalidOperationException("The pending time selection changed before confirmation.");
            }
            _ = ReadOwner(requireOpen: true, requireEditable: true);
        }

        internal TimeSpan? ReadCommitted()
        {
            _ = ReadOwner();
            var root = ReadRoot();
            if (!root.Patterns.Value.TryGetPattern(out var value))
                throw new NotSupportedException("The committed time root must publish canonical TimeSpan through ValuePattern.");
            var result = TimePickerRuntimeMetadata.ReadCommittedValue(value.Value.Value);
            _ = ReadOwner();
            return result;
        }

        internal UiControlDefinition Definition(string locator, UiControlType type, TimePickerPartKind part,
            TimePickerComponentKind? component = null) => new(_rootDefinition.PropertyName, type,
                locator, Parts.LocatorKind, Parts.FallbackToName)
            {
                Scope = _rootDefinition.Scope,
                TimePickerIntent = new(Parts, _rootDefinition, part, component, this)
            };

        internal void Dispose()
        {
            _verifiedPending = null;
            _disposed = true;
        }
    }

    private sealed class FlaUiCommittedTimeControl(FlaUiTimePickerBinding binding)
        : ITimePickerCommittedControl, IUiControlAvailability
    {
        public string AutomationId => binding.ReadRoot().AutomationId;
        public string Name => binding.ReadRoot().Name;
        public bool IsEnabled => binding.ReadRoot().IsEnabled;
        public bool IsAvailable
        {
            get
            {
                var root = binding.ReadRoot();
                return root.IsAvailable && !root.IsOffscreen;
            }
        }
        public bool IsPopupOpen => binding.ReadOwner().IsOpen;
        public object OwnerIdentity => binding;
        public TimeSpan? SelectedTime
        {
            get => binding.ReadCommitted();
            set => throw new NotSupportedException("The committed root is read-only to automation; select and confirm the presenter instead.");
        }
        public void ValidateWrite(TimeSpan value)
        {
            var state = binding.ReadOwner(requireEditable: true);
            state.ValidateWrite(value, binding.Parts);
            _ = binding.ReadCommitted();
        }
        public void Dispose() => binding.Dispose();
    }

    private sealed class FlaUiTimePickerButtonControl : FlaUiControlBase<Button>, IButtonControl, IBudgetedControlInvoke
    {
        private readonly FlaUiControlResolver _resolver;
        private readonly UiControlDefinition _definition;
        private readonly FlaUiTimePickerBinding _binding;
        private readonly Action? _validateBeforeStep;

        internal FlaUiTimePickerButtonControl(FlaUiControlResolver resolver, UiControlDefinition definition,
            FlaUiTimePickerBinding binding, Action? validateBeforeStep = null) : base(resolver.FindTimePickerPart(definition, binding).AsButton())
        {
            _resolver = resolver;
            _definition = definition;
            _binding = binding;
            _validateBeforeStep = validateBeforeStep;
        }

        public void Invoke() => throw new NotSupportedException("A presenter button requires the original operation budget.");

        public void Invoke(UiOperationTimeoutBudget budget)
        {
            using var preparation = EnterPointerPreparation();
            AutomationElement ResolveCurrent()
            {
                _ = budget.Remaining;
                return _resolver.FindTimePickerPart(_definition, _binding);
            }
            var button = ResolveCurrent();
            if (!button.IsEnabled) throw new InvalidOperationException("The time-picker button is disabled.");
            if (button.Patterns.Invoke.TryGetPattern(out var invoke))
            {
                var identity = TimePickerRuntimeId(button);
                RequireTimePickerRuntimeId(ResolveCurrent(), identity);
                _validateBeforeStep?.Invoke();
                _ = budget.Remaining;
                invoke.Invoke();
                return;
            }
            DesktopPointer.ClickFallback(ResolveCurrent, "time-picker button lacks Invoke",
                new PointerClickOptions { Timeout = budget.Remaining }, validateBeforePress:
                _definition.TimePickerIntent!.Part == TimePickerPartKind.Confirm ? () =>
                {
                    _binding.ValidateVerifiedPending();
                    _ = budget.Remaining;
                } : _validateBeforeStep);
        }
    }

    private sealed class FlaUiPresenterTimeControl : ITimePickerPresenterControl, IUiControlAvailability
    {
        private readonly FlaUiControlResolver _resolver;
        private readonly UiControlDefinition _definition;
        private readonly FlaUiTimePickerBinding _binding;
        private readonly Dictionary<TimePickerComponentKind, int[]> _componentIds = new();
        private readonly Dictionary<TimePickerComponentKind, TimePickerComponentState> _componentCapabilities = new();
        private readonly FlaUiPointerPreparation? _preparation = FlaUiPointerPreparation.Capture();
        private bool _disposed;

        internal FlaUiPresenterTimeControl(FlaUiControlResolver resolver, UiControlDefinition definition, FlaUiTimePickerBinding binding)
        {
            _resolver = resolver;
            _definition = definition;
            _binding = binding;
            _ = Current();
        }

        private AutomationElement Current()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _resolver.FindTimePickerPart(_definition, _binding);
        }
        public string AutomationId => Current().AutomationId;
        public string Name => Current().Name;
        public bool IsEnabled => Current().IsEnabled;
        public bool IsAvailable
        {
            get
            {
                var presenter = Current();
                return presenter.IsAvailable && !presenter.IsOffscreen;
            }
        }
        public bool IsPopupOpen => _binding.ReadOwner().IsOpen;
        public void ValidateOwner(bool requireOpen) => _ = _binding.ReadOwner(requireOpen, requireEditable: requireOpen);
        public TimeSpan? SelectedTime
        {
            get => throw new NotSupportedException("Pending panel state is not a committed time value.");
            set => throw new NotSupportedException("Use the budgeted presenter operation.");
        }

        public void SetPendingTime(TimeSpan value, UiOperationTimeoutBudget budget)
        {
            using var preparation = FlaUiPointerPreparation.EnterCaptured(_preparation);
            _ = budget.Remaining;
            _binding.ClearVerifiedPending();
            var owner = _binding.ReadOwner(requireOpen: true, requireEditable: true);
            owner.ValidateWrite(value, _binding.Parts);
            var targets = owner.GetComponentValues(value);

            foreach (var (kind, target) in targets)
            {
                var (element, state) = ReadComponent(kind);
                if (target < state.Minimum || target > state.Maximum || ((long)target - state.Minimum) % state.Step != 0)
                    throw new NotSupportedException("The requested time is outside a real component's selectable range.");
                var parts = _binding.Parts.PresenterParts!.Get(kind);
                if (state.SelectedValue != target)
                {
                    var locator = target > state.SelectedValue ? parts.IncreaseButtonLocator : parts.DecreaseButtonLocator;
                    if (string.IsNullOrWhiteSpace(locator) && !element.Properties.IsKeyboardFocusable.Value)
                        throw new NotSupportedException("The picker panel has neither a configured step button nor keyboard focus support.");
                    if (!string.IsNullOrWhiteSpace(locator)) _ = ResolveStep(kind, locator, target > state.SelectedValue);
                }
                _ = budget.Remaining;
            }

            foreach (var (kind, target) in targets)
            {
                var state = ReadComponent(kind).State;
                var limit = ((long)state.Maximum - state.Minimum) / state.Step + 1;
                for (long step = 0; state.SelectedValue != target; step++)
                {
                    if (step >= limit) throw new InvalidOperationException("The picker panel did not reach its bounded target.");
                    _ = budget.Remaining;
                    var previous = state;
                    var increasing = target > previous.SelectedValue;
                    var parts = _binding.Parts.PresenterParts!.Get(kind);
                    var locator = increasing ? parts.IncreaseButtonLocator : parts.DecreaseButtonLocator;
                    void ValidatePrevious()
                    {
                        if (ReadComponent(kind).State.SelectedValue != previous.SelectedValue)
                            throw new InvalidOperationException("The picker component changed while preparing its native step.");
                    }
                    if (!string.IsNullOrWhiteSpace(locator)) ResolveStep(kind, locator, increasing, ValidatePrevious).Invoke(budget);
                    else DesktopPointer.SendGuardedKey(() => ReadComponent(kind).Element, budget,
                        increasing ? VirtualKeyShort.DOWN : VirtualKeyShort.UP, panel =>
                        {
                            _binding.ValidateKeyboardForeground(panel);
                            ValidatePrevious();
                        });
                    state = UiWait.Until(() => ReadComponent(kind).State,
                        actual => actual.SelectedValue != previous.SelectedValue,
                        new UiWaitOptions { Timeout = budget.Remaining, PollInterval = TimeSpan.FromMilliseconds(25) },
                        "The picker component did not change after its native step action.");
                    if (state.SelectedValue != previous.SelectedValue + (increasing ? state.Step : -state.Step))
                        throw new InvalidOperationException("The picker component changed by an unexpected native step.");
                }
            }
            foreach (var (kind, target) in targets)
                if (ReadComponent(kind).State.SelectedValue != target)
                    throw new InvalidOperationException("The pending time changed before confirmation.");
            ValidateOwner(true);
            _binding.CaptureVerifiedPending(targets, _componentIds, _componentCapabilities);
            _ = budget.Remaining;
        }

        private FlaUiTimePickerButtonControl ResolveStep(TimePickerComponentKind kind, string locator, bool increasing,
            Action? validateBeforeStep = null) =>
            new(_resolver, _binding.Definition(locator, UiControlType.Button,
                increasing ? TimePickerPartKind.Increase : TimePickerPartKind.Decrease, kind), _binding, validateBeforeStep);

        private (AutomationElement Element, TimePickerComponentState State) ReadComponent(TimePickerComponentKind kind)
        {
            var parts = _binding.Parts.PresenterParts!.Get(kind);
            var definition = _binding.Definition(parts.SelectorLocator, UiControlType.AutomationElement, TimePickerPartKind.Component, kind);
            var (element, owner) = _resolver.ResolveTimePickerPart(definition, _binding);
            if (!element.IsEnabled) throw new InvalidOperationException("The picker component is disabled.");
            if (_componentIds.TryGetValue(kind, out var identity)) RequireTimePickerRuntimeId(element, identity);
            else _componentIds.Add(kind, TimePickerRuntimeId(element));
            var state = TimePickerRuntimeMetadata.ReadComponent(element.Properties.ItemStatus.Value, kind);
            TimePickerRuntimeMetadata.RequireIdentity(owner.OwnerId, owner.Epoch, state.OwnerId, state.Epoch);
            if (_componentCapabilities.TryGetValue(kind, out var previous))
            {
                if (state.Minimum != previous.Minimum || state.Maximum != previous.Maximum || state.Step != previous.Step)
                    throw new InvalidOperationException("The picker component capabilities changed during selection.");
            }
            else _componentCapabilities.Add(kind, state);
            var range = owner.GetComponentRange(kind);
            if (state.Minimum < range.Minimum || state.Maximum > range.Maximum
                || (kind is TimePickerComponentKind.Minute or TimePickerComponentKind.Second) && state.Step != range.Step)
                throw new InvalidDataException("The picker component contradicts the owner clock or increment capabilities.");
            _ = _binding.ReadOwner(requireOpen: true, requireEditable: true);
            return (element, state);
        }

        public void Dispose() => _disposed = true;
    }
}
