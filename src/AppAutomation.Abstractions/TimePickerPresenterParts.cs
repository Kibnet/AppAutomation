namespace AppAutomation.Abstractions;

/// <summary>The native surface used by a configured logical time picker.</summary>
public enum TimePickerSurfaceKind { TimePicker, TimePickerPresenter }

/// <summary>The real user action which opens a configured editor.</summary>
public enum TimePickerOpenKind { InvokeButton, ClickInput }

/// <summary>A numeric component of the real presenter.</summary>
public enum TimePickerComponentKind { Hour, Minute, Second, Period }

/// <summary>Locators for one real picker panel and its optional standard step buttons.</summary>
public sealed record TimePickerComponentParts(string SelectorLocator,
    string? IncreaseButtonLocator = null, string? DecreaseButtonLocator = null);

/// <summary>The actual component panels of a confirmed presenter.</summary>
public sealed record TimePickerPresenterParts(TimePickerComponentParts Hours, TimePickerComponentParts Minutes,
    TimePickerComponentParts? Seconds = null, TimePickerComponentParts? Period = null)
{
    internal TimePickerComponentParts Get(TimePickerComponentKind kind) => kind switch
    {
        TimePickerComponentKind.Hour => Hours,
        TimePickerComponentKind.Minute => Minutes,
        TimePickerComponentKind.Second => Seconds ?? throw new NotSupportedException("The presenter has no seconds panel."),
        TimePickerComponentKind.Period => Period ?? throw new NotSupportedException("The presenter has no AM/PM panel."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

internal enum TimePickerPartKind { CommittedRoot, Presenter, Input, Popup, Confirm, Cancel, Component, Increase, Decrease }

// Runtime-only intent transported by ordinary Resolve<T> wrappers; never persisted in a manifest or step.
internal sealed record TimePickerResolveDescriptor(TimePickerParts Parts, UiControlDefinition OwnerDefinition,
    TimePickerPartKind Part, TimePickerComponentKind? Component = null, object? OwnerIdentity = null);

internal interface IBudgetedControlClick
{
    void Click(UiOperationTimeoutBudget budget);
}

internal interface IBudgetedControlInvoke
{
    void Invoke(UiOperationTimeoutBudget budget);
}

internal interface ITimePickerCommittedControl : ITimePickerControl, IDisposable
{
    object OwnerIdentity { get; }
    bool IsPopupOpen { get; }
    void ValidateWrite(TimeSpan value);
}

internal interface ITimePickerPresenterControl : ITimePickerControl, IDisposable
{
    bool IsPopupOpen { get; }
    void SetPendingTime(TimeSpan value, UiOperationTimeoutBudget budget);
    void ValidateOwner(bool requireOpen);
}
