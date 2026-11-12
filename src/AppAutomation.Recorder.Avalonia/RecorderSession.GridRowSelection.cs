using AppAutomation.Abstractions;
using Avalonia.Controls;
using Avalonia.Input;

namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderSession
{
    private PendingGridSelection? _pendingGridSelection;
    private Guid? _lastGridSelectionGestureStepId;
    private Guid? _plainPointerCollapseStepId;

    private sealed record PendingGridSelection(
        Control Grid, object?[]? Before, Guid? PreviousStep, IPointer? Pointer, Key? Key, bool IsPlainPointer);

    private bool TryBeginGridSelectionGesture(Control? source, bool modified, Guid? previousStep,
        IPointer? pointer = null, Key? key = null)
    {
        var grid = _stepFactory.FindMultipleSelectionGrid(source);
        if (grid is null) return false;

        var before = ReadGridSelection(grid);
        // An ordinary single click keeps its legacy command unless it replaces a multiple selection.
        if (!modified && before is not { Length: > 1 }) return false;
        if (pointer is not null && !RecorderStepFactory.IsMultipleSelectionRow(source!, grid)) return false;
        _pendingGridSelection = new PendingGridSelection(grid, before, previousStep, pointer, key,
            IsPlainPointer: !modified && pointer is not null);
        return true;
    }

    private void CompleteGridSelectionGesture(PendingGridSelection pending)
    {
        _pendingGridSelection = null;
        if (_state != RecorderSessionState.Recording || _targetSelectionMode != RecorderTargetSelectionMode.None) return;

        var selected = ReadGridSelection(pending.Grid);
        if (selected is null)
        {
            AddStep(StepCreationResult.Unsupported("The grid does not expose its complete selected row set."),
                pending.Grid, "GridRowSet");
            return;
        }

        if (pending.Before is { } before && before.Length == selected.Length
            && new HashSet<object?>(before, ReferenceEqualityComparer.Instance).SetEquals(selected))
        {
            _lastGridSelectionGestureStepId = pending.PreviousStep;
            return;
        }

        var result = _stepFactory.CreateGridRowSetStep(pending.Grid, selected);
        var replace = _steps.LastOrDefault() is { } last
            && last.StepId == pending.PreviousStep
            && last.ActionKind is RecordedActionKind.SelectGridRow or RecordedActionKind.SelectGridRows
            && result.Step is { } step
            && last.Control.LocatorKind == step.Control.LocatorKind
            && string.Equals(last.Control.LocatorValue, step.Control.LocatorValue, StringComparison.Ordinal)
            ? pending.PreviousStep
            : null;

        if (AddStep(result, pending.Grid, "GridRowSet", suppressRecentDuplicate: false, replaceAdjacent: replace))
        {
            _lastGridSelectionGestureStepId = _steps[^1].StepId;
            if (pending.IsPlainPointer && selected.Length == 1)
                _plainPointerCollapseStepId = _lastGridSelectionGestureStepId;
        }
    }

    private static object?[]? ReadGridSelection(Control grid) =>
        NativeGridSelectionAccess.ReadItems(grid)?.Cast<object?>().ToArray();

    private static bool IsGridSelectionKey(KeyEventArgs e) =>
        (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0 && e.Key == Key.A
        || e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && e.Key is Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Space
        || e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Space;

    private static bool IsGridSelectionModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private void OnGridSelectionKeyCompleted(object? sender, KeyEventArgs e)
    {
        if (_pendingGridSelection is { Key: { } key } pending && key == e.Key)
            CompleteGridSelectionGesture(pending);
    }

    private void ResetGridSelectionGesture()
    {
        _pendingGridSelection = null;
        _lastGridSelectionGestureStepId = null;
        _plainPointerCollapseStepId = null;
    }

    private static RecordedStep LinkGridRowAnchors(RecordedStep step, IEnumerable<RecordedStep> precedingSteps,
        Guid? invalidated = null)
    {
        if (invalidated.HasValue && step.GridRowAnchorCheckpointId == invalidated)
            step = step with { GridRowAnchorCheckpointId = null };
        step = LinkTableRowCheckpoint(step, precedingSteps);
        if (step.GridSelectedRows is not { Count: > 0 } rows) return step;
        var preceding = precedingSteps.ToArray();
        return step with
        {
            GridSelectedRows = rows.Select(row =>
            {
                if (row?.Conditions is not { Count: > 0 }
                    || row.AnchorCheckpointId is { } anchor && anchor != invalidated) return row!;
                var linked = LinkTableRowCheckpoint(step with
                {
                    GridRowConditions = row.Conditions,
                    GridRowAnchorCheckpointId = null
                }, preceding);
                return row with { AnchorCheckpointId = linked.GridRowAnchorCheckpointId };
            }).ToArray()
        };
    }
}
