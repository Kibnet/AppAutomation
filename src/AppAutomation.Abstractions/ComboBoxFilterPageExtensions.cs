using System.Linq.Expressions;

namespace AppAutomation.Abstractions;

public static partial class UiPageExtensions
{
    /// <summary>
    /// Applies the exact selected value set to a logical ComboBoxEditor-style filter.
    /// </summary>
    public static TSelf ApplyFilterSelection<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IComboBoxFilterControl>> selector,
        IReadOnlyCollection<string> values,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        return page.SelectMultiItems(AsMultiSelectSelector(selector), values, timeoutMs);
    }

    /// <summary>
    /// Replays a canceled filter selection while preserving the committed value set.
    /// </summary>
    public static TSelf CancelFilterSelection<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IComboBoxFilterControl>> selector,
        IReadOnlyCollection<string> pendingValues,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        return page.CancelMultiSelection(AsMultiSelectSelector(selector), pendingValues, timeoutMs);
    }

    private static Expression<Func<TSelf, IMultiSelectControl>> AsMultiSelectSelector<TSelf>(
        Expression<Func<TSelf, IComboBoxFilterControl>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return Expression.Lambda<Func<TSelf, IMultiSelectControl>>(selector.Body, selector.Parameters);
    }
}
