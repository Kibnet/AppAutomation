using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AppAutomation.Recorder.Avalonia;

/// <summary>
/// Owns the lifetime of subscriptions to controls and their associated flyouts.
/// Session-specific event handling stays with the session; this component ensures
/// each observable source is subscribed once and detached when it disappears.
/// </summary>
internal sealed class RecorderObservationSubscriptions : IDisposable
{
    private readonly Func<Control, Action> _subscribeControl;
    private readonly EventHandler _flyoutOpened;
    private readonly EventHandler _flyoutClosed;
    private readonly Action<Control> _attachInputRoot;
    private readonly Action<Control> _detachInputRoot;
    private readonly Action _onInputRootReset;
    private readonly Dictionary<Control, Action> _controls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FlyoutBase, Action> _flyouts = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Control> _additionalInputRoots = new(ReferenceEqualityComparer.Instance);
    private Control? _inputRoot;

    public RecorderObservationSubscriptions(
        Func<Control, Action> subscribeControl,
        EventHandler flyoutOpened,
        EventHandler flyoutClosed,
        Action<Control> attachInputRoot,
        Action<Control> detachInputRoot,
        Action onInputRootReset)
    {
        _subscribeControl = subscribeControl ?? throw new ArgumentNullException(nameof(subscribeControl));
        _flyoutOpened = flyoutOpened ?? throw new ArgumentNullException(nameof(flyoutOpened));
        _flyoutClosed = flyoutClosed ?? throw new ArgumentNullException(nameof(flyoutClosed));
        _attachInputRoot = attachInputRoot ?? throw new ArgumentNullException(nameof(attachInputRoot));
        _detachInputRoot = detachInputRoot ?? throw new ArgumentNullException(nameof(detachInputRoot));
        _onInputRootReset = onInputRootReset ?? throw new ArgumentNullException(nameof(onInputRootReset));
    }

    public IEnumerable<Control> ObservedControls => _controls.Keys;
    public Control? InputRoot => _inputRoot;

    public void RebindInputRoot(Control root)
    {
        if (ReferenceEquals(root, _inputRoot))
        {
            return;
        }

        DetachInputRoots();
        _inputRoot = root;
        _attachInputRoot(root);
    }

    public void ReconcileAdditionalInputRoots(IReadOnlyCollection<Control> controls)
    {
        var currentRoots = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        foreach (var control in controls)
        {
            if (TopLevel.GetTopLevel(control) is { } root
                && !ReferenceEquals(root, _inputRoot))
            {
                currentRoots.Add(root);
            }
        }

        foreach (var root in _additionalInputRoots.Except(currentRoots).ToArray())
        {
            _detachInputRoot(root);
            _additionalInputRoots.Remove(root);
        }

        foreach (var root in currentRoots.Except(_additionalInputRoots))
        {
            _attachInputRoot(root);
            _additionalInputRoots.Add(root);
        }
    }

    public bool IsObserved(Control control) => _controls.ContainsKey(control);

    public void EnsureObserved(Control control)
    {
        if (!_controls.ContainsKey(control))
        {
            _controls.Add(control, _subscribeControl(control));
        }
    }

    public void Reconcile(IReadOnlyCollection<Control> currentControls)
    {
        ReconcileFlyouts(currentControls);

        foreach (var control in _controls.Keys.ToArray())
        {
            if (currentControls.Contains(control))
            {
                continue;
            }

            _controls[control]();
            _controls.Remove(control);
        }

        foreach (var control in currentControls)
        {
            EnsureObserved(control);
        }
    }

    private void ReconcileFlyouts(IReadOnlyCollection<Control> controls)
    {
        var currentFlyouts = new HashSet<FlyoutBase>(ReferenceEqualityComparer.Instance);
        foreach (var control in controls)
        {
            foreach (var flyout in RecorderControlTree.EnumerateAssociatedFlyouts(control))
            {
                currentFlyouts.Add(flyout);
            }
        }

        foreach (var flyout in _flyouts.Keys.Except(currentFlyouts).ToArray())
        {
            _flyouts[flyout]();
            _flyouts.Remove(flyout);
        }

        foreach (var flyout in currentFlyouts.Except(_flyouts.Keys))
        {
            flyout.Opened += _flyoutOpened;
            flyout.Closed += _flyoutClosed;
            _flyouts.Add(flyout, () =>
            {
                flyout.Opened -= _flyoutOpened;
                flyout.Closed -= _flyoutClosed;
            });
        }
    }

    public void Dispose()
    {
        foreach (var detach in _controls.Values)
        {
            detach();
        }

        _controls.Clear();

        foreach (var detach in _flyouts.Values)
        {
            detach();
        }

        _flyouts.Clear();
        DetachInputRoots();
    }

    private void DetachInputRoots()
    {
        foreach (var root in _additionalInputRoots)
        {
            _detachInputRoot(root);
        }

        _additionalInputRoots.Clear();
        if (_inputRoot is not null)
        {
            _detachInputRoot(_inputRoot);
        }

        _inputRoot = null;
        _onInputRootReset();
    }
}
