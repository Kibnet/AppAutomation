namespace AppAutomation.Abstractions;

/// <summary>Явные преобразования текста, прочитанного из интерфейса.</summary>
public static class TextValue
{
    /// <summary>Читает часть после первого разделителя, сохраняя остальные разделители.</summary>
    public static string AfterFirstSeparator(string? text, string separator, bool trimLeadingSpaces = true,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(text))] string? source = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(separator);
        if (text is null)
            throw new InvalidOperationException($"The text source '{source}' is unavailable.");
        var index = text.IndexOf(separator, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidOperationException($"The text source '{source}' does not contain separator '{separator}'.");
        var result = text[(index + separator.Length)..];
        return trimLeadingSpaces ? result.TrimStart() : result;
    }
}
