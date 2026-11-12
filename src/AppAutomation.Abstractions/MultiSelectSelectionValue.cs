using System.Text.Json;

namespace AppAutomation.Abstractions;

/// <summary>Encodes the complete committed selection published on a multi-select editor's UIA ItemStatus.</summary>
public static class MultiSelectSelectionValue
{
    private const string Prefix = "AppAutomation.MultiSelect/";
    private const string CurrentPrefix = Prefix + "1:";

    /// <summary>Encodes the actual committed captions. An empty sequence publishes a known empty selection.</summary>
    public static string ToAutomationValue(IEnumerable<string> selectedItems)
    {
        ArgumentNullException.ThrowIfNull(selectedItems);
        var captions = selectedItems.ToArray();
        if (captions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Selected items must have readable captions.", nameof(selectedItems));
        return CurrentPrefix + JsonSerializer.Serialize(captions);
    }

    internal static string[]? ReadAutomationValue(string? value)
    {
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        if (!value.StartsWith(CurrentPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("The multi-select UIA ItemStatus uses an unsupported selection version.");

        try
        {
            var captions = JsonSerializer.Deserialize<string[]>(value[CurrentPrefix.Length..]);
            if (captions is null || captions.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("The multi-select UIA ItemStatus must contain an array of readable captions.");
            return captions;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The multi-select UIA ItemStatus selection is not a valid JSON array.", exception);
        }
    }
}
