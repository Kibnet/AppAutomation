namespace AppAutomation.Abstractions;

/// <summary>
/// Describes the selectable surface exposed by a composite color picker.
/// </summary>
public enum ColorPaletteKind
{
    ListBox = 0,
    ComboBox = 1
}

/// <summary>
/// Describes when a composite color picker commits its pending value.
/// </summary>
public enum ColorPickerCommitMode
{
    Immediate = 0,
    Confirm = 1
}

/// <summary>
/// Provider-neutral parts of a logical color picker.
/// </summary>
public sealed record ColorPickerParts(
    string RootLocator,
    string CurrentValueLocator,
    string? OpenButtonLocator = null,
    string? PopupRootLocator = null,
    string? PaletteLocator = null,
    string? CustomValueLocator = null,
    string? ConfirmButtonLocator = null,
    string? CancelButtonLocator = null,
    ColorPaletteKind PaletteKind = ColorPaletteKind.ListBox,
    ColorPickerCommitMode CommitMode = ColorPickerCommitMode.Immediate,
    UiLocatorKind LocatorKind = UiLocatorKind.AutomationId,
    bool FallbackToName = true)
{
    public static ColorPickerParts ByAutomationIds(
        string rootAutomationId,
        string currentValueAutomationId,
        string? openButtonAutomationId = null,
        string? popupRootAutomationId = null,
        string? paletteAutomationId = null,
        string? customValueAutomationId = null,
        string? confirmButtonAutomationId = null,
        string? cancelButtonAutomationId = null,
        ColorPaletteKind paletteKind = ColorPaletteKind.ListBox,
        ColorPickerCommitMode commitMode = ColorPickerCommitMode.Immediate)
    {
        return new ColorPickerParts(
            rootAutomationId,
            currentValueAutomationId,
            openButtonAutomationId,
            popupRootAutomationId,
            paletteAutomationId,
            customValueAutomationId,
            confirmButtonAutomationId,
            cancelButtonAutomationId,
            paletteKind,
            commitMode);
    }
}

public static partial class UiControlResolverExtensions
{
    /// <summary>
    /// Registers a logical color picker composed from accessible primitive parts.
    /// </summary>
    public static IUiControlResolver WithColorPicker(
        this IUiControlResolver innerResolver,
        string propertyName,
        ColorPickerParts parts)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        return innerResolver.WithAdapters(new ColorPickerControlAdapter(propertyName, parts));
    }
}

/// <summary>
/// Resolves one logical color picker through provider-native primitive controls.
/// </summary>
public sealed class ColorPickerControlAdapter : IUiControlAdapter
{
    private readonly string _propertyName;
    private readonly ColorPickerParts _parts;

    public ColorPickerControlAdapter(string propertyName, ColorPickerParts parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        _propertyName = propertyName.Trim();
        _parts = parts ?? throw new ArgumentNullException(nameof(parts));
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.RootLocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(parts.CurrentValueLocator);
        if (string.IsNullOrWhiteSpace(parts.PaletteLocator)
            && string.IsNullOrWhiteSpace(parts.CustomValueLocator))
        {
            throw new ArgumentException(
                "A palette locator or custom-value locator is required for a color picker.",
                nameof(parts));
        }

        if (parts.CommitMode == ColorPickerCommitMode.Confirm
            && string.IsNullOrWhiteSpace(parts.ConfirmButtonLocator))
        {
            throw new ArgumentException(
                "A confirmation button locator is required for confirmed color pickers.",
                nameof(parts));
        }
    }

    public bool CanResolve(Type requestedType, UiControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        ArgumentNullException.ThrowIfNull(definition);
        return requestedType == typeof(IColorPickerControl)
            && definition.ControlType == UiControlType.ColorPicker
            && string.Equals(definition.PropertyName, _propertyName, StringComparison.Ordinal);
    }

    public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(innerResolver);
        return new CompositeColorPickerControl(
            definition.LocatorValue,
            _propertyName,
            _parts,
            innerResolver);
    }

    private sealed class CompositeColorPickerControl :
        IColorPickerControl,
        IUiControlAvailability,
        IColorPickerOperationControl
    {
        private readonly string _propertyName;
        private readonly ColorPickerParts _parts;
        private readonly IUiControlResolver _resolver;

        public CompositeColorPickerControl(
            string logicalAutomationId,
            string propertyName,
            ColorPickerParts parts,
            IUiControlResolver resolver)
        {
            AutomationId = logicalAutomationId;
            _propertyName = propertyName;
            _parts = parts;
            _resolver = resolver;
        }

        public string AutomationId { get; }

        public string Name => TryResolveAvailabilityAnchor()?.Name ?? AutomationId;

        public bool IsEnabled => TryResolveAvailabilityAnchor()?.IsEnabled == true;

        public bool IsAvailable => TryResolveAvailabilityAnchor() is { } anchor
            && (anchor as IUiControlAvailability)?.IsAvailable != false;

        public string Color
        {
            get => ReadCurrentColor();
            set => SetColor(value, timeoutMs: 5000);
        }

        public void SetColor(string color, int timeoutMs)
        {
            var expected = ColorValue.Normalize(color);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var budget = UiOperationTimeoutBudget.Start(timeoutMs, "color-picker");

            WaitForRoot(budget.Remaining);
            InvokeOptionalButton(_parts.OpenButtonLocator, "Open", budget.Remaining);

            if (!string.IsNullOrWhiteSpace(_parts.CustomValueLocator))
            {
                WaitForTextBox(_parts.CustomValueLocator, "custom value", budget.Remaining)
                    .Enter(expected);
            }
            else
            {
                SelectPaletteColor(expected, budget.Remaining);
            }

            if (_parts.CommitMode == ColorPickerCommitMode.Confirm)
            {
                InvokeOptionalButton(_parts.ConfirmButtonLocator, "Confirm", budget.Remaining);
            }

            if (!string.IsNullOrWhiteSpace(_parts.PopupRootLocator))
            {
                WaitForPopupClosure(budget.Remaining);
            }
        }

        private string ReadCurrentColor()
        {
            var value = TryReadText(_parts.CurrentValueLocator)
                ?? throw new InvalidOperationException(
                    $"Color picker current-value surface '{_parts.CurrentValueLocator}' is unavailable.");
            return ColorValue.Normalize(value);
        }

        private void SelectPaletteColor(string expected, TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(_parts.PaletteLocator))
            {
                throw new InvalidOperationException("The color picker does not expose a palette surface.");
            }

            switch (_parts.PaletteKind)
            {
                case ColorPaletteKind.ListBox:
                    ISelectableListBoxControl? list = null;
                    UiWait.Until(
                        () => (list = TryResolveListBox())?.IsEnabled == true,
                        static ready => ready,
                        new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                        $"Color palette '{_parts.PaletteLocator}' did not become available.");
                    var listMatch = FindSingleColor(
                        list!.Items.Select(static item => item.Text ?? item.Name ?? string.Empty),
                        expected);
                    list.SelectItem(listMatch);
                    break;
                case ColorPaletteKind.ComboBox:
                    IComboBoxControl? combo = null;
                    UiWait.Until(
                        () => (combo = TryResolveComboBox())?.IsEnabled == true,
                        static ready => ready,
                        new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                        $"Color palette '{_parts.PaletteLocator}' did not become available.");
                    combo!.Expand();
                    var items = combo.Items.Select(static item => item.Text ?? item.Name).ToArray();
                    var comboMatch = FindSingleColor(items, expected);
                    combo.SelectByIndex(Array.FindIndex(items, item => string.Equals(item, comboMatch, StringComparison.Ordinal)));
                    break;
                default:
                    throw new NotSupportedException($"Unsupported color palette kind '{_parts.PaletteKind}'.");
            }
        }

        private static string FindSingleColor(IEnumerable<string> values, string expected)
        {
            var matches = values
                .Where(value => ColorValue.TryNormalize(value, out var normalized)
                    && string.Equals(normalized, expected, StringComparison.Ordinal))
                .ToArray();
            return matches.Length switch
            {
                1 => matches[0],
                0 => throw new InvalidOperationException($"Color '{expected}' was not found in the palette."),
                _ => throw new InvalidOperationException($"Color '{expected}' is ambiguous in the palette.")
            };
        }

        private void WaitForRoot(TimeSpan timeout)
        {
            UiWait.Until(
                () => TryResolveAvailabilityAnchor() is { IsEnabled: true } anchor
                    && (anchor as IUiControlAvailability)?.IsAvailable != false,
                static ready => ready,
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Color picker '{_parts.RootLocator}' did not become available.");
        }

        private ITextBoxControl WaitForTextBox(string locator, string purpose, TimeSpan timeout)
        {
            ITextBoxControl? input = null;
            UiWait.Until(
                () => (input = TryResolveTextBox(locator, purpose))?.IsEnabled == true,
                static ready => ready,
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Color picker {purpose} surface '{locator}' did not become available.");
            return input!;
        }

        private void InvokeOptionalButton(string? locator, string purpose, TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(locator))
            {
                return;
            }

            IButtonControl? button = null;
            UiWait.Until(
                () => (button = TryResolveButton(locator, purpose))?.IsEnabled == true,
                static ready => ready,
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Color picker {purpose.ToLowerInvariant()} button '{locator}' did not become available.");
            button!.Invoke();
        }

        private void WaitForPopupClosure(TimeSpan timeout)
        {
            UiWait.Until(
                IsPopupAvailable,
                static available => !available,
                new UiWaitOptions { Timeout = timeout, PollInterval = TimeSpan.FromMilliseconds(50) },
                $"Color picker popup '{_parts.PopupRootLocator}' did not close.");
        }

        private bool IsPopupAvailable()
        {
            IUiControl popup;
            try
            {
                popup = _resolver.Resolve<IUiControl>(Definition(
                    "PopupRoot",
                    UiControlType.AutomationElement,
                    _parts.PopupRootLocator!));
            }
            catch (Exception exception) when (IsResolutionFailure(exception))
            {
                return false;
            }

            return popup is IUiControlAvailability availability
                ? availability.IsAvailable
                : throw new InvalidOperationException(
                    $"Color picker popup '{_parts.PopupRootLocator}' must expose {nameof(IUiControlAvailability)}.");
        }

        private IUiControl? TryResolveRoot()
        {
            try
            {
                return _resolver.Resolve<IUiControl>(Definition("Root", UiControlType.AutomationElement, _parts.RootLocator));
            }
            catch (Exception exception) when (IsResolutionFailure(exception))
            {
                return null;
            }
        }

        private IUiControl? TryResolveAvailabilityAnchor()
        {
            var root = TryResolveRoot();
            if (root is not null)
            {
                return root;
            }

            var currentValue = TryResolveTextBox(_parts.CurrentValueLocator, "CurrentValue");
            if (currentValue is not null)
            {
                return currentValue;
            }

            return !string.IsNullOrWhiteSpace(_parts.OpenButtonLocator)
                ? TryResolveButton(_parts.OpenButtonLocator, "Open")
                : null;
        }

        private ITextBoxControl? TryResolveTextBox(string locator, string purpose)
        {
            try
            {
                return _resolver.Resolve<ITextBoxControl>(Definition(purpose, UiControlType.TextBox, locator));
            }
            catch (Exception exception) when (IsResolutionFailure(exception))
            {
                return null;
            }
        }

        private string? TryReadText(string locator)
        {
            try
            {
                return _resolver.Resolve<ITextBoxControl>(Definition("CurrentValue", UiControlType.TextBox, locator)).Text;
            }
            catch (Exception exception) when (IsResolutionFailure(exception))
            {
                try
                {
                    return _resolver.Resolve<IReadableTextControl>(Definition("CurrentValue", UiControlType.Label, locator)).Text;
                }
                catch (Exception fallbackException) when (IsResolutionFailure(fallbackException))
                {
                    return null;
                }
            }
        }

        private IButtonControl? TryResolveButton(string locator, string purpose)
        {
            try
            {
                return _resolver.Resolve<IButtonControl>(Definition(purpose, UiControlType.Button, locator));
            }
            catch (Exception exception) when (IsResolutionFailure(exception))
            {
                return null;
            }
        }

        private ISelectableListBoxControl? TryResolveListBox()
        {
            try
            {
                return _resolver.Resolve<ISelectableListBoxControl>(Definition(
                    "Palette",
                    UiControlType.ListBox,
                    _parts.PaletteLocator!));
            }
            catch (Exception exception) when (IsResolutionFailure(exception))
            {
                return null;
            }
        }

        private IComboBoxControl? TryResolveComboBox()
        {
            try
            {
                return _resolver.Resolve<IComboBoxControl>(Definition(
                    "Palette",
                    UiControlType.ComboBox,
                    _parts.PaletteLocator!));
            }
            catch (Exception exception) when (IsResolutionFailure(exception))
            {
                return null;
            }
        }

        private static bool IsResolutionFailure(Exception exception)
        {
            return exception is InvalidOperationException
                || string.Equals(exception.GetType().Name, "ElementNotAvailableException", StringComparison.Ordinal);
        }

        private UiControlDefinition Definition(string suffix, UiControlType type, string locator)
        {
            return new UiControlDefinition(
                $"{_propertyName}{suffix}",
                type,
                locator,
                _parts.LocatorKind,
                _parts.FallbackToName);
        }

    }
}

internal interface IColorPickerOperationControl
{
    void SetColor(string color, int timeoutMs);
}
