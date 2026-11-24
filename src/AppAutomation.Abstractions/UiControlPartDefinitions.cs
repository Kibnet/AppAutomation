namespace AppAutomation.Abstractions;

internal readonly struct UiControlPartDefinitions(
    UiControlDefinition owner,
    UiLocatorKind locatorKind,
    bool fallbackToName,
    string? propertyName = null)
{
    public UiControlDefinition Create(string suffix, UiControlType controlType, string? locatorValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locatorValue);

        return new UiControlDefinition((propertyName ?? owner.PropertyName) + suffix, controlType, locatorValue, locatorKind, fallbackToName)
        {
            Scope = owner.Scope
        };
    }
}
