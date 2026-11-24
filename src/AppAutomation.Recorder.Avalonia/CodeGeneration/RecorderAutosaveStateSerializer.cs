using System.Text;
using System.Text.Json;

namespace AppAutomation.Recorder.Avalonia.CodeGeneration;

internal static class RecorderAutosaveStateSerializer
{
    internal const string MarkerPrefix = "// AppAutomation recorder autosave state: ";
    private const int HeaderLineLimit = 8;
    private const int MaximumPayloadLength = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public static string CreateMarker(RecorderAutosaveState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        return MarkerPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static bool TryRead(
        string filePath,
        out RecorderAutosaveState? state,
        out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        state = null;
        error = null;
        string? marker;
        try
        {
            marker = File.ReadLines(filePath)
                .Take(HeaderLineLimit)
                .FirstOrDefault(static line => line.StartsWith(MarkerPrefix, StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"Recorder autosave recovery file '{filePath}' could not be read: {exception.Message}";
            return false;
        }

        if (marker is null)
        {
            return false;
        }

        var payload = marker[MarkerPrefix.Length..];
        if (payload.Length == 0 || payload.Length > MaximumPayloadLength)
        {
            error = $"Recorder autosave recovery file '{filePath}' contains an invalid recovery payload.";
            return false;
        }

        try
        {
            state = JsonSerializer.Deserialize<RecorderAutosaveState>(
                Convert.FromBase64String(payload),
                JsonOptions);
        }
        catch (Exception exception) when (exception is FormatException or JsonException or NotSupportedException or ArgumentException)
        {
            error = $"Recorder autosave recovery file '{filePath}' contains an invalid recovery payload: {exception.Message}";
            return false;
        }

        if (state is null || state.Steps is not { Count: > 0 })
        {
            state = null;
            error = $"Recorder autosave recovery file '{filePath}' contains no recorded steps.";
            return false;
        }

        if (FindInvalidStructure(state) is { } invalidMember)
        {
            state = null;
            error = $"Recorder autosave recovery file '{filePath}' contains an invalid recovery payload: {invalidMember} is missing or null.";
            return false;
        }

        return true;
    }

    private static string? FindInvalidStructure(RecorderAutosaveState state)
    {
        if (state.ScenarioName is null) return nameof(state.ScenarioName);
        if (state.DraftIdentity is null) return nameof(state.DraftIdentity);
        if (state.MethodName is null) return nameof(state.MethodName);

        for (var index = 0; index < state.Steps.Count; index++)
        {
            var path = $"Steps[{index}]";
            var step = state.Steps[index];
            if (step is null) return path;
            var invalid = FindInvalidControl(step.Control, path + ".Control")
                ?? FindInvalidRowConditions(step.GridRowConditions, path + ".GridRowConditions");
            if (invalid is not null) return invalid;

            if (step.StringValues is { } values)
                for (var item = 0; item < values.Count; item++)
                    if (values[item] is null) return $"{path}.StringValues[{item}]";

            if (step.GridSelectedRows is { } rows)
            {
                for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var rowPath = $"{path}.GridSelectedRows[{rowIndex}]";
                    var row = rows[rowIndex];
                    if (row is null) return rowPath;
                    if (row.Conditions is null) return rowPath + ".Conditions";
                    if (FindInvalidRowConditions(row.Conditions, rowPath + ".Conditions") is { } rowError)
                        return rowError;
                }
            }

            if (step.NumericExpectedExpression is { } expression)
            {
                var expressionPath = path + ".NumericExpectedExpression";
                invalid = FindInvalidOperand(expression.Left, expressionPath + ".Left")
                    ?? FindInvalidOperand(expression.Right, expressionPath + ".Right");
                if (invalid is not null) return invalid;
                if (expression.AdditionalOperations is { } operations)
                {
                    for (var operationIndex = 0; operationIndex < operations.Count; operationIndex++)
                    {
                        var operationPath = $"{expressionPath}.AdditionalOperations[{operationIndex}]";
                        var operation = operations[operationIndex];
                        if (operation is null) return operationPath;
                        if (FindInvalidOperand(operation.Operand, operationPath + ".Operand") is { } operandError)
                            return operandError;
                    }
                }
            }

            if (step.RuntimeValidationFindings is { } findings)
            {
                for (var findingIndex = 0; findingIndex < findings.Count; findingIndex++)
                {
                    var findingPath = $"{path}.RuntimeValidationFindings[{findingIndex}]";
                    var finding = findings[findingIndex];
                    if (finding is null) return findingPath;
                    if (finding.Code is null) return findingPath + ".Code";
                    if (finding.Message is null) return findingPath + ".Message";
                }
            }

            if (step.TextTransform is { Separator: null }) return path + ".TextTransform.Separator";
        }

        return null;
    }

    private static string? FindInvalidControl(RecordedControlDescriptor? control, string path)
    {
        if (control is null) return path;
        if (control.ProposedPropertyName is null) return path + ".ProposedPropertyName";
        if (control.LocatorValue is null) return path + ".LocatorValue";
        return control.AvaloniaTypeName is null ? path + ".AvaloniaTypeName" : null;
    }

    private static string? FindInvalidRowConditions(IReadOnlyList<RecordedGridRowCondition>? conditions, string path)
    {
        if (conditions is null) return null;
        for (var index = 0; index < conditions.Count; index++)
        {
            var conditionPath = $"{path}[{index}]";
            var condition = conditions[index];
            if (condition is null) return conditionPath;
            if (condition.ColumnName is null) return conditionPath + ".ColumnName";
            if (condition.Value is null) return conditionPath + ".Value";
        }
        return null;
    }

    private static string? FindInvalidOperand(RecorderNumericOperand? operand, string path)
    {
        if (operand is null) return path;
        if (operand.Control is not null || operand.Kind == RecorderNumericOperandKind.Control)
            if (FindInvalidControl(operand.Control, path + ".Control") is { } controlError) return controlError;
        if (operand.GridValueReference is not { } reference) return null;
        if (reference.RowConditions is null) return path + ".GridValueReference.RowConditions";
        if (reference.TargetColumnName is null) return path + ".GridValueReference.TargetColumnName";
        return FindInvalidRowConditions(reference.RowConditions, path + ".GridValueReference.RowConditions");
    }
}

internal sealed record RecorderAutosaveState(
    string ScenarioName,
    string DraftIdentity,
    string MethodName,
    DateTimeOffset SavedAtUtc,
    IReadOnlyList<RecordedStep> Steps);

internal sealed record RecorderAutosaveRestoreResult(
    bool Found,
    bool Success,
    string Message,
    string? DraftIdentity,
    IReadOnlyList<RecordedStep> Steps)
{
    public static RecorderAutosaveRestoreResult NotFound(string message) =>
        new(false, false, message, null, Array.Empty<RecordedStep>());

    public static RecorderAutosaveRestoreResult Failed(string message) =>
        new(true, false, message, null, Array.Empty<RecordedStep>());

    public static RecorderAutosaveRestoreResult Restored(
        string message,
        string draftIdentity,
        IReadOnlyList<RecordedStep> steps) =>
        new(true, true, message, draftIdentity, steps);
}
