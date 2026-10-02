using System.Globalization;

namespace AppAutomation.Recorder.Avalonia;

internal static class RecorderValueCodec
{
    public static RecorderDateInput CreateDateInput(
        DateTime? exactDate,
        RecorderDateExpression? expression) =>
        new(
            exactDate,
            expression?.ReferenceKind ?? RecorderDateReferenceKind.Exact,
            expression?.DayOffset ?? (exactDate.HasValue
                ? (exactDate.Value.Date - DateTime.Today).Days
                : 0));

    public static RecorderDateExpression? NormalizeDateExpression(RecorderDateExpression? expression) =>
        expression?.ReferenceKind == RecorderDateReferenceKind.Exact ? null : expression;

    public static string FormatDate(DateTime date, RecorderDateExpression? expression)
    {
        var input = CreateDateInput(date, expression);
        if (input.ReferenceKind != RecorderDateReferenceKind.RelativeToToday)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return input.DayOffset switch
        {
            0 => "today",
            > 0 => $"today+{input.DayOffset.ToString(CultureInfo.InvariantCulture)}",
            _ => $"today{input.DayOffset.ToString(CultureInfo.InvariantCulture)}"
        };
    }

    public static bool TryCreateRelativeExpression(
        DateTime? recordedDate,
        string? dayOffsetText,
        out RecorderDateExpression? expression,
        out string error)
    {
        expression = null;
        if (!recordedDate.HasValue)
        {
            error = "A relative expression cannot be used for an empty boundary.";
            return false;
        }

        if (!int.TryParse(dayOffsetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dayOffset))
        {
            error = "Enter a whole number of days.";
            return false;
        }

        return TryCreateRelativeDate(dayOffset, out _, out expression, out error);
    }

    public static bool TryCreateNumericOperand(
        RecorderNumericOperandInput input,
        bool allowPendingControlSelection,
        RecorderNumericOperand? fallbackOperand,
        out RecorderNumericOperand? operand,
        out string? error)
    {
        switch (input.Kind)
        {
            case RecorderNumericOperandKind.Literal:
                if (!TryParseFiniteNumber(input.LiteralText, out var literal))
                {
                    operand = null;
                    error = "Enter a finite numeric operand.";
                    return false;
                }

                operand = RecorderNumericOperand.FromLiteral(literal);
                break;
            case RecorderNumericOperandKind.Checkpoint when input.CheckpointId is { } checkpointId:
                operand = RecorderNumericOperand.FromCheckpoint(checkpointId);
                break;
            case RecorderNumericOperandKind.Checkpoint:
                operand = null;
                error = "Choose a numeric checkpoint.";
                return false;
            case RecorderNumericOperandKind.Control when input.ControlOperand is not null:
                operand = input.ControlOperand;
                break;
            case RecorderNumericOperandKind.Control when allowPendingControlSelection:
                operand = fallbackOperand ?? RecorderNumericOperand.FromLiteral(0);
                break;
            case RecorderNumericOperandKind.Control:
                operand = null;
                error = "Select a numeric UI value.";
                return false;
            default:
                operand = null;
                error = "Choose an operand source.";
                return false;
        }

        error = null;
        return true;
    }

    public static bool DividesByLiteralZero(
        RecorderArithmeticOperation operation,
        RecorderNumericOperand? rightOperand) =>
        operation == RecorderArithmeticOperation.Divide
        && rightOperand?.Kind == RecorderNumericOperandKind.Literal
        && rightOperand.LiteralValue == 0;

    public static bool TryParseFiniteNumber(string? text, out double value)
    {
        const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
        var parsed = double.TryParse(text, styles, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, styles, CultureInfo.InvariantCulture, out value);
        return parsed && double.IsFinite(value);
    }

    public static bool TryParseTimeOfDay(string? text, out TimeSpan value)
    {
        var parsed = TimeSpan.TryParse(text, CultureInfo.CurrentCulture, out value)
            || TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value);
        return parsed && value >= TimeSpan.Zero && value < TimeSpan.FromDays(1);
    }

    public static bool TryParseDate(
        string? text,
        out DateTime value,
        out RecorderDateExpression? expression,
        out string error)
    {
        var normalized = text?.Trim() ?? string.Empty;
        if (normalized.StartsWith("today", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = normalized["today".Length..].Trim();
            if (suffix.Length == 0)
            {
                return TryCreateRelativeDate(0, out value, out expression, out error);
            }

            if (int.TryParse(suffix, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var offset))
            {
                return TryCreateRelativeDate(offset, out value, out expression, out error);
            }

            value = default;
            expression = null;
            error = "Enter a relative date such as today+3 or today-2.";
            return false;
        }

        if (DateTime.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.None, out value)
            || DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            value = value.Date;
            expression = null;
            error = string.Empty;
            return true;
        }

        expression = null;
        error = "Enter a date or a relative value such as today+3.";
        return false;
    }

    public static bool TryCreateRelativeDate(
        int dayOffset,
        out DateTime value,
        out RecorderDateExpression? expression,
        out string error)
    {
        try
        {
            value = DateTime.Today.AddDays(dayOffset);
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            expression = null;
            error = "The relative date is outside the supported range.";
            return false;
        }

        expression = new RecorderDateExpression(
            RecorderDateReferenceKind.RelativeToToday,
            dayOffset);
        error = string.Empty;
        return true;
    }
}

internal sealed record RecorderNumericOperandInput(
    RecorderNumericOperandKind Kind,
    string? LiteralText,
    Guid? CheckpointId,
    RecorderNumericOperand? ControlOperand);
