using FlaUI.Core.AutomationElements;
using FlaUI.EasyUse.Waiting;

namespace FlaUI.EasyUse.Extensions;

public static class AutomationElementWaitExtensions
{
    public static bool WaitUntilEnabled(this AutomationElement element, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => element.IsEnabled,
            static value => value,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilClickable(this AutomationElement element, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => element.IsEnabled && !element.IsOffscreen,
            static value => value,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilNameEquals(this AutomationElement element, string expectedText, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => element.Name ?? string.Empty,
            actual => string.Equals(actual, expectedText, StringComparison.Ordinal),
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilNameContains(this AutomationElement element, string expectedPart, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => element.Name ?? string.Empty,
            actual => actual.Contains(expectedPart, StringComparison.Ordinal),
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilTextEquals(this AutomationElement element, string expectedText, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => ReadText(element),
            actual => string.Equals(actual, expectedText, StringComparison.Ordinal),
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilTextContains(this AutomationElement element, string expectedPart, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => ReadText(element),
            actual => actual.Contains(expectedPart, StringComparison.Ordinal),
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilHasItems(this ListBox listBox, int minCount, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => listBox.Items.Length,
            actual => actual >= minCount,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilHasItemContaining(this ListBox listBox, string expectedText, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => listBox.Items.Select(item => item.Text ?? item.Name ?? string.Empty).ToArray(),
            items => items.Any(item => item.Contains(expectedText, StringComparison.Ordinal)),
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilValueEquals(this Slider slider, double expected, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => slider.Value,
            actual => Math.Abs(actual - expected) < 0.0001,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilValueEquals(this Spinner spinner, double expected, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => spinner.Value,
            actual => Math.Abs(actual - expected) < 0.0001,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilAtLeast(this ProgressBar progressBar, double expectedMin, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => progressBar.Value,
            actual => actual >= expectedMin,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilIsToggled(this ToggleButton toggle, bool expected, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => toggle.IsToggled,
            actual => actual == expected,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilIsToggledEquals(this ToggleButton toggle, bool expected, int timeoutMs = 5000)
    {
        return WaitUntilIsToggled(toggle, expected, timeoutMs);
    }

    public static bool WaitUntilIsSelected(this RadioButton radioButton, bool expected, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => radioButton.IsChecked,
            actual => actual == expected,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilIsSelectedEquals(this RadioButton radioButton, bool expected, int timeoutMs = 5000)
    {
        return WaitUntilIsSelected(radioButton, expected, timeoutMs);
    }

    public static bool WaitUntilIsSelected(this TreeItem treeItem, bool expected, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => treeItem.IsSelected,
            actual => actual == expected,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilIsSelectedEquals(this TreeItem treeItem, bool expected, int timeoutMs = 5000)
    {
        return WaitUntilIsSelected(treeItem, expected, timeoutMs);
    }

    public static bool WaitUntilIsSelected(this TabItem tabItem, bool expected, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => tabItem.IsSelected,
            actual => actual == expected,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilIsSelectedEquals(this TabItem tabItem, bool expected, int timeoutMs = 5000)
    {
        return WaitUntilIsSelected(tabItem, expected, timeoutMs);
    }

    public static bool WaitUntilItemSelected(this Tree tree, TreeItem targetItem, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => tree.SelectedTreeItem,
            actual => actual is not null && string.Equals(actual.Text, targetItem.Text, StringComparison.Ordinal),
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilItemPresent(this Tree tree, string itemText, int timeoutMs = 5000)
    {
        var target = itemText.Trim();
        var waitResult = UiWait.TryUntil(
            () => FindTreeItemByText(tree.Items, target),
            found => found is not null,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilHasRowsAtLeast(this DataGridView dataGridView, int minRows, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => dataGridView.Rows?.Length ?? 0,
            actual => actual >= minRows,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilRowsAtLeast(this Grid grid, int minRows, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => grid.Rows?.Length ?? 0,
            actual => actual >= minRows,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilGridRowsAtLeast(this DataGridView dataGridView, int minRows, int timeoutMs = 5000)
    {
        return WaitUntilHasRowsAtLeast(dataGridView, minRows, timeoutMs);
    }

    public static bool WaitUntilGridRowsAtLeast(this Grid grid, int minRows, int timeoutMs = 5000)
    {
        return WaitUntilRowsAtLeast(grid, minRows, timeoutMs);
    }

    public static bool WaitUntilCellContains(this Grid grid, int rowIndex, int columnIndex, string expected, int timeoutMs = 5000)
    {
        var expectedText = expected ?? string.Empty;
        var waitResult = UiWait.TryUntil(
            () => ReadGridCellText(grid, rowIndex, columnIndex),
            actual => string.Equals(actual, expectedText, StringComparison.Ordinal),
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilDateEquals(this DateTimePicker dateTimePicker, DateTime date, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => dateTimePicker.SelectedDate,
            actual => actual.HasValue && actual.Value.Date == date.Date,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    public static bool WaitUntilCalendarDateEquals(this Calendar calendar, DateTime date, int timeoutMs = 5000)
    {
        var waitResult = UiWait.TryUntil(
            () => ReadCalendarDate(calendar),
            actual => actual.HasValue && actual.Value.Date == date.Date,
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    private static TreeItem? FindTreeItemByText(TreeItem[] items, string itemText)
    {
        foreach (var item in items)
        {
            var actual = item.Text ?? item.Name;
            if (string.Equals(actual, itemText, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            var nested = FindTreeItemByText(item.Items, itemText);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static string ReadGridCellText(Grid grid, int rowIndex, int columnIndex)
    {
        var row = grid.GetRowByIndex(rowIndex);
        if (row is null)
        {
            return string.Empty;
        }

        var cells = row.Cells;
        if (cells is null || cells.Length <= columnIndex || columnIndex < 0)
        {
            return string.Empty;
        }

        var target = cells[columnIndex];
        if (target is null)
        {
            return string.Empty;
        }

        var value = target.Value;
        return value ?? string.Empty;
    }

    private static DateTime? ReadCalendarDate(Calendar calendar)
    {
        if (calendar is null)
        {
            return null;
        }

        var selectedDates = calendar.SelectedDates;
        if (selectedDates is null || selectedDates.Length == 0)
        {
            return null;
        }

        return selectedDates[0];
    }

    private static string ReadText(AutomationElement element)
    {
        if (element is TextBox textBox)
        {
            return textBox.Text;
        }

        if (element is Label label)
        {
            return label.Text;
        }

        return element.Name ?? string.Empty;
    }
}