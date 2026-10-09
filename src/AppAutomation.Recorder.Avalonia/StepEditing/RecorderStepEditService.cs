namespace AppAutomation.Recorder.Avalonia;

internal static class RecorderStepEditService
{
    public static bool CanEdit(RecordedStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return TryResolveEditKind(step, out _);
    }

    public static bool TryCreateDraft(
        RecordedStep step,
        long revision,
        string currentValuePreview,
        string generatedPreview,
        string? resolvedVariableName,
        IReadOnlyList<RecorderCheckpointOption> checkpoints,
        IReadOnlyList<RecorderGeneratedValueOption> generatedValues,
        IReadOnlyList<RecorderCopiedValueOption> copiedValues,
        IReadOnlyList<RecordedStep> precedingSteps,
        out RecorderStepEditDraft? draft)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(generatedValues);
        ArgumentNullException.ThrowIfNull(copiedValues);
        ArgumentNullException.ThrowIfNull(precedingSteps);

        if (!TryResolveEditKind(step, out var editKind))
        {
            draft = null;
            return false;
        }

        var expectedSource = step.ComparisonKind is RecorderComparisonKind.HasValue or RecorderComparisonKind.IsEmpty
            ? RecorderExpectedValueSourceKind.None
            : step.ExpectedCheckpointId.HasValue
                ? RecorderExpectedValueSourceKind.Checkpoint
                : step.ExpectedGeneratedValueId.HasValue
                    ? RecorderExpectedValueSourceKind.GeneratedValue
                    : step.NumericExpectedExpression is not null
                        ? RecorderExpectedValueSourceKind.Calculated
                        : step.HasExpectedLiteral
                            ? RecorderExpectedValueSourceKind.Literal
                            : RecorderExpectedValueSourceKind.None;
        var compatibleCheckpoints = step.ValueKind is { } valueKind
            ? checkpoints.Where(option => option.ValueKind == valueKind).ToArray()
            : Array.Empty<RecorderCheckpointOption>();
        var compatibleGeneratedValues = step.ValueKind is RecorderValueKind.Text or RecorderValueKind.GridCellText
            ? generatedValues.ToArray()
            : Array.Empty<RecorderGeneratedValueOption>();
        var isKeyCheckpoint = step.ActionKind == RecordedActionKind.CaptureCheckpoint
            && step.GridRowConditions is { Count: > 0 } identity
            && identity.Any(condition => string.Equals(
                condition.ColumnName,
                step.GridTargetColumnName,
                StringComparison.Ordinal));
        bool CanOfferAsRowValue(RecorderGridRowValueReference reference)
        {
            if (!isKeyCheckpoint)
            {
                return true;
            }

            var source = precedingSteps.FirstOrDefault(candidate => reference.Kind switch
            {
                RecorderGridRowValueSourceKind.Checkpoint => candidate.CheckpointId == reference.ValueId,
                RecorderGridRowValueSourceKind.GeneratedValue =>
                    candidate.DefinesGeneratedValue && candidate.GeneratedValueId == reference.ValueId,
                RecorderGridRowValueSourceKind.CopiedValue => candidate.CopiedValueId == reference.ValueId,
                _ => false
            });
            return source is not null
                && source.GridRowConditions is not { Count: > 0 }
                && !source.RowIndex.HasValue
                && source.Control.ControlType != AppAutomation.Abstractions.UiControlType.Grid
                && source.ValueAccessorKind is not (
                    RecorderValueAccessorKind.GridCellText or RecorderValueAccessorKind.GridCellValue)
                && !string.IsNullOrWhiteSpace(source.StringValue)
                && step.GridRowConditions!.Any(condition => string.Equals(
                    condition.Value,
                    source.StringValue,
                    StringComparison.Ordinal));
        }

        var rowVariables = checkpoints
            .Where(static option => option.ValueKind is RecorderValueKind.Text or RecorderValueKind.GridCellText)
            .Select(static option => new RecorderGridRowVariableOption(
                $"{option.VariableName} (checkpoint · {option.ControlName})",
                new RecorderGridRowValueReference(RecorderGridRowValueSourceKind.Checkpoint, option.CheckpointId)))
            .Concat(generatedValues.Select(static option => new RecorderGridRowVariableOption(
                $"{option.VariableName} (generated)",
                new RecorderGridRowValueReference(RecorderGridRowValueSourceKind.GeneratedValue, option.GeneratedValueId))))
            .Concat(copiedValues
                .Where(static option => option.ValueKind is RecorderValueKind.Text or RecorderValueKind.GridCellText)
                .Select(static option => new RecorderGridRowVariableOption(
                    $"{option.VariableName} (copied · {option.ControlName})",
                    new RecorderGridRowValueReference(RecorderGridRowValueSourceKind.CopiedValue, option.CopiedValueId))))
            .Where(option => CanOfferAsRowValue(option.Reference))
            .ToArray();
        var supportsCalculatedExpectedValue = step.ActionKind == RecordedActionKind.AssertValue
            && step.ValueKind is { } numericValueKind
            && step.ValueAccessorKind is { } numericAccessorKind
            && RecorderNumericValueContract.TryCreateGridReference(
                step.Control,
                numericValueKind,
                numericAccessorKind,
                step.GridRowConditions,
                step.GridTargetColumnName,
                out _,
                out _);

        draft = new RecorderStepEditDraft(
            step.StepId,
            revision,
            editKind,
            step.ActionKind,
            step.Control.ProposedPropertyName,
            step.ValueKind,
            step.ValueAccessorKind,
            resolvedVariableName,
            step.StringValue,
            step.ItemValue,
            step.BoolValue,
            step.IntValue,
            step.DoubleValue,
            step.NumericInputText,
            step.NumericInputCultureName,
            step.SecondDoubleValue,
            step.DateValue,
            step.SecondDateValue,
            step.TimeValue,
            step.StringValues,
            step.ComparisonKind,
            expectedSource,
            step.ExpectedCheckpointId,
            step.ExpectedGeneratedValueId,
            step.NumericExpectedExpression,
            step.DateExpression,
            step.SecondDateExpression,
            currentValuePreview,
            generatedPreview,
            compatibleCheckpoints,
            compatibleGeneratedValues,
            step.GridRowConditions,
            step.GridTargetColumnName,
            rowVariables,
            supportsCalculatedExpectedValue)
        {
            GridRowSourceMode = step.GridRowSourceMode,
            GridCapturedRowPosition = step.GridCapturedRowPosition
                ?? (step.RowIndex is >= 0 ? step.RowIndex : null)
        };
        return true;
    }

    public static IReadOnlyList<RecorderExpectedValueSourceKind> GetExpectedSources(
        RecorderStepEditDraft draft,
        RecorderComparisonKind comparison)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (comparison is RecorderComparisonKind.HasValue or RecorderComparisonKind.IsEmpty)
        {
            return [RecorderExpectedValueSourceKind.None];
        }

        var sources = new List<RecorderExpectedValueSourceKind>
        {
            RecorderExpectedValueSourceKind.Literal
        };
        if (draft.CompatibleCheckpoints.Count > 0
            && comparison != RecorderComparisonKind.Contains)
        {
            sources.Add(RecorderExpectedValueSourceKind.Checkpoint);
        }

        if (draft.CompatibleGeneratedValues.Count > 0
            && comparison is RecorderComparisonKind.Equal or RecorderComparisonKind.NotEqual)
        {
            sources.Add(RecorderExpectedValueSourceKind.GeneratedValue);
        }

        if (draft.SupportsCalculatedExpectedValue
            && comparison == RecorderComparisonKind.Equal)
        {
            sources.Add(RecorderExpectedValueSourceKind.Calculated);
        }

        return sources;
    }

    public static bool TryCreateCandidate(
        RecordedStep source,
        RecorderStepEditDraft draft,
        out RecordedStep? candidate,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(draft);

        if (source.StepId != draft.StepId || source.ActionKind != draft.ActionKind)
        {
            candidate = null;
            error = "The recorded step changed after the editor was opened.";
            return false;
        }

        var target = ApplyRetarget(source, draft.RetargetPrototype);
        candidate = target with
        {
            CheckpointVariableName = source.ActionKind == RecordedActionKind.CaptureCheckpoint
                ? draft.VariableName?.Trim()
                : target.CheckpointVariableName,
            GeneratedValueVariableName = source.DefinesGeneratedValue
                ? draft.VariableName?.Trim()
                : target.GeneratedValueVariableName,
            CopiedValueVariableName = source.ActionKind == RecordedActionKind.CaptureCopiedValue
                ? draft.VariableName?.Trim()
                : target.CopiedValueVariableName,
            StringValue = draft.StringValue,
            ItemValue = draft.ItemValue,
            BoolValue = draft.BoolValue,
            IntValue = draft.IntValue,
            DoubleValue = draft.DoubleValue,
            NumericInputText = draft.NumericInputText,
            NumericInputCultureName = draft.NumericInputCultureName,
            SecondDoubleValue = draft.SecondDoubleValue,
            DateValue = draft.DateValue,
            SecondDateValue = draft.SecondDateValue,
            TimeValue = draft.TimeValue,
            StringValues = draft.StringValues?.ToArray(),
            GridRowConditions = draft.GridRowConditions?.ToArray(),
            GridRowSourceMode = draft.GridRowSourceMode,
            GridCapturedRowPosition = draft.GridCapturedRowPosition,
            DateExpression = RecorderValueCodec.NormalizeDateExpression(draft.DateExpression),
            SecondDateExpression = RecorderValueCodec.NormalizeDateExpression(draft.SecondDateExpression)
        };

        if (source.ActionKind != RecordedActionKind.AssertValue)
        {
            error = string.Empty;
            return true;
        }

        if (draft.ComparisonKind is not { } comparisonKind)
        {
            candidate = null;
            error = "Choose an assertion comparison.";
            return false;
        }

        var expectedSource = comparisonKind is RecorderComparisonKind.HasValue or RecorderComparisonKind.IsEmpty
            ? RecorderExpectedValueSourceKind.None
            : draft.ExpectedSource;
        candidate = candidate with
        {
            ComparisonKind = comparisonKind,
            ExpectedCheckpointId = expectedSource == RecorderExpectedValueSourceKind.Checkpoint
                ? draft.ExpectedCheckpointId
                : null,
            ExpectedGeneratedValueId = expectedSource == RecorderExpectedValueSourceKind.GeneratedValue
                ? draft.ExpectedGeneratedValueId
                : null,
            NumericExpectedExpression = expectedSource == RecorderExpectedValueSourceKind.Calculated
                ? draft.NumericExpectedExpression
                : null,
            HasExpectedLiteral = expectedSource == RecorderExpectedValueSourceKind.Literal
        };

        if (expectedSource != RecorderExpectedValueSourceKind.Literal)
        {
            candidate = candidate with
            {
                StringValue = null,
                BoolValue = null,
                DoubleValue = null,
                DateValue = null,
                TimeValue = null,
                StringValues = null,
                DateExpression = null
            };
        }

        if (expectedSource == RecorderExpectedValueSourceKind.Checkpoint
            && candidate.ExpectedCheckpointId is null)
        {
            candidate = null;
            error = "Choose a checkpoint for the expected value.";
            return false;
        }

        if (expectedSource == RecorderExpectedValueSourceKind.GeneratedValue
            && candidate.ExpectedGeneratedValueId is null)
        {
            candidate = null;
            error = "Choose a generated value for the expected value.";
            return false;
        }

        if (expectedSource == RecorderExpectedValueSourceKind.Calculated
            && candidate.NumericExpectedExpression is null)
        {
            candidate = null;
            error = "Configure the calculated expected value.";
            return false;
        }

        if (expectedSource == RecorderExpectedValueSourceKind.None
            && comparisonKind is not (RecorderComparisonKind.HasValue or RecorderComparisonKind.IsEmpty))
        {
            candidate = null;
            error = "Choose an expected value source.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static RecorderStepEditDraft RetargetDraft(
        RecorderStepEditDraft draft,
        RecorderSemanticValueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(snapshot);

        var prototype = snapshot.Prototype;
        var rowPosition = prototype.Control.ControlType == AppAutomation.Abstractions.UiControlType.Grid
            ? prototype.GridCapturedRowPosition
            : null;
        return draft with
        {
            ControlName = prototype.Control.ProposedPropertyName,
            ValueKind = prototype.ValueKind,
            ValueAccessorKind = prototype.ValueAccessorKind,
            CurrentPreview = snapshot.Description.CurrentValueText,
            GridRowConditions = prototype.GridRowConditions,
            GridTargetColumnName = prototype.GridTargetColumnName,
            GridCapturedRowPosition = rowPosition,
            GridRowSourceMode = rowPosition is >= 0
                && draft.GridRowSourceMode == RecorderGridRowSourceMode.CurrentTableRow
                    ? RecorderGridRowSourceMode.CurrentTableRow
                    : prototype.GridRowSourceMode,
            RetargetPrototype = prototype
        };
    }

    private static RecordedStep ApplyRetarget(RecordedStep source, RecordedStep? prototype)
    {
        if (prototype is null)
        {
            return source;
        }

        return source with
        {
            Control = prototype.Control,
            ValueKind = prototype.ValueKind,
            ValueAccessorKind = prototype.ValueAccessorKind,
            RowIndex = prototype.RowIndex,
            ColumnIndex = prototype.ColumnIndex,
            GridRowConditions = prototype.GridRowConditions,
            GridTargetColumnName = prototype.GridTargetColumnName,
            GridCapturedRowPosition = prototype.GridCapturedRowPosition,
            GridRowSourceMode = prototype.GridRowSourceMode,
            GridRowAnchorCheckpointId = null,
            StringValue = prototype.StringValue,
            BoolValue = prototype.BoolValue,
            DoubleValue = prototype.DoubleValue,
            DateValue = prototype.DateValue,
            TimeValue = prototype.TimeValue,
            StringValues = prototype.StringValues,
            Warning = prototype.Warning,
            ValidationStatus = prototype.ValidationStatus,
            ValidationMessage = prototype.ValidationMessage,
            CanPersist = prototype.CanPersist,
            LastValidationAt = prototype.LastValidationAt,
            ReviewState = prototype.ReviewState,
            FailureCode = prototype.FailureCode,
            RuntimeValidationFindings = prototype.RuntimeValidationFindings,
            ValidationBeforeGraphError = prototype.ValidationBeforeGraphError
        };
    }

    private static bool TryResolveEditKind(RecordedStep step, out RecorderStepEditKind editKind)
    {
        if (step.ActionKind == RecordedActionKind.CaptureCheckpoint)
        {
            editKind = RecorderStepEditKind.Checkpoint;
            return true;
        }

        if (step.ActionKind == RecordedActionKind.AssertValue)
        {
            editKind = RecorderStepEditKind.Assertion;
            return true;
        }

        if (step.ActionKind == RecordedActionKind.CaptureCopiedValue)
        {
            editKind = RecorderStepEditKind.CopiedValue;
            return true;
        }

        if (step.DefinesGeneratedValue)
        {
            editKind = RecorderStepEditKind.GeneratedValue;
            return true;
        }

        var populatedKinds = 0;
        editKind = RecorderStepEditKind.Composite;
        if ((step.StringValue is not null || step.ItemValue is not null)
            && step.GeneratedValueId is null
            && step.InputCopiedValueId is null)
        {
            editKind = RecorderStepEditKind.Text;
            populatedKinds++;
        }

        if (step.DoubleValue.HasValue || step.SecondDoubleValue.HasValue)
        {
            editKind = RecorderStepEditKind.Number;
            populatedKinds++;
        }

        if (step.IntValue.HasValue)
        {
            editKind = RecorderStepEditKind.Integer;
            populatedKinds++;
        }

        if (step.BoolValue.HasValue)
        {
            editKind = RecorderStepEditKind.Boolean;
            populatedKinds++;
        }

        if (step.DateValue.HasValue || step.SecondDateValue.HasValue)
        {
            editKind = RecorderStepEditKind.Date;
            populatedKinds++;
        }

        if (step.TimeValue.HasValue)
        {
            editKind = RecorderStepEditKind.Time;
            populatedKinds++;
        }

        if (step.StringValues is not null)
        {
            editKind = RecorderStepEditKind.StringSet;
            populatedKinds++;
        }

        if (populatedKinds == 0)
        {
            if (step.GridRowConditions is { Count: > 0 })
            {
                editKind = RecorderStepEditKind.Composite;
                return true;
            }

            editKind = default;
            return false;
        }

        if (populatedKinds > 1)
        {
            editKind = RecorderStepEditKind.Composite;
        }

        return true;
    }
}
