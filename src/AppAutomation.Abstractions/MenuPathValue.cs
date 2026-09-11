using System.Collections;
using System.Reflection;
using System.Text;

namespace AppAutomation.Abstractions;

/// <summary>
/// Validates exact provider-neutral menu paths and normalizes access-key captions.
/// </summary>
public static class MenuPathValue
{
    internal static string? TryGetVisibleCaption(object? value, string? fallback = null)
    {
        var text = TryExtractVisibleText(value, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0)
            ?? fallback;
        return string.IsNullOrWhiteSpace(text)
            ? null
            : ToVisibleCaption(text.Trim());
    }

    /// <summary>
    /// Returns an immutable copy of a non-empty exact menu path.
    /// </summary>
    public static IReadOnlyList<string> Normalize(IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0)
        {
            throw new ArgumentException("A menu path must contain at least one caption.", nameof(path));
        }

        var normalized = path.ToArray();
        if (normalized.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Menu path captions cannot be empty or whitespace.", nameof(path));
        }

        return normalized;
    }

    /// <summary>
    /// Converts an Avalonia access-key header such as <c>_File</c> to its visible caption.
    /// </summary>
    public static string ToVisibleCaption(string caption)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caption);
        var result = new StringBuilder(caption.Length);
        for (var index = 0; index < caption.Length; index++)
        {
            if (caption[index] != '_')
            {
                result.Append(caption[index]);
                continue;
            }

            if (index + 1 < caption.Length && caption[index + 1] == '_')
            {
                result.Append('_');
                index++;
            }
        }

        return result.ToString();
    }

    private static string? TryExtractVisibleText(object? value, HashSet<object> visited, int depth)
    {
        if (value is null || depth > 4)
        {
            return null;
        }

        if (value is string text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        if (value is char character)
        {
            return character.ToString();
        }

        if (!value.GetType().IsValueType && !visited.Add(value))
        {
            return null;
        }

        foreach (var propertyName in new[] { "Text", "Content", "Header" })
        {
            var propertyValue = TryReadProperty(value, propertyName);
            var propertyText = TryExtractVisibleText(propertyValue, visited, depth + 1);
            if (!string.IsNullOrWhiteSpace(propertyText))
            {
                return propertyText;
            }
        }

        if (TryReadProperty(value, "Children") is IEnumerable children)
        {
            foreach (var child in children)
            {
                var childText = TryExtractVisibleText(child, visited, depth + 1);
                if (!string.IsNullOrWhiteSpace(childText))
                {
                    return childText;
                }
            }
        }

        var toString = value.GetType().GetMethod(nameof(ToString), Type.EmptyTypes);
        if (toString?.DeclaringType != typeof(object))
        {
            var rendered = value.ToString();
            return string.IsNullOrWhiteSpace(rendered) ? null : rendered;
        }

        return null;
    }

    private static object? TryReadProperty(object value, string propertyName)
    {
        try
        {
            return value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }
}
