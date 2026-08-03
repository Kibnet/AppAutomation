using System.Linq.Expressions;

namespace AppAutomation.Abstractions;

public static partial class UiPageExtensions
{
    /// <summary>Enters a non-empty search value and waits until it is applied to the input.</summary>
    public static TSelf EnterSearch<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ISearchControl>> selector,
        string value,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var search = Resolve(selector, page);
        WaitUntilSearchEnabled(page, selector, search, timeoutMs);
        search.EnterSearch(value);
        WaitUntil(
            page,
            selector,
            () => string.Equals(search.Text, value, StringComparison.Ordinal),
            timeoutMs,
            $"Search control '{search.AutomationId}' did not accept search text.",
            expectedValue: value,
            lastObservedValueFactory: () => search.Text);
        return page;
    }

    /// <summary>Clears the current search value and waits until the input is empty.</summary>
    public static TSelf ClearSearch<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ISearchControl>> selector,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var search = Resolve(selector, page);
        WaitUntilSearchEnabled(page, selector, search, timeoutMs);
        search.ClearSearch();
        WaitUntil(
            page,
            selector,
            () => string.IsNullOrEmpty(search.Text),
            timeoutMs,
            $"Search control '{search.AutomationId}' did not clear search text.",
            expectedValue: string.Empty,
            lastObservedValueFactory: () => search.Text);
        return page;
    }

    /// <summary>Opens search history, applies an exact item, and waits for the popup to close.</summary>
    public static TSelf ApplySearchFromHistory<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ISearchControl>> selector,
        string value,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var search = Resolve(selector, page);
        WaitUntilSearchEnabled(page, selector, search, timeoutMs);
        search.OpenHistory();
        WaitUntil(
            page,
            selector,
            () => search.HistoryItems.Contains(value, StringComparer.Ordinal),
            timeoutMs,
            $"Search control '{search.AutomationId}' did not expose the requested history item.",
            expectedValue: value,
            lastObservedValueFactory: () => $"History: [{string.Join(", ", search.HistoryItems)}]");
        search.ApplySearchFromHistory(value);
        WaitUntil(
            page,
            selector,
            () => string.Equals(search.Text, value, StringComparison.Ordinal) && !search.IsHistoryOpen,
            timeoutMs,
            $"Search control '{search.AutomationId}' did not apply history item.",
            expectedValue: $"Text={value}; IsHistoryOpen=false",
            lastObservedValueFactory: () => $"Text={search.Text}; IsHistoryOpen={search.IsHistoryOpen}");
        return page;
    }

    private static void WaitUntilSearchEnabled<TSelf>(
        TSelf page,
        Expression<Func<TSelf, ISearchControl>> selector,
        ISearchControl search,
        int timeoutMs)
        where TSelf : UiPage
    {
        WaitUntil(
            page,
            selector,
            () => search.IsEnabled,
            timeoutMs,
            $"Search control '{search.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={search.IsEnabled}");
    }
}
