using AppAutomation.Abstractions;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

/// <summary>Reads the numeric editor value while retaining the user's formatted input text.</summary>
internal static class RecorderNumericValueReader
{
    internal static bool TryReadNumericValue(Control control, out double value)
    {
        return TryReadNumericValue(control, out value, out _);
    }

    internal static bool TryReadNumericValue(
        Control control,
        out double value,
        out string? numericInputText)
    {
        return TryReadNumericValue(
            control,
            control,
            cultureName: null,
            out value,
            out numericInputText);
    }

    internal static bool TryReadNumericValue(
        Control valueSource,
        Control physicalTextSource,
        string? cultureName,
        out double value,
        out string? numericInputText)
    {
        var visibleText = TryReadPhysicalNumericText(physicalTextSource, valueSource);
        foreach (var candidate in RecorderControlTree.EnumerateRelatedControls(valueSource, includePopupPlacementTarget: true))
        {
            if (!TryReadNumericControlValue(candidate, out value))
            {
                continue;
            }

            numericInputText = GridNumericText.PreserveNonCanonicalInput(
                visibleText,
                value,
                cultureName);
            return true;
        }

        if (GridNumericText.TryParse(visibleText, cultureName, out value, out _))
        {
            numericInputText = GridNumericText.PreserveNonCanonicalInput(
                visibleText,
                value,
                cultureName);
            return true;
        }

        value = default;
        numericInputText = null;
        return false;
    }

    private static string? TryReadPhysicalNumericText(Control source, Control valueSource)
    {
        if (source is TextBox sourceTextBox)
        {
            return sourceTextBox.Text;
        }

        if (RecorderControlTree.EnumerateRelatedControls(source, includePopupPlacementTarget: true).OfType<TextBox>().FirstOrDefault() is { } relatedTextBox)
        {
            return relatedTextBox.Text;
        }

        return RecorderControlTree.EnumerateReachableControls(valueSource)
            .OfType<TextBox>()
            .Select(static textBox => textBox.Text)
            .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text));
    }

    private static bool TryReadNumericControlValue(Control control, out double value)
    {
        for (var type = control.GetType(); type is not null; type = type.BaseType)
        {
            var valueProperty = type
                .GetProperties(
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.DeclaredOnly)
                .FirstOrDefault(static property =>
                    string.Equals(property.Name, "Value", StringComparison.Ordinal)
                    && property.CanRead
                    && property.GetIndexParameters().Length == 0);
            if (valueProperty is null)
            {
                continue;
            }

            try
            {
                if (GridNumericText.TryConvertFiniteDouble(valueProperty.GetValue(control), out value))
                {
                    return true;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                // A broken editor getter must not block the text fallback.
            }
        }

        value = default;
        return false;
    }
}
