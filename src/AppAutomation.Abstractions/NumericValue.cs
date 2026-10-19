namespace AppAutomation.Abstractions;

/// <summary>Promotes numeric UI values to decimal for mixed-type calculations.</summary>
public static class NumericValue
{
    public static decimal ToDecimal(decimal value) => value;

    public static decimal ToDecimal(decimal? value) => value
        ?? throw new InvalidOperationException("The numeric operand is unavailable.");

    public static decimal ToDecimal(long value) => value;

    public static decimal ToDecimal(long? value) => value.HasValue
        ? value.Value
        : throw new InvalidOperationException("The numeric operand is unavailable.");

    public static decimal ToDecimal(double? value) => value.HasValue
        ? ToDecimal(value.Value)
        : throw new InvalidOperationException("The numeric operand is unavailable.");

    public static decimal ToDecimal(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException("The numeric operand is not finite.");
        }

        try
        {
            var converted = Convert.ToDecimal(value);
            if (value != 0d && converted == 0m)
            {
                throw new InvalidOperationException("The numeric operand would round to zero as a decimal.");
            }

            return converted;
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("The numeric operand exceeds the decimal range.", exception);
        }
    }
}
