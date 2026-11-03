using System.Globalization;

namespace AppAutomation.Abstractions;

/// <summary>Converts full wall-clock values at the precision exposed by the column.</summary>
internal static class GridDateTimeValueConversion
{
    public static bool TryConvert(GridCellValueSnapshot snapshot, out DateTime? value, out string? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        value = null;
        diagnostic = null;
        if ((snapshot.IsDisplayOnly || snapshot.RawValue is null or string) && string.IsNullOrWhiteSpace(snapshot.DisplayText))
            return true;

        var typed = (snapshot.IsDisplayOnly ? null : snapshot.RawValue) switch
        {
            DateTime date => date,
            DateTimeOffset date => date.DateTime,
            _ => (DateTime?)null
        };
        if (typed.HasValue && string.IsNullOrWhiteSpace(snapshot.FormatString))
        {
            value = DateTime.SpecifyKind(typed.Value, DateTimeKind.Unspecified);
            return true;
        }

        try
        {
            // Formatting the model first makes Headless observe the same precision as display-only UIA.
            if (typed.HasValue)
            {
                var culture = CultureInfo.GetCultureInfo(snapshot.CultureName ?? string.Empty);
                var text = ((IFormattable)snapshot.RawValue!).ToString(snapshot.FormatString, culture);
                if (TryParse(text, snapshot.FormatString, culture, out var parsed))
                {
                    value = parsed;
                    return true;
                }
            }
            else if (snapshot.CultureName is not null)
            {
                var culture = CultureInfo.GetCultureInfo(snapshot.CultureName);
                if (TryParse(snapshot.DisplayText, snapshot.FormatString, culture, out var parsed))
                {
                    value = parsed;
                    return true;
                }
            }
            else
            {
                var candidates = new[]
                    {
                        CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("ru-RU"),
                        CultureInfo.GetCultureInfo("en-US"), CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture
                    }
                    .DistinctBy(static culture => culture.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(culture => TryParse(snapshot.DisplayText, snapshot.FormatString, culture, out var parsed)
                        ? (DateTime?)parsed : null)
                    .Where(static candidate => candidate.HasValue).Distinct().ToArray();
                if (candidates.Length == 1)
                {
                    value = candidates[0];
                    return true;
                }
                if (candidates.Length > 1)
                {
                    diagnostic = $"Grid cell value '{snapshot.DisplayText}' is culture-ambiguous for date-time. "
                        + "Configure the grid column culture explicitly.";
                    return false;
                }
            }
        }
        catch (FormatException)
        {
            // Invalid declared formats are conversion failures, not a reason to guess another format.
        }

        diagnostic = $"Grid cell value '{snapshot.DisplayText ?? "<null>"}' cannot be read as date-time "
            + $"using format '{snapshot.FormatString ?? "<unspecified>"}' and culture "
            + $"'{snapshot.CultureName ?? "<unspecified>"}'.";
        return false;
    }

    private static bool TryParse(string? text, string? format, CultureInfo culture, out DateTime value)
    {
        if (TimeOnly.TryParse(text, culture, DateTimeStyles.AllowWhiteSpaces, out _))
        {
            value = default;
            return false; // A time alone must not acquire today's date from DateTimeOffset.TryParse.
        }
        // A missing offset is immaterial to wall-clock values; do not consult the machine timezone.
        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal;
        var success = string.IsNullOrWhiteSpace(format)
            ? DateTimeOffset.TryParse(text, culture, styles, out var parsed)
            : DateTimeOffset.TryParseExact(text, format, culture, styles, out parsed);
        // DateTime preserves the original clock components; LocalDateTime/UtcDateTime would convert them.
        value = parsed.DateTime;
        return success;
    }
}
