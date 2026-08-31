namespace AppAutomation.Recorder.Avalonia;

internal sealed record RecorderScenarioGraphValidationResult(
    bool Success,
    IReadOnlyDictionary<Guid, string> CheckpointVariables,
    IReadOnlyDictionary<Guid, string> StepErrors,
    string? Error)
{
    public static RecorderScenarioGraphValidationResult Failed(
        string error,
        IReadOnlyDictionary<Guid, string> variables,
        IReadOnlyDictionary<Guid, string> stepErrors) =>
        new(false, variables, stepErrors, error);

    public static RecorderScenarioGraphValidationResult Valid(IReadOnlyDictionary<Guid, string> variables) =>
        new(true, variables, new Dictionary<Guid, string>(), null);
}

internal static class RecorderScenarioGraphValidator
{
    public static RecorderScenarioGraphValidationResult Validate(IReadOnlyList<RecordedStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var variables = new Dictionary<Guid, string>();
        var valueKinds = new Dictionary<Guid, RecorderValueKind>();
        var reservedNames = new HashSet<string>(StringComparer.Ordinal);
        var stepErrors = new Dictionary<Guid, string>();

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            if (step.ActionKind == RecordedActionKind.CaptureCheckpoint)
            {
                var checkpointValidation = ValidateCheckpoint(step, index, variables, valueKinds, reservedNames);
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

            var assertionValidation = ValidateAssertion(step, index, valueKinds);
            if (!assertionValidation.IsValid)
            {
                stepErrors[step.StepId] = assertionValidation.Error;
            }
        }

        return stepErrors.Count == 0
            ? RecorderScenarioGraphValidationResult.Valid(variables)
            : RecorderScenarioGraphValidationResult.Failed(
                stepErrors.Values.First(),
                variables,
                stepErrors);
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
        IReadOnlyDictionary<Guid, RecorderValueKind> valueKinds)
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
            return step.ExpectedCheckpointId.HasValue || step.HasExpectedLiteral
                ? RecorderGraphStepValidationResult.Invalid(
                    $"Assertion step {index + 1} cannot define an expected value for {comparisonKind}.")
                : RecorderGraphStepValidationResult.Valid;
        }

        var expectationSourceCount = (step.ExpectedCheckpointId.HasValue ? 1 : 0)
            + (step.HasExpectedLiteral ? 1 : 0);
        if (expectationSourceCount != 1)
        {
            return RecorderGraphStepValidationResult.Invalid(
                $"Assertion step {index + 1} must define exactly one expected value source: checkpoint or literal.");
        }

        return step.ExpectedCheckpointId is { } expectedCheckpointId
            ? ValidateCheckpointExpectation(index, valueKind, comparisonKind, expectedCheckpointId, valueKinds)
            : RecorderGraphStepValidationResult.Valid;
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
