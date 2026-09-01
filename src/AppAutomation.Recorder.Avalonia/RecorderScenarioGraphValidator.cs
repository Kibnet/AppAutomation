namespace AppAutomation.Recorder.Avalonia;

internal sealed record RecorderScenarioGraphValidationResult(
    bool Success,
    IReadOnlyDictionary<Guid, string> CheckpointVariables,
    IReadOnlyDictionary<Guid, string> GeneratedValueVariables,
    string? GeneratedValueSeriesVariable,
    IReadOnlyDictionary<Guid, string> StepErrors,
    string? Error)
{
    public static RecorderScenarioGraphValidationResult Failed(
        string error,
        IReadOnlyDictionary<Guid, string> checkpointVariables,
        IReadOnlyDictionary<Guid, string> generatedValueVariables,
        string? generatedValueSeriesVariable,
        IReadOnlyDictionary<Guid, string> stepErrors) =>
        new(
            false,
            checkpointVariables,
            generatedValueVariables,
            generatedValueSeriesVariable,
            stepErrors,
            error);

    public static RecorderScenarioGraphValidationResult Valid(
        IReadOnlyDictionary<Guid, string> checkpointVariables,
        IReadOnlyDictionary<Guid, string> generatedValueVariables,
        string? generatedValueSeriesVariable) =>
        new(
            true,
            checkpointVariables,
            generatedValueVariables,
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
        var generatedValueOrdinals = new HashSet<int>();
        var reservedNames = new HashSet<string>(StringComparer.Ordinal);
        var stepErrors = new Dictionary<Guid, string>();

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
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
                generatedValueSeriesVariable)
            : RecorderScenarioGraphValidationResult.Failed(
                stepErrors.Values.First(),
                checkpointVariables,
                generatedValueVariables,
                generatedValueSeriesVariable,
                stepErrors);
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
                    reservedNames));
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
            reservedNames);
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
                ? RecorderGraphStepValidationResult.Invalid(
                    $"Assertion step {index + 1} cannot define an expected value for {comparisonKind}.")
                : RecorderGraphStepValidationResult.Valid;
        }

        var expectationSourceCount = (step.ExpectedCheckpointId.HasValue ? 1 : 0)
            + (step.ExpectedGeneratedValueId.HasValue ? 1 : 0)
            + (step.HasExpectedLiteral ? 1 : 0);
        if (expectationSourceCount != 1)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} must define exactly one expected value source: checkpoint, generated value or literal.");
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

    private static bool SupportsComparison(RecorderValueKind valueKind, RecorderComparisonKind comparisonKind)
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
