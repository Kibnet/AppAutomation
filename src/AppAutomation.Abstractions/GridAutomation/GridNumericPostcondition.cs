using System.Globalization;

namespace AppAutomation.Abstractions;

internal static class GridNumericPostcondition
{
    public static bool Matches(
        GridCellValueSnapshot actual,
        string canonicalValue,
        GridNumericInput? numericInput)
    {
        ArgumentNullException.ThrowIfNull(actual);
        if (!TryReadExpected(canonicalValue, numericInput, out var expected))
        {
            return false;
        }

        if (!actual.IsDisplayOnly
            && GridNumericText.TryConvertFiniteDouble(actual.RawValue, out var semanticValue))
        {
            return semanticValue.Equals(expected);
        }

        if (actual.IsDisplayOnly && !string.IsNullOrWhiteSpace(actual.FormatString))
        {
            var expectedDisplay = GridCellDisplayFormatter.Format(
                expected,
                actual.FormatString,
                actual.CultureName);
            return string.Equals(actual.DisplayText, expectedDisplay, StringComparison.Ordinal);
        }

        return GridValueConversion.TryConvertNumberDouble(actual, out var actualValue, out _)
            && actualValue.Equals(expected);
    }

    private static bool TryReadExpected(
        string canonicalValue,
        GridNumericInput? numericInput,
        out double value)
    {
        if (numericInput is not null)
        {
            value = numericInput.CanonicalValue;
            return true;
        }

        return GridNumericText.TryParse(canonicalValue, CultureInfo.InvariantCulture, out value);
    }
}
