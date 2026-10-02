namespace AppAutomation.Abstractions;

internal static class GridSelectionText
{
    public static bool Equals(string? actual, string? expected)
    {
        return string.Equals(
            Normalize(actual),
            Normalize(expected),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
