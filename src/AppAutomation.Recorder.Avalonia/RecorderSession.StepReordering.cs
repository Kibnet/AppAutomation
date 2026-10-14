namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderSession
{
    private const int StepMoveHistoryLimit = 100;
    private readonly List<Guid[]> _stepMoveHistory = new();

    public bool CanUndoStepMove => !_isDisposed && !IsBusy && _stepMoveHistory.Count > 0;

    public bool MoveStepToIndex(Guid stepId, int targetIndex) =>
        MoveStepToIndexCore(stepId, targetIndex, rememberMove: true, "Recorded step moved.");

    private bool MoveStepToIndexCore(Guid stepId, int targetIndex, bool rememberMove, string message)
    {
        var sourceIndex = _steps.FindIndex(step => step.StepId == stepId);
        if (_isDisposed || IsBusy || sourceIndex < 0 || targetIndex < 0 || targetIndex >= _steps.Count
            || sourceIndex == targetIndex)
        {
            return false;
        }

        if (rememberMove)
        {
            if (_stepMoveHistory.Count == StepMoveHistoryLimit)
            {
                _stepMoveHistory.RemoveAt(0);
            }

            _stepMoveHistory.Add(_steps.Select(static step => step.StepId).ToArray());
        }
        else
        {
            _stepMoveHistory.Clear();
        }

        var step = _steps[sourceIndex];
        _steps.RemoveAt(sourceIndex);
        _steps.Insert(targetIndex, step);
        CompleteStepReorder(message, step.ValidationStatus);
        return true;
    }

    public bool UndoStepMove()
    {
        if (!CanUndoStepMove)
        {
            return false;
        }

        var previousOrder = _stepMoveHistory[_stepMoveHistory.Count - 1];
        var currentSteps = _steps.ToDictionary(static step => step.StepId);
        if (previousOrder.Any(id => !currentSteps.ContainsKey(id)))
        {
            _stepMoveHistory.Clear();
            return false;
        }

        _stepMoveHistory.RemoveAt(_stepMoveHistory.Count - 1);
        var restoredSteps = new List<RecordedStep>(_steps.Count);
        foreach (var id in previousOrder)
        {
            restoredSteps.Add(currentSteps[id]);
            currentSteps.Remove(id);
        }

        // Recording can append steps after a move; undo changes only the saved order.
        restoredSteps.AddRange(_steps.Where(step => currentSteps.ContainsKey(step.StepId)));
        _steps.Clear();
        _steps.AddRange(restoredSteps);
        CompleteStepReorder("Recorded step move undone.", RecorderValidationStatus.Valid);
        return true;
    }

    private void CompleteStepReorder(string message, RecorderValidationStatus validationStatus)
    {
        var graphValidation = ApplyScenarioGraphValidation();
        UpdateLatestPreviewFromSteps(notify: false);
        SetStatusAfterGraphValidation(graphValidation, message, validationStatus);
        RequestAutosaveIfRecording();
    }
}
