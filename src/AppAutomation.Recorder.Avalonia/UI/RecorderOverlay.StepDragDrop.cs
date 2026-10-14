using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AppAutomation.Recorder.Avalonia.UI;

internal sealed partial class RecorderOverlay
{
    private const double DragThreshold = 4;
    private const double AutoScrollEdge = 24;
    private Guid? _dragStepId;
    private Guid[] _dragStartOrder = [];
    private IPointer? _dragPointer;
    private Point _dragStartPoint;
    private Point _dragPoint;
    private bool _isStepDragging;
    private int? _dropBoundary;
    private Border? _draggedCard;
    private IBrush? _draggedCardBorder;
    private Cursor? _previousJournalCursor;
    private Grid? _journalViewport;
    private TextBlock? _stepDropIndicator;
    private DispatcherTimer? _stepDragScrollTimer;
    private Window? _stepDragWindow;

    private bool CanDragJournalStep => _stepReorderDetails is not null
        && _sessionDetails?.IsBusy != true && _activeStepEditDraft is null;

    private Guid[] CurrentJournalOrder() =>
        _sessionDetails?.StepJournal.Select(static entry => entry.StepId).ToArray() ?? [];

    private void InitializeStepDragDrop()
    {
        _journalViewport = this.FindControl<Grid>("StepJournalViewport");
        _stepDropIndicator = this.FindControl<TextBlock>("StepDropIndicator");
        if (_stepJournalScrollViewer is not { } scroll)
        {
            return;
        }

        scroll.AddHandler(PointerMovedEvent, OnStepDragMoved, RoutingStrategies.Tunnel);
        scroll.AddHandler(PointerReleasedEvent, OnStepDragReleased, RoutingStrategies.Tunnel);
        scroll.PointerCaptureLost += (_, _) => CancelStepDrag();
        scroll.ScrollChanged += (_, _) => UpdateStepDropTarget();
        _stepDragScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _stepDragScrollTimer.Tick += (_, _) => ScrollDuringStepDrag();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _stepDragWindow = TopLevel.GetTopLevel(this) as Window;
        if (_stepDragWindow is { } window)
        {
            window.AddHandler(KeyDownEvent, OnStepJournalKeyDown, RoutingStrategies.Tunnel);
            window.Deactivated += OnStepDragWindowDeactivated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        EndStepDrag();
        if (_stepDragWindow is { } window)
        {
            window.RemoveHandler(KeyDownEvent, OnStepJournalKeyDown);
            window.Deactivated -= OnStepDragWindowDeactivated;
        }

        _stepDragWindow = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnStepDragWindowDeactivated(object? sender, EventArgs e) => CancelStepDrag();

    private void WireStepDragSource(Control source, Border card, Guid stepId)
    {
        source.PointerPressed += (_, e) =>
        {
            if (!CanDragJournalStep || _stepJournalScrollViewer is not { } scroll
                || _dragStepId is not null || !e.GetCurrentPoint(scroll).Properties.IsLeftButtonPressed)
            {
                return;
            }

            _dragStartOrder = CurrentJournalOrder();
            if (!_dragStartOrder.Contains(stepId))
            {
                return;
            }

            _dragStepId = stepId;
            _draggedCard = card;
            _draggedCardBorder = card.BorderBrush;
            _dragStartPoint = _dragPoint = e.GetPosition(scroll);
            _previousJournalCursor = scroll.Cursor;
            _dragPointer = e.Pointer;
            _stepDragScrollTimer?.Start();
            e.Pointer.Capture(scroll);
            scroll.Focus();
            e.Handled = true;
        };
    }

    private void OnStepDragMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStepId is null || e.Pointer != _dragPointer || _stepJournalScrollViewer is null)
        {
            return;
        }

        if (!CanDragJournalStep
            || !e.GetCurrentPoint(_stepJournalScrollViewer).Properties.IsLeftButtonPressed)
        {
            CancelStepDrag();
            return;
        }

        _dragPoint = e.GetPosition(_stepJournalScrollViewer);
        if (!_isStepDragging && (Math.Abs(_dragPoint.X - _dragStartPoint.X) > DragThreshold
            || Math.Abs(_dragPoint.Y - _dragStartPoint.Y) > DragThreshold))
        {
            _isStepDragging = true;
            _stepJournalScrollViewer.Cursor = new Cursor(StandardCursorType.SizeAll);
            if (_draggedCard is { } card)
            {
                card.BorderBrush = GetBrush("RecorderAccent");
                card.Opacity = 0.7;
            }

        }

        UpdateStepDropTarget();
        e.Handled = true;
    }

    private void OnStepDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStepId is not { } stepId || e.Pointer != _dragPointer
            || e.InitialPressMouseButton != MouseButton.Left || _stepJournalScrollViewer is null)
        {
            return;
        }

        _dragPoint = e.GetPosition(_stepJournalScrollViewer);
        UpdateStepDropTarget();
        var targetIndex = _dropBoundary is { } boundary
            ? boundary - (boundary > Array.IndexOf(_dragStartOrder, stepId) ? 1 : 0)
            : -1;
        var canDrop = _isStepDragging && CanDragJournalStep
            && CurrentJournalOrder().SequenceEqual(_dragStartOrder) && targetIndex >= 0;
        EndStepDrag();
        if (canDrop && _stepReorderDetails!.MoveStepToIndex(stepId, targetIndex))
        {
            Refresh();
            RevealJournalStep(stepId);
        }
        else
        {
            Refresh();
        }

        e.Handled = true;
    }

    private Rect JournalViewportBounds()
    {
        var scroll = _stepJournalScrollViewer!;
        var presenter = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault();
        var origin = presenter?.TranslatePoint(default, scroll);
        return origin.HasValue
            ? new Rect(origin.Value, presenter!.Bounds.Size)
            : new Rect(scroll.Bounds.Size);
    }

    private void UpdateStepDropTarget()
    {
        if (!_isStepDragging || _stepJournalPanel is null || _stepDropIndicator is null
            || _stepJournalScrollViewer is null || _journalViewport is null)
        {
            return;
        }

        _dropBoundary = null;
        _stepDropIndicator.IsVisible = false;
        var viewport = JournalViewportBounds();
        if (!viewport.Contains(_dragPoint))
        {
            return;
        }

        var rows = _stepJournalPanel.Children;
        var boundary = rows.Count;
        var lineY = viewport.Bottom;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var top = row.TranslatePoint(default, _stepJournalScrollViewer)?.Y;
            if (!top.HasValue)
            {
                return;
            }

            if (_dragPoint.Y < top.Value + row.Bounds.Height / 2)
            {
                boundary = index;
                lineY = top.Value - (index == 0 ? 0 : 3);
                break;
            }

            lineY = top.Value + row.Bounds.Height + 3;
        }

        _dropBoundary = boundary;
        var indicatorPoint = _stepJournalScrollViewer.TranslatePoint(
            new Point(viewport.Left, Math.Clamp(lineY, viewport.Top, Math.Max(viewport.Top, viewport.Bottom - 3))),
            _journalViewport);
        if (indicatorPoint is { } point)
        {
            _stepDropIndicator.Margin = new Thickness(point.X, point.Y, 0, 0);
            _stepDropIndicator.Width = viewport.Width;
            _stepDropIndicator.IsVisible = true;
        }
    }

    private void ScrollDuringStepDrag()
    {
        if (_dragStepId is not null
            && (!CanDragJournalStep || !CurrentJournalOrder().SequenceEqual(_dragStartOrder)))
        {
            CancelStepDrag();
            return;
        }

        if (!_isStepDragging || _stepJournalScrollViewer is not { } scroll)
        {
            return;
        }

        var viewport = JournalViewportBounds();
        if (!viewport.Contains(_dragPoint))
        {
            return;
        }

        var delta = _dragPoint.Y < viewport.Top + AutoScrollEdge ? -8
            : _dragPoint.Y > viewport.Bottom - AutoScrollEdge ? 8 : 0;
        if (delta != 0)
        {
            scroll.Offset = new Vector(scroll.Offset.X,
                Math.Clamp(scroll.Offset.Y + delta, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
            UpdateStepDropTarget();
        }
    }

    private void OnStepJournalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _dragStepId is not null)
        {
            CancelStepDrag();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Z || e.KeyModifiers != KeyModifiers.Control)
        {
            return;
        }

        if (_dragStepId is not null)
        {
            CancelStepDrag();
            e.Handled = true;
            return;
        }

        if (!CanDragJournalStep || e.Source is Control source
            && source.GetSelfAndVisualAncestors().OfType<TextBox>().Any())
        {
            return;
        }

        var previousOrder = CurrentJournalOrder();
        if (_stepReorderDetails!.UndoStepMove())
        {
            Refresh();
            var newOrder = CurrentJournalOrder();
            var previousPositions = previousOrder.Select((id, index) => (id, index))
                .ToDictionary(item => item.id, item => item.index);
            var movedId = Guid.Empty;
            var largestDistance = 0;
            for (var index = 0; index < newOrder.Length; index++)
            {
                var distance = Math.Abs(previousPositions[newOrder[index]] - index);
                if (distance > largestDistance)
                {
                    largestDistance = distance;
                    movedId = newOrder[index];
                }
            }

            RevealJournalStep(movedId);
            e.Handled = true;
        }
    }

    private void RevealJournalStep(Guid stepId) => Dispatcher.Post(() =>
    {
        _stepJournalPanel?.Children.FirstOrDefault(row => row.Tag is Guid id && id == stepId)?.BringIntoView();
    }, DispatcherPriority.Background);

    private void CancelStepDrag()
    {
        if (_dragStepId is null)
        {
            return;
        }

        EndStepDrag();
        Refresh();
    }

    private void EndStepDrag()
    {
        var pointer = _dragPointer;
        _dragStepId = null;
        _dragPointer = null;
        _dragStartOrder = [];
        _isStepDragging = false;
        _dropBoundary = null;
        _stepDragScrollTimer?.Stop();
        if (_stepDropIndicator is { } indicator)
        {
            indicator.IsVisible = false;
        }

        if (_draggedCard is { } card)
        {
            card.BorderBrush = _draggedCardBorder;
            card.Opacity = 1;
        }

        if (_stepJournalScrollViewer is { } scroll && pointer is not null)
        {
            scroll.Cursor = _previousJournalCursor;
        }

        _draggedCard = null;
        pointer?.Capture(null);
    }
}
