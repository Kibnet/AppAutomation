using System.Collections.ObjectModel;

namespace AppAutomation.Recorder.Avalonia;

internal sealed record RecorderPersistenceSnapshot(long ContentRevision, IReadOnlyList<RecordedStep> Steps)
{
    internal static IReadOnlyList<RecordedStep> FreezeSteps(IEnumerable<RecordedStep> steps) =>
        steps is FrozenSteps frozen ? frozen : new FrozenSteps(steps.Select(FreezeStep).ToArray());

    private static RecordedStep FreezeStep(RecordedStep step)
    {
        var findings = Freeze(step.RuntimeValidationFindings);
        var expression = FreezeExpression(step.NumericExpectedExpression);
        if (step is { StringValues: null, GridRowConditions: null, GridSelectedRows: null }
            && ReferenceEquals(findings, step.RuntimeValidationFindings)
            && ReferenceEquals(expression, step.NumericExpectedExpression))
        {
            return step;
        }

        return step with
        {
            StringValues = Freeze(step.StringValues),
            RuntimeValidationFindings = findings,
            GridRowConditions = Freeze(step.GridRowConditions),
            GridSelectedRows = Freeze(step.GridSelectedRows, static row => row is null ? null!
                : row with { Conditions = Freeze(row.Conditions)! }),
            NumericExpectedExpression = expression
        };
    }

    private static RecorderNumericExpectedExpression? FreezeExpression(RecorderNumericExpectedExpression? expression)
    {
        if (expression is null) return null;

        var left = FreezeOperand(expression.Left);
        var right = FreezeOperand(expression.Right);
        var operations = Freeze(expression.AdditionalOperations, static operation => operation is null ? null!
            : operation with { Operand = FreezeOperand(operation.Operand) });
        if (ReferenceEquals(operations, expression.AdditionalOperations)
            && ReferenceEquals(left, expression.Left) && ReferenceEquals(right, expression.Right))
        {
            return expression;
        }

        return expression with
        {
            Left = left,
            Right = right,
            AdditionalOperations = operations
        };
    }

    private static RecorderNumericOperand FreezeOperand(RecorderNumericOperand operand) =>
        operand is { GridValueReference: { } grid }
            ? operand with { GridValueReference = grid with { RowConditions = Freeze(grid.RowConditions)! } }
            : operand;

    private static IReadOnlyList<T>? Freeze<T>(IReadOnlyList<T>? values, Func<T, T>? freezeItem = null) =>
        values is null ? null
        : values.Count == 0 ? Array.Empty<T>()
        : Array.AsReadOnly(freezeItem is null ? values.ToArray() : values.Select(freezeItem).ToArray());

    // The generator also accepts raw step lists; only this wrapper guarantees a frozen graph.
    private sealed class FrozenSteps(RecordedStep[] steps) : ReadOnlyCollection<RecordedStep>(steps);
}
