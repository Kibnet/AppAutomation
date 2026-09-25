namespace AppAutomation.Abstractions;

/// <summary>Describes one logical collection of repeated composite controls.</summary>
public sealed record MultiItemControlDefinition
{
    private MultiItemControlDefinition(
        string pagePropertyName,
        string captureLocatorValue,
        UiLocatorKind captureLocatorKind,
        string runtimeLocatorValue,
        UiLocatorKind runtimeLocatorKind,
        bool runtimeFallbackToName)
    {
        PagePropertyName = Normalize(pagePropertyName, nameof(pagePropertyName));
        CaptureLocatorValue = Normalize(captureLocatorValue, nameof(captureLocatorValue));
        RuntimeLocatorValue = Normalize(runtimeLocatorValue, nameof(runtimeLocatorValue));
        CaptureLocatorKind = captureLocatorKind;
        RuntimeLocatorKind = runtimeLocatorKind;
        RuntimeFallbackToName = runtimeFallbackToName;
    }

    public string PagePropertyName { get; }

    public string CaptureLocatorValue { get; }

    public UiLocatorKind CaptureLocatorKind { get; }

    public string RuntimeLocatorValue { get; }

    public UiLocatorKind RuntimeLocatorKind { get; }

    public bool RuntimeFallbackToName { get; }

    public MultiItemRelativeLocator? ItemContainerLocator { get; private init; }

    public RepeatedItemKeyDefinition? ItemKey { get; private init; }

    public UiControlType NestedControlType { get; private init; } = UiControlType.Spinner;

    public MultiItemSpinnerParts? SpinnerParts { get; private init; }

    public static MultiItemControlDefinition ByAutomationIds(
        string pagePropertyName,
        string captureCollectionAutomationId,
        string runtimeCollectionAutomationId) =>
        new(
            pagePropertyName,
            captureCollectionAutomationId,
            UiLocatorKind.AutomationId,
            runtimeCollectionAutomationId,
            UiLocatorKind.AutomationId,
            runtimeFallbackToName: false);

    public static MultiItemControlDefinition ByLocators(
        string pagePropertyName,
        string captureLocatorValue,
        UiLocatorKind captureLocatorKind,
        string runtimeLocatorValue,
        UiLocatorKind runtimeLocatorKind,
        bool runtimeFallbackToName = false) =>
        new(
            pagePropertyName,
            captureLocatorValue,
            captureLocatorKind,
            runtimeLocatorValue,
            runtimeLocatorKind,
            runtimeFallbackToName);

    public MultiItemControlDefinition WithItems(
        MultiItemRelativeLocator itemContainer,
        RepeatedItemKeyDefinition key)
    {
        ArgumentNullException.ThrowIfNull(itemContainer);
        ArgumentNullException.ThrowIfNull(key);
        if (itemContainer.Scope != MultiItemRelativeLocatorScope.CollectionRoot)
        {
            throw new ArgumentException("Item container must be resolved from CollectionRoot.", nameof(itemContainer));
        }

        return this with { ItemContainerLocator = itemContainer, ItemKey = key };
    }

    public MultiItemControlDefinition WithSpinner(MultiItemSpinnerParts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return this with
        {
            NestedControlType = UiControlType.Spinner,
            SpinnerParts = parts
        };
    }

    internal void Validate()
    {
        ValidateEnum(CaptureLocatorKind, nameof(CaptureLocatorKind));
        ValidateEnum(RuntimeLocatorKind, nameof(RuntimeLocatorKind));

        if (ItemContainerLocator is null)
        {
            throw new ArgumentException($"Multi-item collection '{PagePropertyName}' does not define item containers.");
        }

        if (ItemKey is null)
        {
            throw new ArgumentException($"Multi-item collection '{PagePropertyName}' does not define a stable item key.");
        }

        if (NestedControlType != UiControlType.Spinner || SpinnerParts is null)
        {
            throw new ArgumentException(
                $"Multi-item collection '{PagePropertyName}' must define the supported nested Spinner control.");
        }
    }

    private static void ValidateEnum<TEnum>(TEnum value, string name)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentException($"Unsupported {name} value '{value}'.", nameof(value));
        }
    }

    private static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
