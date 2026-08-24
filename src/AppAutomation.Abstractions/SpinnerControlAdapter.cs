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
        return new SpinnerTextBoxControl(definition.LocatorValue, textBox);
    }

    private sealed class SpinnerTextBoxControl : ISpinnerControl, IUiControlAvailability
    {
        private readonly ITextBoxControl _textBox;

        public SpinnerTextBoxControl(string logicalAutomationId, ITextBoxControl textBox)
        {
            AutomationId = logicalAutomationId;
            _textBox = textBox;
        }

        public string AutomationId { get; }

        public string Name => _textBox.Name;

        public bool IsEnabled => _textBox.IsEnabled;

        public bool IsAvailable => (_textBox as IUiControlAvailability)?.IsAvailable ?? true;

        public double Value
        {
            get
            {
                if (double.TryParse(
                        _textBox.Text?.Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var value))
                {
                    return value;
                }

                throw new InvalidOperationException(
                    $"Spinner text part '{_textBox.AutomationId}' does not contain an invariant numeric value.");
            }
            set
            {
                if (!double.IsFinite(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Spinner value must be finite.");
                }

                _textBox.Enter(value.ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }
}
