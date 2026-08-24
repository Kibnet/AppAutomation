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
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.RootLocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.TimePickerLocator);
        if (parts.CommitMode == TimePickerCommitMode.Confirm
            && string.IsNullOrWhiteSpace(parts.ConfirmButtonLocator))
        {
            throw new ArgumentException("A confirmation button locator is required for confirmed time pickers.", nameof(parts));
        }
    }

    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        return requestedType == typeof(ITimePickerControl)
            && definition.ControlType == UiControlType.TimePicker
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        return new CompositeTimePickerControl(definition.LocatorValue, _propertyName, _parts, innerResolver);
    }

    private sealed class CompositeTimePickerControl : ITimePickerControl, IUiControlAvailability, ITimePickerOperationControl
    {
        private readonly string _propertyName;
        private readonly TimePickerParts _parts;
        private readonly IUiControlResolver _resolver;

        public CompositeTimePickerControl(
            string logicalAutomationId,
            string propertyName,
            TimePickerParts parts,
            IUiControlResolver resolver)
        {
            AutomationId = logicalAutomationId;
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

            var budget = UiOperationTimeoutBudget.Start(timeoutMs, "time-picker");
            WaitForRoot(budget.Remaining);
            InvokeOptionalButton(_parts.OpenButtonLocator, "Open", budget.Remaining);
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
            catch (InvalidOperationException)
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
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private TimeSpan? TryReadCommittedTime()
        {
            if (_parts.CommitMode == TimePickerCommitMode.Immediate
                && TryResolveTimePicker()?.SelectedTime is { } selectedTime)
            {
                return selectedTime;
            }

            var inputTime = TryReadTime(_parts.InputLocator, "Input", UiControlType.TextBox);
            if (inputTime is not null)
            {
                return inputTime;
            }

            return TryReadTime(_parts.RootLocator, "CommittedRoot", UiControlType.TimePicker)
                ?? TryReadTime(_parts.RootLocator, "CommittedRootText", UiControlType.Label);
        }

        private TimeSpan? TryReadTime(string? locator, string suffix, UiControlType controlType)
        {
            if (string.IsNullOrWhiteSpace(locator))
            {
                return null;
            }

            try
            {
                if (controlType == UiControlType.TimePicker)
                {
                    return _resolver.Resolve<ITimePickerControl>(
                        CreateDefinition(suffix, controlType, locator)).SelectedTime;
                }

                var text = controlType == UiControlType.TextBox
                    ? _resolver.Resolve<ITextBoxControl>(CreateDefinition(suffix, controlType, locator)).Text
                    : _resolver.Resolve<IReadableTextControl>(CreateDefinition(suffix, controlType, locator)).Text;
                return TryParseTime(text);
            }
            catch (InvalidOperationException)
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
            catch (InvalidOperationException)
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
                    catch (InvalidOperationException)
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
            catch (InvalidOperationException)
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
                _parts.FallbackToName);
        }
    }
}

internal interface ITimePickerOperationControl
{
    void SetSelectedTime(TimeSpan value, int timeoutMs);
}
