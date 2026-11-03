using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Input;
using System.Diagnostics;
using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;

namespace AppAutomation.FlaUI.Automation;

public sealed partial class FlaUiControlResolver
{
    /// <summary>Owns native row selection and confirmation without changing grid navigation.</summary>
    private static class GridRowSelection
    {
        internal static void SelectAndConfirm(
            AutomationElement target,
            AutomationElement? selectionContainer,
            int timeoutMs,
            string description,
            Func<AutomationElement?> refreshTarget,
            Func<AutomationElement?>? refreshSelectionContainer = null,
            Func<AutomationElement, bool>? isCurrentRowMatch = null)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(refreshTarget);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var stopwatch = Stopwatch.StartNew();
            var currentTarget = target;
            var targetBounds = TryRead(() => currentTarget.BoundingRectangle);
            var targetIsVisible = TryRead(() => currentTarget.IsAvailable)
                && !TryRead(() => currentTarget.IsOffscreen)
                && targetBounds.Width > 0
                && targetBounds.Height > 0;
            if (!targetIsVisible)
            {
                TryScrollIntoView(currentTarget);
            }
            if (!targetIsVisible || isCurrentRowMatch is null || !isCurrentRowMatch(currentTarget))
            {
                currentTarget = WaitForFreshGridRowTarget(refreshTarget, stopwatch, timeoutMs, description);
                selectionContainer = refreshSelectionContainer?.Invoke() ?? selectionContainer;
            }
            var selectable = FindSelectableGridRowElement(currentTarget);
            System.Drawing.Point? completedClickPoint = null;

            TimeSpan RemainingPointerTimeout()
            {
                var remaining = TimeSpan.FromMilliseconds(timeoutMs) - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    throw new TimeoutException($"{description} exhausted its selection timeout before clicking.");
                return remaining;
            }

            AutomationElement ResolvePhysicalTarget()
            {
                _ = RemainingPointerTimeout();
                var bounds = TryRead(() => currentTarget.BoundingRectangle);
                if (!TryRead(() => currentTarget.IsAvailable)
                    || TryRead(() => currentTarget.IsOffscreen)
                    || bounds.Width <= 0
                    || bounds.Height <= 0
                    || isCurrentRowMatch is null
                    || !isCurrentRowMatch(currentTarget))
                {
                    currentTarget = refreshTarget()
                        ?? throw new InvalidOperationException($"{description} disappeared before clicking.");
                    if (refreshSelectionContainer is not null)
                        selectionContainer = refreshSelectionContainer();
                }
                return currentTarget;
            }

            if (!TryConfirmGridRowSelection(
                    currentTarget,
                    selectable,
                    selectionContainer,
                    completedClickPoint: null,
                    out _))
            {
                if (selectable is not null)
                {
                    // A provider can apply Select before throwing; do not replay it as a click.
                    selectable.Patterns.SelectionItem.Pattern.Select();
                }
                else
                {
                    try
                    {
                        System.Drawing.Point clickPoint = default;
                        DesktopPointer.ClickPrepared(
                            ResolvePhysicalTarget,
                            element => { TryScrollIntoView(element); TryFocus(element); },
                            element => clickPoint = GetVisibleGridRowPoint(element, selectionContainer),
                            options: new PointerClickOptions { Timeout = RemainingPointerTimeout() });
                        completedClickPoint = clickPoint;
                    }
                    catch (Exception pointerFallbackException) when (!DesktopPointer.IsTerminalFailure(pointerFallbackException)
                        && pointerFallbackException is not TimeoutException
                        && stopwatch.ElapsedMilliseconds < timeoutMs)
                    {
                        currentTarget = WaitForFreshGridRowTarget(refreshTarget, stopwatch, timeoutMs, description);
                        selectable = FindSelectableGridRowElement(currentTarget);
                        System.Drawing.Point clickPoint = default;
                        DesktopPointer.ClickPrepared(
                            ResolvePhysicalTarget,
                            element => { TryScrollIntoView(element); TryFocus(element); },
                            element => clickPoint = GetVisibleGridRowPoint(element, selectionContainer),
                            options: new PointerClickOptions { Timeout = RemainingPointerTimeout() });
                        completedClickPoint = clickPoint;
                    }
                }
            }

            var selectionStateWasReadable = false;
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                if (isCurrentRowMatch is null || !isCurrentRowMatch(currentTarget))
                {
                    var refreshedTarget = refreshTarget();
                    if (refreshedTarget is null)
                    {
                        Thread.Sleep(Math.Max(
                            1,
                            Math.Min(25, timeoutMs - (int)stopwatch.ElapsedMilliseconds)));
                        continue;
                    }

                    currentTarget = refreshedTarget;
                    selectionContainer = refreshSelectionContainer?.Invoke() ?? selectionContainer;
                    selectable = FindSelectableGridRowElement(currentTarget);
                }
                if (TryConfirmGridRowSelection(
                        currentTarget,
                        selectable,
                        selectionContainer,
                        completedClickPoint,
                        out var currentStateWasReadable))
                {
                    return;
                }
                selectionStateWasReadable |= currentStateWasReadable;

                Thread.Sleep(Math.Max(
                    1,
                    Math.Min(25, timeoutMs - (int)stopwatch.ElapsedMilliseconds)));
            }

            if (!selectionStateWasReadable)
            {
                throw new System.NotSupportedException(
                    $"{description} exposes neither a readable SelectionItem pattern nor a readable grid selection; selection cannot be confirmed.");
            }

            throw new InvalidOperationException(
                $"{description} did not confirm its selected state within {timeoutMs} ms. "
                + DescribeGridSelectionEvidence(currentTarget, selectionContainer));
        }

        private static AutomationElement WaitForFreshGridRowTarget(
            Func<AutomationElement?> resolveTarget,
            Stopwatch stopwatch,
            int timeoutMs,
            string description)
        {
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                var target = resolveTarget();
                if (target is not null)
                {
                    return target;
                }

                Thread.Sleep(Math.Max(
                    1,
                    Math.Min(25, timeoutMs - (int)stopwatch.ElapsedMilliseconds)));
            }

            throw new TimeoutException(
                $"{description} was not materialized after scrolling within {timeoutMs} ms.");
        }

        private static AutomationElement? FindSelectableGridRowElement(AutomationElement target)
        {
            for (var current = target; current is not null; current = TryRead(() => current.Parent))
            {
                if (TryRead(() => current.Patterns.SelectionItem.IsSupported))
                {
                    return current;
                }
            }

            return null;
        }

        private static string DescribeGridSelectionEvidence(
            AutomationElement target,
            AutomationElement? selectionContainer)
        {
            var focused = TryRead(() => target.Automation.FocusedElement());
            var selectionSupported = selectionContainer is not null
                && TryRead(() => selectionContainer.Patterns.Selection.IsSupported);
            var selectedElements = selectionSupported
                ? TryRead(() => selectionContainer!.Patterns.Selection.Pattern.Selection.Value)
                : null;
            return $"targetType={TryRead(() => target.ControlType)}; "
                + $"targetBounds={TryRead(() => target.BoundingRectangle)}; "
                + $"focusedType={TryRead(() => focused?.ControlType)}; "
                + $"focusedBounds={TryRead(() => focused?.BoundingRectangle)}; "
                + $"containerType={TryRead(() => selectionContainer?.ControlType)}; "
                + $"selectionSupported={selectionSupported}; "
                + $"selectedCount={selectedElements?.Length.ToString(CultureInfo.InvariantCulture) ?? "<unreadable>"}; "
                + $"selected={string.Join(", ", (selectedElements ?? [])
                    .Select(selected => $"{TryRead(() => selected.ItemStatus)}/{TryRead(() => selected.Name)}"))}.";
        }

        private static System.Drawing.Point GetVisibleGridRowPoint(
            AutomationElement row,
            AutomationElement? selectionContainer)
        {
            var rowBounds = TryRead(() => row.BoundingRectangle);
            var visibleBounds = selectionContainer is null
                ? rowBounds
                : System.Drawing.Rectangle.Intersect(
                    rowBounds,
                    TryRead(() => selectionContainer.BoundingRectangle));
            if (visibleBounds.Width <= 0 || visibleBounds.Height <= 0)
            {
                throw new InvalidOperationException("The current grid row is outside its selection viewport.");
            }
            var children = TryRead(() => row.FindAllChildren()) ?? [];
            var cells = children
                .Where(child => TryRead(() => child.ControlType) is ControlType.Group or ControlType.Custom or ControlType.DataItem)
                .Select(child => System.Drawing.Rectangle.Intersect(visibleBounds, TryRead(() => child.BoundingRectangle)))
                .Where(static bounds => bounds.Width > 0 && bounds.Height > 0)
                .ToArray();
            if (cells.Length == 0)
                throw new System.NotSupportedException("The grid row exposes no cell bounds for a safe selection-only click.");
            var content = cells.Aggregate(System.Drawing.Rectangle.Union);
            // Click row background/selector space, never a cell's text or editor.
            var background = new[]
            {
                System.Drawing.Rectangle.FromLTRB(visibleBounds.Left, visibleBounds.Top, content.Left, visibleBounds.Bottom),
                System.Drawing.Rectangle.FromLTRB(content.Right, visibleBounds.Top, visibleBounds.Right, visibleBounds.Bottom),
                System.Drawing.Rectangle.FromLTRB(visibleBounds.Left, visibleBounds.Top, visibleBounds.Right, content.Top),
                System.Drawing.Rectangle.FromLTRB(visibleBounds.Left, content.Bottom, visibleBounds.Right, visibleBounds.Bottom)
            }.Where(static bounds => bounds.Width > 0 && bounds.Height > 0)
                .OrderByDescending(static bounds => (long)bounds.Width * bounds.Height).FirstOrDefault();
            if (background.Width > 0 && background.Height > 0)
                return new System.Drawing.Point(background.Left + background.Width / 2, background.Top + background.Height / 2);

            // Some providers expose a trailing filler as a cell-shaped Custom peer.
            // Column headers prove where real columns end, even when that filler has row-sized bounds.
            if (selectionContainer is not null)
            {
                var headers = TryRead(() => selectionContainer.FindAllDescendants(
                    selectionContainer.Automation.ConditionFactory.ByControlType(ControlType.HeaderItem))) ?? [];
                var columnBounds = headers.Where(header => IsUsefulAutomationText(TryRead(() => header.Name))
                        || !string.IsNullOrWhiteSpace(TryRead(() => header.AutomationId)))
                    .Select(header => TryRead(() => header.BoundingRectangle))
                    .Where(bounds => bounds.Width > 0 && bounds.Height > 0 && bounds.Bottom <= visibleBounds.Top
                        && bounds.Right > visibleBounds.Left && bounds.Left < visibleBounds.Right).ToArray();
                if (columnBounds.Length > 0)
                {
                    var columnsRight = columnBounds.Max(static bounds => bounds.Right);
                    if (columnsRight < visibleBounds.Right)
                        return new System.Drawing.Point(columnsRight + (visibleBounds.Right - columnsRight) / 2,
                            visibleBounds.Top + visibleBounds.Height / 2);
                }
            }
            throw new System.NotSupportedException(
                "The grid row exposes no background selection area; a SelectionItem pattern or row selector is required.");
        }

        private static bool TryConfirmGridRowSelection(
            AutomationElement target,
            AutomationElement? selectable,
            AutomationElement? selectionContainer,
            System.Drawing.Point? completedClickPoint,
            out bool selectionStateWasReadable)
        {
            bool? selectionItemIsSelected = null;
            if (selectable is not null
                && TryRead(() => selectable.Patterns.SelectionItem.IsSupported))
            {
                selectionItemIsSelected = TryRead(() =>
                    (bool?)selectable.Patterns.SelectionItem.Pattern.IsSelected.Value);
            }

            AutomationElement[]? selectedElements = null;
            if (selectionContainer is not null
                && TryRead(() => selectionContainer.Patterns.Selection.IsSupported))
            {
                selectedElements = TryRead(() => selectionContainer.Patterns.Selection.Pattern.Selection.Value);
            }

            if (selectedElements is null && selectionContainer is not null)
            {
                selectedElements = TryRead(() => selectionContainer
                    .AsGrid()
                    .SelectedItems
                    .Select(static row => (AutomationElement)row)
                    .ToArray());
            }

            var targetIsSelected = selectedElements?.Any(selected =>
                RepresentsSameSelectedGridRow(selected, target)) == true;
            var focusedElement = TryRead(() => target.Automation.FocusedElement());
            var focusRepresentsTarget = focusedElement is not null
                && RepresentsFocusedGridRow(focusedElement, target, selectionContainer);
            // Click returns the cursor before selection is observed, so use the delivered point.
            var containerFocusConfirmsClick = completedClickPoint.HasValue
                && focusedElement is not null
                && selectionContainer is not null
                && IsSameAutomationElement(focusedElement, selectionContainer)
                && IsPointWithinGridRow(target, selectionContainer, completedClickPoint.Value);

            selectionStateWasReadable = selectionItemIsSelected.HasValue
                || selectedElements is not null
                || focusedElement is not null;
            return selectionItemIsSelected == true
                || (selectedElements is not null && targetIsSelected)
                || focusRepresentsTarget
                || containerFocusConfirmsClick;
        }

        private static bool IsPointWithinGridRow(
            AutomationElement target,
            AutomationElement selectionContainer,
            System.Drawing.Point point)
        {
            using var dpi = WindowsPointerBackend.PhysicalDpiScope.Enter();
            var targetBounds = TryRead(() => target.BoundingRectangle);
            var containerBounds = TryRead(() => selectionContainer.BoundingRectangle);
            if (targetBounds.Width <= 0
                || targetBounds.Height <= 0
                || containerBounds.Width <= 0
                || containerBounds.Height <= 0)
            {
                return false;
            }

            var visibleTargetBounds = System.Drawing.Rectangle.Intersect(targetBounds, containerBounds);
            return visibleTargetBounds.Width > 0
                && visibleTargetBounds.Height > 0
                && visibleTargetBounds.Contains(point);
        }

        private static bool RepresentsFocusedGridRow(
            AutomationElement focusedElement,
            AutomationElement target,
            AutomationElement? selectionContainer)
        {
            if (selectionContainer is not null
                && IsSameAutomationElement(focusedElement, selectionContainer))
            {
                return false;
            }

            if (IsSameAutomationElement(focusedElement, target)
                || IsAutomationAncestor(target, focusedElement))
            {
                return true;
            }

            var focusedBounds = TryRead(() => focusedElement.BoundingRectangle);
            var targetBounds = TryRead(() => target.BoundingRectangle);
            if (focusedBounds.Width <= 0
                || focusedBounds.Height <= 0
                || targetBounds.Width <= 0
                || targetBounds.Height <= 0
                || focusedBounds.Height > targetBounds.Height * 2)
            {
                return false;
            }

            var overlap = System.Drawing.Rectangle.Intersect(focusedBounds, targetBounds);
            return overlap.Width > 0
                && overlap.Height > 0
                && overlap.Height * 2 >= Math.Min(focusedBounds.Height, targetBounds.Height);
        }

        private static bool RepresentsSameSelectedGridRow(
            AutomationElement selected,
            AutomationElement target)
        {
            if (IsSameAutomationElement(selected, target)
                || IsAutomationAncestor(target, selected)
                || IsAutomationAncestor(selected, target))
            {
                return true;
            }

            var selectedStableValue = TryRead(() => selected.ItemStatus);
            var targetStableValue = TryRead(() => target.ItemStatus);
            if (string.IsNullOrWhiteSpace(selectedStableValue)
                || !string.Equals(selectedStableValue, targetStableValue, StringComparison.Ordinal))
            {
                return false;
            }

            var selectedBounds = TryRead(() => selected.BoundingRectangle);
            var targetBounds = TryRead(() => target.BoundingRectangle);
            if (selectedBounds.Height <= 0 || targetBounds.Height <= 0)
            {
                return false;
            }

            const int roundingTolerance = 1;
            var verticalOverlap = Math.Min(selectedBounds.Bottom, targetBounds.Bottom) + roundingTolerance
                - (Math.Max(selectedBounds.Top, targetBounds.Top) - roundingTolerance);
            return verticalOverlap > 0
                && verticalOverlap * 2 >= Math.Min(selectedBounds.Height, targetBounds.Height);
        }

        private static bool IsAutomationAncestor(
            AutomationElement expectedAncestor,
            AutomationElement element)
        {
            var expectedIdentity = GetAutomationElementIdentity(expectedAncestor);
            for (var current = element; current is not null; current = TryRead(() => current.Parent))
            {
                if (string.Equals(
                        GetAutomationElementIdentity(current),
                        expectedIdentity,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSameAutomationElement(AutomationElement left, AutomationElement right) =>
            string.Equals(
                GetAutomationElementIdentity(left),
                GetAutomationElementIdentity(right),
                StringComparison.Ordinal);
    }
}
