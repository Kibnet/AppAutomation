using System.Globalization;

namespace AppAutomation.Abstractions;

/// <summary>
/// Describes when a composite time picker commits its pending value.
/// </summary>
public enum TimePickerCommitMode
{
    /// <summary>
    /// Changing the time surface commits the value immediately.
    /// </summary>
    Immediate = 0,

    /// <summary>
    /// The pending value is committed by a separate confirmation button.
    /// </summary>
    Confirm = 1
}

/// <summary>
/// Provider-neutral parts of a logical time picker.
/// </summary>
public sealed record TimePickerParts(
    string RootLocator,
    string TimePickerLocator,
    string? InputLocator = null,
    string? OpenButtonLocator = null,
    string? PopupRootLocator = null,
    string? ConfirmButtonLocator = null,
    string? CancelButtonLocator = null,
    TimePickerCommitMode CommitMode = TimePickerCommitMode.Immediate,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true)
{
    /// <summary>Defaults to the existing native TimePicker path.</summary>
    public TimePickerSurfaceKind SurfaceKind { get; init; }
    /// <summary>ClickInput uses the real InputLocator without treating it as a button.</summary>
    public TimePickerOpenKind OpenKind { get; init; }
    /// <summary>The declared public TimeSpan/TimeSpan? UI property on the committed root.</summary>
    public string? CommittedValueProperty { get; init; }
    /// <summary>Required real component panels for presenter mode.</summary>
    public TimePickerPresenterParts? PresenterParts { get; init; }
    /// <summary>Optional exact detached-popup search scope; ownership is verified independently.</summary>
    public UiControlScope? PopupScope { get; init; }

    internal void ValidateConfiguration()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RootLocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(TimePickerLocator);
        if (!Enum.IsDefined(SurfaceKind) || !Enum.IsDefined(OpenKind) || !Enum.IsDefined(CommitMode))
            throw new ArgumentException("Invalid time-picker surface, opening or commit mode.");
        if (CommitMode == TimePickerCommitMode.Confirm && string.IsNullOrWhiteSpace(ConfirmButtonLocator))
            throw new ArgumentException("A confirmation button locator is required for confirmed time pickers.");
        if (OpenKind == TimePickerOpenKind.ClickInput && string.IsNullOrWhiteSpace(InputLocator))
            throw new ArgumentException("ClickInput requires an input locator.");
        if (SurfaceKind != TimePickerSurfaceKind.TimePickerPresenter) return;
        if (CommitMode != TimePickerCommitMode.Confirm || PresenterParts is null
            || string.IsNullOrWhiteSpace(PopupRootLocator))
            throw new ArgumentException("A presenter requires Confirm mode, component parts and a popup locator.");
        ArgumentException.ThrowIfNullOrWhiteSpace(CommittedValueProperty);
        if (CommittedValueProperty.Any(static character => !char.IsLetterOrDigit(character) && character != '_')
            || !(char.IsLetter(CommittedValueProperty[0]) || CommittedValueProperty[0] == '_'))
            throw new ArgumentException("The committed source must name one public UI property, not a model path.");
        if (OpenKind == TimePickerOpenKind.ClickInput && !string.IsNullOrWhiteSpace(OpenButtonLocator))
            throw new ArgumentException("ClickInput cannot also configure a separate open button.");
        if (OpenKind == TimePickerOpenKind.InvokeButton && string.IsNullOrWhiteSpace(OpenButtonLocator))
            throw new ArgumentException("A presenter opened by InvokeButton requires an open button locator.");
        foreach (var component in new[] { PresenterParts.Hours, PresenterParts.Minutes, PresenterParts.Seconds, PresenterParts.Period })
            if (component is not null) ArgumentException.ThrowIfNullOrWhiteSpace(component.SelectorLocator);
        ArgumentNullException.ThrowIfNull(PresenterParts.Hours);
        ArgumentNullException.ThrowIfNull(PresenterParts.Minutes);
    }

    public static TimePickerParts ByAutomationIds(
        string rootAutomationId,
        string timePickerAutomationId,
        string? inputAutomationId = null,
        string? openButtonAutomationId = null,
        string? popupRootAutomationId = null,
        string? confirmButtonAutomationId = null,
        string? cancelButtonAutomationId = null,
        TimePickerCommitMode commitMode = TimePickerCommitMode.Immediate)
    {
        return new TimePickerParts(
            rootAutomationId,
            timePickerAutomationId,
            inputAutomationId,
            openButtonAutomationId,
            popupRootAutomationId,
            confirmButtonAutomationId,
            cancelButtonAutomationId,
            commitMode);
    }
}

public static partial class UiControlResolverExtensions
{
    /// <summary>
    /// Registers a logical time picker composed from a real time surface and optional popup buttons.
    /// </summary>
    public static IUiControlResolver WithTimePicker(
        this IUiControlResolver innerResolver,
        string propertyName,
        TimePickerParts parts)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);
        return innerResolver.WithAdapters(new TimePickerControlAdapter(propertyName, parts));
    }
}

/// <summary>
/// Resolves one logical time picker through provider-native primitive controls.
/// </summary>
public sealed class TimePickerControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly TimePickerParts _parts;

    public TimePickerControlAdapter(string propertyName, TimePickerParts parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        _propertyName = propertyName.Trim();
        _parts = parts ?? throw new ArgumentNullException(nameof(parts));
        parts.ValidateConfiguration();
    }

    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        return requestedType == typeof(ITimePickerControl)
            && definition.TimePickerIntent is null
            && definition.ControlType == UiControlType.TimePicker
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        return new CompositeTimePickerControl(definition, _propertyName, _parts, innerResolver);
    }

    private sealed class CompositeTimePickerControl : ITimePickerControl, IUiControlAvailability, ITimePickerOperationControl
    {
        private readonly string _propertyName;
        private readonly TimePickerParts _parts;
        private readonly IUiControlResolver _resolver;
        private readonly UiControlDefinition _definition;

        public CompositeTimePickerControl(
            UiControlDefinition definition,
            string propertyName,
            TimePickerParts parts,
            IUiControlResolver resolver)
        {
            _definition = definition;
            AutomationId = definition.LocatorValue;
            _propertyName = propertyName;
            _parts = parts;
            _resolver = resolver;
        }

        public string AutomationId { get; }

        public string Name => TryResolveRoot()?.Name ?? TryResolveOpenButton()?.Name ?? TryResolveTimePicker()?.Name ?? AutomationId;

        public bool IsEnabled => TryResolveRoot()?.IsEnabled == true;

        public bool IsAvailable => TryResolveRoot() is { } root
            && (root as IUiControlAvailability)?.IsAvailable != false;

        public TimeSpan? SelectedTime
        {
            get => TryReadCommittedTime();
            set
            {
                if (value is null)
                {
                    throw new ArgumentNullException(nameof(value), "A composite time picker cannot commit a null time.");
                }

                SetSelectedTime(value.Value, timeoutMs: 5000);
            }
        }

        public void SetSelectedTime(TimeSpan value, int timeoutMs)
        {
            if (timeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), timeoutMs, "Timeout must be positive.");
            }

            SetSelectedTime(value, UiOperationTimeoutBudget.Start(timeoutMs, "time-picker"));
        }

        public void SetSelectedTime(TimeSpan value, UiOperationTimeoutBudget budget)
        {
            _ = budget.Remaining;
            WaitForRoot(budget.Remaining);
            if (_parts.SurfaceKind == TimePickerSurfaceKind.TimePickerPresenter)
            {
                SetPresenterTime(value, budget);
                return;
            }
            OpenEditor(budget);
            var timePicker = WaitForTimePicker(budget.Remaining);
            timePicker.SelectedTime = value;

            if (_parts.CommitMode == TimePickerCommitMode.Confirm)
            {
                InvokeOptionalButton(_parts.ConfirmButtonLocator, "Confirm", budget.Remaining);
            }

            if (!string.IsNullOrWhiteSpace(_parts.PopupRootLocator))
            {
                WaitForPopupClosure(budget.Remaining);
            }

            WaitForCommittedTime(value, budget.Remaining);
        }

        private ITimePickerControl ResolveTimePicker()
        {
            return _resolver.Resolve<ITimePickerControl>(CreateDefinition(
                "TimePicker",
                UiControlType.TimePicker,
                _parts.TimePickerLocator));
        }

        private ITimePickerControl? TryResolveTimePicker()
        {
            try
            {
                return ResolveTimePicker();
            }
            catch (UiControlResolutionException exception) when (exception.IsTransient)
            {
                return null;
            }
        }

        private IButtonControl? TryResolveOpenButton()
        {
            if (string.IsNullOrWhiteSpace(_parts.OpenButtonLocator))
            {
                return null;
            }

            try
            {
                return ResolveButton("Open", _parts.OpenButtonLocator);
            }
            catch (UiControlResolutionException exception) when (exception.IsTransient)
            {
                return null;
            }
        }

        private TimeSpan? TryReadCommittedTime()
        {
            if (_parts.SurfaceKind == TimePickerSurfaceKind.TimePickerPresenter)
            {
                using var committed = ResolveCommittedRoot();
                return committed.SelectedTime;
            }
            if (_parts.CommitMode == TimePickerCommitMode.Immediate
                && TryResolveTimePicker()?.SelectedTime is { } selectedTime)
            {
                return selectedTime;
            }

            var inputTime = TryReadTime(_parts.InputLocator, "Input");
            if (inputTime is not null)
            {
                return inputTime;
            }

            return TryReadTime(_parts.RootLocator, "CommittedRoot");
        }

        private TimeSpan? TryReadTime(string? locator, string suffix)
        {
            if (string.IsNullOrWhiteSpace(locator))
            {
                return null;
            }

            try
            {
                try
                {
                    return suffix == "CommittedRoot"
                        ? _resolver.Resolve<ITimePickerControl>(CreateDefinition(suffix, UiControlType.TimePicker, locator)).SelectedTime
                        : TryParseTime(_resolver.Resolve<ITextBoxControl>(CreateDefinition(suffix, UiControlType.TextBox, locator)).Text);
                }
                catch (UiControlResolutionException exception) when (exception.Failure == UiControlResolutionFailure.TypeMismatch)
                {
                    var control = _resolver.Resolve<IUiControl>(
                        CreateDefinition(suffix, UiControlType.AutomationElement, locator));
                    var text = control switch
                    {
                        IReadableTextControl readable => readable.Text,
                        _ when suffix == "CommittedRoot" => null,
                        _ => throw new NotSupportedException($"Time-picker input '{locator}' must expose readable text.")
                    };
                    return TryParseTime(text);
                }
            }
            catch (UiControlResolutionException exception) when (exception.IsTransient)
            {
                return null;
            }
        }

        private static TimeSpan? TryParseTime(string? text)
        {
            var value = text?.Trim();
            return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
                || TimeSpan.TryParse(value, CultureInfo.CurrentCulture, out parsed)
                ? parsed
                : null;
        }

        private void WaitForCommittedTime(TimeSpan expected, TimeSpan timeout)
        {
            UiWait.Until(
                TryReadCommittedTime,
                actual => actual == expected,
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Time picker '{_parts.RootLocator}' did not reach expected time '{expected:c}'.");
        }

        private void WaitForRoot(TimeSpan timeout)
        {
            UiWait.Until(
                () => TryResolveRoot() is { IsEnabled: true } root
                    && (root as IUiControlAvailability)?.IsAvailable != false,
                static ready => ready,
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Time picker '{_parts.RootLocator}' did not become available.");
        }

        private IUiControl? TryResolveRoot()
        {
            try
            {
                return _resolver.Resolve<IUiControl>(CreateDefinition(
                    "Root",
                    UiControlType.AutomationElement,
                    _parts.RootLocator));
            }
            catch (UiControlResolutionException exception) when (exception.IsTransient)
            {
                return null;
            }
        }

        private ITimePickerControl WaitForTimePicker(TimeSpan timeout)
        {
            ITimePickerControl? resolved = null;
            UiWait.Until(
                () =>
                {
                    resolved = TryResolveTimePicker();
                    return resolved is not null
                        && resolved.IsEnabled
                        && (resolved as IUiControlAvailability)?.IsAvailable != false;
                },
                static ready => ready,
                new UiWaitOptions
                {
                    Timeout = timeout,
                    PollInterval = TimeSpan.FromMilliseconds(50)
                },
                $"Time picker surface '{_parts.TimePickerLocator}' did not become available.");
            return resolved!;
        }

        private void InvokeOptionalButton(string? locator, string suffix, TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(locator))
            {
                return;
            }

            IButtonControl? button = null;
            UiWait.Until(
                () =>
                {
                    try
                    {
                        button = ResolveButton(suffix, locator);
                        return button.IsEnabled;
                    }
                    catch (UiControlResolutionException exception) when (exception.IsTransient)
                    {
                        return false;
                    }
                },
                static ready => ready,
                new UiWaitOptions
                {
                    Timeout = timeout,
                    PollInterval = TimeSpan.FromMilliseconds(50)
                },
                $"Time picker {suffix.ToLowerInvariant()} button '{locator}' did not become available.");
            button!.Invoke();
        }

        private void WaitForPopupClosure(TimeSpan timeout)
        {
            UiWait.Until(
                IsPopupAvailable,
                static available => !available,
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Time picker popup '{_parts.PopupRootLocator}' did not close.");
        }

        private bool IsPopupAvailable()
        {
            IUiControl popup;
            try
            {
                popup = _resolver.Resolve<IUiControl>(CreateDefinition(
                    "PopupRoot",
                    UiControlType.AutomationElement,
                    _parts.PopupRootLocator!));
            }
            catch (UiControlResolutionException exception) when (exception.IsTransient)
            {
                return false;
            }

            return popup is IUiControlAvailability availability
                ? availability.IsAvailable
                : throw new InvalidOperationException(
                    $"Time picker popup '{_parts.PopupRootLocator}' must expose {nameof(IUiControlAvailability)}.");
        }

        private IButtonControl ResolveButton(string suffix, string locator)
        {
            return _resolver.Resolve<IButtonControl>(CreateDefinition(suffix, UiControlType.Button, locator));
        }

        private UiControlDefinition CreateDefinition(string suffix, UiControlType type, string locator)
        {
            return new UiControlDefinition(
                $"{_propertyName}{suffix}",
                type,
                locator,
                _parts.LocatorKind,
                _parts.FallbackToName)
            {
                Scope = suffix == "PopupRoot" && _parts.PopupScope is not null ? _parts.PopupScope : _definition.Scope
            };
        }

        private ITimePickerCommittedControl ResolveCommittedRoot()
        {
            var definition = CreateTimeDefinition("CommittedRoot", UiControlType.TimePicker,
                _parts.RootLocator, TimePickerPartKind.CommittedRoot);
            return _resolver.Resolve<ITimePickerControl>(definition) as ITimePickerCommittedControl
                ?? throw new NotSupportedException("The provider cannot read the declared committed UI time value.");
        }

        private UiControlDefinition CreateTimeDefinition(string suffix, UiControlType type, string locator,
            TimePickerPartKind part, object? identity = null) => CreateDefinition(suffix, type, locator) with
        {
            TimePickerIntent = new TimePickerResolveDescriptor(_parts,
                CreateDefinition("Owner", UiControlType.AutomationElement, _parts.RootLocator), part,
                OwnerIdentity: identity)
        };

        private void OpenEditor(UiOperationTimeoutBudget budget, object? identity = null)
        {
            if (_parts.OpenKind == TimePickerOpenKind.ClickInput)
            {
                var definition = _parts.SurfaceKind == TimePickerSurfaceKind.TimePickerPresenter
                    ? CreateTimeDefinition("Input", UiControlType.TextBox, _parts.InputLocator!, TimePickerPartKind.Input, identity)
                    : CreateDefinition("Input", UiControlType.TextBox, _parts.InputLocator!);
                var input = _resolver.Resolve<ITextBoxControl>(definition);
                if (input is not IBudgetedControlClick clickable)
                    throw new NotSupportedException("The provider cannot open the time editor by clicking its input.");
                clickable.Click(budget);
                return;
            }
            if (string.IsNullOrWhiteSpace(_parts.OpenButtonLocator)) return;
            if (_parts.SurfaceKind == TimePickerSurfaceKind.TimePickerPresenter)
                InvokeTimePart(_parts.OpenButtonLocator, "Open", TimePickerPartKind.Input, identity!, budget);
            else InvokeOptionalButton(_parts.OpenButtonLocator, "Open", budget.Remaining);
        }

        private void InvokeTimePart(string locator, string suffix, TimePickerPartKind part, object identity,
            UiOperationTimeoutBudget budget)
        {
            var definition = CreateTimeDefinition(suffix, UiControlType.Button, locator, part, identity);
            var button = _resolver.Resolve<IButtonControl>(definition);
            if (button is not IBudgetedControlInvoke invokable)
                throw new NotSupportedException("The provider cannot invoke a time-editor part within the original budget.");
            invokable.Invoke(budget);
        }

        private void SetPresenterTime(TimeSpan value, UiOperationTimeoutBudget budget)
        {
            using var committed = ResolveCommittedRoot();
            committed.ValidateWrite(value);
            _ = budget.Remaining;
            var identity = committed.OwnerIdentity;
            if (!committed.IsPopupOpen) OpenEditor(budget, identity);
            ITimePickerPresenterControl? surface = null;
            try
            {
                UiWait.Until(() =>
                {
                    try
                    {
                        surface = _resolver.Resolve<ITimePickerControl>(CreateTimeDefinition("Presenter",
                            UiControlType.TimePicker, _parts.TimePickerLocator, TimePickerPartKind.Presenter, identity))
                            as ITimePickerPresenterControl
                            ?? throw new NotSupportedException("The provider does not support the declared presenter surface.");
                        surface.ValidateOwner(requireOpen: true);
                        return true;
                    }
                    catch (UiControlResolutionException error) when (error.IsTransient)
                    {
                        surface?.Dispose();
                        surface = null;
                        return false;
                    }
                }, static ready => ready, new UiWaitOptions { Timeout = budget.Remaining, PollInterval = TimeSpan.FromMilliseconds(50) },
                    "The owner-bound time presenter did not open.");
                surface!.SetPendingTime(value, budget);
                surface.ValidateOwner(requireOpen: true);
                _ = budget.Remaining;
                InvokeTimePart(_parts.ConfirmButtonLocator!, "Confirm", TimePickerPartKind.Confirm, identity, budget);
                UiWait.Until(() => !surface.IsPopupOpen, static closed => closed,
                    new UiWaitOptions { Timeout = budget.Remaining, PollInterval = TimeSpan.FromMilliseconds(50) },
                    "The confirmed time presenter did not close.");
                UiWait.Until(() =>
                {
                    surface.ValidateOwner(requireOpen: false);
                    return committed.SelectedTime == value;
                }, static equal => equal, new UiWaitOptions { Timeout = budget.Remaining, PollInterval = TimeSpan.FromMilliseconds(50) },
                    "The time editor did not commit the requested exact value.");
                _ = budget.Remaining;
            }
            finally { surface?.Dispose(); }
        }
    }
}

internal interface ITimePickerOperationControl
{
    void SetSelectedTime(TimeSpan value, int timeoutMs);
    void SetSelectedTime(TimeSpan value, UiOperationTimeoutBudget budget) => SetSelectedTime(value, budget.RemainingMilliseconds);
}
