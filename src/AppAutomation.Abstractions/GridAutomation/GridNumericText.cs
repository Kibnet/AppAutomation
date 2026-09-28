using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AppAutomation.Abstractions;

internal static class GridNumericText
{
    private const NumberStyles SupportedStyles = NumberStyles.Number | NumberStyles.AllowExponent;

    public static bool TryParse(string? text, CultureInfo culture, out double value)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (string.IsNullOrWhiteSpace(text))
        {
            value = default;
            return false;
        }

        var normalized = NormalizeGroupingWhitespace(text.Trim(), culture.NumberFormat.NumberGroupSeparator);
        return double.TryParse(normalized, SupportedStyles, culture, out value)
            && double.IsFinite(value);
    }

    public static bool TryParse(
        string? text,
        string? cultureName,
        out double value,
        out string? diagnostic)
    {
        return TryParseConfigured(
            text,
            cultureName,
            static (string candidate, CultureInfo candidateCulture, out double number) =>
                TryParse(candidate, candidateCulture, out number),
            out value,
            out diagnostic);
    }

    public static bool TryParseDecimal(
        string? text,
        CultureInfo culture,
        out decimal value)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return TryParseDecimalCore(text, culture, out value);
    }

    public static bool TryParseDecimal(
        string? text,
        string? cultureName,
        out decimal value,
        out string? diagnostic)
    {
        return TryParseConfigured(
            text,
            cultureName,
            static (string candidate, CultureInfo candidateCulture, out decimal number) =>
                TryParseDecimalCore(candidate, candidateCulture, out number),
            out value,
            out diagnostic);
    }

    public static bool Represents(string? text, double expected, string? cultureName = null)
    {
        return double.IsFinite(expected)
            && TryParse(text, cultureName, out var actual, out _)
            && actual.Equals(expected);
    }

    public static string? PreserveNonCanonicalInput(
        string? text,
        double canonicalValue,
        string? cultureName = null)
    {
        if (!double.IsFinite(canonicalValue) || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (!Represents(trimmed, canonicalValue, cultureName))
        {
            return null;
        }

        var canonicalText = canonicalValue.ToString("G17", CultureInfo.InvariantCulture);
        return string.Equals(trimmed, canonicalText, StringComparison.Ordinal)
            ? null
            : trimmed;
    }

    public static void ValidateInput(
        double canonicalValue,
        string text,
        string parameterName,
        string? cultureName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, parameterName);
        if (!Represents(text, canonicalValue, cultureName))
        {
            throw new ArgumentException(
                $"Numeric input text '{text}' does not represent canonical value "
                + $"'{canonicalValue.ToString("G17", CultureInfo.InvariantCulture)}'.",
                parameterName);
        }
    }

    public static void ValidateCultureName(string? cultureName, string parameterName)
    {
        if (cultureName is null)
        {
            return;
        }

        if (cultureName.Length > 0 && string.IsNullOrWhiteSpace(cultureName))
        {
            throw new ArgumentException("Numeric culture name cannot contain only whitespace.", parameterName);
        }

        try
        {
            _ = CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException exception)
        {
            throw new ArgumentException($"Numeric culture '{cultureName}' is not valid.", parameterName, exception);
        }
    }

    public static bool TryConvertFiniteDouble(object? rawValue, out double value)
    {
        switch (rawValue)
        {
            case byte number:
                value = number;
                return true;
            case sbyte number:
                value = number;
                return true;
            case short number:
                value = number;
                return true;
            case ushort number:
                value = number;
                return true;
            case int number:
                value = number;
                return true;
            case uint number:
                value = number;
                return true;
            case long number:
                value = number;
                return true;
            case ulong number:
                value = number;
                return true;
            case float number when float.IsFinite(number):
                value = number;
                return true;
            case double number when double.IsFinite(number):
                value = number;
                return true;
            case decimal number:
                value = (double)number;
                return double.IsFinite(value);
            default:
                value = default;
                return false;
        }
    }

    public static bool TryConvertFiniteDecimal(object? rawValue, out decimal value)
    {
        switch (rawValue)
        {
            case byte number:
                value = number;
                return true;
            case sbyte number:
                value = number;
                return true;
            case short number:
                value = number;
                return true;
            case ushort number:
                value = number;
                return true;
            case int number:
                value = number;
                return true;
            case uint number:
                value = number;
                return true;
            case long number:
                value = number;
                return true;
            case ulong number:
                value = number;
                return true;
            case float number when float.IsFinite(number):
                return TryConvertFloatingPointToDecimal(number, out value);
            case double number when double.IsFinite(number):
                return TryConvertFloatingPointToDecimal(number, out value);
            case decimal number:
                value = number;
                return true;
            default:
                value = default;
                return false;
        }
    }

    private static bool TryConvertFloatingPointToDecimal(double number, out decimal value)
    {
        try
        {
            value = (decimal)number;
            return true;
        }
        catch (OverflowException)
        {
            value = default;
            return false;
        }
    }

    private static bool TryParseDecimalCore(string? text, CultureInfo culture, out decimal value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = default;
            return false;
        }

        var normalized = NormalizeGroupingWhitespace(text.Trim(), culture.NumberFormat.NumberGroupSeparator);
        return decimal.TryParse(normalized, SupportedStyles, culture, out value)
            || TryExtractNumberToken(text, culture, out value);
    }

    private static bool TryParseAcrossCultures<TValue>(
        string? text,
        TryParseValue<TValue> tryParse,
        out TValue value,
        out string? diagnostic)
        where TValue : struct
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = default;
            diagnostic = $"Numeric text '{text ?? "<null>"}' is empty.";
            return false;
        }

        var found = false;
        var firstValue = default(TValue);
        CultureInfo? firstCulture = null;
        List<(CultureInfo Culture, TValue Value)>? conflicts = null;

        TryCandidate(CultureInfo.CurrentUICulture);
        if (!string.Equals(
                CultureInfo.CurrentCulture.Name,
                CultureInfo.CurrentUICulture.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            TryCandidate(CultureInfo.CurrentCulture);
        }

        if (!string.Equals(
                CultureInfo.InvariantCulture.Name,
                CultureInfo.CurrentUICulture.Name,
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                CultureInfo.InvariantCulture.Name,
                CultureInfo.CurrentCulture.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            TryCandidate(CultureInfo.InvariantCulture);
        }

        if (found && conflicts is null)
        {
            value = firstValue;
            diagnostic = null;
            return true;
        }

        value = default;
        diagnostic = !found
            ? $"Numeric text '{text}' cannot be read using the UI, current, or invariant culture."
            : $"Numeric text '{text}' is culture-ambiguous and resolves to different values: "
                + string.Join(
                    ", ",
                    conflicts!.Select(static candidate =>
                        $"{DisplayCultureName(candidate.Culture)}={candidate.Value}"))
                + ". Configure the grid column culture explicitly.";
        return false;

        void TryCandidate(CultureInfo culture)
        {
            if (!tryParse(text, culture, out var candidate))
            {
                return;
            }

            if (!found)
            {
                found = true;
                firstValue = candidate;
                firstCulture = culture;
                return;
            }

            if (EqualityComparer<TValue>.Default.Equals(firstValue, candidate))
            {
                conflicts?.Add((culture, candidate));
                return;
            }

            conflicts ??= [(firstCulture!, firstValue)];
            conflicts.Add((culture, candidate));
        }
    }

    private static bool TryParseConfigured<TValue>(
        string? text,
        string? cultureName,
        TryParseValue<TValue> tryParse,
        out TValue value,
        out string? diagnostic)
        where TValue : struct
    {
        if (!TryGetConfiguredCulture(cultureName, out var culture, out diagnostic))
        {
            value = default;
            return false;
        }

        if (culture is null)
        {
            return TryParseAcrossCultures(text, tryParse, out value, out diagnostic);
        }

        if (!string.IsNullOrWhiteSpace(text) && tryParse(text, culture, out value))
        {
            diagnostic = null;
            return true;
        }

        value = default;
        diagnostic = $"Numeric text '{text ?? "<null>"}' cannot be read using configured culture '{culture.Name}'.";
        return false;
    }

    private static bool TryGetConfiguredCulture(
        string? cultureName,
        out CultureInfo? culture,
        out string? diagnostic)
    {
        if (cultureName is null)
        {
            culture = null;
            diagnostic = null;
            return true;
        }

        try
        {
            if (cultureName.Length > 0 && string.IsNullOrWhiteSpace(cultureName))
            {
                culture = null;
                diagnostic = "Configured numeric culture cannot contain only whitespace.";
                return false;
            }

            culture = CultureInfo.GetCultureInfo(cultureName);
            diagnostic = null;
            return true;
        }
        catch (CultureNotFoundException)
        {
            culture = null;
            diagnostic = $"Configured numeric culture '{cultureName}' is not valid.";
            return false;
        }
    }

    private static bool TryExtractNumberToken(string text, CultureInfo culture, out decimal value)
    {
        var matches = Regex.Matches(text, @"[+-]?\d[\d\s\u00A0\u202F.,']*")
            .Select(static match => match.Value.Trim())
            .Where(static token => token.Length > 0)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            value = default;
            return false;
        }

        var groupSeparator = culture.NumberFormat.NumberGroupSeparator;
        var normalized = new string(matches[0]
                .SelectMany(character => IsGroupingWhitespace(character) || character == '\''
                    ? groupSeparator
                    : character.ToString())
                .ToArray())
            .Trim();
        return decimal.TryParse(normalized, SupportedStyles, culture, out value);
    }

    private static string NormalizeGroupingWhitespace(string text, string groupSeparator)
    {
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (IsGroupingWhitespace(character)
                && index > 0
                && index + 1 < text.Length
                && char.IsDigit(text[index - 1])
                && char.IsDigit(text[index + 1]))
            {
                builder.Append(groupSeparator);
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static bool IsGroupingWhitespace(char character) =>
        char.IsWhiteSpace(character) || character is '\u00A0' or '\u202F';

    private static string DisplayCultureName(CultureInfo culture) =>
        culture.Name.Length == 0 ? "Invariant" : culture.Name;

    private delegate bool TryParseValue<TValue>(string text, CultureInfo culture, out TValue value);
}
