namespace AppAutomation.Abstractions;

/// <summary>Describes how an editor part is located relative to the active cell transaction.</summary>
public sealed record GridRelativeLocator
{
    public GridRelativeLocator(
        string locatorValue,
        GridRelativeLocatorScope scope = GridRelativeLocatorScope.EditorRoot,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId)
    {
        LocatorValue = GridAutomationDefinition.NormalizeRequired(locatorValue, nameof(locatorValue));
        Scope = scope;
        LocatorKind = locatorKind;
    }

    public string LocatorValue { get; }

    public GridRelativeLocatorScope Scope { get; }

    public UiLocatorKind LocatorKind { get; }
}
