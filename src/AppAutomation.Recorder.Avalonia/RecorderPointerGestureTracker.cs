using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

/// <summary>Tracks one physical pointer gesture and its immediately related recorder actions.</summary>
internal sealed class RecorderPointerGestureTracker
{
    private int _sequence;
    private int? _activeSequence;
    private int? _recordedButtonSequence;
    private Control? _recordedButtonTarget;
    private PendingGridRowSelection? _pendingGridRowSelection;
    private Control? _recentPointerControl;
    private DateTimeOffset _recentPointerAt;
    private Control? _recentKeyboardControl;
    private DateTimeOffset _recentKeyboardAt;

    public void Begin()
    {
        _sequence = _sequence == int.MaxValue ? 1 : _sequence + 1;
        _activeSequence = _sequence;
        _pendingGridRowSelection = null;
    }

    public void Reset()
    {
        _activeSequence = null;
        _pendingGridRowSelection = null;
    }

    public bool TryMarkButton(Control target)
    {
        if (_activeSequence is not { } sequence)
        {
            return true;
        }

        if (_recordedButtonSequence == sequence
            && ReferenceEquals(_recordedButtonTarget, target))
        {
            return false;
        }

        _recordedButtonSequence = sequence;
        _recordedButtonTarget = target;
        return true;
    }

    public void RememberGridRowSelection(Guid stepId)
    {
        if (_activeSequence is { } sequence)
        {
            _pendingGridRowSelection = new PendingGridRowSelection(sequence, stepId);
        }
    }

    public bool TryConsumeLinkedGridRowSelection(out Guid stepId)
    {
        stepId = default;
        if (_activeSequence is not { } sequence
            || _pendingGridRowSelection is not { } pending
            || pending.PointerGestureSequence != sequence)
        {
            return false;
        }

        _pendingGridRowSelection = null;
        stepId = pending.SelectionStepId;
        return true;
    }

    public void RememberPointer(Control? control)
    {
        _recentPointerControl = control;
        _recentPointerAt = DateTimeOffset.UtcNow;
    }

    public void RememberKeyboard(Control control)
    {
        _recentKeyboardControl = control;
        _recentKeyboardAt = DateTimeOffset.UtcNow;
    }

    public bool WasRecentlyTriggeredByUser(
        Control control,
        TimeSpan recentInputWindow,
        Func<Control, Control?, bool> areRelated)
    {
        var now = DateTimeOffset.UtcNow;
        return (now - _recentPointerAt <= recentInputWindow
                && areRelated(control, _recentPointerControl))
            || (now - _recentKeyboardAt <= recentInputWindow
                && areRelated(control, _recentKeyboardControl));
    }

    private sealed record PendingGridRowSelection(int PointerGestureSequence, Guid SelectionStepId);
}
