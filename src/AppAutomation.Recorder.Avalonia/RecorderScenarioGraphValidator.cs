namespace AppAutomation.Recorder.Avalonia;

internal sealed record RecorderScenarioGraphValidationResult(
    bool Success,
    IReadOnlyDictionary<Guid, string> CheckpointVariables,
    IReadOnlyDictionary<Guid, string> GeneratedValueVariables,
    IReadOnlyDictionary<Guid, string> CopiedValueVariables,
    string? GeneratedValueSeriesVariable,
    IReadOnlyDictionary<Guid, string> StepErrors,
    string? Error)
{
    public static RecorderScenarioGraphValidationResult Failed(
        string error,
        IReadOnlyDictionary<Guid, string> checkpointVariables,
        IReadOnlyDictionary<Guid, string> generatedValueVariables,
        IReadOnlyDictionary<Guid, string> copiedValueVariables,
        string? generatedValueSeriesVariable,
        IReadOnlyDictionary<Guid, string> stepErrors) =>
        new(
            false,
            checkpointVariables,
            generatedValueVariables,
            copiedValueVariables,
            generatedValueSeriesVariable,
            stepErrors,
            error);

    public static RecorderScenarioGraphValidationResult Valid(
        IReadOnlyDictionary<Guid, string> checkpointVariables,
        IReadOnlyDictionary<Guid, string> generatedValueVariables,
        IReadOnlyDictionary<Guid, string> copiedValueVariables,
        string? generatedValueSeriesVariable) =>
        new(
            true,
            checkpointVariables,
            generatedValueVariables,
            copiedValueVariables,
            generatedValueSeriesVariable,
            new Dictionary<Guid, string>(),
            null);
}

internal static class RecorderScenarioGraphValidator
{
    public static RecorderScenarioGraphValidationResult Validate(IReadOnlyList<RecordedStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var checkpointVariables = new Dictionary<Guid, string>();
        var checkpointValueKinds = new Dictionary<Guid, RecorderValueKind>();
        var generatedValueVariables = new Dictionary<Guid, string>();
        var copiedValueVariables = new Dictionary<Guid, string>();
        var generatedValueOrdinals = new HashSet<int>();
        var reservedNames = new HashSet<string>(StringComparer.Ordinal);
        var stepErrors = new Dictionary<Guid, string>();

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var rowReferenceValidation = ValidateGridRowReferences(
                step,
                index,
                checkpointValueKinds,
                generatedValueVariables,
                copiedValueVariables);
            if (!rowReferenceValidation.IsValid)
            {
                stepErrors[step.StepId] = rowReferenceValidation.Error;
                continue;
            }

            if (step.GeneratedValueId is not null && step.InputCopiedValueId is not null)
            {
                stepErrors[step.StepId] =
                    $"Text input step {index + 1} cannot use a generated value and a copied value at the same time.";
                continue;
            }

            if (step.GeneratedValueId is not null)
            {
                var generatedValueValidation = ValidateGeneratedValue(
                    step,
                    index,
                    generatedValueVariables,
                    generatedValueOrdinals,
                    reservedNames);
                if (!generatedValueValidation.IsValid)
                {
                    stepErrors[step.StepId] = generatedValueValidation.Error;
                }
            }

            if (step.ActionKind == RecordedActionKind.CaptureCopiedValue)
            {
                var copiedValueValidation = ValidateCopiedValueDefinition(
                    step,
                    index,
                    copiedValueVariables,
                    reservedNames);
                if (!copiedValueValidation.IsValid)
                {
                    stepErrors[step.StepId] = copiedValueValidation.Error;
                }

                continue;
            }

            if (step.InputCopiedValueId is not null)
            {
                var copiedValueValidation = ValidateCopiedValueUse(
                    step,
                    index,
                    copiedValueVariables);
                if (!copiedValueValidation.IsValid)
                {
                    stepErrors[step.StepId] = copiedValueValidation.Error;
                }
            }

            if (step.ActionKind == RecordedActionKind.CaptureCheckpoint)
            {
                var checkpointValidation = ValidateCheckpoint(
                    step,
                    index,
                    checkpointVariables,
                    checkpointValueKinds,
                    reservedNames);
                if (!checkpointValidation.IsValid)
                {
                    stepErrors[step.StepId] = checkpointValidation.Error;
                }

                continue;
            }

            if (step.ActionKind != RecordedActionKind.AssertValue)
            {
                continue;
            }

            var assertionValidation = ValidateAssertion(
                step,
                index,
                checkpointValueKinds,
                generatedValueVariables);
            if (!assertionValidation.IsValid)
            {
                stepErrors[step.StepId] = assertionValidation.Error;
            }
        }

        var generatedValueSeriesVariable = generatedValueVariables.Count == 0
            ? null
            : RecorderNaming.EnsureUniqueName("recordedValues", reservedNames);
        return stepErrors.Count == 0
            ? RecorderScenarioGraphValidationResult.Valid(
                checkpointVariables,
                generatedValueVariables,
                copiedValueVariables,
                generatedValueSeriesVariable)
            : RecorderScenarioGraphValidationResult.Failed(
                stepErrors.Values.First(),
                checkpointVariables,
                generatedValueVariables,
                copiedValueVariables,
                generatedValueSeriesVariable,
                stepErrors);
    }

    private static RecorderGraphStepValidationResult ValidateGridRowReferences(
        RecordedStep step,
        int index,
        IReadOnlyDictionary<Guid, RecorderValueKind> checkpointValueKinds,
        IReadOnlyDictionary<Guid, string> generatedValueVariables,
        IReadOnlyDictionary<Guid, string> copiedValueVariables)
    {
        var rowConditions = new[]
        {
            step.GridRowConditions,
            step.NumericExpectedExpression?.Left.GridValueReference?.RowConditions,
            step.NumericExpectedExpression?.Right.GridValueReference?.RowConditions
        };
        foreach (var conditions in rowConditions)
        {
            if (conditions is null)
            {
                continue;
            }

            foreach (var condition in conditions)
            {
                if (condition.ValueReference is not { } reference)
                {
                    continue;
                }

                if (reference.ValueId == Guid.Empty)
                {
                    return RecorderGraphStepValidationResult.Invalid(
                        $"Grid step {index + 1} has an invalid variable reference for row column '{condition.ColumnName}'.");
                }

                var isAvailable = reference.Kind switch
                {
                    RecorderGridRowValueSourceKind.Checkpoint =>
                        checkpointValueKinds.TryGetValue(reference.ValueId, out var valueKind)
                        && valueKind is RecorderValueKind.Text or RecorderValueKind.GridCellText,
                    RecorderGridRowValueSourceKind.GeneratedValue =>
                        generatedValueVariables.ContainsKey(reference.ValueId),
                    RecorderGridRowValueSourceKind.CopiedValue =>
                        copiedValueVariables.ContainsKey(reference.ValueId),
                    _ => false
                };
                if (!isAvailable)
                {
                    return RecorderGraphStepValidationResult.Invalid(
                        $"Grid step {index + 1} references a missing, later or non-text variable "
                        + $"for row column '{condition.ColumnName}'.");
                }
            }
        }

        return RecorderGraphStepValidationResult.Valid;
    }

    private static RecorderGraphStepValidationResult ValidateCopiedValueDefinition(
        RecordedStep step,
        int index,
        Dictionary<Guid, string> variables,
        HashSet<string> reservedNames)
    {
        if (step.CopiedValueId is not { } copiedValueId || copiedValueId == Guid.Empty)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Copied value step {index + 1} does not have a stable copied value id.");
        }

        if (variables.ContainsKey(copiedValueId))
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Copied value id '{copiedValueId}' is defined more than once.");
        }

        if (step.ValueKind is not (RecorderValueKind.Text or RecorderValueKind.GridCellText)
            || step.ValueAccessorKind is null)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Copied value step {index + 1} does not define a readable text value.");
        }

        variables.Add(
            copiedValueId,
            RecorderNaming.CreateCopiedValueVariableName(
                step.CopiedValueVariableName,
                reservedNames,
                step.PreserveVariableName));
        return RecorderGraphStepValidationResult.Valid;
    }

    private static RecorderGraphStepValidationResult ValidateCopiedValueUse(
        RecordedStep step,
        int index,
        IReadOnlyDictionary<Guid, string> variables)
    {
        if (step.ActionKind is not (RecordedActionKind.EnterText or RecordedActionKind.EnterSearch))
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Copied value use {index + 1} must be an EnterText or EnterSearch action.");
        }

        var copiedValueId = step.InputCopiedValueId!.Value;
        return copiedValueId != Guid.Empty && variables.ContainsKey(copiedValueId)
            ? RecorderGraphStepValidationResult.Valid
            : RecorderGraphStepValidationResult.Invalid(
                $"Copied value use {index + 1} references a missing or later copied value '{copiedValueId}'.");
    }

    private static RecorderGraphStepValidationResult ValidateGeneratedValue(
        RecordedStep step,
        int index,
        Dictionary<Guid, string> variables,
        HashSet<int> ordinals,
        HashSet<string> reservedNames)
    {
        if (step.ActionKind != RecordedActionKind.EnterText)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Generated value step {index + 1} must be an EnterText action.");
        }

        var generatedValueId = step.GeneratedValueId!.Value;
        if (generatedValueId == Guid.Empty)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Generated value step {index + 1} does not have a stable generated value id.");
        }

        if (step.DefinesGeneratedValue)
        {
            if (variables.ContainsKey(generatedValueId))
            {
                return RecorderGraphStepValidationResult.Invalid(
                    $"Generated value id '{generatedValueId}' is defined more than once.");
            }

            if (step.GeneratedValueOrdinal is not > 0)
            {
                return RecorderGraphStepValidationResult.Invalid(
                    $"Generated value step {index + 1} does not define a positive ordinal.");
            }

            if (!ordinals.Add(step.GeneratedValueOrdinal.Value))
            {
                return RecorderGraphStepValidationResult.Invalid(
                    $"Generated value ordinal '{step.GeneratedValueOrdinal.Value}' is defined more than once.");
            }

            variables.Add(
                generatedValueId,
                RecorderNaming.CreateGeneratedValueVariableName(
                    step.GeneratedValueVariableName,
                    reservedNames,
                    step.PreserveVariableName));
            return RecorderGraphStepValidationResult.Valid;
        }

        return variables.ContainsKey(generatedValueId)
            ? RecorderGraphStepValidationResult.Valid
            : RecorderGraphStepValidationResult.Invalid(
                $"Generated value step {index + 1} references a missing or later generated value '{generatedValueId}'.");
    }

    private static RecorderGraphStepValidationResult ValidateCheckpoint(
        RecordedStep step,
        int index,
        Dictionary<Guid, string> variables,
        Dictionary<Guid, RecorderValueKind> valueKinds,
        HashSet<string> reservedNames)
    {
        if (step.CheckpointId is not { } checkpointId || checkpointId == Guid.Empty)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Checkpoint step {index + 1} does not have a stable checkpoint id.");
        }

        if (variables.ContainsKey(checkpointId))
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Checkpoint id '{checkpointId}' is defined more than once.");
        }

        if (step.ValueKind is not { } valueKind || step.ValueAccessorKind is null)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Checkpoint step {index + 1} does not define a readable semantic value.");
        }

        var variableName = RecorderNaming.CreateCheckpointVariableName(
            step.CheckpointVariableName,
            reservedNames,
            step.PreserveVariableName);
        variables.Add(checkpointId, variableName);
        valueKinds.Add(checkpointId, valueKind);
        return RecorderGraphStepValidationResult.Valid;
    }

    private static RecorderGraphStepValidationResult ValidateAssertion(
        RecordedStep step,
        int index,
        IReadOnlyDictionary<Guid, RecorderValueKind> checkpointValueKinds,
        IReadOnlyDictionary<Guid, string> generatedValueVariables)
    {
        if (step.ValueKind is not { } valueKind || step.ValueAccessorKind is null)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} does not define a readable semantic value.");
        }

        if (step.ComparisonKind is not { } comparisonKind)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} does not define a comparison.");
        }

        if (!SupportsComparison(valueKind, comparisonKind))
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} cannot use {comparisonKind} with {valueKind}.");
        }

        if (comparisonKind is RecorderComparisonKind.HasValue or RecorderComparisonKind.IsEmpty)
        {
            return step.ExpectedCheckpointId.HasValue
                || step.ExpectedGeneratedValueId.HasValue
                || step.HasExpectedLiteral
                || step.NumericExpectedExpression is not null
                ? RecorderGraphStepValidationResult.Invalid(
                    $"Assertion step {index + 1} cannot define an expected value for {comparisonKind}.")
                : RecorderGraphStepValidationResult.Valid;
        }

        var expectationSourceCount = (step.ExpectedCheckpointId.HasValue ? 1 : 0)
            + (step.ExpectedGeneratedValueId.HasValue ? 1 : 0)
            + (step.HasExpectedLiteral ? 1 : 0)
            + (step.NumericExpectedExpression is not null ? 1 : 0);
        if (expectationSourceCount != 1)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} must define exactly one expected value source: checkpoint, generated value, literal or calculated expression.");
        }

        if (step.NumericExpectedExpression is { } numericExpression)
        {
            return ValidateNumericExpression(
                step,
                index,
                valueKind,
                comparisonKind,
                numericExpression,
                checkpointValueKinds);
        }

        if (step.ExpectedCheckpointId is { } expectedCheckpointId)
        {
            return ValidateCheckpointExpectation(
                index,
                valueKind,
                comparisonKind,
                expectedCheckpointId,
                checkpointValueKinds);
        }

        return step.ExpectedGeneratedValueId is { } expectedGeneratedValueId
            ? ValidateGeneratedValueExpectation(
                index,
                valueKind,
                comparisonKind,
                expectedGeneratedValueId,
                generatedValueVariables)
            : RecorderGraphStepValidationResult.Valid;
    }

    private static RecorderGraphStepValidationResult ValidateNumericExpression(
        RecordedStep step,
        int index,
        RecorderValueKind actualKind,
        RecorderComparisonKind comparisonKind,
        RecorderNumericExpectedExpression expression,
        IReadOnlyDictionary<Guid, RecorderValueKind> checkpointValueKinds)
    {
        if (!Enum.IsDefined(expression.Operation))
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} has an unsupported arithmetic operation '{expression.Operation}'.");
        }

        if (comparisonKind != RecorderComparisonKind.Equal
            || !RecorderNumericValueContract.TryCreateGridReference(
                step.Control,
                actualKind,
                step.ValueAccessorKind!.Value,
                step.GridRowConditions,
                step.GridTargetColumnName,
                out _,
                out _))
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} can use a calculated expected value only with an equal numeric assertion.");
        }

        var leftValidation = ValidateNumericOperand(
            expression.Left,
            "left",
            index,
            checkpointValueKinds);
        if (!leftValidation.IsValid)
        {
            return leftValidation;
        }

        var rightValidation = ValidateNumericOperand(
            expression.Right,
            "right",
            index,
            checkpointValueKinds);
        if (!rightValidation.IsValid)
        {
            return rightValidation;
        }

        return expression.Operation == RecorderArithmeticOperation.Divide
               && expression.Right.Kind == RecorderNumericOperandKind.Literal
               && expression.Right.LiteralValue == 0
            ? RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} cannot divide by a literal zero.")
            : RecorderGraphStepValidationResult.Valid;
    }

    private static RecorderGraphStepValidationResult ValidateNumericOperand(
        RecorderNumericOperand? operand,
        string operandName,
        int index,
        IReadOnlyDictionary<Guid, RecorderValueKind> checkpointValueKinds)
    {
        if (operand is null)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} does not define its {operandName} numeric operand.");
        }

        return operand.Kind switch
        {
            RecorderNumericOperandKind.Literal => ValidateNumericLiteralOperand(operand, operandName, index),
            RecorderNumericOperandKind.Checkpoint => ValidateNumericCheckpointOperand(
                operand,
                operandName,
                index,
                checkpointValueKinds),
            RecorderNumericOperandKind.Control => ValidateNumericControlOperand(operand, operandName, index),
            _ => RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} has an unsupported {operandName} numeric operand kind '{operand.Kind}'.")
        };
    }

    private static RecorderGraphStepValidationResult ValidateNumericLiteralOperand(
        RecorderNumericOperand operand,
        string operandName,
        int index)
    {
        var sourceCount = (operand.LiteralValue.HasValue ? 1 : 0)
            + (operand.CheckpointId.HasValue ? 1 : 0)
            + (operand.Control is not null ? 1 : 0)
            + (operand.ValueAccessorKind.HasValue ? 1 : 0)
            + (operand.GridValueReference is not null ? 1 : 0);
        return sourceCount == 1
               && operand.LiteralValue is { } value
               && double.IsFinite(value)
            ? RecorderGraphStepValidationResult.Valid
            : RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} has an invalid {operandName} numeric literal.");
    }

    private static RecorderGraphStepValidationResult ValidateNumericCheckpointOperand(
        RecorderNumericOperand operand,
        string operandName,
        int index,
        IReadOnlyDictionary<Guid, RecorderValueKind> checkpointValueKinds)
    {
        var sourceCount = (operand.LiteralValue.HasValue ? 1 : 0)
            + (operand.CheckpointId.HasValue ? 1 : 0)
            + (operand.Control is not null ? 1 : 0)
            + (operand.ValueAccessorKind.HasValue ? 1 : 0)
            + (operand.GridValueReference is not null ? 1 : 0);
        if (sourceCount != 1 || operand.CheckpointId is not { } checkpointId || checkpointId == Guid.Empty)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} has an invalid {operandName} numeric checkpoint operand.");
        }

        if (!checkpointValueKinds.TryGetValue(checkpointId, out var checkpointKind))
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} references a missing or later checkpoint '{checkpointId}' in its {operandName} numeric operand.");
        }

        return checkpointKind == RecorderValueKind.Number
            ? RecorderGraphStepValidationResult.Valid
            : RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} references non-numeric checkpoint kind {checkpointKind} in its {operandName} operand.");
    }

    private static RecorderGraphStepValidationResult ValidateNumericControlOperand(
        RecorderNumericOperand operand,
        string operandName,
        int index)
    {
        var sourceCount = (operand.LiteralValue.HasValue ? 1 : 0)
            + (operand.CheckpointId.HasValue ? 1 : 0)
            + (operand.Control is not null ? 1 : 0);
        if (sourceCount != 1
            || operand.Control is not { } control
            || operand.ValueAccessorKind is not { } accessorKind
            || !RecorderNumericValueContract.TryCreateGridReference(
                control,
                RecorderValueKind.Number,
                accessorKind,
                operand.GridValueReference?.RowConditions,
                operand.GridValueReference?.TargetColumnName,
                out _,
                out _))
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} {operandName} control does not expose a numeric value.");
        }

        return RecorderGraphStepValidationResult.Valid;
    }

    private static RecorderGraphStepValidationResult ValidateGeneratedValueExpectation(
        int index,
        RecorderValueKind actualKind,
        RecorderComparisonKind comparisonKind,
        Guid generatedValueId,
        IReadOnlyDictionary<Guid, string> generatedValueVariables)
    {
        if (!generatedValueVariables.ContainsKey(generatedValueId))
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} references a missing or later generated value '{generatedValueId}'.");
        }

        if (actualKind is not RecorderValueKind.Text and not RecorderValueKind.GridCellText)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} compares {actualKind} with an incompatible generated text value.");
        }

        return comparisonKind is RecorderComparisonKind.Equal or RecorderComparisonKind.NotEqual
            ? RecorderGraphStepValidationResult.Valid
            : RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} cannot use {comparisonKind} with a generated text value.");
    }

    private static RecorderGraphStepValidationResult ValidateCheckpointExpectation(
        int index,
        RecorderValueKind actualKind,
        RecorderComparisonKind comparisonKind,
        Guid checkpointId,
        IReadOnlyDictionary<Guid, RecorderValueKind> valueKinds)
    {
        if (!valueKinds.TryGetValue(checkpointId, out var expectedKind))
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} references a missing or later checkpoint '{checkpointId}'.");
        }

        if (expectedKind != actualKind)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} compares {actualKind} with incompatible checkpoint kind {expectedKind}.");
        }

        return comparisonKind == RecorderComparisonKind.Contains
            ? RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} cannot use Contains with a checkpoint expectation.")
            : RecorderGraphStepValidationResult.Valid;
    }

    internal static bool SupportsComparison(RecorderValueKind valueKind, RecorderComparisonKind comparisonKind)
    {
        return comparisonKind switch
        {
            RecorderComparisonKind.Equal => valueKind != RecorderValueKind.StringSet,
            RecorderComparisonKind.NotEqual => valueKind != RecorderValueKind.StringSet,
            RecorderComparisonKind.Contains => valueKind is RecorderValueKind.Text or RecorderValueKind.GridCellText,
            RecorderComparisonKind.Equivalent => valueKind == RecorderValueKind.StringSet,
            RecorderComparisonKind.HasValue =>
                RecorderValueAssertions.TryGetHasValueAssertionKind(valueKind, out _),
            RecorderComparisonKind.IsEmpty =>
                RecorderValueAssertions.TryGetPresenceAssertionKind(valueKind, expectEmpty: true, out _),
            _ => false
        };
    }

    private readonly record struct RecorderGraphStepValidationResult(bool IsValid, string Error)
    {
        public static RecorderGraphStepValidationResult Valid { get; } = new(true, string.Empty);

        public static RecorderGraphStepValidationResult Invalid(string error) => new(false, error);
    }
}
