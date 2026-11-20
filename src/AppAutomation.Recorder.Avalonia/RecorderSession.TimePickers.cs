using AppAutomation.Abstractions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderSession
{
    private PresenterTimeTransaction? _presenterTimeTransaction;
    private long _presenterTimeGeneration;
    private bool _presenterTimeReconcileQueued;
    private WeakReference<Control>? _discardedPresenterTimeOwner;
    private long _discardedPresenterTimeEpoch;
    private PresenterTimeReopening? _presenterTimeReopening;

    private sealed record PresenterTimeReopening(WeakReference<Control> Owner, WeakReference<Popup> Popup);
    private sealed record PresenterTimeCommit(TimeSpan? Value);

    private sealed class PresenterTimeTransaction(RecorderTimePickerBinding binding, TimeSpan? initialValue, long generation)
    {
        internal RecorderTimePickerBinding Binding { get; } = binding;
        internal TimeSpan? InitialValue { get; } = initialValue;
        internal long Generation { get; } = generation;
        internal bool ConfirmRequested { get; set; }
        internal bool HasUserInput { get; set; }
        internal bool FinalizationQueued { get; set; }
        internal bool CommitSnapshotQueued { get; set; }
        internal bool EditabilityCheckQueued { get; set; }
        internal PresenterTimeCommit? Commit { get; set; }
    }

    private bool IsPresenterTimeObservation(Control control) => _options.TimePickerHints.Any(hint =>
        RecorderTimePickerBinding.IsRoot(control, hint)
        || control is TimePickerPresenter && RecorderTimePickerBinding.IsCandidate(control, hint));

    private Action? AttachPresenterTimeObservation(Control control)
    {
        if (!IsPresenterTimeObservation(control)) return null;
        control.PropertyChanged += OnPresenterTimePropertyChanged;
        if (control is TimePickerPresenter presenter)
        {
            presenter.Confirmed += OnPresenterTimeConfirmed;
            presenter.Dismissed += OnPresenterTimeDismissed;
        }
        return () =>
        {
            control.PropertyChanged -= OnPresenterTimePropertyChanged;
            if (control is TimePickerPresenter presenter)
            {
                presenter.Confirmed -= OnPresenterTimeConfirmed;
                presenter.Dismissed -= OnPresenterTimeDismissed;
            }
        };
    }

    private void OnPresenterTimePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (_state != RecorderSessionState.Recording || sender is not Control source) return;
        if (_presenterTimeTransaction is { } ownerTransaction && ReferenceEquals(ownerTransaction.Binding.Root, source))
        {
            if (args.Property.Name is "Template" or "Content" or "DataContext")
                InvalidatePresenterTimeTransaction();
            else if (args.Property.Name is "IsReadOnly" or "IsEnabled")
                QueuePresenterTimeEditabilityCheck(ownerTransaction);
        }
        if (args.Property.Name is "PopupContent" or "IsPopupOpen")
        {
            Dispatcher.UIThread.Post(RefreshObservedControlsAfterPopupLayout, DispatcherPriority.Loaded);
        }
        if (source is TimePickerPresenter && args.Property != TimePickerPresenter.TimeProperty)
        {
            if (_presenterTimeTransaction is { } transaction && ReferenceEquals(transaction.Binding.Presenter, source)
                && args.Property.Name is nameof(TimePickerPresenter.ClockIdentifier) or nameof(TimePickerPresenter.UseSeconds)
                    or nameof(TimePickerPresenter.MinuteIncrement) or nameof(TimePickerPresenter.SecondIncrement))
                InvalidatePresenterTimeTransaction();
        }
        if (_presenterTimeTransaction is { ConfirmRequested: true } pending
            && ReferenceEquals(pending.Binding.Root, source)) QueuePresenterTimeFinalization(pending);
        QueuePresenterTimeReconcile();
    }

    private void QueuePresenterTimeEditabilityCheck(PresenterTimeTransaction transaction)
    {
        if (transaction.ConfirmRequested || transaction.EditabilityCheckQueued) return;
        transaction.EditabilityCheckQueued = true;
        // The host can lock the editor inside its Confirmed handler, before our handler runs.
        Dispatcher.UIThread.Post(() =>
        {
            transaction.EditabilityCheckQueued = false;
            if (IsCurrentPresenterTimeTransaction(transaction) && !transaction.ConfirmRequested)
                InvalidatePresenterTimeTransaction();
        }, DispatcherPriority.Send);
    }

    private void QueuePresenterTimeReconcile()
    {
        if (_presenterTimeReconcileQueued || _state != RecorderSessionState.Recording
            || !_options.TimePickerHints.Any(RecorderTimePickerBinding.IsPresenterHint)) return;
        _presenterTimeReconcileQueued = true;
        // Read a complete host handler, not its transient Value/ItemStatus publications.
        // Loaded precedes the next queued Input, so opening captures the baseline before Enter.
        Dispatcher.UIThread.Post(() =>
        {
            _presenterTimeReconcileQueued = false;
            if (!_isDisposed && _state == RecorderSessionState.Recording) ReconcilePresenterTimeTransaction();
        }, DispatcherPriority.Loaded);
    }

    private void ObservePresenterTimeOwner(Control source)
    {
        if (_state != RecorderSessionState.Recording) return;
        if (!_stepFactory.TryResolvePresenterTimeBinding(source, out var binding, out var error))
        {
            if (_presenterTimeTransaction is { } pending && pending.Binding.Contains(source))
            {
                InvalidatePresenterTimeTransaction();
                if (!string.IsNullOrEmpty(error)) LogCaptureFailure("TimePickerPresenterBinding", source, error);
            }
            return;
        }
        if (_presenterTimeTransaction is { } transaction)
        {
            if (SamePresenterTimeOpening(transaction.Binding, binding))
            {
                if (!binding.Owner.IsOpen) QueuePresenterTimeFinalization(transaction);
                return;
            }
            InvalidatePresenterTimeTransaction();
        }
        if (_presenterTimeReopening is { } reopening && reopening.Owner.TryGetTarget(out var reopeningOwner)
            && ReferenceEquals(reopeningOwner, binding.Root))
        {
            if (!reopening.Popup.TryGetTarget(out var reopenedPopup) || !ReferenceEquals(reopenedPopup, binding.Popup)
                || !binding.Owner.IsOpen) return;
            _presenterTimeReopening = null;
        }
        if (_discardedPresenterTimeOwner?.TryGetTarget(out var discardedOwner) == true
            && ReferenceEquals(discardedOwner, binding.Root))
        {
            if (!binding.HasPublishedIdentity || binding.Owner.Epoch == _discardedPresenterTimeEpoch) return;
            _discardedPresenterTimeOwner = null;
        }
        if (binding.Owner.IsOpen && binding.Root.IsEffectivelyEnabled && !binding.Owner.IsReadOnly
            && binding.Presenter is not null && binding.Popup is { IsOpen: true })
            _presenterTimeTransaction = new(binding, binding.ReadValue(), ++_presenterTimeGeneration)
            {
                HasUserInput = _pointerGestures.WasRecentlyTriggeredByUser(binding.Root, RecentInputWindow,
                    (_, input) => input is not null && binding.Contains(input))
            };
    }

    private static bool SamePresenterTimeOpening(RecorderTimePickerBinding left, RecorderTimePickerBinding right) =>
        ReferenceEquals(left.Root, right.Root)
        && (ReferenceEquals(left.Presenter, right.Presenter) || !right.Owner.IsOpen && right.Presenter is null)
        && (ReferenceEquals(left.Popup, right.Popup) || !right.Owner.IsOpen && right.Popup is null)
        && Equals(left.Hint, right.Hint)
        && left.HasPublishedIdentity == right.HasPublishedIdentity
        && left.Owner.OwnerId == right.Owner.OwnerId && left.Owner.Epoch == right.Owner.Epoch
        && (!right.HasPublishedIdentity && right.Presenter is null && !right.Owner.IsOpen
            || left.Owner.Clock == right.Owner.Clock && left.Owner.UseSeconds == right.Owner.UseSeconds
                && left.Owner.MinuteStep == right.Owner.MinuteStep && left.Owner.SecondStep == right.Owner.SecondStep);

    private void OnPresenterTimeConfirmed(object? sender, EventArgs args)
    {
        if (_state != RecorderSessionState.Recording || sender is not TimePickerPresenter presenter
            || _presenterTimeTransaction is not { } transaction
            || !ReferenceEquals(transaction.Binding.Presenter, presenter)) return;
        transaction.ConfirmRequested = true;
        DiscardPresenterTimeText(transaction.Binding);
        QueuePresenterTimeFinalization(transaction);
    }

    private void OnPresenterTimeDismissed(object? sender, EventArgs args)
    {
        if (sender is TimePickerPresenter presenter && _presenterTimeTransaction is { } transaction
            && ReferenceEquals(transaction.Binding.Presenter, presenter))
        {
            DiscardPresenterTimeText(transaction.Binding);
            InvalidatePresenterTimeTransaction();
        }
    }

    private void OnPresenterTimePopupOpened(Control? popup)
    {
        // A reopening is a new epoch even without published metadata. It invalidates queued G1 work.
        if (_presenterTimeTransaction is { } transaction && ReferenceEquals(transaction.Binding.Popup, popup))
        {
            if (!transaction.Binding.HasPublishedIdentity || transaction.ConfirmRequested)
                InvalidatePresenterTimeTransaction();
        }
        var discarded = _discardedPresenterTimeOwner?.TryGetTarget(out var owner) == true ? owner
            : _presenterTimeReopening?.Owner.TryGetTarget(out var openingOwner) == true ? openingOwner : null;
        if (discarded is not null && popup is Popup { IsOpen: true } nativePopup
            && (nativePopup.PlacementTarget is { } placement
                ? RecorderTimePickerBinding.IsWithin(placement, discarded)
                : RecorderTimePickerBinding.IsWithin(nativePopup, discarded)))
        {
            if (string.IsNullOrEmpty(AutomationProperties.GetItemStatus(discarded)))
            {
                // Opened proves a fresh native opening even before template panels are ready.
                // Loaded must still bind this exact popup, not another popup of the same owner.
                _discardedPresenterTimeOwner = null;
                _presenterTimeReopening = new(new(discarded), new(nativePopup));
                _presenterTimeGeneration++;
            }
            else if (_stepFactory.TryResolvePresenterTimeBinding(discarded, out var binding, out _)
                && ReferenceEquals(binding.Popup, nativePopup) && binding.Owner.Epoch > _discardedPresenterTimeEpoch)
                _discardedPresenterTimeOwner = null;
        }
        QueuePresenterTimeReconcile();
    }

    private void ReconcilePresenterTimeTransaction()
    {
        if (_state != RecorderSessionState.Recording
            || !_options.TimePickerHints.Any(RecorderTimePickerBinding.IsPresenterHint)) return;
        if (_presenterTimeTransaction is { } transaction)
        {
            if (!RecorderTimePickerBinding.TryResolve(_options, _validationRootProvider(), transaction.Binding.Root,
                    out var current, out _, transaction.Binding.Hint)
                || !SamePresenterTimeOpening(transaction.Binding, current))
                InvalidatePresenterTimeTransaction();
            else if (!current.Owner.IsOpen) QueuePresenterTimeFinalization(transaction);
        }
        if (_presenterTimeTransaction is not null || _validationRootProvider() is not { } root) return;
        var bindings = RecorderControlTree.EnumerateReachableControls(root)
            .Where(IsPresenterTimeObservation).Select(control =>
                _stepFactory.TryResolvePresenterTimeBinding(control, out var binding, out _) ? binding : null)
            .OfType<RecorderTimePickerBinding>().Where(binding => binding.Owner.IsOpen)
            .DistinctBy(binding => binding.Root, ReferenceEqualityComparer.Instance).Take(2).ToArray();
        // No implicit first-card choice when more than one editor is already open.
        if (bindings.Length == 1) ObservePresenterTimeOwner(bindings[0].Root);
    }

    private void QueuePresenterTimeFinalization(PresenterTimeTransaction transaction)
    {
        if (transaction.ConfirmRequested && transaction.Commit is null && !transaction.CommitSnapshotQueued)
        {
            transaction.CommitSnapshotQueued = true;
            // Freeze the completed host handler's value before later queued UI updates.
            // Journal finalization remains deferred and may still be invalidated by Stop.
            Dispatcher.UIThread.Post(() =>
            {
                transaction.CommitSnapshotQueued = false;
                TryCapturePresenterTimeCommit(transaction);
            }, DispatcherPriority.Send);
        }
        if (transaction.FinalizationQueued) return;
        transaction.FinalizationQueued = true;
        Dispatcher.UIThread.Post(() => FinalizePresenterTime(transaction), DispatcherPriority.Background);
    }

    private void FinalizePresenterTime(PresenterTimeTransaction transaction)
    {
        if (!IsCurrentPresenterTimeTransaction(transaction)) return;
        _presenterTimeTransaction = null;
        _discardedPresenterTimeOwner = new(transaction.Binding.Root);
        _discardedPresenterTimeEpoch = transaction.Binding.Owner.Epoch;
        if (!transaction.ConfirmRequested || !transaction.HasUserInput) return;
        if (!RecorderTimePickerBinding.TryResolve(_options, _validationRootProvider(), transaction.Binding.Root,
                out var current, out var error, transaction.Binding.Hint)
            || !SamePresenterTimeOpening(transaction.Binding, current)
            || current.Owner.IsOpen || current.Popup is { IsOpen: true }
            || !current.Root.IsEffectivelyVisible
            || !current.HasPublishedIdentity && transaction.Binding.Popup?.PlacementTarget is { } placement
                && !RecorderTimePickerBinding.IsWithin(placement, current.Root))
        {
            LogCaptureFailure("TimePickerPresenterCommit", transaction.Binding.Root,
                string.IsNullOrEmpty(error) ? "The confirmed time-picker owner changed, is unavailable or did not close." : error);
            return;
        }
        if (transaction.Commit is not { } commit)
        {
            LogCaptureFailure("TimePickerPresenterCommit", current.Root,
                "The confirmed time-picker opening has no completed committed-value snapshot.");
            return;
        }
        if (commit.Value == transaction.InitialValue) return;
        if (!current.HasPublishedIdentity && current.Presenter is null)
            current = current with { Owner = current.Owner with { Clock = transaction.Binding.Owner.Clock,
                UseSeconds = transaction.Binding.Owner.UseSeconds, MinuteStep = transaction.Binding.Owner.MinuteStep,
                SecondStep = transaction.Binding.Owner.SecondStep } };
        DiscardPresenterTimeText(current);
        AddStep(_stepFactory.TryCreatePresenterTimeStep(current, commit.Value), current.Root,
            "TimePickerPresenterCommit", suppressRecentDuplicate: false);
    }

    private bool IsCurrentPresenterTimeTransaction(PresenterTimeTransaction transaction) =>
        _state == RecorderSessionState.Recording && !_isDisposed
        && ReferenceEquals(_presenterTimeTransaction, transaction)
        && transaction.Generation == _presenterTimeGeneration;

    private bool TryCapturePresenterTimeCommit(PresenterTimeTransaction transaction)
    {
        if (!IsCurrentPresenterTimeTransaction(transaction) || !transaction.ConfirmRequested) return false;
        if (transaction.Commit is not null) return true;
        if (!RecorderTimePickerBinding.TryResolve(_options, _validationRootProvider(), transaction.Binding.Root,
                out var current, out _, transaction.Binding.Hint)
            || !SamePresenterTimeOpening(transaction.Binding, current)
            || current.Owner.IsOpen || current.Popup is { IsOpen: true }) return false;
        transaction.Commit = new(current.ReadValue());
        return true;
    }

    private void FlushConfirmedPresenterTime()
    {
        if (_presenterTimeTransaction is { ConfirmRequested: true } transaction
            && TryCapturePresenterTimeCommit(transaction)) FinalizePresenterTime(transaction);
    }

    private bool HandlePresenterTimeInput(Control? source, Key? key = null)
    {
        if (source is null || !_stepFactory.IsPresenterTimeSource(source)) return false;
        if (!_stepFactory.TryResolvePresenterTimeBinding(source, out var binding, out var error))
        {
            LogCaptureFailure("TimePickerPresenterBinding", source, error);
            return true;
        }
        if (!binding.Root.IsEffectivelyEnabled || binding.Owner.IsReadOnly)
        {
            if (_presenterTimeTransaction is { ConfirmRequested: false } pending && pending.Binding.Contains(source))
                InvalidatePresenterTimeTransaction();
            return true;
        }
        DiscardPresenterTimeText(binding);
        var owner = ResolveInteractionOwner(source);
        FlushPendingTextIfSwitchingTo(owner);
        FlushPendingSliderIfSwitchingTo(owner);
        FlushPendingSpinnerIfSwitchingTo(owner);
        CommitPendingCatalogGridEditIfSwitchingTo(owner);
        ObservePresenterTimeOwner(source);
        if (_presenterTimeTransaction is { } transaction) transaction.HasUserInput = true;
        if (key == Key.Escape) InvalidatePresenterTimeTransaction();
        return true;
    }

    private bool TryHandlePresenterTimeButton(Control? source)
    {
        if (source is null) return false;
        var binding = _presenterTimeTransaction?.Binding;
        if (binding is null || !binding.Contains(source))
        {
            if (!_stepFactory.IsPresenterTimeSource(source)) return false;
            if (!_stepFactory.TryResolvePresenterTimeBinding(source, out binding, out var error))
            {
                LogCaptureFailure("TimePickerPresenterBinding", source, error);
                return true;
            }
        }
        DiscardPresenterTimeText(binding);
        if (binding.IsButton(source, binding.Hint.Parts.CancelButtonLocator)) InvalidatePresenterTimeTransaction();
        // Native Confirmed, including Enter, is the commit proof. A button click alone is not.
        return true;
    }

    private void DiscardPresenterTimeText(RecorderTimePickerBinding binding)
    {
        if (_pendingTextBox is not null && binding.Contains(_pendingTextBox)) DiscardPendingText();
        if (_textEditInputTarget is not null && binding.Contains(_textEditInputTarget)) _textEditInputTarget = null;
    }

    private void InvalidatePresenterTimeTransaction()
    {
        _presenterTimeReopening = null;
        if (_presenterTimeTransaction is { } transaction)
        {
            _discardedPresenterTimeOwner = new(transaction.Binding.Root);
            _discardedPresenterTimeEpoch = transaction.Binding.Owner.Epoch;
        }
        _presenterTimeGeneration++;
        _presenterTimeTransaction = null;
    }
}
