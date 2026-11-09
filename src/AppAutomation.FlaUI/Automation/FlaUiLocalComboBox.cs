using AppAutomation.Abstractions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace AppAutomation.FlaUI.Automation;

public sealed partial class FlaUiControlResolver
{
    private static IReadOnlyList<string> SelectLocalComboBox(
        string editorName,
        string itemText,
        bool commit,
        UiOperationTimeoutBudget budget,
        Func<bool?> readOpen,
        Action open,
        Func<LocalComboBoxSurface<AutomationElement>?> resolveResults,
        Func<string?>? readCommittedText = null)
    {
        return LocalComboBoxSelection.Select(editorName, itemText, budget, readOpen, open, ResolveVisibleResults, Activate,
            readCommittedText is null ? null : IsCommitted);

        LocalComboBoxSurface<AutomationElement>? ResolveVisibleResults() =>
            resolveResults() is { } surface
                && (surface.SelectFallback is not null || IsVisibleComboBoxResults(surface.Native!))
                    ? surface
                    : null;

        IReadOnlyList<string> Activate(AutomationElement results)
        {
            switch (TryRead(() => results.ControlType))
            {
                case ControlType.List:
                    var list = new FlaUiListBoxControl(results.AsListBox(), readCommittedText is null
                        ? () => resolveResults()?.Native
                        : () => IsVisibleComboBoxResults(results) ? results : null);
                    if (commit)
                        return list.ActivateItem(itemText, budget.Remaining,
                            verifyListSelection: readCommittedText is null);
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
        TryRead(() => element.IsAvailable && !element.IsOffscreen && element.IsEnabled
            && element.BoundingRectangle is { Width: > 0, Height: > 0 });
}
