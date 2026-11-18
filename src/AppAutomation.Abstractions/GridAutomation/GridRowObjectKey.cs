using System.Runtime.CompilerServices;

namespace AppAutomation.Abstractions;

/// <summary>Provides process-local row keys for the lifetime of each reference object.</summary>
public static class GridRowObjectKey
{
    private static readonly ConditionalWeakTable<object, ObjectKey> Keys = new();

    /// <summary>
    /// Gets the same opaque key for the same object reference, independently of its values or equality implementation.
    /// A cloned or reloaded row receives a different key; keys do not keep rows alive.
    /// </summary>
    public static string Get(object rowObject)
    {
        ArgumentNullException.ThrowIfNull(rowObject);
        if (rowObject.GetType().IsValueType)
        {
            throw new ArgumentException("Object row identity requires a reference-type row, not a boxed value type.", nameof(rowObject));
        }

        return Keys.GetValue(rowObject, static _ => new ObjectKey()).Value;
    }

    private sealed class ObjectKey
    {
        public string Value { get; } = Guid.NewGuid().ToString("N");
    }
}
