using System.Linq.Expressions;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.EasyUse.PageObjects;

namespace FlaUI.EasyUse.Extensions;

public static class UiPageExtensions
{
    public static TSelf EnterText<TSelf>(this TSelf page, Expression<Func<TSelf, TextBox>> selector, string value, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var textBox = Resolve(selector, page);
        textBox.EnterText(value, timeoutMs);
        return page;
    }

    public static TSelf ClickButton<TSelf>(this TSelf page, Expression<Func<TSelf, Button>> selector, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var button = Resolve(selector, page);
        button.ClickButton(timeoutMs);
        return page;
    }

    public static TSelf SetChecked<TSelf>(this TSelf page, Expression<Func<TSelf, CheckBox>> selector, bool isChecked, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var checkBox = Resolve(selector, page);
        if (!checkBox.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"CheckBox [{checkBox.AutomationId}] is not clickable.");
        }

        if (checkBox.IsChecked is not true && isChecked)
        {
            checkBox.IsChecked = true;
        }
        else if (checkBox.IsChecked is true && !isChecked)
        {
            checkBox.IsChecked = false;
        }

        return page;
    }

    public static TSelf SelectComboItem<TSelf>(this TSelf page, Expression<Func<TSelf, ComboBox>> selector, string itemText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);
        if (itemText is null)
        {
            throw new ArgumentNullException(nameof(itemText));
        }

        var combo = Resolve(selector, page);
        if (!combo.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"ComboBox [{combo.AutomationId}] is not enabled.");
        }

        combo.Select(itemText);

        var target = itemText?.Trim();
        var selectedItemText = combo.SelectedItem?.Text?.Trim();
        var selectedItemName = combo.SelectedItem?.Name?.Trim();
        if (!string.Equals(selectedItemText, target, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(selectedItemName, target, StringComparison.OrdinalIgnoreCase))
        {
            var itemIndex = Array.FindIndex(
                combo.Items,
                item => string.Equals(item.Text?.Trim(), target, StringComparison.OrdinalIgnoreCase));
            if (itemIndex < 0)
            {
                itemIndex = Array.FindIndex(
                    combo.Items,
                    item => string.Equals(item.Name?.Trim(), target, StringComparison.OrdinalIgnoreCase));
            }

            if (itemIndex < 0)
            {
                itemIndex = Array.FindIndex(
                    combo.Items,
                    item => (item.Text?.Trim().Contains(target ?? string.Empty, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (item.Name?.Trim().Contains(target ?? string.Empty, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            if (itemIndex < 0)
            {
                throw new InvalidOperationException($"ComboBox item '{itemText}' was not found.");
            }

            combo.Select(itemIndex);
        }

        return page;
    }

    public static TSelf WaitUntilListBoxContains<TSelf>(this TSelf page, Expression<Func<TSelf, ListBox>> selector, string expectedText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var listBox = Resolve(selector, page);
        if (!listBox.WaitUntilHasItemContaining(expectedText, timeoutMs))
        {
            throw new TimeoutException($"ListBox did not contain an item with text '{expectedText}'.");
        }

        return page;
    }

    public static TSelf WaitUntilNameEquals<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, string expectedText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var element = Resolve(selector, page);
        if (!element.WaitUntilNameEquals(expectedText, timeoutMs))
        {
            throw new TimeoutException($"Element [{expectedText}] was not reached.");
        }

        return page;
    }

    public static TSelf WaitUntilNameContains<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, string expectedPart, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var element = Resolve(selector, page);
        if (!element.WaitUntilNameContains(expectedPart, timeoutMs))
        {
            throw new TimeoutException($"Element did not contain '{expectedPart}'.");
        }

        return page;
    }

    public static TSelf WaitUntilHasItemsAtLeast<TSelf>(this TSelf page, Expression<Func<TSelf, ListBox>> selector, int minCount, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var listBox = Resolve(selector, page);
        if (!listBox.WaitUntilHasItems(minCount, timeoutMs))
        {
            throw new TimeoutException($"ListBox did not contain at least {minCount} items.");
        }

        return page;
    }

    private static T Resolve<TSelf, T>(Expression<Func<TSelf, T>> selector, TSelf page)
        where TSelf : UiPage
    {
        var control = selector.Compile().Invoke(page);
        if (control is null)
        {
            throw new InvalidOperationException("Selector returned null.");
        }

        return control;
    }
}
