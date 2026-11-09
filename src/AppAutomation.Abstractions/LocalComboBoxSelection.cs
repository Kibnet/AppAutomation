namespace AppAutomation.Abstractions;

internal interface ILocalComboBoxEditor
{
    IReadOnlyList<string> SelectLocalItem(string itemText, UiOperationTimeoutBudget budget, LocalComboBoxParts parts);
}

internal sealed record LocalComboBoxParts(
    Func<IUiControl?> ResolveInput,
    Func<IButtonControl?> ResolveOpenButton,
    Func<ISelectableListBoxControl?> ResolveResults,
    Func<bool>? IsPopupOpen,
    Func<string?> ReadCommittedText,
    Func<ISelectableListBoxControl, IReadOnlyList<string>> SelectFallback)
{
    public LocalComboBoxSurface<TNative>? ResolveSurface<TNative>(Func<ISelectableListBoxControl, TNative?> unwrap)
        where TNative : class
    {
        var results = ResolveResults();
        if (results is null)
            return null;
        if (unwrap(results) is { } native)
            return new(native);
        return results.IsEnabled && results is not IUiControlAvailability { IsAvailable: false }
            ? new(() => SelectFallback(results))
            : null;
    }
}

internal sealed class LocalComboBoxSurface<TNative> where TNative : class
{
    public LocalComboBoxSurface(TNative native) => Native = native;

    public LocalComboBoxSurface(Func<IReadOnlyList<string>> selectFallback) => SelectFallback = selectFallback;

    public TNative? Native { get; }

    public Func<IReadOnlyList<string>>? SelectFallback { get; }
}

internal static class LocalComboBoxSelection
{
    public static IButtonControl WaitForOpenButton(
        string editor, Func<IButtonControl?> resolve, UiOperationTimeoutBudget budget, IButtonControl? initial = null)
    {
        return Wait(() =>
        {
            var button = initial ?? resolve();
            initial = null;
            return button;
        }, static button => button is { IsEnabled: true }
            and not IUiControlAvailability { IsAvailable: false }, budget,
            $"ComboBox '{editor}' open button did not become available.")!;
    }

    public static IReadOnlyList<string> Select<TNative>(
        string editor,
        string itemText,
        UiOperationTimeoutBudget budget,
        Func<bool?> readOpen,
        Action open,
        Func<LocalComboBoxSurface<TNative>?> resolveVisibleResults,
        Func<TNative, IReadOnlyList<string>> activate,
        Func<TNative, bool>? isCommitted = null)
        where TNative : class
    {
        _ = budget.Remaining;
        var isOpen = readOpen();
        var surface = isOpen == false ? null : resolveVisibleResults();
        if (isOpen != true && surface is null)
        {
            _ = budget.Remaining;
            open();
        }

        surface ??= Wait(() => readOpen() == false ? null : resolveVisibleResults(),
            static value => value is not null, budget,
            $"ComboBox '{editor}' results did not appear.")!;
        _ = budget.Remaining;
        if (surface.SelectFallback is { } select)
            return select();

        var results = surface.Native!;
        var captions = activate(results);

        // A grid owns the final cell commit; the standalone editor must apply and close here.
        if (isCommitted is not null)
        {
            Wait(() => isCommitted(results), static value => value, budget,
                $"ComboBox '{editor}' did not apply item '{itemText}' and close its popup.");
        }
        _ = budget.Remaining;
        return captions;
    }

    private static T Wait<T>(Func<T> read, Predicate<T> ready, UiOperationTimeoutBudget budget, string error) =>
        UiWait.Until(read, ready, new UiWaitOptions
        {
            Timeout = budget.Remaining,
            PollInterval = TimeSpan.FromMilliseconds(25)
        }, error);
}
