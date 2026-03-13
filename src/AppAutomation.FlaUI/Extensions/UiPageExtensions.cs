using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using UiWait = AppAutomation.Abstractions.UiWait;
using UiWaitOptions = AppAutomation.Abstractions.UiWaitOptions;
using FlaUICalendar = FlaUI.Core.AutomationElements.Calendar;
using FlaUI.Core;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.Core.AutomationElements;
using AppAutomation.FlaUI.PageObjects;

namespace AppAutomation.FlaUI.Extensions;

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
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var textBox = Resolve(selector, page);
        textBox.EnterText(value, timeoutMs);
        return page;
    }

    public static TSelf EnterText<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, string value, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var element = Resolve(selector, page);
        if (!element.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Element [{element.AutomationId}] is not enabled.");
        }

        if (!TrySetText(element, value))
        {
            throw new TimeoutException($"Element [{element.AutomationId}] is not writable.");
        }

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

    public static TSelf SetChecked<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, bool isChecked, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var element = Resolve(selector, page);
        if (!element.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Element [{element.AutomationId}] is not enabled.");
        }

        if (!TrySetChecked(element, isChecked))
        {
            throw new TimeoutException($"Element [{element.AutomationId}] is not checkable.");
        }

        return page;
    }

    public static TSelf SetChecked<TSelf>(this TSelf page, Expression<Func<TSelf, RadioButton>> selector, bool isChecked, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var radioButton = Resolve(selector, page);
        if (!radioButton.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"RadioButton [{radioButton.AutomationId}] is not enabled.");
        }

        if (isChecked && radioButton.IsChecked is not true)
        {
            radioButton.IsChecked = true;
        }
        else if (!isChecked && radioButton.IsChecked is true)
        {
            radioButton.IsChecked = false;
        }

        return page;
    }

    public static TSelf SetToggled<TSelf>(this TSelf page, Expression<Func<TSelf, ToggleButton>> selector, bool isToggled, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

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

        if (!TrySelectComboItem(combo, itemText, timeoutMs))
        {
            throw new InvalidOperationException($"ComboBox item '{itemText}' was not found.");
        }

        return page;
    }

    public static TSelf SelectComboItem<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, string itemText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);
        if (itemText is null)
        {
            throw new ArgumentNullException(nameof(itemText));
        }

        var comboElement = Resolve(selector, page);
        var combo = comboElement.AsComboBox();
        if (combo is null)
        {
            throw new InvalidOperationException("Selector does not point to a ComboBox.");
        }

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
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

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
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var spinner = Resolve(selector, page);
        if (!spinner.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Spinner [{spinner.AutomationId}] is not enabled.");
        }

        if (!TrySetSpinnerValue(spinner, value))
        {
            throw new TimeoutException($"Spinner [{spinner.AutomationId}] value '{value}' was not reached.");
        }

        return page;
    }

    public static TSelf SetSpinnerValue<TSelf>(this TSelf page, Expression<Func<TSelf, TextBox>> selector, double value, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var textBox = Resolve(selector, page);
        if (!textBox.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Spinner-like textbox [{textBox.AutomationId}] is not enabled.");
        }

        if (!TrySetSpinnerValue(textBox, value))
        {
            throw new TimeoutException($"Spinner-like textbox [{textBox.AutomationId}] value '{value}' was not reached.");
        }

        return page;
    }

    public static TSelf SelectTabItem<TSelf>(this TSelf page, Expression<Func<TSelf, Tab>> selector, string itemText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);
        if (itemText is null)
        {
            throw new ArgumentNullException(nameof(itemText));
        }

        var tab = Resolve(selector, page);
        if (!tab.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Tab control [{tab.AutomationId}] is not enabled.");
        }

        var targetText = itemText.Trim();
        var matched = FindTabItemByText(tab, targetText, includeDescendants: true);
        if (matched is null)
        {
            try
            {
                tab.SelectTabItem(itemText);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Tab item '{itemText}' was not found.", ex);
            }

            matched = FindTabItemByText(tab, targetText, includeDescendants: true);
            if (matched is null)
            {
                throw new InvalidOperationException($"Tab item '{itemText}' was not found.");
            }
        }

        if (!TrySelectTabItem(tab, matched))
        {
            throw new TimeoutException($"Tab item '{itemText}' could not be selected.");
        }

        if (matched is not null && !matched.WaitUntilIsSelected(true, timeoutMs))
        {
            throw new TimeoutException($"Tab control [{tab.AutomationId}] did not select '{itemText}'.");
        }

        return page;
    }

    public static TSelf SelectTreeItem<TSelf>(this TSelf page, Expression<Func<TSelf, Tree>> selector, string itemText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);
        if (itemText is null)
        {
            throw new ArgumentNullException(nameof(itemText));
        }

        var tree = Resolve(selector, page);
        if (!tree.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Tree [{tree.AutomationId}] is not enabled.");
        }

        var target = FindTreeItemByText(tree, itemText);
        if (target is null)
        {
            throw new InvalidOperationException($"Tree item '{itemText}' was not found.");
        }

        if (!TrySelectTreeItem(target, timeoutMs))
        {
            throw new TimeoutException($"Tree item '{itemText}' could not be selected.");
        }

        if (target is TreeItem treeItem)
        {
            if (!tree.WaitUntilItemSelected(treeItem, timeoutMs))
            {
                throw new TimeoutException($"Tree item '{itemText}' was not selected.");
            }
        }
        else
        {
            if (!WaitUntilElementSelected(target, timeoutMs))
            {
                throw new TimeoutException($"Tree item '{itemText}' was not selected.");
            }
        }

        return page;
    }

    public static TSelf SetDate<TSelf>(this TSelf page, Expression<Func<TSelf, DateTimePicker>> selector, DateTime date, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var datePicker = Resolve(selector, page);
        if (!datePicker.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"DateTimePicker [{datePicker.AutomationId}] is not enabled.");
        }

        if (!TrySetDate(datePicker, date))
        {
            throw new TimeoutException($"DateTimePicker [{datePicker.AutomationId}] value '{date:d}' was not reached.");
        }

        return page;
    }

    public static TSelf SetDate<TSelf>(this TSelf page, Expression<Func<TSelf, FlaUICalendar>> selector, DateTime date, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var calendar = Resolve(selector, page);
        if (!calendar.WaitUntilEnabled(timeoutMs))
        {
            throw new TimeoutException($"Calendar [{calendar.AutomationId}] is not enabled.");
        }

        if (!TrySetCalendarDate(calendar, date))
        {
            throw new TimeoutException($"Calendar [{calendar.AutomationId}] value '{date:d}' was not reached.");
        }

        return page;
    }

    public static TSelf WaitUntilValueEquals<TSelf>(this TSelf page, Expression<Func<TSelf, Slider>> selector, double expected, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

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
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var spinner = Resolve(selector, page);
        if (!WaitUntilSpinnerValueEquals(spinner, expected, timeoutMs))
        {
            throw new TimeoutException($"Spinner did not reach value '{expected}'.");
        }

        return page;
    }

    public static TSelf WaitUntilValueEquals<TSelf>(this TSelf page, Expression<Func<TSelf, TextBox>> selector, double expected, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var textBox = Resolve(selector, page);
        if (!WaitUntilSpinnerValueEquals(textBox, expected, timeoutMs))
        {
            throw new TimeoutException($"Text box value '{expected}' was not reached.");
        }

        return page;
    }

    public static TSelf WaitUntilProgressAtLeast<TSelf>(this TSelf page, Expression<Func<TSelf, ProgressBar>> selector, double expectedMin, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

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
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

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
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

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
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

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
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

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
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

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
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

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
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

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
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var calendar = Resolve(selector, page);
        if (!calendar.WaitUntilCalendarDateEquals(date, timeoutMs))
        {
            throw new TimeoutException($"Calendar did not reach date '{date:yyyy-MM-dd}'.");
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

    public static TSelf WaitUntilTextEquals<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, string expectedText, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var element = Resolve(selector, page);
        if (!element.WaitUntilTextEquals(expectedText, timeoutMs))
        {
            throw new TimeoutException($"Element did not reach text '{expectedText}'.");
        }

        return page;
    }

    public static TSelf WaitUntilTextContains<TSelf>(this TSelf page, Expression<Func<TSelf, AutomationElement>> selector, string expectedPart, int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var element = Resolve(selector, page);
        if (!element.WaitUntilTextContains(expectedPart, timeoutMs))
        {
            throw new TimeoutException($"Element did not contain '{expectedPart}'.");
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

    private static AutomationElement? FindTreeItemByText(Tree tree, string itemText)
    {
        var target = NormalizeTextForLookup(itemText);
        if (target.Length == 0)
        {
            return null;
        }

        var allDescendants = tree.FindAllDescendants().ToArray();
        var exactByAutomationId = allDescendants
            .FirstOrDefault(node =>
                string.Equals(NormalizeTextForLookup(GetAutomationIdSafe(node)), target, StringComparison.OrdinalIgnoreCase));
        if (exactByAutomationId is not null)
        {
            return exactByAutomationId;
        }

        var exactByName = allDescendants
            .FirstOrDefault(node =>
                string.Equals(NormalizeTextForLookup(node.Name), target, StringComparison.OrdinalIgnoreCase));
        if (exactByName is not null)
        {
            return exactByName;
        }

        foreach (var treeItem in tree.Items)
        {
            if (string.Equals(NormalizeTextForLookup(GetText(treeItem)), target, StringComparison.OrdinalIgnoreCase))
            {
                return treeItem;
            }
        }

        foreach (var root in FindCandidateTreeNodes(tree))
        {
            if (string.Equals(NormalizeTextForLookup(GetText(root)), target, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }
        }

        var exactNode = FindCandidateTreeNodes(tree)
            .FirstOrDefault(node => string.Equals(NormalizeTextForLookup(GetText(node)), target, StringComparison.OrdinalIgnoreCase));
        if (exactNode is not null)
        {
            return exactNode;
        }

        var anyNode = FindCandidateTreeNodes(tree)
            .FirstOrDefault(node => NormalizeTextForLookup(GetText(node)).Contains(target, StringComparison.OrdinalIgnoreCase));
        if (anyNode is not null)
        {
            return anyNode;
        }

        var nameContains = allDescendants
            .FirstOrDefault(node => NormalizeTextForLookup(node.Name).Contains(target, StringComparison.OrdinalIgnoreCase))
            ?? allDescendants
                .FirstOrDefault(node => NormalizeTextForLookup(GetAutomationIdSafe(node)).Contains(target, StringComparison.OrdinalIgnoreCase));
        if (nameContains is not null)
        {
            return nameContains;
        }

        return allDescendants
            .FirstOrDefault(node => GetText(node) is not null && NormalizeTextForLookup(GetText(node)).Contains(target, StringComparison.OrdinalIgnoreCase));
    }

    private static AutomationElement[] FindCandidateTreeNodes(Tree tree)
    {
        var items = new HashSet<AutomationElement>();
        foreach (var root in tree.Items)
        {
            if (root is null)
            {
                continue;
            }

            AddTreeNode(root, items);
        }

        foreach (var descendant in tree.FindAllDescendants().Where(IsTreeItemCandidate))
        {
            items.Add(descendant);
        }

        return items.ToArray();
    }

    private static bool TrySelectTreeItem(AutomationElement target, int timeoutMs)
    {
        if (target is TreeItem treeItem)
        {
            try
            {
                treeItem.Select();
            }
            catch
            {
                target.Click();
            }

            if (!target.Patterns.SelectionItem.IsSupported)
            {
                return true;
            }

            return WaitUntilElementSelected(target, timeoutMs);
        }

        var asTreeItem = target.AsTreeItem();
        if (asTreeItem is not null)
        {
            try
            {
                asTreeItem.Select();
            }
            catch
            {
                asTreeItem.Click();
            }

            if (!asTreeItem.Patterns.SelectionItem.IsSupported)
            {
                return true;
            }

            return WaitUntilElementSelected(asTreeItem, timeoutMs);
        }

        if (target.Patterns.SelectionItem.IsSupported)
        {
            target.Patterns.SelectionItem.Pattern.Select();
            return WaitUntilElementSelected(target, timeoutMs);
        }

        target.Click();
        return true;
    }

    private static TabItem? FindTabItemByText(Tab tab, string itemText, bool includeDescendants = false)
    {
        var target = itemText.Trim();
        var targetNormalized = NormalizeTabText(itemText);

        var item = tab.TabItems.FirstOrDefault(candidate =>
        {
            var name = candidate.Name?.Trim();
            var automationId = GetAutomationIdSafe(candidate).Trim();
            var normalizedName = NormalizeTabText(name);
            var normalizedAutomationId = NormalizeTabText(automationId);

            return string.Equals(name, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, targetNormalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(automationId, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(automationId, targetNormalized, StringComparison.OrdinalIgnoreCase)
                || (name?.Contains(target, StringComparison.OrdinalIgnoreCase) == true)
                || (normalizedName.Length > 0 && normalizedName.Contains(targetNormalized, StringComparison.OrdinalIgnoreCase))
                || (normalizedAutomationId.Length > 0 && normalizedAutomationId.Contains(targetNormalized, StringComparison.OrdinalIgnoreCase));
        });

        if (item is not null)
        {
            return item;
        }

        if (!includeDescendants)
        {
            return null;
        }

        foreach (var candidate in tab.FindAllDescendants())
        {
            if (candidate.ControlType != ControlType.TabItem)
            {
                continue;
            }

            var name = candidate.Name?.Trim();
            var automationId = GetAutomationIdSafe(candidate).Trim();
            var normalizedName = NormalizeTabText(name);
            var normalizedAutomationId = NormalizeTabText(automationId);

            if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, targetNormalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(automationId, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(automationId, targetNormalized, StringComparison.OrdinalIgnoreCase)
                || (name?.Contains(target, StringComparison.OrdinalIgnoreCase) == true)
                || (normalizedName.Length > 0 && normalizedName.Contains(targetNormalized, StringComparison.OrdinalIgnoreCase))
                || (normalizedAutomationId.Length > 0 && normalizedAutomationId.Contains(targetNormalized, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate.AsTabItem();
            }
        }

        return null;
    }

    private static bool TrySelectTabItem(Tab tab, TabItem tabItem)
    {
        if (tabItem.IsSelected)
        {
            return true;
        }

        try
        {
            tabItem.Select();
            return true;
        }
        catch
        {
            try
            {
                tab.SelectTabItem(tabItem.Name ?? string.Empty);
                return true;
            }
            catch
            {
                // Some providers expose header buttons/cells inside tab item.
                try
                {
                    var fallbackButton = tabItem
                        .FindAllDescendants()
                        .FirstOrDefault(candidate =>
                        {
                            return candidate.ControlType == ControlType.Button
                                || candidate.ControlType == ControlType.RadioButton
                                || candidate.ControlType == ControlType.Thumb
                                || candidate.ControlType == ControlType.ListItem;
                        });
                    if (fallbackButton is null)
                    {
                        return false;
                    }

                    fallbackButton.Click();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    private static bool TrySetSpinnerValue(Spinner spinner, double value, int timeoutMs = 5000)
    {
        try
        {
            spinner.Value = value;
            return spinner.WaitUntilValueEquals(value, timeoutMs);
        }
        catch
        {
            // Fallback through nested text editor.
        }

        var textInput = FindTextInputForSpinner(spinner);
        if (textInput is null)
        {
            return false;
        }

        var expected = value.ToString(CultureInfo.InvariantCulture);
        textInput.Text = expected;

        var waitResult = UiWait.TryUntil(
            () => textInput.Text,
            actual => string.Equals(actual?.Trim(), expected, StringComparison.Ordinal));

        return waitResult.Success;
    }

    private static TextBox? FindTextInputForSpinner(Spinner spinner)
    {
        var rootTextBox = spinner.AsTextBox();
        if (rootTextBox is not null)
        {
            return rootTextBox;
        }

        var descendant = spinner
            .FindAllDescendants()
            .FirstOrDefault(candidate => candidate.ControlType == ControlType.Edit);

        return descendant?.AsTextBox();
    }

    private static bool WaitUntilSpinnerValueEquals(Spinner spinner, double expected, int timeoutMs)
    {
        if (spinner.WaitUntilValueEquals(expected, timeoutMs))
        {
            return true;
        }

        var textInput = FindTextInputForSpinner(spinner);
        if (textInput is null)
        {
            return false;
        }

        var expectedText = expected.ToString(CultureInfo.InvariantCulture);
        var waitResult = UiWait.TryUntil(
            () => textInput.Text,
            actual => string.Equals(actual?.Trim(), expectedText, StringComparison.Ordinal),
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    private static bool TrySetSpinnerValue(TextBox textBox, double value)
    {
        var expected = value.ToString(CultureInfo.InvariantCulture);
        try
        {
            textBox.EnterText(expected);
        }
        catch
        {
            if (!TrySetText(textBox, expected))
            {
                return false;
            }
        }

        var waitResult = UiWait.TryUntil(
            () => textBox.Text,
            actual => string.Equals(actual?.Trim(), expected, StringComparison.Ordinal),
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(5000) });

        return waitResult.Success;
    }

    private static bool WaitUntilSpinnerValueEquals(TextBox textBox, double expected, int timeoutMs)
    {
        var expectedText = expected.ToString(CultureInfo.InvariantCulture);
        var waitResult = UiWait.TryUntil(
            () => textBox.Text,
            actual => string.Equals(actual?.Trim(), expectedText, StringComparison.Ordinal),
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        return waitResult.Success;
    }

    private static bool TrySetDate(DateTimePicker dateTimePicker, DateTime date, int timeoutMs = 5000)
    {
        try
        {
            dateTimePicker.SelectedDate = date;
            if (dateTimePicker.WaitUntilDateEquals(date, timeoutMs))
            {
                return true;
            }
        }
        catch
        {
            // Fallback through text editor in provider.
        }

        var textInput = FindTextInputForDate(dateTimePicker);
        if (textInput is null)
        {
            return false;
        }

        var candidates = new[]
        {
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            date.ToShortDateString(),
            date.ToString("M/d/yyyy", CultureInfo.InvariantCulture),
            date.ToString("d", CultureInfo.CurrentCulture)
        };

        foreach (var candidate in candidates)
        {
            textInput.Text = candidate;
            var waitResult = UiWait.TryUntil(
                () => textInput.Text,
                actual =>
                {
                    if (string.IsNullOrWhiteSpace(actual))
                    {
                        return false;
                    }

                    return DateTime.TryParse(actual, out var actualDate)
                        && actualDate.Date == date.Date;
                },
                new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(Math.Max(350, timeoutMs / 2)) });

            if (waitResult.Success)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TrySetCalendarDate(FlaUICalendar calendar, DateTime date, int timeoutMs = 5000)
    {
        try
        {
            calendar.SelectDate(date);
            return calendar.WaitUntilCalendarDateEquals(date, timeoutMs);
        }
        catch
        {
            return false;
        }
    }

    private static TextBox? FindTextInputForDate(DateTimePicker dateTimePicker)
    {
        var rootTextBox = dateTimePicker.AsTextBox();
        if (rootTextBox is not null && rootTextBox.IsAvailable)
        {
            return rootTextBox;
        }

        var descendant = dateTimePicker
            .FindAllDescendants()
            .FirstOrDefault(candidate => candidate.ControlType == ControlType.Edit);

        return descendant?.AsTextBox();
    }

    private static string NormalizeTabText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var compacted = new string(text.Where(char.IsLetterOrDigit).ToArray());
        return compacted
            .Replace("tabitem", string.Empty, StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
    }

    private static string? GetText(TreeItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            return item.Text;
        }

        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            return item.Name;
        }

        var fallback = item
            .FindAllDescendants()
            .FirstOrDefault(candidate => candidate.ControlType == ControlType.Text);

        return fallback?.Name;
    }

    private static string? GetText(AutomationElement item)
    {
        if (item is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            return item.Name;
        }

        try
        {
            if (item.Patterns.Value.IsSupported)
            {
                var value = item.Patterns.Value.Pattern.Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // ignore and continue with descendants fallback
        }

        var textChild = item
            .FindAllDescendants()
            .FirstOrDefault(candidate => candidate.ControlType == ControlType.Text);

        if (textChild is not null && !string.IsNullOrWhiteSpace(textChild.Name))
        {
            return textChild.Name;
        }

        var automationId = GetAutomationIdSafe(item);
        if (!string.IsNullOrWhiteSpace(automationId))
        {
            return automationId;
        }

        return item.Name;
    }

    private static string? GetText(TabItem item) => item.Name;

    private static string GetAutomationIdSafe(AutomationElement element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        try
        {
            return element.AutomationId ?? string.Empty;
        }
        catch (PropertyNotSupportedException)
        {
            return string.Empty;
        }
    }

    private static bool IsTreeItemCandidate(AutomationElement candidate)
    {
        return candidate.ControlType == ControlType.TreeItem
            || candidate.ControlType == ControlType.ListItem
            || candidate.ControlType == ControlType.DataItem
            || candidate.ControlType == ControlType.Text
            || candidate.ControlType == ControlType.Custom
            || candidate.ControlType == ControlType.Group
            || candidate.ControlType == ControlType.Button
            || candidate.ControlType == ControlType.Tree
            || candidate.ControlType == ControlType.Pane;
    }

    private static bool WaitUntilElementSelected(AutomationElement element, int timeoutMs)
    {
        if (element is TreeItem treeItem)
        {
            return WaitUntilElementSelected(treeItem, timeoutMs);
        }

        if (element.Patterns.SelectionItem.IsSupported)
        {
            var waitResult = UiWait.TryUntil(
                () => element.Patterns.SelectionItem.Pattern.IsSelected.Value,
                static isSelected => isSelected,
                new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

            return waitResult.Success;
        }

        return true;
    }

    private static bool WaitUntilElementSelected(TreeItem treeItem, int timeoutMs)
    {
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        var waitResult = UiWait.TryUntil(
            () => treeItem.IsSelected,
            actual => actual,
            new UiWaitOptions { Timeout = timeout });
        return waitResult.Success;
    }

    private static bool TrySetText(AutomationElement element, string value)
    {
        var textBox = element.AsTextBox();
        if (textBox is not null)
        {
            textBox.Text = value;
            return string.Equals(textBox.Text, value, StringComparison.Ordinal);
        }

        var nestedTextBox = element
            .FindAllDescendants()
            .FirstOrDefault(candidate => candidate.ControlType == ControlType.Edit)
            ?.AsTextBox();

        if (nestedTextBox is not null)
        {
            nestedTextBox.Text = value;
            return string.Equals(nestedTextBox.Text, value, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool TrySetChecked(AutomationElement element, bool isChecked)
    {
        var checkBox = element.AsCheckBox();
        if (checkBox is not null)
        {
            if (checkBox.IsChecked is not true && isChecked)
            {
                checkBox.IsChecked = true;
            }
            else if (checkBox.IsChecked is true && !isChecked)
            {
                checkBox.IsChecked = false;
            }

            return checkBox.IsChecked == isChecked;
        }

        var toggle = element.AsToggleButton();
        if (toggle is not null)
        {
            if (toggle.IsToggled != isChecked)
            {
                toggle.Toggle();
            }

            return toggle.IsToggled == isChecked;
        }

        var radio = element.AsRadioButton();
        if (radio is not null)
        {
            if (isChecked && radio.IsChecked is not true)
            {
                radio.IsChecked = true;
            }

            return radio.IsChecked == isChecked;
        }

        return false;
    }

    private static bool TrySelectComboItem(ComboBox combo, string itemText, int timeoutMs)
    {
        var target = NormalizeTextForLookup(itemText);
        if (target.Length == 0)
        {
            return false;
        }

        ExpandCombo(combo);

        var candidates = GetComboCandidates(combo);
        var directCandidate = FindComboItemMatch(candidates, target, out var directCandidateIndex);
        if (directCandidate is not null)
        {
            if (directCandidateIndex >= 0)
            {
                try
                {
                    combo.Select(directCandidateIndex);
                    if (WaitUntilComboSelectionMatches(combo, target, timeoutMs))
                    {
                        return true;
                    }
                }
                catch
                {
                    // ignore and continue with direct click fallback
                }
            }

            if (TryClickComboItem(directCandidate))
            {
                return WaitUntilComboSelectionMatches(combo, target, timeoutMs);
            }
        }

        return false;
    }

    private static AutomationElement[] GetComboCandidates(ComboBox combo)
    {
        var list = new List<AutomationElement>();

        if (combo.Items is not null)
        {
            foreach (var item in combo.Items)
            {
                if (item is null)
                {
                    continue;
                }

                list.Add(item);
            }
        }

        foreach (var candidate in combo.FindAllDescendants())
        {
            if (IsComboItemCandidate(candidate) && !list.Contains(candidate))
            {
                list.Add(candidate);
            }
        }

        return list.ToArray();
    }

    private static bool IsComboItemCandidate(AutomationElement candidate)
    {
        return candidate.ControlType == ControlType.ListItem
            || candidate.ControlType == ControlType.ComboBox
            || candidate.ControlType == ControlType.Text
            || candidate.ControlType == ControlType.List
            || candidate.ControlType == ControlType.Button
            || candidate.ControlType == ControlType.DataItem;
    }

    private static bool TryClickComboItem(AutomationElement candidate)
    {
        try
        {
            candidate.Click();
            return true;
        }
        catch
        {
        }

        if (candidate.Patterns.Invoke.IsSupported)
        {
            candidate.Patterns.Invoke.Pattern.Invoke();
            return true;
        }

        return false;
    }

    private static void ExpandCombo(ComboBox combo)
    {
        try
        {
            combo.Expand();
        }
        catch
        {
            // ignore if combo does not expose expand pattern directly
        }
    }

    private static bool WaitUntilComboSelectionMatches(ComboBox combo, string expectedText, int timeoutMs)
    {
        var waitResult = UiWait.TryUntil(
            () => ReadComboSelectionText(combo),
            actual => string.Equals(NormalizeTextForLookup(actual), expectedText, StringComparison.OrdinalIgnoreCase),
            new UiWaitOptions { Timeout = TimeSpan.FromMilliseconds(timeoutMs) });

        if (waitResult.Success)
        {
            return true;
        }

        var candidates = GetComboCandidates(combo);
        var hasExpectedCandidate = candidates.Any(candidate =>
            string.Equals(NormalizeTextForLookup(GetText(candidate)), expectedText, StringComparison.OrdinalIgnoreCase));

        return hasExpectedCandidate;
    }

    private static void AddTreeNode(TreeItem node, HashSet<AutomationElement> collector)
    {
        if (node is null || collector.Contains(node))
        {
            return;
        }

        collector.Add(node);
        ExpandTreeBranch(node);

        foreach (var child in node.Items)
        {
            if (child is null)
            {
                continue;
            }

            AddTreeNode(child, collector);
        }

        foreach (var descendant in node.FindAllDescendants().Where(IsTreeItemCandidate))
        {
            collector.Add(descendant);
        }
    }

    private static void ExpandTreeBranch(TreeItem item)
    {
        try
        {
            item.Expand();
        }
        catch
        {
            // ignore expansion errors for leaf nodes
        }
    }

    private static string? ReadComboSelectionText(ComboBox combo)
    {
        if (combo.SelectedItem is null)
        {
            return null;
        }

        if (combo.SelectedItem is ComboBoxItem selectedComboBoxItem)
        {
            return GetText(selectedComboBoxItem);
        }

        if (combo.SelectedItem is AutomationElement selectedElement)
        {
            return GetText(selectedElement);
        }

        return combo.SelectedItem.ToString();
    }

    private static AutomationElement? FindComboItemMatch(AutomationElement[] candidates, string target, out int foundIndex)
    {
        for (var index = 0; index < candidates.Length; index++)
        {
            var item = candidates[index];
            var itemText = NormalizeTextForLookup(GetText(item));
            if (string.Equals(itemText, target, StringComparison.OrdinalIgnoreCase))
            {
                foundIndex = index;
                return item;
            }
        }

        for (var index = 0; index < candidates.Length; index++)
        {
            var item = candidates[index];
            var itemText = NormalizeTextForLookup(GetText(item));
            if (itemText.Length == 0)
            {
                continue;
            }

            if (itemText.Contains(target, StringComparison.OrdinalIgnoreCase))
            {
                foundIndex = index;
                return item;
            }
        }

        foundIndex = -1;
        return null;
    }

    private static string NormalizeTextForLookup(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value.Trim();
    }
}

