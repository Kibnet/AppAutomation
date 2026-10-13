using System.Linq.Expressions;

namespace AppAutomation.Abstractions;

public static partial class UiPageExtensions
{
    /// <summary>
    /// Opens a multi-select popup, selects the exact requested item set, applies it, and waits for closure.
    /// </summary>
    public static TSelf SelectMultiItems<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IMultiSelectControl>> selector,
        IReadOnlyCollection<string> values,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(values);
        return ExecuteMultiSelectSelection(page, selector, values, timeoutMs, cancel: false);
    }

    /// <summary>
    /// Opens a multi-select popup, selects the requested pending item set, cancels it, and verifies that the committed selection is preserved.
    /// </summary>
    public static TSelf CancelMultiSelection<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IMultiSelectControl>> selector,
        IReadOnlyCollection<string> values,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(values);
        return ExecuteMultiSelectSelection(page, selector, values, timeoutMs, cancel: true);
    }

    private static TSelf ExecuteMultiSelectSelection<TSelf>(
        TSelf page,
        Expression<Func<TSelf, IMultiSelectControl>> selector,
        IReadOnlyCollection<string> values,
        int timeoutMs,
        bool cancel)
        where TSelf : UiPage
    {
        var operationName = cancel ? nameof(CancelMultiSelection) : nameof(SelectMultiItems);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var control = Resolve(selector, page);
        var expectedItems = ValidateExpectedMultiSelectItems(
            page,
            selector,
            values,
            control,
            timeoutMs,
            operationName,
            startedAtUtc);

        WaitUntil(
            page,
            selector,
            () => control.IsEnabled,
            timeoutMs,
            $"Multi-select popup '{control.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={control.IsEnabled}",
            operationName: operationName);

        ExecuteMultiSelectAction(
            page,
            selector,
            expectedItems,
            control,
            timeoutMs,
            startedAtUtc,
            control.Open,
            "failed to open",
            operationName);

        WaitUntil(
            page,
            selector,
            () => ReadMultiSelectState(() => control.IsOpen),
            timeoutMs,
            $"Multi-select popup '{control.AutomationId}' did not open.",
            expectedValue: "IsOpen=true",
            lastObservedValueFactory: () => DescribeMultiSelect(control),
            operationName: operationName);

        IReadOnlyList<string> committedItems = expectedItems;
        if (cancel)
        {
            committedItems = control.SelectedItems.Select(NormalizeMultiSelectItem).ToArray();
            EnsureMultiSelectPopupRemainsOpen(
                page,
                selector,
                expectedItems,
                control,
                timeoutMs,
                operationName,
                startedAtUtc);
        }

        ExecuteMultiSelectAction(
            page,
            selector,
            expectedItems,
            control,
            timeoutMs,
            startedAtUtc,
            () => control.SetSelectedItems(expectedItems),
            cancel ? "failed to select the requested pending items" : "failed to select the requested items",
            operationName);

        WaitUntil(
            page,
            selector,
            () => ReadMultiSelectState(() => MultiSelectSetsEqual(control.SelectedItems, expectedItems)),
            timeoutMs,
            $"Multi-select popup '{control.AutomationId}' did not reach the requested pending selection.",
            expectedValue: FormatMultiSelectItems(expectedItems),
            lastObservedValueFactory: () => DescribeMultiSelect(control),
            operationName: operationName);

        ExecuteMultiSelectAction(
            page,
            selector,
            expectedItems,
            control,
            timeoutMs,
            startedAtUtc,
            cancel ? control.Cancel : control.Apply,
            cancel ? "failed to cancel" : "failed to apply the requested items",
            operationName);

        WaitUntil(
            page,
            selector,
            () => ReadMultiSelectState(() => !control.IsOpen),
            timeoutMs,
            $"Multi-select popup '{control.AutomationId}' did not close after {(cancel ? "Cancel" : "Apply")}.",
            expectedValue: "IsOpen=false",
            lastObservedValueFactory: () => DescribeMultiSelect(control),
            operationName: operationName);

        if (!cancel && IsConfirmedByClosedFilterDisplay(control, expectedItems))
        {
            return page;
        }

        return WaitUntilSelectedItemsEqual(page, selector, committedItems, timeoutMs);
    }

    private static bool IsConfirmedByClosedFilterDisplay(
        IMultiSelectControl control,
        IReadOnlyList<string> expectedItems)
    {
        if (expectedItems.Count == 0
            || control is not IComboBoxFilterControl
            || control is not IReadableTextControl display)
        {
            return false;
        }

        try
        {
            var displayedText = display.Text?.Trim();
            if (expectedItems.Count == 1)
            {
                return string.Equals(displayedText, expectedItems[0], StringComparison.OrdinalIgnoreCase);
            }

            char[] separators = [',', ';'];
            var availableItems = control.Items;
            if (availableItems.Count == 0
                || availableItems.Any(item => item is null || item.IndexOfAny(separators) >= 0))
            {
                return false;
            }

            var displayedItems = (displayedText ?? string.Empty)
                .Split(separators, StringSplitOptions.TrimEntries);
            return MultiSelectSetsEqual(displayedItems, expectedItems);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Waits until a multi-select control exposes exactly the requested committed item set.
    /// </summary>
    public static TSelf WaitUntilSelectedItemsEqual<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IMultiSelectControl>> selector,
        IReadOnlyCollection<string> values,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(values);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var control = Resolve(selector, page);
        var expectedItems = ValidateExpectedMultiSelectItems(
            page,
            selector,
            values,
            control,
            timeoutMs,
            nameof(WaitUntilSelectedItemsEqual),
            startedAtUtc);

        if (control is IMultiSelectCommittedStateControl committedState && !control.IsOpen)
        {
            ExecuteMultiSelectAction(page, selector, expectedItems, control, timeoutMs, startedAtUtc,
                () => committedState.RefreshCommittedItems(),
                "failed to refresh its committed selection", nameof(WaitUntilSelectedItemsEqual));
        }

        WaitUntil(
            page,
            selector,
            () => ReadMultiSelectState(() => MultiSelectSetsEqual(control.SelectedItems, expectedItems)),
            timeoutMs,
            $"Multi-select popup '{control.AutomationId}' did not expose the expected committed selection.",
            expectedValue: FormatMultiSelectItems(expectedItems),
            lastObservedValueFactory: () => DescribeMultiSelect(control),
            operationName: nameof(WaitUntilSelectedItemsEqual));

        return page;
    }

    private static string[] ValidateExpectedMultiSelectItems<TSelf>(
        TSelf page,
        Expression<Func<TSelf, IMultiSelectControl>> selector,
        IReadOnlyCollection<string> values,
        IMultiSelectControl control,
        int timeoutMs,
        string operationName,
        DateTimeOffset startedAtUtc)
        where TSelf : UiPage
    {
        try
        {
            var normalized = values.Select(NormalizeMultiSelectItem).ToArray();
            if (normalized.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("Multi-select item text cannot be empty.", nameof(values));
            }

            if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
            {
                throw new ArgumentException("Multi-select item text must be distinct.", nameof(values));
            }

            return normalized;
        }
        catch (ArgumentException ex)
        {
            throw CreateMultiSelectException(
                page,
                selector,
                values.Select(NormalizeMultiSelectItem).ToArray(),
                control,
                timeoutMs,
                "received an invalid requested item set",
                ex,
                operationName,
                startedAtUtc);
        }
    }

    private static void ExecuteMultiSelectAction<TSelf>(
        TSelf page,
        Expression<Func<TSelf, IMultiSelectControl>> selector,
        IReadOnlyCollection<string> expectedItems,
        IMultiSelectControl control,
        int timeoutMs,
        DateTimeOffset startedAtUtc,
        Action action,
        string failure,
        string operationName)
        where TSelf : UiPage
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is not UiOperationException and not OperationCanceledException)
        {
            throw CreateMultiSelectException(
                page,
                selector,
                expectedItems,
                control,
                timeoutMs,
                failure,
                ex,
                operationName,
                startedAtUtc);
        }
    }

    private static void EnsureMultiSelectPopupRemainsOpen<TSelf>(
        TSelf page,
        Expression<Func<TSelf, IMultiSelectControl>> selector,
        IReadOnlyCollection<string> expectedItems,
        IMultiSelectControl control,
        int timeoutMs,
        string operationName,
        DateTimeOffset startedAtUtc)
        where TSelf : UiPage
    {
        if (control.IsOpen)
        {
            return;
        }

        ExecuteMultiSelectAction(
            page,
            selector,
            expectedItems,
            control,
            timeoutMs,
            startedAtUtc,
            control.Open,
            "closed while reading the committed selection and failed to reopen",
            operationName);

        WaitUntil(
            page,
            selector,
            () => ReadMultiSelectState(() => control.IsOpen),
            timeoutMs,
            $"Multi-select popup '{control.AutomationId}' did not reopen after reading the committed selection.",
            expectedValue: "IsOpen=true",
            lastObservedValueFactory: () => DescribeMultiSelect(control),
            operationName: operationName);
    }

    private static bool MultiSelectSetsEqual(
        IEnumerable<string> actualItems,
        IEnumerable<string> expectedItems)
    {
        var actual = actualItems
            .Select(NormalizeMultiSelectItem)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var expected = expectedItems
            .Select(NormalizeMultiSelectItem)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return actual.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase);
    }

    private static bool ReadMultiSelectState(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (IsRetryableResolveFailure(exception))
        {
            return false;
        }
    }

    private static string NormalizeMultiSelectItem(string? value) => value?.Trim() ?? string.Empty;

    private static string FormatMultiSelectItems(IEnumerable<string> values)
    {
        return $"[{string.Join(", ", values.Select(static value => $"'{value}'"))}]";
    }

    private static string DescribeMultiSelect(IMultiSelectControl control)
    {
        var selected = !control.IsOpen
            && control is IMultiSelectCommittedStateControl committedState
            && !committedState.TryGetCommittedItems(out _)
                ? "<unobserved>"
                : FormatMultiSelectItems(control.SelectedItems);
        return $"IsOpen={control.IsOpen}; Available={FormatMultiSelectItems(control.Items)}; "
            + $"Selected={selected}";
    }

    private static UiOperationException CreateMultiSelectException<TSelf>(
        TSelf page,
        Expression<Func<TSelf, IMultiSelectControl>> selector,
        IReadOnlyCollection<string> expectedItems,
        IMultiSelectControl control,
        int timeoutMs,
        string failure,
        Exception exception,
        string operationName,
        DateTimeOffset startedAtUtc)
        where TSelf : UiPage
    {
        return CreateUiOperationException(
            page,
            selector,
            TimeSpan.FromMilliseconds(timeoutMs),
            startedAtUtc,
            $"Multi-select popup '{control.AutomationId}' {failure}.",
            FormatMultiSelectItems(expectedItems),
            () => DescribeMultiSelect(control),
            operationName,
            exception);
    }
}
