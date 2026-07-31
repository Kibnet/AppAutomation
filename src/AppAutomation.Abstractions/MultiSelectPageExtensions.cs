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
        var control = Resolve(selector, page);
        var expectedItems = ValidateExpectedMultiSelectItems(
            page,
            selector,
            values,
            control,
            timeoutMs,
            nameof(SelectMultiItems));

        WaitUntil(
            page,
            selector,
            () => control.IsEnabled,
            timeoutMs,
            $"Multi-select popup '{control.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={control.IsEnabled}",
            operationName: nameof(SelectMultiItems));

        try
        {
            control.Open();
        }
        catch (Exception ex) when (ex is not UiOperationException and not OperationCanceledException)
        {
            throw CreateMultiSelectException(
                page,
                selector,
                expectedItems,
                control,
                timeoutMs,
                "failed to open",
                ex,
                nameof(SelectMultiItems));
        }

        WaitUntil(
            page,
            selector,
            () => control.IsOpen,
            timeoutMs,
            $"Multi-select popup '{control.AutomationId}' did not open.",
            expectedValue: "IsOpen=true",
            lastObservedValueFactory: () => DescribeMultiSelect(control),
            operationName: nameof(SelectMultiItems));

        try
        {
            control.SetSelectedItems(expectedItems);
        }
        catch (Exception ex) when (ex is not UiOperationException and not OperationCanceledException)
        {
            throw CreateMultiSelectException(
                page,
                selector,
                expectedItems,
                control,
                timeoutMs,
                "failed to select the requested items",
                ex,
                nameof(SelectMultiItems));
        }

        WaitUntil(
            page,
            selector,
            () => MultiSelectSetsEqual(control.SelectedItems, expectedItems),
            timeoutMs,
            $"Multi-select popup '{control.AutomationId}' did not reach the requested pending selection.",
            expectedValue: FormatMultiSelectItems(expectedItems),
            lastObservedValueFactory: () => DescribeMultiSelect(control),
            operationName: nameof(SelectMultiItems));

        try
        {
            control.Apply();
        }
        catch (Exception ex) when (ex is not UiOperationException and not OperationCanceledException)
        {
            throw CreateMultiSelectException(
                page,
                selector,
                expectedItems,
                control,
                timeoutMs,
                "failed to apply the requested items",
                ex,
                nameof(SelectMultiItems));
        }

        WaitUntil(
            page,
            selector,
            () => !control.IsOpen,
            timeoutMs,
            $"Multi-select popup '{control.AutomationId}' did not close after Apply.",
            expectedValue: "IsOpen=false",
            lastObservedValueFactory: () => DescribeMultiSelect(control),
            operationName: nameof(SelectMultiItems));

        return WaitUntilSelectedItemsEqual(page, selector, expectedItems, timeoutMs);
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
        var control = Resolve(selector, page);
        var expectedItems = ValidateExpectedMultiSelectItems(
            page,
            selector,
            values,
            control,
            timeoutMs,
            nameof(CancelMultiSelection));

        WaitUntil(
            page,
            selector,
            () => control.IsEnabled,
            timeoutMs,
            $"Multi-select popup '{control.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={control.IsEnabled}",
            operationName: nameof(CancelMultiSelection));

        try
        {
            control.Open();
        }
        catch (Exception ex) when (ex is not UiOperationException and not OperationCanceledException)
        {
            throw CreateMultiSelectException(
                page,
                selector,
                expectedItems,
                control,
                timeoutMs,
                "failed to open",
                ex,
                nameof(CancelMultiSelection));
        }

        WaitUntil(
            page,
            selector,
            () => control.IsOpen,
            timeoutMs,
            $"Multi-select popup '{control.AutomationId}' did not open.",
            expectedValue: "IsOpen=true",
            lastObservedValueFactory: () => DescribeMultiSelect(control),
            operationName: nameof(CancelMultiSelection));

        var committedItems = control.SelectedItems
            .Select(NormalizeMultiSelectItem)
            .ToArray();

        try
        {
            control.SetSelectedItems(expectedItems);
        }
        catch (Exception ex) when (ex is not UiOperationException and not OperationCanceledException)
        {
            throw CreateMultiSelectException(
                page,
                selector,
                expectedItems,
                control,
                timeoutMs,
                "failed to select the requested pending items",
                ex,
                nameof(CancelMultiSelection));
        }

        WaitUntil(
            page,
            selector,
            () => MultiSelectSetsEqual(control.SelectedItems, expectedItems),
            timeoutMs,
            $"Multi-select popup '{control.AutomationId}' did not reach the requested pending selection.",
            expectedValue: FormatMultiSelectItems(expectedItems),
            lastObservedValueFactory: () => DescribeMultiSelect(control),
            operationName: nameof(CancelMultiSelection));

        try
        {
            control.Cancel();
        }
        catch (Exception ex) when (ex is not UiOperationException and not OperationCanceledException)
        {
            throw CreateMultiSelectException(
                page,
                selector,
                expectedItems,
                control,
                timeoutMs,
                "failed to cancel",
                ex,
                nameof(CancelMultiSelection));
        }

        WaitUntil(
            page,
            selector,
            () => !control.IsOpen,
            timeoutMs,
            $"Multi-select popup '{control.AutomationId}' did not close after Cancel.",
            expectedValue: "IsOpen=false",
            lastObservedValueFactory: () => DescribeMultiSelect(control),
            operationName: nameof(CancelMultiSelection));

        return WaitUntilSelectedItemsEqual(page, selector, committedItems, timeoutMs);
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
        var control = Resolve(selector, page);
        var expectedItems = ValidateExpectedMultiSelectItems(
            page,
            selector,
            values,
            control,
            timeoutMs,
            nameof(WaitUntilSelectedItemsEqual));

        WaitUntil(
            page,
            selector,
            () => MultiSelectSetsEqual(control.SelectedItems, expectedItems),
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
        string operationName)
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
                operationName);
        }
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

    private static string NormalizeMultiSelectItem(string? value) => value?.Trim() ?? string.Empty;

    private static string FormatMultiSelectItems(IEnumerable<string> values)
    {
        return $"[{string.Join(", ", values.Select(static value => $"'{value}'"))}]";
    }

    private static string DescribeMultiSelect(IMultiSelectControl control)
    {
        return $"IsOpen={control.IsOpen}; Available={FormatMultiSelectItems(control.Items)}; "
            + $"Selected={FormatMultiSelectItems(control.SelectedItems)}";
    }

    private static UiOperationException CreateMultiSelectException<TSelf>(
        TSelf page,
        Expression<Func<TSelf, IMultiSelectControl>> selector,
        IReadOnlyCollection<string> expectedItems,
        IMultiSelectControl control,
        int timeoutMs,
        string failure,
        Exception exception,
        string operationName)
        where TSelf : UiPage
    {
        var now = DateTimeOffset.UtcNow;
        return CreateUiOperationException(
            page,
            selector,
            TimeSpan.FromMilliseconds(timeoutMs),
            now,
            $"Multi-select popup '{control.AutomationId}' {failure}.",
            FormatMultiSelectItems(expectedItems),
            () => DescribeMultiSelect(control),
            operationName,
            exception);
    }
}
