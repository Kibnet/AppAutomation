using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Internal.AutomationModel;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Control = Avalonia.Controls.Control;
using NativePresenter = Avalonia.Controls.TimePickerPresenter;

namespace AppAutomation.Avalonia.Headless.Automation;

public sealed partial class HeadlessControlResolver
{
    private T TimePickerUi<T>(Func<T> action) => _window.Native.Dispatcher.CheckAccess()
        ? action() : HeadlessRuntime.Dispatch(action);

    private TControl ResolveConfiguredTimePicker<TControl>(UiControlDefinition definition) where TControl : class
    {
        var context = UiInputPreparationContext.Capture();
        return TimePickerUi(() =>
        {
            using var inputScope = UiInputPreparationContext.Enter(context);
            var intent = definition.TimePickerIntent!;
            var binding = GetTimePickerBinding(definition);
            object control = intent.Part switch
            {
                TimePickerPartKind.CommittedRoot => new HeadlessCommittedTime(binding),
                TimePickerPartKind.Presenter => new HeadlessPresenterTime(binding),
                TimePickerPartKind.Input when definition.ControlType == UiControlType.TextBox =>
                    new HeadlessTextBoxControl(FindElement(definition).AsTextBox()),
                TimePickerPartKind.Input => new HeadlessTimePickerButton(binding, definition),
                TimePickerPartKind.Confirm or TimePickerPartKind.Cancel or TimePickerPartKind.Increase
                    or TimePickerPartKind.Decrease => new HeadlessTimePickerButton(binding, definition),
                _ => new HeadlessUiControl(FindElement(definition))
            };
            return control as TControl ?? throw new UiControlResolutionException(UiControlResolutionFailure.TypeMismatch,
                $"Time-picker part '{definition.PropertyName}' cannot be cast to '{typeof(TControl).FullName}'.");
        });
    }

    private HeadlessTimePickerBinding GetTimePickerBinding(UiControlDefinition definition)
    {
        var intent = definition.TimePickerIntent!;
        if (intent.OwnerIdentity is null)
            return new HeadlessTimePickerBinding(this, definition);
        if (intent.OwnerIdentity is not HeadlessTimePickerBinding binding || !ReferenceEquals(binding.Resolver, this)
            || binding.Parts != intent.Parts)
            throw new InvalidOperationException("The time-picker part has a different native owner binding.");
        binding.ValidateDefinitionScope(definition);
        return binding;
    }

    private AutomationElement FindTimePickerPart(UiControlDefinition definition) => TimePickerUi(() =>
        AutomationElement.WrapControl(GetTimePickerBinding(definition).ResolvePart(definition)));

    private sealed class HeadlessTimePickerBinding
    {
        private enum PopupState { Unobserved, WaitingForOpen, Open, Closed }
        private enum ConfirmationState { NotStarted, InProgress, Completed }

        private readonly UiControlDefinition _ownerDefinition;
        private readonly Control _owner;
        private readonly AvaloniaProperty _valueProperty;
        private readonly List<Action> _unsubscribe = [];
        private Popup? _popup;
        private NativePresenter? _presenter;
        private Control? _popupChild;
        private Control? _input;
        private Control? _confirm;
        private readonly Dictionary<string, DateTimePickerPanel> _panels = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Control> _stepButtons = new(StringComparer.Ordinal);
        private TimePickerOwnerState? _capabilities;
        private TimePickerOwnerState? _publishedOwner;
        private long? _openingEpoch;
        private TimeSpan? _pendingValue;
        private PopupState _popupState;
        private ConfirmationState _confirmationState;
        private bool _startedOpen;
        private bool _popupChildReleased;
        private bool _invalid;
        private bool _disposed;

        private bool IsObserving => _popupState != PopupState.Unobserved;
        private bool HasOpened => _popupState is PopupState.Open or PopupState.Closed;

        internal HeadlessTimePickerBinding(HeadlessControlResolver resolver, UiControlDefinition definition)
        {
            Resolver = resolver;
            var intent = definition.TimePickerIntent!;
            Parts = intent.Parts;
            _ownerDefinition = intent.OwnerDefinition with
            {
                LocatorValue = Parts.RootLocator, LocatorKind = Parts.LocatorKind, FallbackToName = Parts.FallbackToName,
                Scope = definition.Scope ?? intent.OwnerDefinition.Scope, TimePickerIntent = null
            };
            _owner = FindOwner();
            var name = Parts.CommittedValueProperty;
            var publicProperty = name is null ? null : _owner.GetType().GetProperty(name);
            _valueProperty = name is null ? null! : AvaloniaPropertyRegistry.Instance.FindRegistered(_owner.GetType(), name)!;
            if (publicProperty is null || publicProperty.GetMethod is not { IsPublic: true } || publicProperty.GetIndexParameters().Length != 0
                || _valueProperty is null || publicProperty.PropertyType != _valueProperty.PropertyType
                || _valueProperty.PropertyType != typeof(TimeSpan) && _valueProperty.PropertyType != typeof(TimeSpan?))
                throw new NotSupportedException("The committed time must name a public TimeSpan/TimeSpan? UI AvaloniaProperty on the editor root.");
        }

        internal HeadlessControlResolver Resolver { get; }
        internal TimePickerParts Parts { get; }
        internal Control Owner => _owner;

        internal void ValidateDefinitionScope(UiControlDefinition definition)
        {
            if (definition.Scope is { } scope && scope != _ownerDefinition.Scope
                && (definition.TimePickerIntent!.Part == TimePickerPartKind.Input || scope != Parts.PopupScope))
                throw new InvalidOperationException("The time-picker part was resolved through another editor scope.");
        }

        private Control FindOwner()
        {
            var roots = _ownerDefinition.Scope is { } scope
                ? FindExact(ControlTree.EnumerateDescendants(Resolver._window.Native).Prepend(Resolver._window.Native),
                    scope.LocatorValue, scope.LocatorKind, scope.FallbackToName)
                : [Resolver._window.Native];
            return RequireOne(FindExact(roots.SelectMany(root => ControlTree.EnumerateDescendants(root).Prepend(root)),
                Parts.RootLocator, Parts.LocatorKind, Parts.FallbackToName), "committed root");
        }

        private static Control[] FindExact(IEnumerable<Control> controls, string locator, UiLocatorKind kind, bool fallback)
        {
            var candidates = controls.Distinct().ToArray();
            var matches = candidates.Where(control => kind == UiLocatorKind.Name
                ? AutomationElement.ReadControlName(control) == locator : AutomationProperties.GetAutomationId(control) == locator).ToArray();
            return matches.Length > 0 || !fallback || kind == UiLocatorKind.Name ? matches
                : candidates.Where(control => AutomationElement.ReadControlName(control) == locator).ToArray();
        }

        private static Control RequireOne(Control[] matches, string part) => matches.Length == 1 ? matches[0]
            : throw new UiControlResolutionException(matches.Length == 0 ? UiControlResolutionFailure.NotFound : UiControlResolutionFailure.Ambiguous,
                $"The configured time-picker {part} matched {matches.Length} native controls; expected exactly one.");

        private Control FindInside(Control root, string locator) => RequireOne(
            FindExact(ControlTree.EnumerateDescendants(root).Prepend(root), locator, Parts.LocatorKind, Parts.FallbackToName), locator);

        private static bool BelongsTo(Control? control, Control owner) => control is not null
            && (ReferenceEquals(control, owner) || control.GetLogicalAncestors().Any(parent => ReferenceEquals(parent, owner))
                || control.GetVisualAncestors().Any(parent => ReferenceEquals(parent, owner)));

        private void ValidateRoot()
        {
            if (_disposed || _invalid || !ReferenceEquals(FindOwner(), _owner) || !_owner.IsAttachedToVisualTree()
                || !_owner.IsEffectivelyVisible)
                throw new InvalidOperationException("The configured time-picker owner was detached, replaced or retemplated.");
        }

        internal TimeSpan? ReadCommitted()
        {
            ValidateRoot();
            if (IsObserving) ValidateOwner(requireOpen: false);
            return _owner.GetValue(_valueProperty) switch
            {
                null => null,
                TimeSpan value => value,
                _ => throw new InvalidDataException("The committed UI time property returned an unsupported value.")
            };
        }

        private bool ReadIsReadOnly()
        {
            var property = AvaloniaPropertyRegistry.Instance.FindRegistered(_owner.GetType(), "IsReadOnly");
            if (property?.PropertyType == typeof(bool)) return (bool)_owner.GetValue(property)!;
            var provider = ControlAutomationPeer.CreatePeerForElement(_owner)?.GetProvider<IValueProvider>();
            return provider?.IsReadOnly ?? throw new NotSupportedException("The editor root does not expose its real read-only UI state.");
        }

        private TimePickerOwnerState NativeCapabilities() => new("native", 0, _popup!.IsOpen, ReadIsReadOnly(),
            _presenter!.ClockIdentifier switch
            {
                "12HourClock" => 12, "24HourClock" => 24,
                _ => throw new NotSupportedException("Unsupported native presenter clock identifier.")
            }, _presenter.UseSeconds, _presenter.MinuteIncrement, _presenter.SecondIncrement);

        private void DiscoverSurface()
        {
            if (_presenter is not null) return;
            if (_owner is TemplatedControl templated) templated.ApplyTemplate();
            var popups = ControlTree.EnumerateDescendants(Resolver._window.Native).OfType<Popup>()
                .Where(popup => popup.PlacementTarget is { } target ? BelongsTo(target, _owner) : BelongsTo(popup, _owner))
                .Distinct().ToArray();
            var surfaces = popups.SelectMany(popup => FindExact(ControlTree.EnumerateDescendants(popup),
                    Parts.TimePickerLocator, Parts.LocatorKind, Parts.FallbackToName)
                .OfType<NativePresenter>().Select(presenter => (Popup: popup, Presenter: presenter))).ToArray();
            if (surfaces.Length != 1)
                throw new NotSupportedException("The declared native presenter does not have one provable owner-bound Popup.");
            (_popup, _presenter) = surfaces[0];
            _popupChild = _popup.Child;
            _presenter.ApplyTemplate();
            if (Parts.PopupRootLocator is { Length: > 0 } popupLocator)
            {
                var popupRoot = FindInside(_popup, popupLocator);
                if (!ControlTree.EnumerateDescendants(popupRoot).Prepend(popupRoot).Contains(_presenter))
                    throw new NotSupportedException("The declared popup root does not contain its native presenter.");
            }
            if (Parts.PopupScope is { } scope)
            {
                var scopeRoot = RequireOne(FindExact(ControlTree.EnumerateDescendants(Resolver._window.Native)
                        .Prepend(Resolver._window.Native).Concat(ControlTree.EnumerateDescendants(_popup)).Append(_popup),
                    scope.LocatorValue, scope.LocatorKind, scope.FallbackToName), "popup scope");
                if (!BelongsTo(_owner, scopeRoot) && !BelongsTo(_popup, scopeRoot) && !BelongsTo(scopeRoot, _popup))
                    throw new NotSupportedException("The configured popup scope does not contain the native editor or its popup.");
            }
            var openingLocator = Parts.OpenKind == TimePickerOpenKind.ClickInput ? Parts.InputLocator : Parts.OpenButtonLocator;
            _input = openingLocator is { Length: > 0 } ? FindInside(_owner, openingLocator) : null;
            if (_input is null || (Parts.OpenKind == TimePickerOpenKind.ClickInput
                    ? _input is not global::Avalonia.Controls.TextBox : _input is not global::Avalonia.Controls.Button))
                throw new NotSupportedException("The configured time-picker opening part has the wrong native type.");
            _confirm = FindInside(_presenter, Parts.ConfirmButtonLocator!);
            if (_confirm is not global::Avalonia.Controls.Button)
                throw new NotSupportedException("The configured native confirmation part is not a button.");
        }

        internal void ValidateWrite(TimeSpan value)
        {
            ValidateRoot();
            DiscoverSurface();
            if (!_owner.IsEffectivelyEnabled) throw new InvalidOperationException("The time editor is disabled.");
            var capabilities = NativeCapabilities();
            capabilities.ValidateWrite(value, Parts);
            ValidatePanels(value, capabilities, requireSelected: false);
            if (IsObserving)
            {
                ValidateCapabilities();
                return;
            }
            _capabilities = capabilities;
            _publishedOwner = ReadPublishedOwner();
            _startedOpen = _popup!.IsOpen;
            if (_startedOpen) _openingEpoch = _publishedOwner?.Epoch;
            Observe();
        }

        private TimePickerOwnerState? ReadPublishedOwner()
        {
            var text = AutomationProperties.GetItemStatus(_owner);
            if (string.IsNullOrEmpty(text)) return null;
            var state = TimePickerRuntimeMetadata.ReadOwner(text);
            var native = NativeCapabilities();
            if (state.IsOpen != native.IsOpen || state.IsReadOnly != native.IsReadOnly || state.Clock != native.Clock
                || state.UseSeconds != native.UseSeconds || state.MinuteStep != native.MinuteStep || state.SecondStep != native.SecondStep)
                throw new InvalidDataException("Published time-picker metadata contradicts the native UI state.");
            return state;
        }

        private void Observe()
        {
            _popupState = _startedOpen ? PopupState.Open : PopupState.WaitingForOpen;
            EventHandler<VisualTreeAttachmentEventArgs> detached = (sender, args) => _invalid = true;
            EventHandler<TemplateAppliedEventArgs> template = (sender, args) => _invalid = true;
            EventHandler opened = (sender, args) =>
            {
                if (HasOpened) _invalid = true;
                _popupState = PopupState.Open;
            };
            EventHandler<EventArgs> closed = (sender, args) =>
            {
                if (!HasOpened) _invalid = true;
                _popupState = PopupState.Closed;
            };
            EventHandler<AvaloniaPropertyChangedEventArgs> presenterChanged = (sender, args) =>
            {
                if (args.Property == NativePresenter.ClockIdentifierProperty || args.Property == NativePresenter.UseSecondsProperty
                    || args.Property == NativePresenter.MinuteIncrementProperty || args.Property == NativePresenter.SecondIncrementProperty
                    || args.Property == TemplatedControl.TemplateProperty)
                    _invalid = true;
            };
            EventHandler<AvaloniaPropertyChangedEventArgs> ownerChanged = (sender, args) =>
            {
                if (args.Property == TemplatedControl.TemplateProperty || _confirmationState == ConfirmationState.NotStarted
                    && (args.Property.Name == "IsReadOnly" || args.Property == Control.IsEnabledProperty))
                    _invalid = true;
            };
            EventHandler<AvaloniaPropertyChangedEventArgs> popupChanged = (sender, args) =>
            {
                if (args.Property == Popup.PlacementTargetProperty) _invalid = true;
                if (args.Property == Popup.ChildProperty)
                {
                    // A real Confirm may release its old visual tree while closing. A replacement,
                    // an editing-time detach or any subsequent opening is not normal close cleanup.
                    if (_confirmationState != ConfirmationState.NotStarted
                        && ReferenceEquals(args.OldValue, _popupChild) && args.NewValue is null)
                        _popupChildReleased = true;
                    else _invalid = true;
                }
            };
            _owner.DetachedFromVisualTree += detached;
            _owner.PropertyChanged += ownerChanged;
            _unsubscribe.Add(() => { _owner.DetachedFromVisualTree -= detached; _owner.PropertyChanged -= ownerChanged; });
            if (_owner is TemplatedControl templated)
            {
                templated.TemplateApplied += template;
                _unsubscribe.Add(() => templated.TemplateApplied -= template);
            }
            _presenter!.TemplateApplied += template;
            _presenter.PropertyChanged += presenterChanged;
            _popup!.Opened += opened;
            _popup.Closed += closed;
            _popup.PropertyChanged += popupChanged;
            _unsubscribe.Add(() => { _presenter.TemplateApplied -= template; _presenter.PropertyChanged -= presenterChanged;
                _popup.Opened -= opened; _popup.Closed -= closed; _popup.PropertyChanged -= popupChanged; });
        }

        private void ValidateCapabilities(bool requireEditable = true)
        {
            var native = NativeCapabilities();
            if (_capabilities is not { } expected || requireEditable && (native.IsReadOnly || !_owner.IsEffectivelyEnabled)
                || native.Clock != expected.Clock || native.UseSeconds != expected.UseSeconds
                || native.MinuteStep != expected.MinuteStep || native.SecondStep != expected.SecondStep)
                throw new InvalidOperationException("The native time-picker capabilities or editability changed during the operation.");
            var state = ReadPublishedOwner();
            if (_publishedOwner is { } original)
            {
                if (state is null || state.OwnerId != original.OwnerId) throw new InvalidOperationException("The native time-picker owner token changed.");
                if (HasOpened)
                {
                    _openingEpoch ??= state.Epoch;
                    if (!_startedOpen && state.Epoch <= original.Epoch || state.Epoch != _openingEpoch)
                        throw new InvalidOperationException("The native time-picker opening epoch changed.");
                    if (!(_confirmationState == ConfirmationState.Completed && _popupChildReleased && _popup!.Child is null))
                    {
                        var presenter = TimePickerRuntimeMetadata.ReadPresenter(AutomationProperties.GetItemStatus(_presenter!));
                        TimePickerRuntimeMetadata.RequireIdentity(state.OwnerId, state.Epoch, presenter.OwnerId, presenter.Epoch);
                    }
                }
                else if (state.Epoch != original.Epoch) throw new InvalidOperationException("The native time-picker epoch changed before opening.");
            }
            else if (state is not null) throw new InvalidOperationException("Time-picker metadata appeared during an unbound native operation.");
        }

        internal void ValidateOwner(bool requireOpen)
        {
            ValidateRoot();
            var expectedState = requireOpen ? PopupState.Open : PopupState.Closed;
            if (_popupState != expectedState || requireOpen != _popup!.IsOpen
                || !requireOpen && _confirmationState != ConfirmationState.Completed)
                throw new InvalidOperationException("The native time-picker popup is not in the expected operation state.");
            if (_popup.PlacementTarget is { } target ? !BelongsTo(target, _owner) : !BelongsTo(_popup, _owner))
                throw new InvalidOperationException("The native time-picker popup no longer belongs to its editor.");
            if (!ControlTree.EnumerateDescendants(_popup).Contains(_presenter!) && !IsConfirmedCloseCleanup)
                throw new InvalidOperationException("The native time-picker presenter was replaced.");
            ValidateCapabilities(requireEditable: requireOpen);
        }

        private bool IsConfirmedCloseCleanup => _confirmationState == ConfirmationState.Completed && _popupState == PopupState.Closed
            && _popupChildReleased && !_popup!.IsOpen && _popup.Child is null;

        internal void BeginConfirm()
        {
            if (_confirmationState != ConfirmationState.NotStarted)
                throw new InvalidOperationException("The native time-picker confirmation was already attempted.");
            _confirmationState = ConfirmationState.InProgress;
        }

        internal void EndConfirm() => _confirmationState = ConfirmationState.Completed;

        internal void FailConfirm() => _invalid = true;

        internal bool IsPopupOpen()
        {
            ValidateRoot();
            DiscoverSurface();
            if (IsObserving) ValidateCapabilities(requireEditable: _confirmationState != ConfirmationState.Completed);
            return _popup!.IsOpen;
        }

        internal Control ResolvePart(UiControlDefinition definition)
        {
            ValidateRoot();
            var role = definition.TimePickerIntent!.Part;
            if (role == TimePickerPartKind.CommittedRoot) return _owner;
            if (role == TimePickerPartKind.Input)
            {
                if (IsObserving)
                {
                    ValidateCapabilities();
                    if (HasOpened || _popup!.IsOpen)
                        throw new InvalidOperationException("The time-picker opening part cannot be invoked again in this operation.");
                }
                var input = FindInside(_owner, definition.LocatorValue);
                if (_input is not null && !ReferenceEquals(_input, input)) throw new InvalidOperationException("The time-picker input was replaced.");
                return input;
            }
            DiscoverSurface();
            if (IsObserving) ValidateOwner(requireOpen: true);
            var part = role == TimePickerPartKind.Presenter ? _presenter!
                : FindInside(role == TimePickerPartKind.Popup ? _popup! : _presenter!, definition.LocatorValue);
            if (role == TimePickerPartKind.Confirm && _confirm is not null && !ReferenceEquals(_confirm, part))
                throw new InvalidOperationException("The time-picker confirmation button was replaced.");
            if (role == TimePickerPartKind.Confirm)
            {
                if (_pendingValue is not { } pending || _presenter!.Time != pending)
                    throw new InvalidOperationException("The native presenter has no verified pending time to confirm.");
                ValidatePanels(pending, _capabilities!, requireSelected: true);
            }
            return part;
        }

        private void ValidatePanels(TimeSpan value, TimePickerOwnerState capabilities, bool requireSelected)
        {
            var panels = Parts.PresenterParts!;
            foreach (var (kind, selected) in capabilities.GetComponentValues(value))
            {
                var part = panels.Get(kind);
                var (minimum, maximum, step) = capabilities.GetComponentRange(kind);
                if (FindInside(_presenter!, part.SelectorLocator) is not DateTimePickerPanel panel
                    || panel.MinimumValue != minimum || panel.MaximumValue != maximum || panel.Increment != step
                    || panel.SelectedValue < minimum || panel.SelectedValue > maximum
                    || (panel.SelectedValue - minimum) % step != 0
                    || requireSelected && panel.SelectedValue != selected)
                    throw new NotSupportedException("A declared native presenter panel has inconsistent range, precision or selected value.");
                if (_panels.TryGetValue(part.SelectorLocator, out var original) && !ReferenceEquals(original, panel))
                    throw new InvalidOperationException("A native time-picker component panel was replaced.");
                _panels[part.SelectorLocator] = panel;
                if (_publishedOwner is { } published && HasOpened && _openingEpoch is { } epoch)
                {
                    var snapshot = TimePickerRuntimeMetadata.ReadComponent(AutomationProperties.GetItemStatus(panel), kind);
                    TimePickerRuntimeMetadata.RequireIdentity(published.OwnerId, epoch, snapshot.OwnerId, snapshot.Epoch);
                    if (snapshot.Minimum != minimum || snapshot.Maximum != maximum || snapshot.Step != step
                        || snapshot.SelectedValue != panel.SelectedValue)
                        throw new InvalidDataException("Published time-picker component metadata contradicts its native panel.");
                }
                foreach (var locator in new[] { part.IncreaseButtonLocator, part.DecreaseButtonLocator })
                {
                    if (locator is null) continue;
                    var button = FindInside(_presenter!, locator);
                    if (button is not global::Avalonia.Controls.Button)
                        throw new NotSupportedException("A configured native presenter step part is not a button.");
                    if (_stepButtons.TryGetValue(locator, out var previous) && !ReferenceEquals(previous, button))
                        throw new InvalidOperationException("A native presenter step button was replaced.");
                    _stepButtons[locator] = button;
                }
            }
        }

        internal TimeSpan ReadPending() { ValidateOwner(requireOpen: true); return _presenter!.Time; }

        internal void SetPending(TimeSpan value, UiOperationTimeoutBudget budget)
        {
            _ = budget.Remaining;
            _pendingValue = null;
            ValidateOwner(requireOpen: true);
            NativeCapabilities().ValidateWrite(value, Parts);
            ValidatePanels(value, _capabilities!, requireSelected: false);
            _ = budget.Remaining;
            _presenter!.SetCurrentValue(NativePresenter.TimeProperty, value);
            _presenter.UpdateLayout();
            _presenter.Dispatcher.RunJobs();
            _ = budget.Remaining;
            ValidateOwner(requireOpen: true);
            SelectNativePanels(value, _capabilities!, budget);
            ValidatePanels(value, _capabilities!, requireSelected: true);
            if (_presenter.Time != value) throw new InvalidOperationException("The native presenter did not retain the requested pending time.");
            _pendingValue = value;
        }

        private void SelectNativePanels(TimeSpan value, TimePickerOwnerState capabilities, UiOperationTimeoutBudget budget)
        {
            var parts = Parts.PresenterParts!;
            foreach (var (kind, expected) in capabilities.GetComponentValues(value))
            {
                var part = parts.Get(kind);
                var panel = _panels[part.SelectorLocator];
                var limit = (panel.MaximumValue - panel.MinimumValue) / panel.Increment + 1;
                for (var attempt = 0; panel.SelectedValue != expected && attempt < limit; attempt++)
                {
                    _ = budget.Remaining;
                    ValidateOwner(requireOpen: true);
                    ValidatePanels(value, capabilities, requireSelected: false);
                    if (!panel.IsAttachedToVisualTree() || !panel.IsEffectivelyVisible || !panel.IsEffectivelyEnabled)
                        throw new InvalidOperationException("The native presenter panel is unavailable for selection.");
                    var previous = panel.SelectedValue;
                    var direction = previous < expected ? 1 : -1;
                    var buttonLocator = direction > 0 ? part.IncreaseButtonLocator : part.DecreaseButtonLocator;
                    if (buttonLocator is not null)
                    {
                        var button = _stepButtons[buttonLocator];
                        if (!button.IsEffectivelyVisible || !button.IsEffectivelyEnabled)
                            throw new InvalidOperationException("The native presenter step button is unavailable.");
                        _ = budget.Remaining;
                        AutomationElement.InvokeOnUi(button);
                    }
                    else
                    {
                        _ = budget.Remaining;
                        if (!panel.Focus(NavigationMethod.Pointer) || !panel.IsFocused)
                            throw new NotSupportedException("The native presenter panel cannot receive keyboard selection.");
                        var owner = TopLevel.GetTopLevel(panel)
                            ?? throw new InvalidOperationException("The native presenter panel has no owning top-level.");
                        var key = direction > 0 ? Key.Down : Key.Up;
                        ValidateOwner(requireOpen: true);
                        ValidatePanels(value, capabilities, requireSelected: false);
                        if (!panel.IsFocused) throw new InvalidOperationException("The native time-picker panel lost keyboard focus.");
                        _ = budget.Remaining;
                        try { owner.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, keySymbol: null); }
                        finally { owner.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, keySymbol: null); }
                        panel.Dispatcher.RunJobs();
                    }
                    _ = budget.Remaining;
                    ValidateOwner(requireOpen: true);
                    ValidatePanels(value, capabilities, requireSelected: false);
                    if (panel.SelectedValue != previous + direction * panel.Increment)
                        throw new InvalidOperationException("The native presenter did not apply the requested numeric selection step.");
                }
                if (panel.SelectedValue != expected)
                    throw new InvalidOperationException("The native presenter did not reach the requested numeric selection.");
            }
        }

        internal void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var unsubscribe in _unsubscribe) unsubscribe();
            _unsubscribe.Clear();
        }
    }

    private sealed class HeadlessCommittedTime(HeadlessTimePickerBinding binding) : ITimePickerCommittedControl, IUiControlAvailability
    {
        public object OwnerIdentity => binding;
        public bool IsPopupOpen => binding.Resolver.TimePickerUi(binding.IsPopupOpen);
        public string AutomationId => binding.Resolver.TimePickerUi(() => AutomationProperties.GetAutomationId(binding.Owner) ?? string.Empty);
        public string Name => binding.Resolver.TimePickerUi(() => AutomationElement.ReadControlName(binding.Owner));
        public bool IsEnabled => binding.Resolver.TimePickerUi(() => binding.Owner.IsEffectivelyEnabled);
        public bool IsAvailable => binding.Resolver.TimePickerUi(() => binding.Owner.IsAttachedToVisualTree() && binding.Owner.IsEffectivelyVisible);
        public TimeSpan? SelectedTime { get => binding.Resolver.TimePickerUi(binding.ReadCommitted); set => throw new NotSupportedException("The committed UI time source is read-only to automation."); }
        public void ValidateWrite(TimeSpan value) => binding.Resolver.TimePickerUi(() => { binding.ValidateWrite(value); return true; });
        public void Dispose() => binding.Resolver.TimePickerUi(() => { binding.Dispose(); return true; });
    }

    private sealed class HeadlessPresenterTime(HeadlessTimePickerBinding binding) : ITimePickerPresenterControl
    {
        public string AutomationId => binding.Parts.TimePickerLocator;
        public string Name => AutomationId;
        public bool IsEnabled => binding.Resolver.TimePickerUi(() => binding.Owner.IsEffectivelyEnabled);
        public bool IsPopupOpen => binding.Resolver.TimePickerUi(binding.IsPopupOpen);
        public TimeSpan? SelectedTime { get => binding.Resolver.TimePickerUi(binding.ReadPending); set => throw new NotSupportedException("Use the budgeted pending-time operation."); }
        public void SetPendingTime(TimeSpan value, UiOperationTimeoutBudget budget) => binding.Resolver.TimePickerUi(() => { binding.SetPending(value, budget); return true; });
        public void ValidateOwner(bool requireOpen) => binding.Resolver.TimePickerUi(() => { binding.ValidateOwner(requireOpen); return true; });
        public void Dispose() { }
    }

    private sealed class HeadlessTimePickerButton(HeadlessTimePickerBinding binding, UiControlDefinition definition)
        : IButtonControl, IUiControlAvailability, IBudgetedControlInvoke
    {
        private Control Resolve() => binding.ResolvePart(definition);
        public string AutomationId => definition.LocatorValue;
        public string Name => binding.Resolver.TimePickerUi(() => AutomationElement.ReadControlName(Resolve()));
        public bool IsEnabled => binding.Resolver.TimePickerUi(() => Resolve().IsEffectivelyEnabled);
        public bool IsAvailable => binding.Resolver.TimePickerUi(() => Resolve().IsEffectivelyVisible);
        public void Invoke() => Invoke(UiOperationTimeoutBudget.Start(5000, "time-picker button"));
        public void Invoke(UiOperationTimeoutBudget budget) => binding.Resolver.TimePickerUi(() =>
        {
            _ = budget.Remaining;
            var target = Resolve();
            if (!target.IsEffectivelyEnabled) throw new InvalidOperationException("The time-picker button is disabled.");
            _ = budget.Remaining;
            var confirm = definition.TimePickerIntent!.Part == TimePickerPartKind.Confirm;
            if (confirm) binding.BeginConfirm();
            try
            {
                AutomationElement.InvokeOnUi(target);
                if (confirm) binding.EndConfirm();
            }
            catch
            {
                if (confirm) binding.FailConfirm();
                throw;
            }
            _ = budget.Remaining;
            return true;
        });
    }
}
