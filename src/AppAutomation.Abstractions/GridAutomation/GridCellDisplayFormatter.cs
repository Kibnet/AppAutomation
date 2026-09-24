using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;

namespace AppAutomation.Abstractions;

/// <summary>Formats a typed grid value with the same provider-neutral rules in every runtime.</summary>
public static class GridCellDisplayFormatter
{
    public static string? Format(
        object? value,
        string? formatString = null,
        string? cultureName = null)
    {
        if (value is null)
        {
            return null;
        }

        var culture = string.IsNullOrWhiteSpace(cultureName)
            ? CultureInfo.InvariantCulture
            : CultureInfo.GetCultureInfo(cultureName);
        if (!string.IsNullOrWhiteSpace(formatString) && value is IFormattable formattable)
        {
            return formattable.ToString(formatString, culture);
        }

        if (value is Enum enumValue)
        {
            var member = enumValue.GetType().GetMember(enumValue.ToString()).SingleOrDefault();
            return member?.GetCustomAttribute<DisplayAttribute>()?.GetName()
                ?? member?.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? member?.GetCustomAttribute<EnumMemberAttribute>()?.Value
                ?? enumValue.ToString();
        }

        return Convert.ToString(value, culture);
    }
}
