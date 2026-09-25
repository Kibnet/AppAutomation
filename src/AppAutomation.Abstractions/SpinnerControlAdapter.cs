using System.Globalization;

namespace AppAutomation.Abstractions;

public static partial class UiControlResolverExtensions
{
    /// <summary>
    /// Registers a logical spinner backed by a writable text-box part.
    /// </summary>
    public static IUiControlResolver WithSpinnerTextBoxProxy(
        this IUiControlResolver innerResolver,
        string propertyName,
        string targetLocatorValue,
        UiLocatorKind targetLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLocatorValue);

        return innerResolver.WithAdapters(new SpinnerTextBoxControlAdapter(
            propertyName,
            targetLocatorValue,
            targetLocatorKind,
            fallbackToName));
    }
}

/// <summary>
/// Resolves a logical <see cref="ISpinnerControl"/> through a real writable text box.
/// </summary>
public sealed class SpinnerTextBoxControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly string _targetLocatorValue;
    private readonly UiLocatorKind _targetLocatorKind;
    private readonly bool _fallbackToName;

    public SpinnerTextBoxControlAdapter(
        string propertyName,
        string targetLocatorValue,
        UiLocatorKind targetLocatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLocatorValue);

        _propertyName = propertyName.Trim();
        _targetLocatorValue = targetLocatorValue.Trim();
        _targetLocatorKind = targetLocatorKind;
        _fallbackToName = fallbackToName;
    }

    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);

        return requestedType.IsAssignableFrom(typeof(ISpinnerControl))
            && definition.ControlType == UiControlType.Spinner
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(innerResolver);

        var textBox = innerResolver.Resolve<ITextBoxControl>(new UiControlDefinition(
            definition.PropertyName,
            UiControlType.TextBox,
            _targetLocatorValue,
            _targetLocatorKind,
            _fallbackToName));
        return new SpinnerTextControl(
            definition.LocatorValue,
            () => textBox.Name,
            () => textBox.IsEnabled,
            () => (textBox as IUiControlAvailability)?.IsAvailable ?? true,
            () => textBox.Text,
            textBox.Enter);
    }

}

internal sealed class SpinnerTextControl : ISpinnerControl, IUiControlAvailability
{
    private readonly Func<string> _readName;
    private readonly Func<bool> _readIsEnabled;
    private readonly Func<bool> _readIsAvailable;
    private readonly Func<string?> _readText;
    private readonly Action<string> _enterText;

    public SpinnerTextControl(
        string automationId,
        Func<string> readName,
        Func<bool> readIsEnabled,
        Func<bool> readIsAvailable,
        Func<string?> readText,
        Action<string> enterText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);
        ArgumentNullException.ThrowIfNull(readName);
        ArgumentNullException.ThrowIfNull(readIsEnabled);
        ArgumentNullException.ThrowIfNull(readIsAvailable);
        ArgumentNullException.ThrowIfNull(readText);
        ArgumentNullException.ThrowIfNull(enterText);

        AutomationId = automationId;
        _readName = readName;
        _readIsEnabled = readIsEnabled;
        _readIsAvailable = readIsAvailable;
        _readText = readText;
        _enterText = enterText;
    }

    public string AutomationId { get; }

    public string Name => _readName();

    public bool IsEnabled => _readIsEnabled();

    public bool IsAvailable => _readIsAvailable();

    public double Value
    {
        get => SpinnerValueCodec.ParseInvariant(_readText(), AutomationId);
        set => _enterText(SpinnerValueCodec.FormatInvariant(value));
    }
}

internal static class SpinnerValueCodec
{
    public static double ParseInvariant(string? text, string automationId)
    {
        if (double.TryParse(
                text?.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
            && double.IsFinite(value))
        {
            return value;
        }

        throw new InvalidOperationException(
            $"Spinner text part '{automationId}' does not contain a finite invariant numeric value.");
    }

    public static string FormatInvariant(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Spinner value must be finite.");
        }

        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
