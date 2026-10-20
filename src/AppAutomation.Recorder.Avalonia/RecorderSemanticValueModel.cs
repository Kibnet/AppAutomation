using AppAutomation.Abstractions;

namespace AppAutomation.Recorder.Avalonia;

/// <summary>Pure transformations of captured semantic values into recorder steps and assertions.</summary>
internal static class RecorderSemanticValueModel
{
    internal static bool RefersToSameSemanticTarget(
        RecorderSemanticValueSnapshot left,
        RecorderSemanticValueSnapshot right)
    {
        var leftStep = left.Prototype;
        var rightStep = right.Prototype;
        return leftStep.Control.ControlType == rightStep.Control.ControlType
            && leftStep.Control.LocatorKind == rightStep.Control.LocatorKind
            && string.Equals(
                leftStep.Control.LocatorValue,
                rightStep.Control.LocatorValue,
                StringComparison.Ordinal)
            && leftStep.RowIndex == rightStep.RowIndex
            && leftStep.ColumnIndex == rightStep.ColumnIndex
            && string.Equals(leftStep.RepeatedItemKey, rightStep.RepeatedItemKey, StringComparison.Ordinal)
            && string.Equals(
                leftStep.GridTargetColumnName,
                rightStep.GridTargetColumnName,
                StringComparison.Ordinal)
            && GridRowConditionsEqual(leftStep.GridRowConditions, rightStep.GridRowConditions);
    }

    private static bool GridRowConditionsEqual(
        IReadOnlyList<RecordedGridRowCondition>? left,
        IReadOnlyList<RecordedGridRowCondition>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].ColumnName, right[index].ColumnName, StringComparison.Ordinal)
                || !string.Equals(left[index].Value, right[index].Value, StringComparison.Ordinal)
                || left[index].ValueReference != right[index].ValueReference)
            {
                return false;
            }
        }

        return true;
    }

    internal static string DescribeSemanticTarget(RecorderSemanticValueSnapshot snapshot)
    {
        var step = snapshot.Prototype;
        var target = $"{step.Control.LocatorKind}:{step.Control.LocatorValue}";
        if (step.RepeatedItemKey is { } itemKey)
        {
            return $"{target}[{itemKey}]";
        }
        if (step.GridRowConditions is { Count: > 0 })
        {
            var row = string.Join(
                ", ",
                step.GridRowConditions.Select(condition => $"{condition.ColumnName}={condition.Value}"));
            return $"{target}[{row}; {step.GridTargetColumnName}]";
        }

        return step.RowIndex is { } rowIndex && step.ColumnIndex is { } columnIndex
            ? $"{target}[{rowIndex}, {columnIndex}]"
            : target;
    }

    internal static RecordedStep CreateSemanticValueStep(
        RecordedActionKind actionKind,
        SemanticValueCandidate candidate,
        Guid? checkpointId = null,
        string? checkpointVariableName = null,
        RecorderComparisonKind? comparisonKind = null,
        Guid? expectedCheckpointId = null,
        Guid? expectedGeneratedValueId = null,
        bool hasExpectedLiteral = false,
        RecorderDateExpression? dateExpression = null,
        RecorderNumericExpectedExpression? numericExpectedExpression = null)
    {
        return new RecordedStep(
            actionKind,
            candidate.Control,
            StringValue: candidate.StringValue,
            BoolValue: candidate.BoolValue,
            DoubleValue: candidate.DoubleValue,
            DateValue: candidate.DateValue,
            RowIndex: candidate.GridContext?.RowIndex,
            ColumnIndex: candidate.GridContext?.ColumnIndex,
            StringValues: candidate.StringValues,
            TimeValue: candidate.TimeValue,
            ValueKind: candidate.ValueKind,
            ValueAccessorKind: candidate.ValueAccessorKind,
            ComparisonKind: comparisonKind,
            CheckpointId: checkpointId,
            CheckpointVariableName: checkpointVariableName,
            ExpectedCheckpointId: expectedCheckpointId,
            ExpectedGeneratedValueId: expectedGeneratedValueId,
            HasExpectedLiteral: hasExpectedLiteral,
            DateExpression: dateExpression,
            NumericExpectedExpression: numericExpectedExpression)
        {
            GridRowConditions = candidate.GridContext?.RowConditions,
            GridTargetColumnName = candidate.GridContext?.TargetColumnName,
            RepeatedItemKey = candidate.RepeatedItemKey
        };
    }

    internal static bool TryNormalizeLiteralDateExpression(
        SemanticValueCandidate candidate,
        RecorderDateExpression? requested,
        DateTime captureDate,
        out RecorderDateExpression? normalized,
        out string error)
    {
        normalized = requested?.ReferenceKind == RecorderDateReferenceKind.Exact
            ? null
            : requested;
        error = string.Empty;
        if (normalized is null)
        {
            return true;
        }

        if (candidate.ValueKind != RecorderValueKind.Date)
        {
            error = "A relative date can only be used with a date assertion.";
            return false;
        }

        if (!candidate.DateValue.HasValue)
        {
            error = "A relative date cannot be used when the expected date is null.";
            return false;
        }

        if (normalized.ReferenceKind != RecorderDateReferenceKind.RelativeToToday)
        {
            error = "The requested date reference is not supported.";
            return false;
        }

        try
        {
            _ = captureDate.AddDays(normalized.DayOffset);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            error = "The relative date is outside the supported range.";
            return false;
        }
    }

    internal static SemanticValueCandidate CreateCandidate(RecorderSemanticValueSnapshot snapshot)
    {
        var prototype = snapshot.Prototype;
        GridValueContext? gridContext = null;
        if (prototype.GridRowConditions is not null
            || prototype.RowIndex.HasValue
            || prototype.ColumnIndex.HasValue)
        {
            gridContext = new GridValueContext(
                prototype.RowIndex ?? -1,
                prototype.ColumnIndex ?? -1,
                prototype.GridRowConditions,
                prototype.GridTargetColumnName);
        }

        return new SemanticValueCandidate(
            prototype.Control,
            prototype.ValueKind ?? throw new InvalidOperationException("Semantic value snapshot does not contain a value kind."),
            prototype.ValueAccessorKind ?? throw new InvalidOperationException("Semantic value snapshot does not contain an accessor kind."),
            prototype.StringValue,
            prototype.BoolValue,
            prototype.DoubleValue,
            prototype.DateValue,
            prototype.TimeValue,
            prototype.StringValues,
            gridContext,
            prototype.RepeatedItemKey);
    }

    internal static StepCreationResult CreateStepFromSnapshot(
        RecorderSemanticValueSnapshot snapshot,
        RecordedStep step,
        string message,
        Guid stepId,
        DateTimeOffset validationAt)
    {
        var prototype = snapshot.Prototype;
        return StepCreationResult.Created(
            step with
            {
                Warning = prototype.Warning,
                ValidationStatus = prototype.ValidationStatus,
                ValidationMessage = prototype.ValidationMessage,
                CanPersist = prototype.CanPersist,
                StepId = stepId,
                LastValidationAt = validationAt,
                GridRowConditions = prototype.GridRowConditions,
                GridTargetColumnName = prototype.GridTargetColumnName,
                GridCapturedRowPosition = prototype.GridCapturedRowPosition,
                RepeatedItemKey = prototype.RepeatedItemKey
            },
            message);
    }

    internal static bool HasLiteral(SemanticValueCandidate candidate)
    {
        return candidate.ValueKind switch
        {
            RecorderValueKind.Text or RecorderValueKind.Color or RecorderValueKind.GridCellText => candidate.StringValue is not null,
            RecorderValueKind.Number => candidate.DoubleValue.HasValue,
            RecorderValueKind.Boolean => candidate.BoolValue.HasValue,
            RecorderValueKind.Date => true,
            RecorderValueKind.Time => true,
            RecorderValueKind.StringSet => candidate.StringValues is not null,
            _ => false
        };
    }

    internal static string FormatLiteralText(SemanticValueCandidate candidate)
    {
        return candidate.ValueKind switch
        {
            RecorderValueKind.Text or RecorderValueKind.Color or RecorderValueKind.GridCellText => candidate.StringValue ?? string.Empty,
            RecorderValueKind.Number => candidate.DoubleValue?.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            RecorderValueKind.Boolean => candidate.BoolValue == true ? "true" : "false",
            RecorderValueKind.Date => candidate.DateValue?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "null",
            RecorderValueKind.Time => candidate.TimeValue?.ToString("c", System.Globalization.CultureInfo.InvariantCulture) ?? "null",
            RecorderValueKind.StringSet => string.Join(", ", candidate.StringValues ?? []),
            _ => string.Empty
        };
    }

    internal static bool TryApplyLiteralText(
        SemanticValueCandidate candidate,
        string text,
        out SemanticValueCandidate updated,
        out string error)
    {
        updated = candidate;
        error = string.Empty;
        switch (candidate.ValueKind)
        {
            case RecorderValueKind.Text:
            case RecorderValueKind.GridCellText:
                updated = candidate with { StringValue = text };
                return true;
            case RecorderValueKind.Color:
                if (ColorValue.TryNormalize(text, out var color))
                {
                    updated = candidate with { StringValue = color };
                    return true;
                }

                error = "Expected color must use #RRGGBB or #AARRGGBB.";
                return false;
            case RecorderValueKind.Number:
                if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                {
                    updated = candidate with { DoubleValue = number };
                    return true;
                }

                error = "Expected number must use invariant numeric format.";
                return false;
            case RecorderValueKind.Boolean:
                if (bool.TryParse(text, out var boolean))
                {
                    updated = candidate with { BoolValue = boolean };
                    return true;
                }

                error = "Expected boolean must be true or false.";
                return false;
            case RecorderValueKind.Date:
                if (string.Equals(text.Trim(), "null", StringComparison.OrdinalIgnoreCase))
                {
                    updated = candidate with { DateValue = null };
                    return true;
                }

                if (DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
                {
                    updated = candidate with { DateValue = date.Date };
                    return true;
                }

                error = "Expected date must use yyyy-MM-dd or null.";
                return false;
            case RecorderValueKind.Time:
                if (string.Equals(text.Trim(), "null", StringComparison.OrdinalIgnoreCase))
                {
                    updated = candidate with { TimeValue = null };
                    return true;
                }

                if (TimeSpan.TryParse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var time))
                {
                    updated = candidate with { TimeValue = time };
                    return true;
                }

                error = "Expected time must use a TimeSpan format or null.";
                return false;
            case RecorderValueKind.StringSet:
                updated = candidate with
                {
                    StringValues = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                };
                return true;
            default:
                error = $"Literal kind '{candidate.ValueKind}' is not supported.";
                return false;
        }
    }

    internal sealed record SemanticValueCandidate(
        RecordedControlDescriptor Control,
        RecorderValueKind ValueKind,
        RecorderValueAccessorKind ValueAccessorKind,
        string? StringValue = null,
        bool? BoolValue = null,
        double? DoubleValue = null,
        DateTime? DateValue = null,
        TimeSpan? TimeValue = null,
        IReadOnlyList<string>? StringValues = null,
        GridValueContext? GridContext = null,
        string? RepeatedItemKey = null);

    internal sealed record GridValueContext(
        int RowIndex,
        int ColumnIndex,
        IReadOnlyList<RecordedGridRowCondition>? RowConditions,
        string? TargetColumnName);
}
