namespace AppAutomation.Abstractions;

/// <summary>
/// Describes a primitive control that should be resolved behind a logical wrapper property.
/// </summary>
/// <param name="TargetLocatorValue">The locator value of the real primitive control.</param>
/// <param name="TargetControlType">The primitive control type to resolve.</param>
/// <param name="TargetLocatorKind">The locator kind used to resolve the primitive control.</param>
/// <param name="FallbackToName">Whether name fallback should be used for the primitive control.</param>
public sealed record PrimitiveProxyTarget(
    string TargetLocatorValue,
    UiControlType TargetControlType,
    UiLocatorKind TargetLocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true)
{
    /// <summary>
    /// Creates a proxy target addressed by automation ID.
    /// </summary>
    public static PrimitiveProxyTarget ByAutomationId(
        string targetAutomationId,
        UiControlType targetControlType,
        bool fallbackToName = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetAutomationId);
        return new PrimitiveProxyTarget(
            targetAutomationId,
            targetControlType,
            UiLocatorKind.AutomationId,
            fallbackToName);
    }

    /// <summary>
    /// Creates a proxy target addressed by name.
    /// </summary>
    public static PrimitiveProxyTarget ByName(
        string targetName,
        UiControlType targetControlType,
        bool fallbackToName = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        return new PrimitiveProxyTarget(
            targetName,
            targetControlType,
            UiLocatorKind.Name,
            fallbackToName);
    }
}

public static partial class UiControlResolverExtensions
{
    /// <summary>
    /// Registers a logical control property that resolves through a real primitive child control.
    /// </summary>
    public static IUiControlResolver WithProxy(
        this IUiControlResolver innerResolver,
        string propertyName,
        PrimitiveProxyTarget target)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(target);

        return innerResolver.WithAdapters(new PrimitiveProxyControlAdapter(propertyName, target));
    }

    /// <summary>
    /// Registers a text-box proxy for a logical wrapper property.
    /// </summary>
    public static IUiControlResolver WithTextBoxProxy(
        this IUiControlResolver innerResolver,
        string propertyName,
        string targetLocatorValue,
        UiLocatorKind targetLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return innerResolver.WithProxy(
            propertyName,
            new PrimitiveProxyTarget(targetLocatorValue, UiControlType.TextBox, targetLocatorKind, fallbackToName));
    }

    /// <summary>
    /// Registers a button proxy for a logical wrapper property.
    /// </summary>
    public static IUiControlResolver WithButtonProxy(
        this IUiControlResolver innerResolver,
        string propertyName,
        string targetLocatorValue,
        UiLocatorKind targetLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return innerResolver.WithProxy(
            propertyName,
            new PrimitiveProxyTarget(targetLocatorValue, UiControlType.Button, targetLocatorKind, fallbackToName));
    }

    /// <summary>
    /// Registers a label proxy for a logical wrapper property.
    /// </summary>
    public static IUiControlResolver WithLabelProxy(
        this IUiControlResolver innerResolver,
        string propertyName,
        string targetLocatorValue,
        UiLocatorKind targetLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return innerResolver.WithProxy(
            propertyName,
            new PrimitiveProxyTarget(targetLocatorValue, UiControlType.Label, targetLocatorKind, fallbackToName));
    }

    /// <summary>
    /// Registers a list-box proxy for a logical wrapper property.
    /// </summary>
    public static IUiControlResolver WithListBoxProxy(
        this IUiControlResolver innerResolver,
        string propertyName,
        string targetLocatorValue,
        UiLocatorKind targetLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return innerResolver.WithProxy(
            propertyName,
            new PrimitiveProxyTarget(targetLocatorValue, UiControlType.ListBox, targetLocatorKind, fallbackToName));
    }

    /// <summary>
    /// Registers a combo-box proxy for a logical wrapper property.
    /// </summary>
    public static IUiControlResolver WithComboBoxProxy(
        this IUiControlResolver innerResolver,
        string propertyName,
        string targetLocatorValue,
        UiLocatorKind targetLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return innerResolver.WithProxy(
            propertyName,
            new PrimitiveProxyTarget(targetLocatorValue, UiControlType.ComboBox, targetLocatorKind, fallbackToName));
    }

    /// <summary>
    /// Registers a date-time picker proxy for a logical wrapper property.
    /// </summary>
    public static IUiControlResolver WithDateTimePickerProxy(
        this IUiControlResolver innerResolver,
        string propertyName,
        string targetLocatorValue,
        UiLocatorKind targetLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return innerResolver.WithProxy(
            propertyName,
            new PrimitiveProxyTarget(targetLocatorValue, UiControlType.DateTimePicker, targetLocatorKind, fallbackToName));
    }

    /// <summary>
    /// Registers a spinner proxy for a logical wrapper property.
    /// </summary>
    public static IUiControlResolver WithSpinnerProxy(
        this IUiControlResolver innerResolver,
        string propertyName,
        string targetLocatorValue,
        UiLocatorKind targetLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return innerResolver.WithProxy(
            propertyName,
            new PrimitiveProxyTarget(targetLocatorValue, UiControlType.Spinner, targetLocatorKind, fallbackToName));
    }
}

/// <summary>
/// Resolves a logical control property through a real primitive control locator.
/// </summary>
public sealed class PrimitiveProxyControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly PrimitiveProxyTarget _target;

    public PrimitiveProxyControlAdapter(string propertyName, PrimitiveProxyTarget target)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("Property name is required.", nameof(propertyName));
        }

        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.TargetLocatorValue);

        EnsureSupportedTargetType(target.TargetControlType);
        _propertyName = propertyName.Trim();
        _target = target with { TargetLocatorValue = target.TargetLocatorValue.Trim() };
    }

    /// <inheritdoc />
    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);

        return string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal)
            && SupportsRequestedType(requestedType, _target.TargetControlType);
    }

    /// <inheritdoc />
    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);

        var targetDefinition = new UiControlDefinition(
            definition.PropertyName,
            _target.TargetControlType,
            _target.TargetLocatorValue,
            _target.TargetLocatorKind,
            _target.FallbackToName);

        object resolved = _target.TargetControlType switch
        {
            UiControlType.TextBox => innerResolver.Resolve<ITextBoxControl>(targetDefinition),
            UiControlType.Button => innerResolver.Resolve<IButtonControl>(targetDefinition),
            UiControlType.Label => innerResolver.Resolve<ILabelControl>(targetDefinition),
            UiControlType.ListBox => innerResolver.Resolve<IListBoxControl>(targetDefinition),
            UiControlType.ComboBox => innerResolver.Resolve<IComboBoxControl>(targetDefinition),
            UiControlType.DateTimePicker => innerResolver.Resolve<IDateTimePickerControl>(targetDefinition),
            UiControlType.Spinner => innerResolver.Resolve<ISpinnerControl>(targetDefinition),
            _ => throw new NotSupportedException(
                $"Primitive proxy '{_propertyName}' does not support target UiControlType.{_target.TargetControlType}.")
        };

        return requestedType.IsInstanceOfType(resolved)
            ? resolved
            : throw new InvalidOperationException(
                $"Primitive proxy '{_propertyName}' resolved target '{_target.TargetControlType}' to '{resolved.GetType().FullName}', which is incompatible with '{requestedType.FullName}'.");
    }

    private static void EnsureSupportedTargetType(UiControlType targetControlType)
    {
        if (targetControlType is UiControlType.TextBox
            or UiControlType.Button
            or UiControlType.Label
            or UiControlType.ListBox
            or UiControlType.ComboBox
            or UiControlType.DateTimePicker
            or UiControlType.Spinner)
        {
            return;
        }

        throw new NotSupportedException(
            $"Primitive proxy target UiControlType.{targetControlType} is not supported.");
    }

    private static bool SupportsRequestedType(Type requestedType, UiControlType targetControlType)
    {
        return targetControlType switch
        {
            UiControlType.TextBox => requestedType.IsAssignableFrom(typeof(ITextBoxControl)),
            UiControlType.Button => requestedType.IsAssignableFrom(typeof(IButtonControl)),
            UiControlType.Label => requestedType.IsAssignableFrom(typeof(ILabelControl)),
            UiControlType.ComboBox => requestedType.IsAssignableFrom(typeof(IComboBoxControl)),
            UiControlType.DateTimePicker => requestedType.IsAssignableFrom(typeof(IDateTimePickerControl)),
            UiControlType.Spinner => requestedType.IsAssignableFrom(typeof(ISpinnerControl)),
            UiControlType.ListBox => requestedType.IsAssignableFrom(typeof(IListBoxControl))
                || requestedType == typeof(ISelectableListBoxControl),
            _ => false
        };
    }
}
