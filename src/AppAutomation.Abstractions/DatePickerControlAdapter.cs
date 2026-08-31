using System.Globalization;

namespace AppAutomation.Abstractions;

/// <summary>
/// Provider-neutral parts of a logical date picker whose popup and value surface can be separate controls.
/// </summary>
public sealed record DatePickerParts(
    string RootLocator,
    string ValueLocator,
    string? OpenButtonLocator = null,
    string? CalendarLocator = null,
    string? PopupRootLocator = null,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true)
{
    /// <summary>
    /// Creates date-picker parts addressed by automation IDs.
    /// </summary>
    public static DatePickerParts ByAutomationIds(
        string rootAutomationId,
        string valueAutomationId,
        string? openButtonAutomationId = null,
        string? calendarAutomationId = null,
        string? popupRootAutomationId = null)
    {
        return new DatePickerParts(
            rootAutomationId,
            valueAutomationId,
            openButtonAutomationId,
            calendarAutomationId,
            popupRootAutomationId);
    }
}

public static partial class UiControlResolverExtensions
{
    /// <summary>
    /// Registers a logical date-time picker composed from stable primitive parts.
    /// </summary>
    public static IUiControlResolver WithDateTimePickerProxy(
        this IUiControlResolver innerResolver,
        string propertyName,
        DatePickerParts parts)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);
        return innerResolver.WithAdapters(new DatePickerControlAdapter(propertyName, parts));
    }
}

/// <summary>
/// Resolves a logical date picker through provider-native primitive controls.
/// </summary>
public sealed class DatePickerControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly DatePickerParts _parts;

    public DatePickerControlAdapter(string propertyName, DatePickerParts parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.RootLocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.ValueLocator);

        _propertyName = propertyName.Trim();
        _parts = parts;
    }

    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        return requestedType == typeof(IDateTimePickerControl)
            && definition.ControlType == UiControlType.DateTimePicker
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        return new CompositeDatePickerControl(definition.LocatorValue, _propertyName, _parts, innerResolver);
    }

    private sealed class CompositeDatePickerControl :
        IDateTimePickerControl,
        IUiControlAvailability,
        IDateTimePickerOperationControl
    {
        private readonly string _propertyName;
        private readonly DatePickerParts _parts;
        private readonly IUiControlResolver _resolver;

        public CompositeDatePickerControl(
            string automationId,
            string propertyName,
            DatePickerParts parts,
            IUiControlResolver resolver)
        {
            AutomationId = automationId;
            _propertyName = propertyName;
            _parts = parts;
            _resolver = resolver;
        }

        public string AutomationId { get; }

        public string Name => TryResolveRoot()?.Name ?? TryResolveValueControl()?.Name ?? AutomationId;

        public bool IsEnabled => TryResolveRoot()?.IsEnabled == true;

        public bool IsAvailable => TryResolveRoot() is { } root
            && (root as IUiControlAvailability)?.IsAvailable != false;

        public DateTime? SelectedDate
        {
            get => TryReadCommittedDate();
            set
            {
                if (value is null)
                {
                    throw new ArgumentNullException(nameof(value), "A composite date picker cannot select a null date.");
                }

                SetSelectedDate(value.Value, timeoutMs: 60_000);
            }
        }

        public void SetSelectedDate(DateTime value, int timeoutMs)
        {
            var expected = value.Date;
            var budget = UiOperationTimeoutBudget.Start(timeoutMs, "date-picker");
            WaitForRoot(budget.Remaining);

            if (!string.IsNullOrWhiteSpace(_parts.CalendarLocator))
            {
                InvokeOpenButton(budget.Remaining);
                WaitForCalendar(budget.Remaining).SelectDate(expected);
                if (!string.IsNullOrWhiteSpace(_parts.PopupRootLocator))
                {
                    WaitForPopupClosure(budget.Remaining);
                }
            }
            else if (TryResolveNativeValue() is { } nativeValue)
            {
                nativeValue.SelectedDate = expected;
            }
            else if (TryResolveTextValue() is { } textValue)
            {
                textValue.Text = expected.ToString("d", CultureInfo.CurrentCulture);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Date picker value surface '{_parts.ValueLocator}' is not available.");
            }

            UiWait.Until(
                TryReadCommittedDate,
                actual => actual?.Date == expected,
                new UiWaitOptions { Timeout = budget.Remaining, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Date picker '{_parts.RootLocator}' did not reach expected date '{expected:yyyy-MM-dd}'.");
        }

        private DateTime? TryReadCommittedDate()
        {
            if (TryResolveNativeValue()?.SelectedDate is { } selectedDate)
            {
                return selectedDate.Date;
            }

            var text = TryResolveTextValue()?.Text;
            return TryParseDate(text, out var parsed) ? parsed.Date : null;
        }

        private IUiControl? TryResolveValueControl()
        {
            if (TryResolveNativeValue() is { } nativeValue)
            {
                return nativeValue;
            }

            return TryResolveTextValue();
        }

        private IDateTimePickerControl? TryResolveNativeValue()
        {
            try
            {
                return _resolver.Resolve<IDateTimePickerControl>(CreateDefinition(
                    "Value",
                    UiControlType.DateTimePicker,
                    _parts.ValueLocator));
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private ITextBoxControl? TryResolveTextValue()
        {
            try
            {
                return _resolver.Resolve<ITextBoxControl>(CreateDefinition(
                    "ValueText",
                    UiControlType.TextBox,
                    _parts.ValueLocator));
            }
            catch (InvalidOperationException)
            {
                return null;
            }
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

        private void WaitForRoot(TimeSpan timeout)
        {
            UiWait.Until(
                () => TryResolveRoot() is { IsEnabled: true } root
                    && (root as IUiControlAvailability)?.IsAvailable != false,
                static ready => ready,
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Date picker '{_parts.RootLocator}' did not become available.");
        }

        private void InvokeOpenButton(TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(_parts.OpenButtonLocator))
            {
                return;
            }

            IButtonControl? button = null;
            UiWait.Until(
                () =>
                {
                    try
                    {
                        button = _resolver.Resolve<IButtonControl>(CreateDefinition(
                            "Open",
                            UiControlType.Button,
                            _parts.OpenButtonLocator));
                        return button.IsEnabled;
                    }
                    catch (InvalidOperationException)
                    {
                        return false;
                    }
                },
                static ready => ready,
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Date picker open button '{_parts.OpenButtonLocator}' did not become available.");
            button!.Invoke();
        }

        private ICalendarControl WaitForCalendar(TimeSpan timeout)
        {
            ICalendarControl? calendar = null;
            UiWait.Until(
                () =>
                {
                    try
                    {
                        calendar = _resolver.Resolve<ICalendarControl>(CreateDefinition(
                            "Calendar",
                            UiControlType.Calendar,
                            _parts.CalendarLocator!));
                        return calendar.IsEnabled
                            && (calendar as IUiControlAvailability)?.IsAvailable != false;
                    }
                    catch (InvalidOperationException)
                    {
                        return false;
                    }
                },
                static ready => ready,
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Date picker calendar '{_parts.CalendarLocator}' did not become available.");
            return calendar!;
        }

        private void WaitForPopupClosure(TimeSpan timeout)
        {
            UiWait.Until(
                IsPopupAvailable,
                static available => !available,
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Date picker popup '{_parts.PopupRootLocator}' did not close.");
        }

        private bool IsPopupAvailable()
        {
            try
            {
                var popup = _resolver.Resolve<IUiControl>(CreateDefinition(
                    "PopupRoot",
                    UiControlType.AutomationElement,
                    _parts.PopupRootLocator!));
                return popup is IUiControlAvailability availability
                    ? availability.IsAvailable
                    : true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
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

        private static bool TryParseDate(string? text, out DateTime date)
        {
            var value = text?.Trim();
            return DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out date)
                || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date);
        }
    }
}

internal interface IDateTimePickerOperationControl
{
    void SetSelectedDate(DateTime value, int timeoutMs);
}
