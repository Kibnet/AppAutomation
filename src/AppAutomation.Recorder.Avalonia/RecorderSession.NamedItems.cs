using AppAutomation.Abstractions;
using AppAutomation.Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderSession
{
    private PointerPressedEventArgs? _lastNamedItemPressEvent;
    private bool _lastPressWasNamed;
    private NamedItemPress? _firstNamedItemPress;
    private PendingNamedItemTap? _pendingNamedItemTap;

    private void ResetNamedItemPresses()
    {
        _lastNamedItemPressEvent = null;
        _lastPressWasNamed = false;
        _firstNamedItemPress = null;
        _pendingNamedItemTap = null;
    }

    private bool ObserveNamedItemPress(Control? source, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(_lastNamedItemPressEvent, e))
            return _lastPressWasNamed;

        var first = _firstNamedItemPress;
        ResetNamedItemPresses();
        _lastNamedItemPressEvent = e;
        if (_targetSelectionMode != RecorderTargetSelectionMode.None || source is null
            || e.GetCurrentPoint(source).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed
            || e.ClickCount <= 0)
            return false;

        var surface = source.GetVisualAncestors().Prepend(source).OfType<Control>()
            .FirstOrDefault(NamedItemSurfacePeers.IsPublished);
        if (surface is null || TopLevel.GetTopLevel(surface) is not { } window)
            return false;

        var press = ReadPress(surface, source, window, e);
        _lastPressWasNamed = press is not null;
        // Avalonia starts a new tap pair on every odd press, including 3 before 4.
        if (e.ClickCount % 2 == 1)
        {
            _firstNamedItemPress = press;
            return _lastPressWasNamed;
        }

        if (first is not null && press is not null && ReferenceEquals(first.Surface, surface)
            && ReferenceEquals(first.Pointer, e.Pointer) && first.ClickCount == e.ClickCount - 1)
        {
            StepCreationResult? result = (first.Error ?? press.Error) is { } error
                ? StepCreationResult.Unsupported(error)
                : first.Key == press.Key && first.Name == press.Name && first.Revision == press.Revision
                    ? _stepFactory.TryCreateNamedItemStep(surface, press.Name!)
                    : null;
            if (result is not null)
                _pendingNamedItemTap = new PendingNamedItemTap(e.Pointer, e.Timestamp, source, surface, result);
        }

        return _lastPressWasNamed;
    }

    private static NamedItemPress? ReadPress(Control surface, Control source, TopLevel window, PointerPressedEventArgs e)
    {
        try
        {
            var snapshot = NamedItemSurfacePeers.Read(surface);
            var point = e.GetPosition(window);
            var hits = snapshot.Items.Where(item =>
            {
                var bounds = NamedItemSurfacePeers.VisibleBounds(item.Element);
                return bounds.Width > 0 && bounds.Height > 0 && bounds.Contains(point);
            }).Take(2).ToArray();
            if (hits.Length > 1 && ReferenceEquals(surface, source))
                throw new InvalidOperationException("The named surface publishes overlapping click areas.");
            if (hits.Length != 1 || !NamedItemSurfacePeers.OwnsHit(surface, hits[0].Element, source))
                return null;
            var item = snapshot.Find(hits[0].Name);
            return new NamedItemPress(surface, e.Pointer, e.ClickCount, item.Key, item.Name, snapshot.Revision, null);
        }
        catch (InvalidOperationException error)
        {
            // Leave unrelated visual descendants to the ordinary recorder handlers.
            return ReferenceEquals(surface, source)
                ? new NamedItemPress(surface, e.Pointer, e.ClickCount, null, null, null, error.Message)
                : null;
        }
    }

    private void OnNamedItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_state != RecorderSessionState.Recording || _targetSelectionMode != RecorderTargetSelectionMode.None
            || _pendingNamedItemTap is not { } pending || !ReferenceEquals(e.Pointer, pending.Pointer)
            || e.Timestamp != pending.Timestamp || !ReferenceEquals(e.Source, pending.Source))
            return;

        _pendingNamedItemTap = null; // One publication even when the route reaches multiple watched roots.
        _firstNamedItemPress = null;
        // The application's handler may already have replaced the view. Use the selector validated on press.
        AddStep(pending.Step, pending.Surface, "NamedItemDoubleTap",
            suppressRecentDuplicate: false, useCapturedSelectorValidation: true);
    }

    private sealed record NamedItemPress(
        Control Surface, IPointer Pointer, int ClickCount, string? Key, string? Name, long? Revision, string? Error);

    private sealed record PendingNamedItemTap(
        IPointer Pointer, ulong Timestamp, Control Source, Control Surface, StepCreationResult Step);
}
