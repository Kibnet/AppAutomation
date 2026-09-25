namespace AppAutomation.Abstractions;

/// <summary>Describes how to read a stable business key from a repeated item.</summary>
public sealed record RepeatedItemKeyDefinition
{
    private RepeatedItemKeyDefinition(
        UiAutomationValueProperty property,
        MultiItemRelativeLocator? source)
    {
        if (!Enum.IsDefined(property))
        {
            throw new ArgumentOutOfRangeException(nameof(property), property, "Unsupported automation property.");
        }

        if (source is not null && source.Scope != MultiItemRelativeLocatorScope.ItemRoot)
        {
            throw new ArgumentException("An item key source must be resolved from ItemRoot.", nameof(source));
        }

        Property = property;
        Source = source;
    }

    public UiAutomationValueProperty Property { get; }

    public MultiItemRelativeLocator? Source { get; }

    public static RepeatedItemKeyDefinition FromItem(UiAutomationValueProperty property) =>
        new(property, source: null);

    public static RepeatedItemKeyDefinition FromPart(
        MultiItemRelativeLocator source,
        UiAutomationValueProperty property)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new RepeatedItemKeyDefinition(property, source);
    }
}
