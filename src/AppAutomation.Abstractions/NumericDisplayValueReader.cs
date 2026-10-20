namespace AppAutomation.Abstractions;

/// <summary>
/// Reads a number displayed in a text control without changing the control's text semantics.
/// </summary>
public static class NumericDisplayValueReader
{
    /// <summary>Reads the current displayed value as an exact decimal.</summary>
    public static decimal ReadDecimal(ITextBoxControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        try
        {
            return NumericText.ParseDecimal(control.Text);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Control '{control.AutomationId}' does not expose an unambiguous displayed decimal: {exception.Message}",
                exception);
        }
    }

    /// <summary>Reads the current displayed value as a 64-bit integer.</summary>
    public static long ReadInteger(ITextBoxControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        try
        {
            return NumericText.ParseInteger(control.Text);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Control '{control.AutomationId}' does not expose an unambiguous displayed integer: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Reads the current displayed value. A missing or ambiguous number is an error, not zero.
    /// </summary>
    public static double Read(ITextBoxControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var text = control.Text;
        if (TryParse(text, out var value, out var diagnostic))
        {
            return value;
        }

        throw new InvalidOperationException(
            $"Control '{control.AutomationId}' does not expose an unambiguous displayed number: {diagnostic}");
    }

    /// <summary>
    /// Interprets one complete displayed numeric value with an optional separated unit suffix.
    /// </summary>
    public static bool TryParse(string? text, out double value, out string? diagnostic)
    {
        try
        {
            value = NumericText.ParseDouble(text);
            diagnostic = null;
            return true;
        }
        catch (InvalidOperationException exception)
        {
            value = default;
            diagnostic = exception.Message;
            return false;
        }
    }
}
