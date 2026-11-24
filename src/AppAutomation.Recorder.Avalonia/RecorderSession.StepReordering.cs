namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderSession
{
    public bool CanUndoStepMove => !_isDisposed && !IsBusy && _journal.CanUndoMove;

    public bool CanMoveStep(Guid stepId, RecorderStepMoveDirection direction)
    {
        if (_isDisposed || IsBusy) return false;
        var index = _journal.IndexOf(stepId);
        return _journal.CanMove(index, GetMoveTarget(index, direction));
    }

    public bool MoveStep(Guid stepId, RecorderStepMoveDirection direction)
    {
        var index = _journal.IndexOf(stepId);
        return MoveStepToIndexCore(index, GetMoveTarget(index, direction), rememberMove: false,
            direction == RecorderStepMoveDirection.Earlier ? "Recorded step moved earlier." : "Recorded step moved later.");
    }

    public bool MoveStepToIndex(Guid stepId, int targetIndex) =>
        MoveStepToIndexCore(_journal.IndexOf(stepId), targetIndex, rememberMove: true, "Recorded step moved.");

    private static int GetMoveTarget(int sourceIndex, RecorderStepMoveDirection direction) => direction switch
    {
        RecorderStepMoveDirection.Earlier => sourceIndex - 1,
        RecorderStepMoveDirection.Later => sourceIndex + 1,
        _ => -1
    };

    private bool MoveStepToIndexCore(int sourceIndex, int targetIndex, bool rememberMove, string message)
    {
        if (_isDisposed || IsBusy || _journal.Move(sourceIndex, targetIndex, rememberMove) is not { } step)
        {
            return false;
        }

        CompleteStepReorder(message, step.ValidationStatus);
        return true;
    }

    public bool UndoStepMove()
    {
        if (!CanUndoStepMove)
        {
            return false;
        }

        if (!_journal.UndoMove()) return false;
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
