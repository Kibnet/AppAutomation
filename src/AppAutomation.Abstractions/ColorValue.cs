namespace AppAutomation.Abstractions;

/// <summary>
/// Normalizes provider-neutral color values used by automation commands.
/// </summary>
public static class ColorValue
{
    /// <summary>
    /// Converts <c>#RRGGBB</c> or <c>#AARRGGBB</c> to uppercase <c>#AARRGGBB</c>.
    /// </summary>
    public static string Normalize(string color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(color);

        var value = color.Trim();
        if (value.Length == 7 && value[0] == '#')
        {
            value = $"#FF{value[1..]}";
        }

        if (value.Length != 9 || value[0] != '#' || !value.AsSpan(1).ContainsOnlyHexDigits())
        {
            throw new FormatException(
                $"Color '{color}' must use #RRGGBB or #AARRGGBB hexadecimal format.");
        }

        return value.ToUpperInvariant();
    }

    /// <summary>
    /// Attempts to normalize a provider-neutral color value.
    /// </summary>
    public static bool TryNormalize(string? color, out string normalized)
    {
        try
        {
            normalized = Normalize(color!);
            return true;
        }
        catch (ArgumentException)
        {
            normalized = string.Empty;
            return false;
        }
        catch (FormatException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    private static bool ContainsOnlyHexDigits(this ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!((character >= '0' && character <= '9')
                  || (character >= 'A' && character <= 'F')
                  || (character >= 'a' && character <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}
