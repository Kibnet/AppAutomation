using System.Globalization;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

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
    /// Creates a short text value for a positive one-based ordinal in this series.
    /// </summary>
    public string Create(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ordinal, 1);
        var date = _identifier[.._identifier.IndexOf('_')];
        var suffix = _identifier[(_identifier.LastIndexOf('_') + 1)..];
        return $"Recorded_{date}_{suffix}_{ordinal.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Creates a synthetic ten-digit INN with a valid check digit. Ordinals are limited to 100,000.</summary>
    public string CreateOrganizationInn(int ordinal)
    {
        var digits = "7701" + CreateDigits(ordinal, 5, "organization-inn");
        return digits + CheckDigit(digits, [2, 4, 10, 3, 5, 9, 4, 6, 8]);
    }

    /// <summary>Creates a compact synthetic +7999 mobile number. Ordinals are limited to 10,000,000.</summary>
    public string CreateRussianPhoneNumber(int ordinal) => "+7999" + CreateDigits(ordinal, 7, "phone");

    /// <summary>Creates an address for test data; no mailbox existence or safe delivery is implied.</summary>
    public string CreateEmail(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ordinal, 1);
        var suffix = _identifier[(_identifier.IndexOf('_') + 1)..];
        return $"{suffix.ToLowerInvariant()}_{ordinal.ToString(CultureInfo.InvariantCulture)}@example.ru";
    }

    private string CreateDigits(int ordinal, int count, string format)
    {
        var capacity = (int)Math.Pow(10, count);
        ArgumentOutOfRangeException.ThrowIfLessThan(ordinal, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ordinal, capacity);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{_identifier}:{format}"));
        var offset = BinaryPrimitives.ReadUInt64BigEndian(hash) % (ulong)capacity;
        // A rotation, not a retry loop: distinct supported ordinals cannot collide within a series.
        var value = (offset + (ulong)ordinal - 1) % (ulong)capacity;
        return value.ToString($"D{count}", CultureInfo.InvariantCulture);
    }

    private static char CheckDigit(string digits, ReadOnlySpan<int> weights)
    {
        var sum = 0;
        for (var index = 0; index < weights.Length; index++)
            sum += (digits[index] - '0') * weights[index];
        return (char)('0' + sum % 11 % 10);
    }
}
