using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Input;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace AppAutomation.FlaUI.Automation;

public sealed partial class FlaUiControlResolver
{
    private AutomationElement? FindExactVisibleComboBoxResults(UiControlDefinition definition)
    {
        var scopeRoots = definition.Scope is { } scope ? FindScopeRoots(scope) : null;
        AutomationElement[]? detachedRoots = null;
        var matches = FindPreferredLocatorMatches(definition.LocatorKind, definition.FallbackToName, FindVisible);
        if (matches.Length > 1)
            throw new UiControlResolutionException(UiControlResolutionFailure.Ambiguous,
                $"More than one visible combo-box result matches [{definition.LocatorKind}:{definition.LocatorValue}].");
        return matches.SingleOrDefault();

        AutomationElement[] FindVisible(UiLocatorKind kind)
        {
            var condition = CreateCondition(definition.LocatorValue, kind);
            if (scopeRoots is not null)
                return FindIn(scopeRoots, TreeScope.Descendants);

            var inWindow = FindIn([_window], TreeScope.Subtree);
            return inWindow.Length > 0
                ? inWindow
                : FindIn(detachedRoots ??= EnumerateDetachedProcessRoots(), TreeScope.Subtree);

            AutomationElement[] FindIn(IEnumerable<AutomationElement> roots, TreeScope searchScope) =>
                DistinctElements(roots.SelectMany(root => TryRead(() => root.FindAll(searchScope, condition)) ?? [])
                    .Where(IsVisibleComboBoxResults)).Take(2).ToArray();
        }
    }

    private static AutomationElement? FindEditorComboBoxPart(AutomationElement editor, SingleSelectParts? registration,
        string? locator, Func<AutomationElement, bool> isAvailable, bool includeEditor = false)
    {
        if (registration is null || string.IsNullOrWhiteSpace(locator)) return null;
        var matches = FindPreferredLocatorMatches(registration.LocatorKind, registration.FallbackToName, FindVisible);
        return matches.Length == 1 ? matches[0] : null;

        AutomationElement[] FindVisible(UiLocatorKind kind) =>
            (TryRead(() => editor.FindAll(includeEditor ? TreeScope.Subtree : TreeScope.Descendants,
                CreatePropertyCondition(editor.ConditionFactory, locator, kind))) ?? [])
            .Where(isAvailable).Take(2).ToArray();
    }

    private static AutomationElement[] FindPreferredLocatorMatches(UiLocatorKind kind, bool fallbackToName,
        Func<UiLocatorKind, AutomationElement[]> find)
    {
        var matches = find(kind);
        return matches.Length == 0 && fallbackToName && kind != UiLocatorKind.Name
            ? find(UiLocatorKind.Name)
            : matches;
    }

    private static bool IsVisibleEditorComboBoxPart(AutomationElement element) =>
        TryRead(() => element.IsAvailable && !element.IsOffscreen);

    private static IReadOnlyList<string> SelectLocalComboBox(
        string editorName,
        string itemText,
        bool commit,
        UiOperationTimeoutBudget budget,
        Func<bool?> readOpen,
        Action open,
        Func<LocalComboBoxSurface<AutomationElement>?> resolveResults,
        Func<string?>? readCommittedText = null,
        AutomationElement? foregroundOwner = null,
        Action? prepareForeground = null)
    {
        Func<AutomationElement, bool>? verifyCommit = readCommittedText is null ? null : IsCommitted;
        if (foregroundOwner is null)
            return LocalComboBoxSelection.Select(editorName, itemText, budget, readOpen, open, ResolveVisibleResults,
                results => Activate(results, null), verifyCommit);

        ArgumentNullException.ThrowIfNull(prepareForeground);
        LocalComboBoxSurface<AutomationElement>? deferred = null;
        var captions = DesktopPointer.WithForegroundClick(foregroundOwner, budget.Remaining,
            "activate single-select result", prepareForeground, click =>
            {
                var surface = LocalComboBoxSelection.OpenAndResolve(budget, readOpen, open, ResolveVisibleResults,
                    $"ComboBox '{editorName}' results did not appear.");
                if (surface.Native is not { } results || TryRead(() => results.ControlType) != ControlType.List)
                {
                    // Wrapped surfaces and native ComboBox controls own their selection and may
                    // acquire the desktop themselves. Do not invoke them inside this operation.
                    deferred = surface;
                    return Array.Empty<string>();
                }
                return LocalComboBoxSelection.SelectResolved(editorName, itemText, budget, surface,
                    native => Activate(native, click), verifyCommit);
            });
        return deferred is null ? captions : LocalComboBoxSelection.SelectResolved(editorName, itemText, budget, deferred,
            results => Activate(results, null), verifyCommit);

        LocalComboBoxSurface<AutomationElement>? ResolveVisibleResults() =>
            resolveResults() is { } surface
                && (surface.SelectFallback is not null || IsSelectableComboBoxResults(surface.Native!))
                    ? surface
                    : null;

        IReadOnlyList<string> Activate(AutomationElement results, Action<Func<AutomationElement>>? click)
        {
            switch (TryRead(() => results.ControlType))
            {
                case ControlType.List:
                    var list = new FlaUiListBoxControl(results.AsListBox(), readCommittedText is null
                        ? () => resolveResults()?.Native
                        : () => IsSelectableComboBoxResults(results) ? results : null);
                    if (commit)
                        return list.ActivateItem(itemText, budget.Remaining,
                            verifyListSelection: readCommittedText is null, click);
                    list.SelectItem(itemText, budget.Remaining);
                    return [];
                case ControlType.ComboBox:
                    new FlaUiComboBoxControl(results.AsComboBox()).SelectItem(itemText, budget.Remaining);
                    return [];
                default:
                    throw new InvalidOperationException(
                        $"ComboBox '{editorName}' results expose '{TryRead(() => results.ControlType)}', not ListBox or ComboBox.");
            }
        }

        bool IsCommitted(AutomationElement results)
        {
            if (readOpen() ?? IsVisibleComboBoxResults(results)) return false;
            try
            {
                return GridSelectionText.Equals(readCommittedText!(), itemText);
            }
            catch (UiControlResolutionException exception) when (exception.IsTransient)
            {
                return false;
            }
        }
    }

    private static bool IsVisibleComboBoxResults(AutomationElement element) =>
        TryRead(() => element.IsAvailable && !element.IsOffscreen
            && element.BoundingRectangle is { Width: > 0, Height: > 0 });

    private static bool IsSelectableComboBoxResults(AutomationElement element) =>
        TryRead(() => element.IsEnabled) && IsVisibleComboBoxResults(element);
}
