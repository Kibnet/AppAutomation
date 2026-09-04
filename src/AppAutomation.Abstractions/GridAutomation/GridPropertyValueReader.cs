using System.Collections.Concurrent;
using System.Reflection;

namespace AppAutomation.Abstractions;

internal static class GridPropertyValueReader
{
    private static readonly ConcurrentDictionary<PropertyCacheKey, PropertyResolution> PropertyCache = new();

    public static bool TryReadPath(object source, string? propertyPath, out object? value)
    {
        return TryReadPath(source, propertyPath, out value, out _);
    }

    public static bool TryReadPath(
        object source,
        string? propertyPath,
        out object? value,
        out string? unresolvedSegment)
    {
        ArgumentNullException.ThrowIfNull(source);
        value = source;
        unresolvedSegment = null;
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            return true;
        }

        foreach (var segment in propertyPath.Split('.', StringSplitOptions.TrimEntries))
        {
            if (value is null || !TryReadProperty(value, segment, out value))
            {
                unresolvedSegment = segment;
                return false;
            }
        }

        return true;
    }

    public static bool TryReadProperty(object source, string propertyName, out object? value)
    {
        ArgumentNullException.ThrowIfNull(source);
        value = null;
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var key = new PropertyCacheKey(source.GetType(), propertyName.Trim());
        var resolution = PropertyCache.GetOrAdd(key, static candidate => Resolve(candidate));
        if (resolution.Property is null)
        {
            return false;
        }

        try
        {
            value = resolution.Property.GetValue(source);
            return true;
        }
        catch (Exception ex) when (ex is TargetInvocationException or MethodAccessException or TargetException)
        {
            return false;
        }
    }

    private static PropertyResolution Resolve(PropertyCacheKey key)
    {
        var readable = key.SourceType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(candidate =>
                string.Equals(candidate.Name, key.PropertyName, StringComparison.Ordinal)
                && candidate.GetIndexParameters().Length == 0
                && candidate.GetMethod is { IsPublic: true })
            .Take(2)
            .ToArray();

        return readable.Length == 1
            ? new PropertyResolution(readable[0])
            : default;
    }

    private readonly record struct PropertyCacheKey(Type SourceType, string PropertyName);

    private readonly record struct PropertyResolution(PropertyInfo? Property);
}
