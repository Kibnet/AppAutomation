using AppAutomation.Abstractions;
using System.Text.RegularExpressions;

namespace AppAutomation.Recorder.Avalonia;

internal static class RecorderNaming
{
    private static readonly Regex InvalidIdentifierChars = new("[^A-Za-z0-9_]+", RegexOptions.Compiled);

    public static string CreateControlPropertyName(string locatorValue, UiControlType controlType)
    {
        var candidate = SanitizeIdentifier(locatorValue, $"Recorded{controlType}");
        return string.IsNullOrWhiteSpace(candidate) ? $"Recorded{controlType}" : candidate;
    }

    public static string CreateRecordedMethodBaseName(string scenarioName, DateTimeOffset now)
    {
        return $"Recorded_{SanitizeIdentifier(scenarioName, "Scenario")}_{now:yyyyMMdd_HHmmss}";
    }

    public static string CreateFileSafeName(string value, string fallback)
    {
        var candidate = InvalidIdentifierChars.Replace(value ?? string.Empty, "-").Trim('-');
        return string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
    }

    public static string EnsureUniqueName(string proposedName, ISet<string> reservedNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposedName);
        ArgumentNullException.ThrowIfNull(reservedNames);

        if (reservedNames.Add(proposedName))
        {
            return proposedName;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{proposedName}{suffix}";
            if (reservedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    public static string SanitizeIdentifier(string? rawValue, string fallbackPrefix)
    {
        var value = rawValue?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return fallbackPrefix;
        }

        if (IsValidIdentifier(value))
        {
            return NormalizeIdentifier(value, fallbackPrefix);
        }

        var normalized = InvalidIdentifierChars.Replace(value, " ");
        var words = normalized
            .Split([' ', '-', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ToPascalWord)
            .Where(static word => word.Length > 0)
            .ToArray();

        if (words.Length == 0)
        {
            return fallbackPrefix;
        }

        var candidate = string.Concat(words);
        return NormalizeIdentifier(candidate, fallbackPrefix);
    }

    private static string NormalizeIdentifier(string candidate, string fallbackPrefix)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return fallbackPrefix;
        }

        if (!char.IsLetter(candidate[0]) && candidate[0] != '_')
        {
            candidate = $"{fallbackPrefix}{candidate}";
        }

        return char.ToUpperInvariant(candidate[0]) + candidate[1..];
    }

    private static bool IsValidIdentifier(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (!char.IsLetter(candidate[0]) && candidate[0] != '_')
        {
            return false;
        }

        return candidate.Skip(1).All(static ch => char.IsLetterOrDigit(ch) || ch == '_');
    }

    private static string ToPascalWord(string rawWord)
    {
        if (string.IsNullOrWhiteSpace(rawWord))
        {
            return string.Empty;
        }

        return rawWord.Length == 1
            ? rawWord.ToUpperInvariant()
            : char.ToUpperInvariant(rawWord[0]) + rawWord[1..];
    }
}
