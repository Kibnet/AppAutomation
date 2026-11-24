using AppAutomation.Abstractions;
using Avalonia.Controls;
using static AppAutomation.Recorder.Avalonia.RecorderCaptureControlReader;

namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderGridCaptureResolver
{
    internal bool IsGridCellContextPart(Control source, GridComboSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        var definition = FindGridDefinition(context.GridHint);
        var column = definition is null || string.IsNullOrWhiteSpace(context.LogicalColumnName)
            ? null
            : definition.FindColumn(context.LogicalColumnName);
        if (column?.EditorParts is not { } parts)
        {
            return ReferenceEquals(source, context.SelectionSource)
                || IsWithin(source, context.EditorRoot);
        }

        return EnumerateEditorPartLocators(parts)
            .Any(part => MatchesScopedEditorPart(source, context, part));
    }

    private bool MatchesScopedEditorPart(
        Control source,
        GridComboSelectionContext context,
        GridRelativeLocator part)
    {
        if (FindUniqueEditorPart(EnumerateRelatedControls(source), part) is not { } partSource)
        {
            return false;
        }

        return part.Scope switch
        {
            GridRelativeLocatorScope.Cell => IsUniqueScopedPart(partSource, context.CellSource, part),
            GridRelativeLocatorScope.EditorRoot => IsUniqueScopedPart(partSource, context.EditorRoot, part),
            GridRelativeLocatorScope.GridRoot => IsUniqueScopedPart(partSource, context.GridSource, part),
            GridRelativeLocatorScope.DetachedPopup => IsValidDetachedPartEventSource(source, partSource, part),
            _ => false
        };
    }

    private bool IsValidDetachedPartEventSource(
        Control eventSource,
        Control partSource,
        GridRelativeLocator part)
    {
        if (ReferenceEquals(partSource, eventSource)
            && TopLevel.GetTopLevel(partSource) is null)
        {
            return true;
        }

        return IsUniqueActiveDetachedPart(partSource, part);
    }

    private static bool IsUniqueScopedPart(
        Control source,
        Control scopeRoot,
        GridRelativeLocator part)
    {
        if (!IsWithin(source, scopeRoot))
        {
            return false;
        }

        return ReferenceEquals(source, FindUniqueEditorPart(EnumerateDescendantControls(scopeRoot), part, requireVisible: true));
    }

    private bool IsUniqueActiveDetachedPart(Control source, GridRelativeLocator part)
    {
        var root = _validationRootProvider?.Invoke();
        if (root is null)
        {
            return source.IsEffectivelyVisible;
        }

        var scopeRoots = new[]
            {
                root,
                TopLevel.GetTopLevel(source) as Control
            }
            .Where(static candidate => candidate is not null)
            .Cast<Control>()
            .Distinct<Control>(ReferenceEqualityComparer.Instance);
        var candidates = scopeRoots
            .SelectMany(EnumerateDescendantControls)
            .Distinct<Control>(ReferenceEqualityComparer.Instance);
        return ReferenceEquals(source, FindUniqueEditorPart(candidates, part, requireVisible: true));
    }

    private static Control? FindUniqueEditorPart(
        IEnumerable<Control> candidates, GridRelativeLocator locator, bool requireVisible = false)
    {
        Control? match = null;
        foreach (var candidate in candidates)
        {
            if (!HasExactLocator(candidate, locator.LocatorKind, locator.LocatorValue)
                || requireVisible && !IsVisibleForCapture(candidate))
                continue;
            if (match is not null) return null;
            match = candidate;
        }
        return match;
    }

    private static bool IsVisibleForCapture(Control control) =>
        TopLevel.GetTopLevel(control) is null ? control.IsVisible : control.IsEffectivelyVisible;

    private static bool IsWithin(Control source, Control scopeRoot)
    {
        return EnumerateRelatedControls(source)
            .Any(candidate => ReferenceEquals(candidate, scopeRoot));
    }

    private static IEnumerable<GridRelativeLocator> EnumerateEditorPartLocators(GridCellEditorParts parts)
    {
        if (parts.Input is not null)
        {
            yield return parts.Input;
        }

        if (parts.Results is not null)
        {
            yield return parts.Results;
        }

        if (parts.OpenButton is not null)
        {
            yield return parts.OpenButton;
        }

        if (parts.ConfirmButton is not null)
        {
            yield return parts.ConfirmButton;
        }

        if (parts.CancelButton is not null)
        {
            yield return parts.CancelButton;
        }
    }

    internal RecorderGridSelectionCaptureContext? ResolveSelectionCaptureContext(
        GridComboSelectionContext context, Control validationSource, out string? error)
    {
        var definition = FindGridDefinition(context.GridHint);
        var column = definition is null || string.IsNullOrWhiteSpace(context.LogicalColumnName)
            ? null
            : definition.FindColumn(context.LogicalColumnName);
        var rowIndex = context.RowIndex;
        var captureSource = validationSource;
        if (definition?.Columns.Any(static candidate => candidate.IsObjectKey) == true)
        {
            if (context.RowObject is null || !NativeGridSelectionAccess.TryReadCurrentView(context.GridSource, out var currentView)
                || currentView.Count(item => ReferenceEquals(item, context.RowObject)) != 1
                || !TryResolveGridRow(context.CellSource, context.GridSource, currentView, out rowIndex, out var currentRow, referenceOnly: true)
                || !ReferenceEquals(currentRow, context.RowObject))
            {
                error = "The selection editor's original object row is no longer unique in the current view; capture its runtime row again.";
                return null;
            }
            captureSource = context.CellSource;
        }
        error = null;
        return new(column?.EditorKind == GridCellEditorKind.SearchPicker, rowIndex, captureSource);
    }

}

internal sealed record RecorderGridSelectionCaptureContext(bool IsSearchPicker, int RowIndex, Control Source);
