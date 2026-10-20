using System.Text.RegularExpressions;

namespace AppAutomation.Abstractions;

/// <summary>Converts one complete displayed number, optionally followed by a unit.</summary>
public static partial class NumericText
{
    [GeneratedRegex(@"^(?<number>[+-]?(?:\d{1,3}(?:[ \u00A0\u202F]\d{3})+|\d+)(?:[.,]\d+)?)(?:[ \u00A0\u202F]+[\p{L}\p{Sc}][\p{L}\p{M}\p{Sc}²³.]*)*$")]
    private static partial Regex DisplayNumberPattern();

    public static decimal ParseDecimal(string? text)
    {
        var number = ExtractNumber(text);
        if (GridNumericText.TryParseDecimalUnambiguous(number, out var value, out var diagnostic))
        {
            return value;
        }

        throw new InvalidOperationException($"Displayed text '{text ?? "<null>"}' is not a decimal value: {diagnostic}");
    }

    public static long ParseInteger(string? text)
    {
        var value = ParseDecimal(text);
        if (decimal.Truncate(value) != value || value < long.MinValue || value > long.MaxValue)
        {
            throw new InvalidOperationException($"Displayed text '{text ?? "<null>"}' is not a 64-bit integer.");
        }

        return (long)value;
    }

    public static double ParseDouble(string? text)
    {
        var number = ExtractNumber(text);
        if (!GridNumericText.TryParseUnambiguous(number, out var value, out var diagnostic))
        {
            throw new InvalidOperationException($"Displayed text '{text ?? "<null>"}' is not a finite number: {diagnostic}");
        }

        if (Math.Abs(value) > 9_007_199_254_740_991d)
        {
            throw new InvalidOperationException($"Displayed text '{text}' exceeds the precise integer range of the numeric assertion contract.");
        }

        return value;
    }

    private static string ExtractNumber(string? text)
    {
        var match = DisplayNumberPattern().Match(text?.Trim() ?? string.Empty);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Displayed text '{text ?? "<null>"}' is not one numeric value.");
        }

        return match.Groups["number"].Value;
    }
}
