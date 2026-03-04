using System.Globalization;
using System.Linq.Expressions;
using FlaUICalendar = FlaUI.Core.AutomationElements.Calendar;
using FlaUI.Core.AutomationElements;
using FlaUI.EasyUse.PageObjects;

namespace FlaUI.EasyUse.Extensions;

public static class UiPageExtensions
{
    public static TSelf ClickElement<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var element = Resolve(selector, page);
        if (!element.WaitUntilClickable(timeoutMs))
        {
            throw new TimeoutException($"Element [{element.AutomationId}] is not clickable.");
        }

        element.Click();
        return page;
    }

    public static TSelf EnterText<TSelf>(this TSelf page, Expression<Func<TSelf, TextBox>> selector, string value, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var textBox = Resolve(selector, page);
        textBox.EnterText(value, timeoutMs);
        return page;
    }

    public static TSelf EnterText<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, string value, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var element = Resolve(selector, page);
        if (!element.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Element [{element.AutomationId}] is not enabled.");
        }

        if (element is TextBox textBox)
        {
            textBox.EnterText(value, timeoutMs);
            return page;
        }

        var asTextBox = element.AsTextBox();
        asTextBox.EnterText(value, timeoutMs);
        return page;
    }

    public static TSelf ClickButton<TSelf>(this TSelf page, Expression<Func<TSelf, Button>> selector, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var button = Resolve(selector, page);
        button.ClickButton(timeoutMs);
        return page;
    }

    public static TSelf SetChecked<TSelf>(this TSelf page, Expression<Func<TSelf, CheckBox>> selector, bool isChecked, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var checkBox = Resolve(selector, page);
        if (!checkBox.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"CheckBox [{checkBox.AutomationId}] is not clickable.");
        }

        if (checkBox.IsChecked != isChecked)
        {
            checkBox.IsChecked = isChecked;
        }

        return page;
    }

    public static TSelf SetChecked<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, bool isChecked, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var element = Resolve(selector, page);
        if (!element.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Element [{element.AutomationId}] is not enabled.");
        }

        if (element is CheckBox checkBox)
        {
            if (checkBox.IsChecked != isChecked)
            {
                checkBox.IsChecked = isChecked;
            }

            return page;
        }

        if (element is RadioButton radioButton)
        {
            if (radioButton.IsChecked != isChecked)
            {
                radioButton.IsChecked = isChecked;
            }

            return page;
        }

        if (element is ToggleButton toggle)
        {
            if (toggle.IsToggled != isChecked)
            {
                toggle.Toggle();
            }

            return page;
        }

        throw new TimeoutException($"Element [{element.AutomationId}] is not checkable.");
    }

    public static TSelf SetChecked<TSelf>(this TSelf page, Expression<Func<TSelf, RadioButton>> selector, bool isChecked, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var radioButton = Resolve(selector, page);
        if (!radioButton.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"RadioButton [{radioButton.AutomationId}] is not enabled.");
        }

        if (radioButton.IsChecked != isChecked)
        {
            radioButton.IsChecked = isChecked;
        }

        return page;
    }

    public static TSelf SetToggled<TSelf>(this TSelf page, Expression<Func<TSelf, ToggleButton>> selector, bool isToggled, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var toggle = Resolve(selector, page);
        if (!toggle.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"ToggleButton [{toggle.AutomationId}] is not enabled.");
        }

        if (toggle.IsToggled != isToggled)
        {
            toggle.Toggle();
        }

        return page;
    }

    public static TSelf SelectComboItem<TSelf>(this TSelf page, Expression<Func<TSelf, ComboBox>> selector, string itemText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var combo = Resolve(selector, page);
        if (!combo.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"ComboBox [{combo.AutomationId}] is not enabled.");
        }

        if (!TrySelectComboItem(combo, itemText, timeoutMs))
        {
            throw new InvalidOperationException($"ComboBox item '{itemText}' was not found.");
        }

        return page;
    }

    public static TSelf SelectComboItem<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, string itemText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var comboElement = Resolve(selector, page);
        var combo = comboElement.AsComboBox();

        if (!combo.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"ComboBox [{combo.AutomationId}] is not enabled.");
        }

        if (!TrySelectComboItem(combo, itemText, timeoutMs))
        {
            throw new InvalidOperationException($"ComboBox item '{itemText}' was not found.");
        }

        return page;
    }

    public static TSelf SetSliderValue<TSelf>(this TSelf page, Expression<Func<TSelf, Slider>> selector, double value, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var slider = Resolve(selector, page);
        if (!slider.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Slider [{slider.AutomationId}] is not enabled.");
        }

        slider.Value = value;
        if (!slider.WaitUntilValueEquals(value, timeoutMs))
        {
            throw new TimeoutException($"Slider [{slider.AutomationId}] value '{value}' was not reached.");
        }

        return page;
    }

    public static TSelf SetSpinnerValue<TSelf>(this TSelf page, Expression<Func<TSelf, Spinner>> selector, double value, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var spinner = Resolve(selector, page);
        if (!spinner.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Spinner [{spinner.AutomationId}] is not enabled.");
        }

        spinner.Value = value;
        if (!spinner.WaitUntilValueEquals(value, timeoutMs))
        {
            throw new TimeoutException($"Spinner [{spinner.AutomationId}] value '{value}' was not reached.");
        }

        return page;
    }

    public static TSelf SetSpinnerValue<TSelf>(this TSelf page, Expression<Func<TSelf, TextBox>> selector, double value, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var textBox = Resolve(selector, page);
        if (!textBox.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Spinner-like textbox [{textBox.AutomationId}] is not enabled.");
        }

        textBox.Text = value.ToString(CultureInfo.InvariantCulture);
        if (!WaitUntilSpinnerTextEquals(textBox, value, timeoutMs))
        {
            throw new TimeoutException($"Spinner-like textbox [{textBox.AutomationId}] value '{value}' was not reached.");
        }

        return page;
    }

    public static TSelf SelectTabItem<TSelf>(this TSelf page, Expression<Func<TSelf, Tab>> selector, string itemText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var tab = Resolve(selector, page);
        if (!tab.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Tab control [{tab.AutomationId}] is not enabled.");
        }

        tab.SelectTabItem(itemText);

        return page;
    }

    public static TSelf SelectTreeItem<TSelf>(this TSelf page, Expression<Func<TSelf, Tree>> selector, string itemText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var tree = Resolve(selector, page);
        if (!tree.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Tree [{tree.AutomationId}] is not enabled.");
        }

        var target = FindTreeItemByText(tree.Items, itemText);
        if (target is null)
        {
            throw new InvalidOperationException($"Tree item '{itemText}' was not found.");
        }

        target.Select();
        if (!target.WaitUntilIsSelected(true, timeoutMs))
        {
            throw new TimeoutException($"Tree item '{itemText}' was not selected.");
        }

        return page;
    }

    public static TSelf SetDate<TSelf>(this TSelf page, Expression<Func<TSelf, DateTimePicker>> selector, DateTime date, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var datePicker = Resolve(selector, page);
        if (!datePicker.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"DateTimePicker [{datePicker.AutomationId}] is not enabled.");
        }

        datePicker.SelectedDate = date;
        if (!datePicker.WaitUntilDateEquals(date, timeoutMs))
        {
            throw new TimeoutException($"DateTimePicker [{datePicker.AutomationId}] value '{date:d}' was not reached.");
        }

        return page;
    }

    public static TSelf SetDate<TSelf>(this TSelf page, Expression<Func<TSelf, FlaUICalendar>> selector, DateTime date, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var calendar = Resolve(selector, page);
        if (!calendar.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Calendar [{calendar.AutomationId}] is not enabled.");
        }

        calendar.SelectDate(date);
        if (!calendar.WaitUntilCalendarDateEquals(date, timeoutMs))
        {
            throw new TimeoutException($"Calendar [{calendar.AutomationId}] value '{date:d}' was not reached.");
        }

        return page;
    }

    public static TSelf WaitUntilValueEquals<TSelf>(this TSelf page, Expression<Func<TSelf, Slider>> selector, double expected, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var slider = Resolve(selector, page);
        if (!slider.WaitUntilValueEquals(expected, timeoutMs))
        {
            throw new TimeoutException($"Slider did not reach value '{expected}'.");
        }

        return page;
    }

    public static TSelf WaitUntilValueEquals<TSelf>(this TSelf page, Expression<Func<TSelf, Spinner>> selector, double expected, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var spinner = Resolve(selector, page);
        if (!spinner.WaitUntilValueEquals(expected, timeoutMs))
        {
            throw new TimeoutException($"Spinner did not reach value '{expected}'.");
        }

        return page;
    }

    public static TSelf WaitUntilValueEquals<TSelf>(this TSelf page, Expression<Func<TSelf, TextBox>> selector, double expected, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var textBox = Resolve(selector, page);
        if (!WaitUntilSpinnerTextEquals(textBox, expected, timeoutMs))
        {
            throw new TimeoutException($"Text box value '{expected}' was not reached.");
        }

        return page;
    }

    public static TSelf WaitUntilProgressAtLeast<TSelf>(this TSelf page, Expression<Func<TSelf, ProgressBar>> selector, double expectedMin, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var progressBar = Resolve(selector, page);
        if (!progressBar.WaitUntilAtLeast(expectedMin, timeoutMs))
        {
            throw new TimeoutException($"ProgressBar did not reach minimum value '{expectedMin}'.");
        }

        return page;
    }

    public static TSelf WaitUntilIsSelected<TSelf>(this TSelf page, Expression<Func<TSelf, TabItem>> selector, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var tabItem = Resolve(selector, page);
        if (!tabItem.WaitUntilIsSelected(true, timeoutMs))
        {
            throw new TimeoutException("TabItem was not selected.");
        }

        return page;
    }

    public static TSelf WaitUntilIsSelected<TSelf>(this TSelf page, Expression<Func<TSelf, TreeItem>> selector, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var treeItem = Resolve(selector, page);
        if (!treeItem.WaitUntilIsSelected(true, timeoutMs))
        {
            throw new TimeoutException("TreeItem was not selected.");
        }

        return page;
    }

    public static TSelf WaitUntilIsSelected<TSelf>(this TSelf page, Expression<Func<TSelf, RadioButton>> selector, bool expected, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var radioButton = Resolve(selector, page);
        if (!radioButton.WaitUntilIsSelected(expected, timeoutMs))
        {
            throw new TimeoutException("RadioButton was not selected as expected.");
        }

        return page;
    }

    public static TSelf WaitUntilIsToggled<TSelf>(this TSelf page, Expression<Func<TSelf, ToggleButton>> selector, bool expected, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var toggle = Resolve(selector, page);
        if (!toggle.WaitUntilIsToggled(expected, timeoutMs))
        {
            throw new TimeoutException($"Toggle did not become '{expected}'.");
        }

        return page;
    }

    public static TSelf WaitUntilHasItem<TSelf>(this TSelf page, Expression<Func<TSelf, Tree>> selector, string expectedItemText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var tree = Resolve(selector, page);
        if (!tree.WaitUntilItemPresent(expectedItemText, timeoutMs))
        {
            throw new TimeoutException($"Tree did not contain '{expectedItemText}'.");
        }

        return page;
    }

    public static TSelf WaitUntilHasRowsAtLeast<TSelf>(this TSelf page, Expression<Func<TSelf, DataGridView>> selector, int minRows, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var grid = Resolve(selector, page);
        if (!grid.WaitUntilHasRowsAtLeast(minRows, timeoutMs))
        {
            throw new TimeoutException($"Grid did not contain at least {minRows} rows.");
        }

        return page;
    }

    public static TSelf WaitUntilHasRowsAtLeast<TSelf>(this TSelf page, Expression<Func<TSelf, Grid>> selector, int minRows, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var grid = Resolve(selector, page);
        if (!grid.WaitUntilRowsAtLeast(minRows, timeoutMs))
        {
            throw new TimeoutException($"Grid did not contain at least {minRows} rows.");
        }

        return page;
    }

    public static TSelf WaitUntilDateEquals<TSelf>(this TSelf page, Expression<Func<TSelf, FlaUICalendar>> selector, DateTime date, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var calendar = Resolve(selector, page);
        if (!calendar.WaitUntilCalendarDateEquals(date, timeoutMs))
        {
            throw new TimeoutException($"Calendar did not reach date '{date:d}'.");
        }

        return page;
    }

    public static TSelf WaitUntilListBoxContains<TSelf>(this TSelf page, Expression<Func<TSelf, ListBox>> selector, string expectedText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var listBox = Resolve(selector, page);
        if (!listBox.WaitUntilHasItemContaining(expectedText, timeoutMs))
        {
            throw new TimeoutException($"ListBox did not contain '{expectedText}'.");
        }

        return page;
    }

    public static TSelf WaitUntilNameEquals<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, string expectedText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var element = Resolve(selector, page);
        if (!element.WaitUntilNameEquals(expectedText, timeoutMs))
        {
            throw new TimeoutException($"Name did not become '{expectedText}'.");
        }

        return page;
    }

    public static TSelf WaitUntilNameContains<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, string expectedPart, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var element = Resolve(selector, page);
        if (!element.WaitUntilNameContains(expectedPart, timeoutMs))
        {
            throw new TimeoutException($"Name did not contain '{expectedPart}'.");
        }

        return page;
    }

    public static TSelf WaitUntilHasItemsAtLeast<TSelf>(this TSelf page, Expression<Func<TSelf, ListBox>> selector, int minCount, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var list = Resolve(selector, page);
        if (!list.WaitUntilHasItems(minCount, timeoutMs))
        {
            throw new TimeoutException($"List did not reach {minCount} items.");
        }

        return page;
    }

    public static TSelf WaitUntilTextEquals<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, string expectedText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var element = Resolve(selector, page);
        if (!element.WaitUntilTextEquals(expectedText, timeoutMs))
        {
            throw new TimeoutException($"Text did not become '{expectedText}'.");
        }

        return page;
    }

    public static TSelf WaitUntilTextContains<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, string expectedPart, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var element = Resolve(selector, page);
        if (!element.WaitUntilTextContains(expectedPart, timeoutMs))
        {
            throw new TimeoutException($"Text did not contain '{expectedPart}'.");
        }

        return page;
    }

    private static TElement Resolve<TSelf, TElement>(Expression<Func<TSelf, TElement>> selector, TSelf page)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(page);

        return selector.Compile()(page);
    }

    private static bool TrySelectComboItem(ComboBox combo, string itemText, int timeoutMs)
    {
        if (itemText is null)
        {
            throw new ArgumentNullException(nameof(itemText));
        }

        var target = itemText.Trim();
        var items = combo.Items;
        for (var index = 0; index < items.Length; index++)
        {
            var actual = items[index].Text.Trim();
            if (!string.Equals(actual, target, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            combo.Select(index);
            return FlaUI.EasyUse.Waiting.UiWait.TryUntil(
                () => combo.SelectedIndex,
                actualIndex => actualIndex == index,
                new FlaUI.EasyUse.Waiting.UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) }).Success;
        }

        return false;
    }

    private static TreeItem? FindTreeItemByText(IEnumerable<TreeItem> items, string targetText)
    {
        foreach (var item in items)
        {
            if (string.Equals(item.Text, targetText, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Name, targetText, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            var nested = FindTreeItemByText(item.Items, targetText);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool WaitUntilSpinnerTextEquals(TextBox textBox, double expected, int timeoutMs)
    {
        var expectedText = expected.ToString(CultureInfo.InvariantCulture);
        var waitResult = FlaUI.EasyUse.Waiting.UiWait.TryUntil(
            () => textBox.Text,
            actual => string.Equals(actual?.Trim(), expectedText, StringComparison.Ordinal),
            new FlaUI.EasyUse.Waiting.UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }
}
