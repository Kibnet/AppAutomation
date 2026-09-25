namespace AppAutomation.Abstractions;

/// <summary>Describes a locator resolved relative to a repeated-control scope.</summary>
public sealed record MultiItemRelativeLocator
{
    public MultiItemRelativeLocator(
        string locatorValue,
        MultiItemRelativeLocatorScope scope,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locatorValue);
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unsupported multi-item locator scope.");
        }

        if (!Enum.IsDefined(locatorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(locatorKind), locatorKind, "Unsupported locator kind.");
        }

        LocatorValue = locatorValue.Trim();
        Scope = scope;
        LocatorKind = locatorKind;
        FallbackToName = fallbackToName;
    }

    public string LocatorValue { get; }

    public MultiItemRelativeLocatorScope Scope { get; }

    public UiLocatorKind LocatorKind { get; }

    public bool FallbackToName { get; }

    public static MultiItemRelativeLocator ByAutomationId(
        string automationId,
        MultiItemRelativeLocatorScope scope,
        bool fallbackToName = false) =>
        new(automationId, scope, UiLocatorKind.AutomationId, fallbackToName);

    public static MultiItemRelativeLocator ByName(
        string name,
        MultiItemRelativeLocatorScope scope) =>
        new(name, scope, UiLocatorKind.Name);
}
