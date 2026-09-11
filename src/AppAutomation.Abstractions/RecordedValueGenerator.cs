using System.Globalization;
using System.Security.Cryptography;

namespace AppAutomation.Abstractions;

/// <summary>
/// Creates a new series of short generated values for one test invocation.
/// </summary>
public static class RecordedValueGenerator
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// Starts a value series with a UTC date and cryptographically random identifier.
    /// Random identifiers protect independent processes from collisions without coordinating them;
    /// they do not replace a data store's uniqueness constraint.
    /// </summary>
    public static RecordedValueSeries Start()
    {
        var identifier = new char[10];
        for (var index = 0; index < identifier.Length; index++)
        {
            identifier[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        var date = DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return new RecordedValueSeries($"{date}_{new string(identifier, 0, 4)}_{new string(identifier, 4, 6)}");
    }
}

/// <summary>
/// Formats generated values that belong to one test invocation.
/// </summary>
public sealed class RecordedValueSeries
{
    private readonly string _identifier;

    internal RecordedValueSeries(string identifier)
    {
        _identifier = identifier;
    }

    /// <summary>
    /// Creates the value for a positive one-based ordinal in this series.
    /// </summary>
    public string Create(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ordinal, 1);
        return $"Recorded_{_identifier}_{ordinal.ToString(CultureInfo.InvariantCulture)}";
    }
}
