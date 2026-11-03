using AppAutomation.Abstractions;

namespace AppAutomation.Recorder.Avalonia.CodeGeneration;

/// <summary>One scenario-local bootstrap per independent target; no captured business keys in emitted C#.</summary>
internal sealed class RecorderGridTargetVariables
{
    private readonly Dictionary<RecordedStep, Target> _targets = new(ReferenceEqualityComparer.Instance);
    internal sealed record Target(string Name, RecordedStep Definition, bool IsHeader, IReadOnlyList<string>? ExcludedRows = null);
    public IEnumerable<string> Names => _targets.Values.Select(static target => target.Name).Distinct(StringComparer.Ordinal);
    public Target? Find(RecordedStep step) => _targets.GetValueOrDefault(step);

    public static RecorderGridTargetVariables Create(IReadOnlyList<RecordedStep> steps,
        RecorderScenarioGraphValidationResult graph, IReadOnlyDictionary<Guid, string> rowAnchorNames)
    {
        var result = new RecorderGridTargetVariables();
        var names = graph.CheckpointVariables.Values.Concat(graph.GeneratedValueVariables.Values)
            .Concat(graph.CopiedValueVariables.Values).Concat(rowAnchorNames.Values).ToHashSet(StringComparer.Ordinal);
        if (graph.GeneratedValueSeriesVariable is { } series) names.Add(series);
        var headers = new List<Target>();
        var rows = new List<Target>();
        foreach (var step in steps.Where(static step => !step.IsIgnored))
        {
            if (step.GridRowSourceMode == RecorderGridRowSourceMode.CurrentTableRow
                && step.CheckpointId is { } checkpointId && rowAnchorNames.TryGetValue(checkpointId, out var anchorName))
                rows.Add(new(anchorName, step, IsHeader: false));
            var readsValue = step.ActionKind is (RecordedActionKind.AssertValue
                or RecordedActionKind.CaptureCheckpoint or RecordedActionKind.CaptureCopiedValue);
            if (step.GridHeader is { Kind: GridHeaderKind.Group } header)
            {
                var target = headers.LastOrDefault(target => SameGrid(step, target.Definition)
                    && header.Equals(target.Definition.GridHeader));
                if (target is null && readsValue)
                {
                    target = new(RecorderNaming.EnsureUniqueName("gridHeader", names), step, IsHeader: true);
                    headers.Add(target);
                }
                if (target is not null) result._targets.Add(step, target);
            }
            else if (step.GridRowConditions is { Count: > 0 } conditions
                && step.NumericExpectedExpression is null
                && step.GridRowAnchorCheckpointId is null
                && step.GridRowSourceMode != RecorderGridRowSourceMode.CurrentTableRow
                && conditions.All(static condition => condition.ValueReference is null))
            {
                var target = rows.LastOrDefault(target => SameGrid(step, target.Definition)
                    && SameRow(conditions, target.Definition.GridRowConditions!));
                // All registered row-addressed commands share the same plan, including later row selections.
                // Distinct recorded rows must not collapse to the same runtime row.
                var needsRuntimeRow = step.CanCaptureGridRowKey;
                var isFirstGroupedRowAction = step.ActionKind is (RecordedActionKind.SelectGridRow or RecordedActionKind.OpenGridRow)
                    && headers.Any(target => SameGrid(step, target.Definition))
                    && !rows.Any(target => SameGrid(step, target.Definition));
                if (target is null && (needsRuntimeRow || isFirstGroupedRowAction))
                {
                    var excludedRows = rows.Where(target => SameGrid(step, target.Definition)).Select(static target => target.Name).ToArray();
                    target = new(RecorderNaming.EnsureUniqueName("gridRow", names), step, IsHeader: false, excludedRows);
                    rows.Add(target);
                }
                if (target is not null) result._targets.Add(step, target);
            }
        }
        return result;
    }

    private static bool SameGrid(RecordedStep left, RecordedStep right) =>
        left.Control.LocatorKind == right.Control.LocatorKind
        && string.Equals(left.Control.LocatorValue, right.Control.LocatorValue, StringComparison.Ordinal);

    private static bool SameRow(IReadOnlyList<RecordedGridRowCondition> left, IReadOnlyList<RecordedGridRowCondition> right) =>
        left.Count == right.Count && left.All(condition => right.Contains(condition));
}
