using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AppAutomation.Abstractions;

/// <summary>
/// Provides fluent extension methods for interacting with UI controls on page objects.
/// </summary>
/// <remarks>
/// <para>
/// All methods in this class follow a fluent pattern, returning the page instance
/// to enable method chaining. Each method waits for the control to be enabled
/// before performing the action, and validates the result afterward.
/// </para>
/// <para>
/// If an operation fails or times out, a <see cref="UiOperationException"/> is thrown
/// containing detailed diagnostic information including expected/actual values and failure artifacts.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// page.EnterText(p => p.UserName, "testuser")
///     .EnterText(p => p.Password, "secret")
///     .ClickButton(p => p.LoginButton)
///     .WaitUntilNameContains(p => p.StatusLabel, "Welcome");
/// </code>
/// </example>
public static class UiPageExtensions
{
    /// <summary>
    /// Enters text into a text box control.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the text box control.</param>
    /// <param name="value">The text to enter.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the operation to complete.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the text box is not enabled or the text was not entered successfully.</exception>
    public static TSelf EnterText<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ITextBoxControl>> selector,
        string value,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var textBox = Resolve(selector, page);
        textBox.Enter(value);
        WaitUntil(
            page,
            selector,
            () => string.Equals(textBox.Text, value, StringComparison.Ordinal),
            timeoutMs,
            $"TextBox '{textBox.AutomationId}' did not reach expected value.",
            expectedValue: value,
            lastObservedValueFactory: () => textBox.Text);
        return page;
    }

    /// <summary>
    /// Clicks a button control after waiting for it to be enabled.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the button control.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the button to be enabled.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the button is not enabled within the timeout.</exception>
    public static TSelf ClickButton<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IButtonControl>> selector,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var button = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => button.IsEnabled,
            timeoutMs,
            $"Button '{button.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={button.IsEnabled}");
        button.Invoke();
        return page;
    }

    /// <summary>
    /// Sets the checked state of a check box control.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the check box control.</param>
    /// <param name="isChecked">The desired checked state.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the operation to complete.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the check box is not enabled or the state was not changed successfully.</exception>
    public static TSelf SetChecked<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ICheckBoxControl>> selector,
        bool isChecked,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var checkBox = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => checkBox.IsEnabled,
            timeoutMs,
            $"CheckBox '{checkBox.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={checkBox.IsEnabled}");
        checkBox.IsChecked = isChecked;
        WaitUntil(
            page,
            selector,
            () => checkBox.IsChecked == isChecked,
            timeoutMs,
            $"CheckBox '{checkBox.AutomationId}' did not reach expected checked state.",
            expectedValue: $"IsChecked={isChecked}",
            lastObservedValueFactory: () => $"IsChecked={checkBox.IsChecked}");
        return page;
    }

    /// <summary>
    /// Sets the checked state of a radio button control.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the radio button control.</param>
    /// <param name="isChecked">The desired checked state.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the operation to complete.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the radio button is not enabled or the state was not changed successfully.</exception>
    public static TSelf SetChecked<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IRadioButtonControl>> selector,
        bool isChecked,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var radioButton = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => radioButton.IsEnabled,
            timeoutMs,
            $"RadioButton '{radioButton.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={radioButton.IsEnabled}");
        radioButton.IsChecked = isChecked;
        WaitUntil(
            page,
            selector,
            () => radioButton.IsChecked == isChecked,
            timeoutMs,
            $"RadioButton '{radioButton.AutomationId}' did not reach expected checked state.",
            expectedValue: $"IsChecked={isChecked}",
            lastObservedValueFactory: () => $"IsChecked={radioButton.IsChecked}");
        return page;
    }

    /// <summary>
    /// Sets the toggled state of a toggle button control.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the toggle button control.</param>
    /// <param name="isToggled">The desired toggled state.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the operation to complete.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the toggle button is not enabled or the state was not changed successfully.</exception>
    public static TSelf SetToggled<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IToggleButtonControl>> selector,
        bool isToggled,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var toggle = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => toggle.IsEnabled,
            timeoutMs,
            $"Toggle '{toggle.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={toggle.IsEnabled}");
        if (toggle.IsToggled != isToggled)
        {
            toggle.Toggle();
        }

        WaitUntil(
            page,
            selector,
            () => toggle.IsToggled == isToggled,
            timeoutMs,
            $"Toggle '{toggle.AutomationId}' did not reach expected toggled state.",
            expectedValue: $"IsToggled={isToggled}",
            lastObservedValueFactory: () => $"IsToggled={toggle.IsToggled}");
        return page;
    }

    /// <summary>
    /// Selects an item in a combo box by its display text.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the combo box control.</param>
    /// <param name="itemText">The text of the item to select (matched case-insensitively).</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the operation to complete.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="itemText"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the specified item is not found in the combo box.</exception>
    /// <exception cref="UiOperationException">Thrown when the combo box is not enabled or the item was not selected successfully.</exception>
    public static TSelf SelectComboItem<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IComboBoxControl>> selector,
        string itemText,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemText);

        var comboBox = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => comboBox.IsEnabled,
            timeoutMs,
            $"ComboBox '{comboBox.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={comboBox.IsEnabled}");
        comboBox.Expand();

        var target = NormalizeLookupText(itemText);
        var index = comboBox.Items
            .Select((item, candidateIndex) => (Item: item, Index: candidateIndex))
            .Where(candidate =>
                string.Equals(NormalizeLookupText(candidate.Item.Text), target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeLookupText(candidate.Item.Name), target, StringComparison.OrdinalIgnoreCase))
            .Select(static candidate => (int?)candidate.Index)
            .FirstOrDefault();

        if (index is null)
        {
            throw new InvalidOperationException($"ComboBox '{comboBox.AutomationId}' item '{itemText}' was not found. Available items: [{string.Join(", ", comboBox.Items.Select(i => i.Text ?? i.Name))}].");
        }

        comboBox.SelectByIndex(index.Value);
        WaitUntil(
            page,
            selector,
            () => comboBox.SelectedIndex == index.Value
                || string.Equals(NormalizeLookupText(comboBox.SelectedItem?.Text), target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeLookupText(comboBox.SelectedItem?.Name), target, StringComparison.OrdinalIgnoreCase),
            timeoutMs,
            $"ComboBox '{comboBox.AutomationId}' failed to select item.",
            expectedValue: itemText,
            lastObservedValueFactory: () => comboBox.SelectedItem?.Text ?? comboBox.SelectedItem?.Name ?? $"SelectedIndex={comboBox.SelectedIndex}");
        return page;
    }

    /// <summary>
    /// Sets the value of a slider control.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the slider control.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the operation to complete.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the slider is not enabled or the value was not set successfully.</exception>
    public static TSelf SetSliderValue<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ISliderControl>> selector,
        double value,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var slider = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => slider.IsEnabled,
            timeoutMs,
            $"Slider '{slider.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={slider.IsEnabled}");
        slider.Value = value;
        WaitUntil(
            page,
            selector,
            () => Math.Abs(slider.Value - value) < 0.001,
            timeoutMs,
            $"Slider '{slider.AutomationId}' did not reach expected value.",
            expectedValue: value.ToString(CultureInfo.InvariantCulture),
            lastObservedValueFactory: () => slider.Value.ToString(CultureInfo.InvariantCulture));
        return page;
    }

    /// <summary>
    /// Sets a numeric value in a spinner-like text box control by entering the value as text.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the text box control used as a spinner.</param>
    /// <param name="value">The numeric value to set.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the operation to complete.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the control is not enabled or the value was not set successfully.</exception>
    public static TSelf SetSpinnerValue<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ITextBoxControl>> selector,
        double value,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var textBox = Resolve(selector, page);
        var expected = value.ToString(CultureInfo.InvariantCulture);
        textBox.Enter(expected);
        WaitUntil(
            page,
            selector,
            () => string.Equals(textBox.Text?.Trim(), expected, StringComparison.Ordinal),
            timeoutMs,
            $"Spinner-like text box '{textBox.AutomationId}' did not reach expected value.",
            expectedValue: expected,
            lastObservedValueFactory: () => textBox.Text);
        return page;
    }

    /// <summary>
    /// Selects a tab item within a tab control by its display text.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the tab control.</param>
    /// <param name="itemText">The text of the tab to select.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the operation to complete.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="itemText"/> is null or whitespace.</exception>
    /// <exception cref="UiOperationException">Thrown when the tab control is not enabled or the tab was not selected successfully.</exception>
    public static TSelf SelectTabItem<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ITabControl>> selector,
        string itemText,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemText);

        var tab = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => tab.IsEnabled,
            timeoutMs,
            $"Tab '{tab.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={tab.IsEnabled}");
        tab.SelectTabItem(itemText);

        WaitUntil(
            page,
            selector,
            () => tab.Items.Any(item =>
                item.IsSelected &&
                TextMatches(item.Name, itemText)),
            timeoutMs,
            $"Tab '{tab.AutomationId}' failed to select tab item.",
            expectedValue: itemText,
            lastObservedValueFactory: () => tab.Items.FirstOrDefault(static item => item.IsSelected)?.Name ?? "<none selected>");
        return page;
    }

    /// <summary>
    /// Searches for and selects an item in a search picker control.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the search picker control.</param>
    /// <param name="searchText">The text to enter in the search field.</param>
    /// <param name="itemText">The text of the item to select from the results.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the operation to complete.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="searchText"/> or <paramref name="itemText"/> is null or whitespace.</exception>
    /// <exception cref="UiOperationException">Thrown when the control is not enabled or the search/selection failed.</exception>
    public static TSelf SearchAndSelect<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ISearchPickerControl>> selector,
        string searchText,
        string itemText,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchText);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemText);

        var searchPicker = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => searchPicker.IsEnabled,
            timeoutMs,
            $"Search picker '{searchPicker.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={searchPicker.IsEnabled}");

        searchPicker.Search(searchText);
        WaitUntil(
            page,
            selector,
            () => string.Equals(searchPicker.SearchText, searchText, StringComparison.Ordinal),
            timeoutMs,
            $"Search picker '{searchPicker.AutomationId}' did not accept search text.",
            expectedValue: searchText,
            lastObservedValueFactory: () => searchPicker.SearchText);

        searchPicker.SelectItem(itemText);
        WaitUntil(
            page,
            selector,
            () => string.Equals(searchPicker.SelectedItemText, itemText, StringComparison.OrdinalIgnoreCase),
            timeoutMs,
            $"Search picker '{searchPicker.AutomationId}' failed to select item.",
            expectedValue: itemText,
            lastObservedValueFactory: () => searchPicker.SelectedItemText);
        return page;
    }

    /// <summary>
    /// Selects a specific tab item control directly.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the tab item control.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the operation to complete.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the tab item is not enabled or was not selected successfully.</exception>
    public static TSelf SelectTabItem<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ITabItemControl>> selector,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var tabItem = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => tabItem.IsEnabled,
            timeoutMs,
            $"Tab item '{tabItem.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={tabItem.IsEnabled}");
        tabItem.SelectTab();
        WaitUntil(
            page,
            selector,
            () => tabItem.IsSelected,
            timeoutMs,
            $"Tab item '{tabItem.AutomationId}' was not selected.",
            expectedValue: "IsSelected=true",
            lastObservedValueFactory: () => $"IsSelected={tabItem.IsSelected}");
        return page;
    }

    /// <summary>
    /// Selects a tree item within a tree control by its display text.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the tree control.</param>
    /// <param name="itemText">The text of the tree item to select (searches recursively).</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the operation to complete.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="itemText"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the specified item is not found in the tree.</exception>
    /// <exception cref="UiOperationException">Thrown when the tree is not enabled or the item was not selected successfully.</exception>
    public static TSelf SelectTreeItem<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ITreeControl>> selector,
        string itemText,
        int timeoutMs = 10000)
        where TSelf : UiPage
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemText);

        var tree = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => tree.IsEnabled,
            timeoutMs,
            $"Tree '{tree.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={tree.IsEnabled}");

        var target = FindTreeItem(tree.Items, itemText);
        if (target is null)
        {
            throw new InvalidOperationException($"Tree '{tree.AutomationId}' item '{itemText}' was not found.");
        }

        target.SelectNode();
        WaitUntil(
            page,
            selector,
            () =>
            {
                if (target.IsSelected)
                {
                    return true;
                }

                var selectedTreeItem = tree.SelectedTreeItem;
                return TreeItemMatches(selectedTreeItem, target, itemText);
            },
            timeoutMs,
            $"Tree '{tree.AutomationId}' failed to select item.",
            expectedValue: itemText,
            lastObservedValueFactory: () =>
            {
                var selectedTreeItem = tree.SelectedTreeItem;
                return GetTreeItemDisplayValue(selectedTreeItem)
                    ?? GetTreeItemDisplayValue(target);
            });
        return page;
    }

    /// <summary>
    /// Sets the selected date on a date-time picker control.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the date-time picker control.</param>
    /// <param name="date">The date to select.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the operation to complete.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the date picker is not enabled or the date was not set successfully.</exception>
    public static TSelf SetDate<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IDateTimePickerControl>> selector,
        DateTime date,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var datePicker = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => datePicker.IsEnabled,
            timeoutMs,
            $"Date picker '{datePicker.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={datePicker.IsEnabled}");
        datePicker.SelectedDate = date.Date;
        WaitUntil(
            page,
            selector,
            () => datePicker.SelectedDate?.Date == date.Date,
            timeoutMs,
            $"Date picker '{datePicker.AutomationId}' did not reach expected date.",
            expectedValue: date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            lastObservedValueFactory: () => datePicker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "<null>");
        return page;
    }

    /// <summary>
    /// Sets the selected date on a calendar control.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the calendar control.</param>
    /// <param name="date">The date to select.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the operation to complete.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the calendar is not enabled or the date was not set successfully.</exception>
    public static TSelf SetDate<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ICalendarControl>> selector,
        DateTime date,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var calendar = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => calendar.IsEnabled,
            timeoutMs,
            $"Calendar '{calendar.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={calendar.IsEnabled}");
        calendar.SelectDate(date.Date);
        WaitUntil(
            page,
            selector,
            () => calendar.SelectedDates.Any(candidate => candidate.Date == date.Date),
            timeoutMs,
            $"Calendar '{calendar.AutomationId}' did not reach expected date.",
            expectedValue: date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            lastObservedValueFactory: () => calendar.SelectedDates.Any()
                ? string.Join(", ", calendar.SelectedDates.Select(static candidate => candidate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                : "<none selected>");
        return page;
    }

    /// <summary>
    /// Waits until the progress bar value reaches at least the specified minimum.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the progress bar control.</param>
    /// <param name="expectedMin">The minimum value the progress bar should reach.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the progress bar does not reach the minimum value within the timeout.</exception>
    public static TSelf WaitUntilProgressAtLeast<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IProgressBarControl>> selector,
        double expectedMin,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var progressBar = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => progressBar.Value >= expectedMin,
            timeoutMs,
            $"Progress bar '{progressBar.AutomationId}' did not reach minimum value.",
            expectedValue: $">={expectedMin.ToString(CultureInfo.InvariantCulture)}",
            lastObservedValueFactory: () => progressBar.Value.ToString(CultureInfo.InvariantCulture));
        return page;
    }

    /// <summary>
    /// Waits until a control reaches the expected enabled state.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the UI control.</param>
    /// <param name="expected">The expected enabled state. Defaults to <see langword="true"/>.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the control does not reach the expected state within the timeout.</exception>
    public static TSelf WaitUntilIsEnabled<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IUiControl>> selector,
        bool expected = true,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var control = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => control.IsEnabled == expected,
            timeoutMs,
            $"Control '{control.AutomationId}' did not reach expected enabled state.",
            expectedValue: $"IsEnabled={expected}",
            lastObservedValueFactory: () => $"IsEnabled={control.IsEnabled}");
        return page;
    }

    /// <summary>
    /// Waits until a check box reaches the expected checked state.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the check box control.</param>
    /// <param name="expected">The expected checked state. Defaults to <see langword="true"/>.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the check box does not reach the expected state within the timeout.</exception>
    public static TSelf WaitUntilIsChecked<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ICheckBoxControl>> selector,
        bool expected = true,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var checkBox = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => checkBox.IsChecked == expected,
            timeoutMs,
            $"CheckBox '{checkBox.AutomationId}' did not reach expected checked state.",
            expectedValue: $"IsChecked={expected}",
            lastObservedValueFactory: () => $"IsChecked={checkBox.IsChecked}");
        return page;
    }

    /// <summary>
    /// Waits until a radio button reaches the expected checked state.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the radio button control.</param>
    /// <param name="expected">The expected checked state.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the radio button does not reach the expected state within the timeout.</exception>
    public static TSelf WaitUntilIsSelected<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IRadioButtonControl>> selector,
        bool expected,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var radioButton = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => radioButton.IsChecked == expected,
            timeoutMs,
            $"Radio button '{radioButton.AutomationId}' did not reach expected checked state.",
            expectedValue: $"IsChecked={expected}",
            lastObservedValueFactory: () => $"IsChecked={radioButton.IsChecked}");
        return page;
    }

    /// <summary>
    /// Waits until a tab item reaches the expected selected state.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the tab item control.</param>
    /// <param name="expected">The expected selected state. Defaults to <see langword="true"/>.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the tab item does not reach the expected state within the timeout.</exception>
    public static TSelf WaitUntilIsSelected<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ITabItemControl>> selector,
        bool expected = true,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var tabItem = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => tabItem.IsSelected == expected,
            timeoutMs,
            $"Tab item '{tabItem.AutomationId}' did not reach expected selected state.",
            expectedValue: $"IsSelected={expected}",
            lastObservedValueFactory: () => $"IsSelected={tabItem.IsSelected}");
        return page;
    }

    /// <summary>
    /// Waits until a toggle button reaches the expected toggled state.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the toggle button control.</param>
    /// <param name="expected">The expected toggled state.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the toggle button does not reach the expected state within the timeout.</exception>
    public static TSelf WaitUntilIsToggled<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IToggleButtonControl>> selector,
        bool expected,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var toggle = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => toggle.IsToggled == expected,
            timeoutMs,
            $"Toggle '{toggle.AutomationId}' did not reach expected toggled state.",
            expectedValue: $"IsToggled={expected}",
            lastObservedValueFactory: () => $"IsToggled={toggle.IsToggled}");
        return page;
    }

    /// <summary>
    /// Waits until a label's text equals the expected text.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the label control.</param>
    /// <param name="expectedText">The expected text value.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the label does not reach the expected text within the timeout.</exception>
    public static TSelf WaitUntilTextEquals<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ILabelControl>> selector,
        string expectedText,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        return WaitUntilText(
            page,
            selector,
            static control => control.Text,
            text => string.Equals(text, expectedText, StringComparison.Ordinal),
            timeoutMs,
            "text",
            expectedText);
    }

    /// <summary>
    /// Waits until a text box's text equals the expected text.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the text box control.</param>
    /// <param name="expectedText">The expected text value.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the text box does not reach the expected text within the timeout.</exception>
    public static TSelf WaitUntilTextEquals<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ITextBoxControl>> selector,
        string expectedText,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        return WaitUntilText(
            page,
            selector,
            static control => control.Text,
            text => string.Equals(text, expectedText, StringComparison.Ordinal),
            timeoutMs,
            "text",
            expectedText);
    }

    /// <summary>
    /// Waits until a label's text contains the expected text.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the label control.</param>
    /// <param name="expectedPart">The text that should appear in the label.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the label does not contain the expected text within the timeout.</exception>
    public static TSelf WaitUntilTextContains<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ILabelControl>> selector,
        string expectedPart,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        return WaitUntilText(
            page,
            selector,
            static control => control.Text,
            text => text.Contains(expectedPart, StringComparison.Ordinal),
            timeoutMs,
            "text",
            $"Contains '{expectedPart}'");
    }

    /// <summary>
    /// Waits until a text box's text contains the expected text.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the text box control.</param>
    /// <param name="expectedPart">The text that should appear in the text box.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the text box does not contain the expected text within the timeout.</exception>
    public static TSelf WaitUntilTextContains<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ITextBoxControl>> selector,
        string expectedPart,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        return WaitUntilText(
            page,
            selector,
            static control => control.Text,
            text => text.Contains(expectedPart, StringComparison.Ordinal),
            timeoutMs,
            "text",
            $"Contains '{expectedPart}'");
    }

    /// <summary>
    /// Selects an item in a list box by its display text.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the list box control.</param>
    /// <param name="itemText">The text of the item to select.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the selection to complete.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    public static TSelf SelectListBoxItem<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IListBoxControl>> selector,
        string itemText,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemText);

        var listBox = Resolve(selector, page);
        if (listBox is not ISelectableListBoxControl selectableListBox)
        {
            throw new InvalidOperationException(
                $"ListBox '{listBox.AutomationId}' does not support interactive selection in the current runtime.");
        }

        WaitUntil(
            page,
            selector,
            () => listBox.IsEnabled,
            timeoutMs,
            $"ListBox '{listBox.AutomationId}' is not enabled.",
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={listBox.IsEnabled}");

        selectableListBox.SelectItem(itemText);
        var expected = NormalizeLookupText(itemText);
        WaitUntil(
            page,
            selector,
            () => string.Equals(
                NormalizeLookupText(selectableListBox.SelectedItemText),
                expected,
                StringComparison.OrdinalIgnoreCase),
            timeoutMs,
            $"ListBox '{listBox.AutomationId}' failed to select item.",
            expectedValue: itemText,
            lastObservedValueFactory: () => selectableListBox.SelectedItemText);
        return page;
    }

    /// <summary>
    /// Waits until a list box contains an item with the specified text.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the list box control.</param>
    /// <param name="expectedText">The text that should appear in one of the items.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when no item containing the expected text is found within the timeout.</exception>
    public static TSelf WaitUntilListBoxContains<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IListBoxControl>> selector,
        string expectedText,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var listBox = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => listBox.Items.Any(item =>
                (item.Text ?? item.Name ?? string.Empty).Contains(expectedText, StringComparison.Ordinal)),
            timeoutMs,
            $"ListBox '{listBox.AutomationId}' did not contain expected item.",
            expectedValue: $"Contains '{expectedText}'",
            lastObservedValueFactory: () => $"Items: [{string.Join(", ", listBox.Items.Select(static item => item.Text ?? item.Name ?? string.Empty))}]");
        return page;
    }

    /// <summary>
    /// Waits until a control's Name property equals the expected text.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the UI control.</param>
    /// <param name="expectedText">The expected Name value.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the control's Name does not equal the expected text within the timeout.</exception>
    public static TSelf WaitUntilNameEquals<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IUiControl>> selector,
        string expectedText,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var control = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => string.Equals(control.Name, expectedText, StringComparison.Ordinal),
            timeoutMs,
            $"Control '{control.AutomationId}' did not reach expected name.",
            expectedValue: expectedText,
            lastObservedValueFactory: () => control.Name);
        return page;
    }

    /// <summary>
    /// Waits until a control's Name property contains the expected text.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the UI control.</param>
    /// <param name="expectedPart">The text that should appear in the Name property.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the control's Name does not contain the expected text within the timeout.</exception>
    public static TSelf WaitUntilNameContains<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IUiControl>> selector,
        string expectedPart,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var control = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => control.Name.Contains(expectedPart, StringComparison.Ordinal),
            timeoutMs,
            $"Control '{control.AutomationId}' did not contain expected text in name.",
            expectedValue: $"Contains '{expectedPart}'",
            lastObservedValueFactory: () => control.Name);
        return page;
    }

    /// <summary>
    /// Waits until a list box contains at least the specified number of items.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the list box control.</param>
    /// <param name="minCount">The minimum number of items expected.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the list box does not contain the minimum number of items within the timeout.</exception>
    public static TSelf WaitUntilHasItemsAtLeast<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IListBoxControl>> selector,
        int minCount,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        var listBox = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => listBox.Items.Count >= minCount,
            timeoutMs,
            $"ListBox '{listBox.AutomationId}' did not reach minimum item count.",
            expectedValue: $">={minCount} items",
            lastObservedValueFactory: () => $"{listBox.Items.Count} items");
        return page;
    }

    /// <summary>
    /// Waits until a grid contains at least the specified number of rows.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the grid control.</param>
    /// <param name="minRows">The minimum number of rows expected.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the grid does not contain the minimum number of rows within the timeout.</exception>
    public static TSelf WaitUntilGridRowsAtLeast<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        int minRows,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minRows);

        var grid = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => grid.Rows.Count >= minRows,
            timeoutMs,
            $"Grid '{grid.AutomationId}' did not reach minimum row count.",
            expectedValue: $">={minRows} rows",
            lastObservedValueFactory: () => $"{grid.Rows.Count} rows");
        return page;
    }

    /// <summary>
    /// Waits until a grid cell equals the expected value.
    /// </summary>
    /// <typeparam name="TSelf">The page type.</typeparam>
    /// <param name="page">The page instance.</param>
    /// <param name="selector">Expression selecting the grid control.</param>
    /// <param name="rowIndex">The zero-based row index.</param>
    /// <param name="columnIndex">The zero-based column index.</param>
    /// <param name="expectedValue">The expected cell value.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait.</param>
    /// <returns>The page instance for fluent chaining.</returns>
    /// <exception cref="UiOperationException">Thrown when the cell does not reach the expected value within the timeout.</exception>
    public static TSelf WaitUntilGridCellEquals<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        int rowIndex,
        int columnIndex,
        string expectedValue,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
        ArgumentNullException.ThrowIfNull(expectedValue);

        var grid = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => string.Equals(
                TryReadGridCellValue(grid, rowIndex, columnIndex),
                expectedValue,
                StringComparison.Ordinal),
            timeoutMs,
            $"Grid '{grid.AutomationId}' cell [{rowIndex},{columnIndex}] did not reach expected value.",
            expectedValue: expectedValue,
            lastObservedValueFactory: () => TryReadGridCellValue(grid, rowIndex, columnIndex));
        return page;
    }

    /// <summary>
    /// Opens or activates a grid row by its zero-based index.
    /// </summary>
    public static TSelf OpenGridRow<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        int rowIndex,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);

        return ExecuteGridUserAction(
            page,
            selector,
            nameof(OpenGridRow),
            timeoutMs,
            grid => grid.OpenRow(rowIndex),
            grid => $"rowIndex={rowIndex}; rows={grid.Rows.Count}");
    }

    /// <summary>
    /// Sorts a grid by a stable column name or visible header text.
    /// </summary>
    public static TSelf SortGridByColumn<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        string columnName,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

        return ExecuteGridUserAction(
            page,
            selector,
            nameof(SortGridByColumn),
            timeoutMs,
            grid => grid.SortByColumn(columnName),
            _ => $"columnName={columnName}");
    }

    /// <summary>
    /// Scrolls a grid to the end or triggers its load-more behavior.
    /// </summary>
    public static TSelf ScrollGridToEnd<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        return ExecuteGridUserAction(
            page,
            selector,
            nameof(ScrollGridToEnd),
            timeoutMs,
            grid => grid.ScrollToEnd(),
            grid => $"rows={grid.Rows.Count}");
    }

    /// <summary>
    /// Copies or reads a grid cell by zero-based row and column indexes.
    /// </summary>
    public static TSelf CopyGridCell<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        int rowIndex,
        int columnIndex,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);

        return ExecuteGridUserAction(
            page,
            selector,
            nameof(CopyGridCell),
            timeoutMs,
            grid => grid.CopyCell(rowIndex, columnIndex),
            grid => TryReadGridCellValue(grid, rowIndex, columnIndex));
    }

    /// <summary>
    /// Invokes a grid export action.
    /// </summary>
    public static TSelf ExportGrid<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        int timeoutMs = 5000)
        where TSelf : UiPage
    {
        return ExecuteGridUserAction(
            page,
            selector,
            nameof(ExportGrid),
            timeoutMs,
            grid => grid.Export(),
            grid => $"rows={grid.Rows.Count}");
    }

    private static TControl Resolve<TSelf, TControl>(Expression<Func<TSelf, TControl>> selector, TSelf page)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(page);

        var control = selector.Compile().Invoke(page);
        if (control is null)
        {
            throw new InvalidOperationException("Selector returned null.");
        }

        return control;
    }

    private static TSelf ExecuteGridUserAction<TSelf>(
        TSelf page,
        Expression<Func<TSelf, IGridControl>> selector,
        string actionName,
        int timeoutMs,
        Action<IGridUserActionControl> action,
        Func<IGridControl, string?> lastObservedValueFactory)
        where TSelf : UiPage
    {
        ArgumentNullException.ThrowIfNull(action);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        var grid = Resolve(selector, page);
        try
        {
            if (grid is not IGridUserActionControl actionGrid)
            {
                throw new NotSupportedException(
                    $"Grid '{grid.AutomationId}' does not support user action '{actionName}' in adapter '{page.Capabilities.AdapterId}'.");
            }

            action(actionGrid);
            return page;
        }
        catch (Exception ex) when (ex is not UiOperationException and not OperationCanceledException)
        {
            throw CreateUiOperationException(
                page,
                selector,
                timeout,
                startedAtUtc,
                $"Grid '{grid.AutomationId}' failed to execute user action '{actionName}'.",
                expectedValue: actionName,
                lastObservedValueFactory: () => lastObservedValueFactory(grid),
                actionName,
                ex);
        }
    }

    private static string? TryReadGridCellValue(IGridControl grid, int rowIndex, int columnIndex)
    {
        var row = grid.GetRowByIndex(rowIndex);
        if (row is null)
        {
            return $"<missing row {rowIndex}; rows={grid.Rows.Count}>";
        }

        var cells = row.Cells;
        if (columnIndex >= cells.Count)
        {
            return $"<missing cell {rowIndex},{columnIndex}; cells={cells.Count}>";
        }

        return cells[columnIndex].Value;
    }

    private static TSelf WaitUntilText<TSelf, TControl>(
        TSelf page,
        Expression<Func<TSelf, TControl>> selector,
        Func<TControl, string> textAccessor,
        Func<string, bool> predicate,
        int timeoutMs,
        string valueLabel,
        string expectedValue)
        where TSelf : UiPage
        where TControl : IUiControl
    {
        var control = Resolve(selector, page);
        WaitUntil(
            page,
            selector,
            () => predicate(textAccessor(control)),
            timeoutMs,
            $"Control '{control.AutomationId}' did not reach expected {valueLabel}.",
            expectedValue: expectedValue,
            lastObservedValueFactory: () => textAccessor(control));
        return page;
    }

    private static void WaitUntil<TSelf, TControl>(
        TSelf page,
        Expression<Func<TSelf, TControl>> selector,
        Func<bool> condition,
        int timeoutMs,
        string timeoutMessage,
        string? expectedValue = null,
        Func<string?>? lastObservedValueFactory = null,
        [CallerMemberName] string operationName = "")
        where TSelf : UiPage
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        var waitOptions = new UiWaitOptions
        {
            Timeout = timeout,
            PollInterval = TimeSpan.FromMilliseconds(100)
        };

        try
        {
            UiWait.Until(
                condition,
                static value => value,
                waitOptions,
                timeoutMessage);
        }
        catch (TimeoutException ex)
        {
            throw CreateUiOperationException(
                page,
                selector,
                timeout,
                startedAtUtc,
                timeoutMessage,
                expectedValue,
                lastObservedValueFactory,
                operationName,
                ex);
        }
        catch (Exception ex) when (ex is not UiOperationException and not OperationCanceledException)
        {
            throw CreateUiOperationException(
                page,
                selector,
                timeout,
                startedAtUtc,
                timeoutMessage,
                expectedValue,
                lastObservedValueFactory,
                operationName,
                ex);
        }
    }

    private static UiOperationException CreateUiOperationException<TSelf, TControl>(
        TSelf page,
        Expression<Func<TSelf, TControl>> selector,
        TimeSpan timeout,
        DateTimeOffset startedAtUtc,
        string failureMessage,
        string? expectedValue,
        Func<string?>? lastObservedValueFactory,
        string operationName,
        Exception exception)
        where TSelf : UiPage
    {
        var finishedAtUtc = DateTimeOffset.UtcNow;
        var propertyName = TryGetPropertyName(selector);
        var definition = TryGetControlDefinition(page.GetType(), propertyName);
        var lastObservedValue = TryReadLastObservedValue(lastObservedValueFactory);
        var failureContext = new UiFailureContext(
            OperationName: string.IsNullOrWhiteSpace(operationName) ? "UiOperation" : operationName,
            AdapterId: page.Capabilities.AdapterId,
            Timeout: timeout,
            StartedAtUtc: startedAtUtc,
            FinishedAtUtc: finishedAtUtc,
            Capabilities: page.Capabilities,
            Artifacts: Array.Empty<UiFailureArtifact>(),
            PageTypeFullName: page.GetType().FullName,
            ControlPropertyName: propertyName,
            LocatorValue: definition?.LocatorValue,
            LocatorKind: definition?.LocatorKind,
            ExpectedValue: expectedValue,
            LastObservedValue: lastObservedValue);
        failureContext = AttachArtifacts(page, failureContext);
        var elapsed = finishedAtUtc - startedAtUtc;
        return new UiOperationException(
            CreateUiOperationMessage(failureMessage, timeout, elapsed, expectedValue, lastObservedValue, exception),
            failureContext,
            exception);
    }

    private static string CreateUiOperationMessage(
        string failureMessage,
        TimeSpan timeout,
        TimeSpan elapsed,
        string? expectedValue,
        string? lastObservedValue,
        Exception exception)
    {
        var timeoutMs = (int)timeout.TotalMilliseconds;
        var elapsedMs = (int)elapsed.TotalMilliseconds;

        var details = new List<string>();

        if (expectedValue is not null)
        {
            details.Add($"Expected: '{expectedValue}'");
        }

        if (lastObservedValue is not null)
        {
            details.Add($"Actual: '{lastObservedValue}'");
        }

        details.Add($"Timeout: {timeoutMs}ms");
        details.Add($"Elapsed: {elapsedMs}ms");

        var detailsText = string.Join(". ", details);

        if (exception is TimeoutException)
        {
            return $"{failureMessage} {detailsText}.";
        }

        return $"{failureMessage} Operation failed before timeout: {exception.Message}. {detailsText}.";
    }

    private static UiFailureContext AttachArtifacts(UiPage page, UiFailureContext failureContext)
    {
        if (page.ResolverInternal is not IUiArtifactCollector collector)
        {
            return failureContext;
        }

        try
        {
            var artifacts = collector.CollectAsync(failureContext).AsTask().GetAwaiter().GetResult();
            return failureContext with
            {
                Artifacts = artifacts ?? Array.Empty<UiFailureArtifact>()
            };
        }
        catch (Exception ex)
        {
            return failureContext with
            {
                Artifacts =
                [
                    new UiFailureArtifact(
                        Kind: "artifact-collection-error",
                        LogicalName: "artifact-collection-error",
                        RelativePath: $"artifacts/ui-failures/{page.Capabilities.AdapterId}/artifact-collection-error.txt",
                        ContentType: "text/plain",
                        IsRequiredByContract: false,
                        InlineTextPreview: ex.Message)
                ]
            };
        }
    }

    private static string? TryGetPropertyName<TSelf, TControl>(Expression<Func<TSelf, TControl>> selector)
    {
        return selector.Body switch
        {
            MemberExpression memberExpression => memberExpression.Member.Name,
            UnaryExpression { Operand: MemberExpression memberExpression } => memberExpression.Member.Name,
            _ => null
        };
    }

    private static UiControlDefinition? TryGetControlDefinition(Type pageType, string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(pageType.FullName))
        {
            return null;
        }

        var definitionsType = pageType.Assembly.GetType($"{pageType.FullName}Definitions");
        var definitionProperty = definitionsType?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
        return definitionProperty?.GetValue(null) as UiControlDefinition;
    }

    private static string? TryReadLastObservedValue(Func<string?>? lastObservedValueFactory)
    {
        if (lastObservedValueFactory is null)
        {
            return null;
        }

        try
        {
            return lastObservedValueFactory();
        }
        catch (Exception ex)
        {
            return $"<error: {ex.Message}>";
        }
    }

    private static ITreeItemControl? FindTreeItem(IEnumerable<ITreeItemControl> items, string itemText)
    {
        foreach (var item in items)
        {
            if (TextMatches(item.Text, itemText) || TextMatches(item.Name, itemText))
            {
                return item;
            }

            try
            {
                item.Expand();
            }
            catch
            {
                // Tree expansion is best effort for mixed runtimes.
            }

            var nested = FindTreeItem(item.Items, itemText);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool TreeItemMatches(ITreeItemControl? actual, ITreeItemControl expected, string expectedText)
    {
        if (actual is null)
        {
            return false;
        }

        return TextMatches(actual.Text, expectedText)
            || TextMatches(actual.Name, expectedText)
            || TextMatches(actual.AutomationId, expectedText)
            || TextMatches(actual.AutomationId, expected.AutomationId)
            || TextMatches(actual.Name, expected.Name)
            || TextMatches(actual.Text, expected.Text);
    }

    private static string? GetTreeItemDisplayValue(ITreeItemControl? item)
    {
        if (item is null)
        {
            return null;
        }

        return FirstNonWhiteSpace(item.Text, item.Name, item.AutomationId);
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
    }

    private static bool TextMatches(string? actual, string expected)
    {
        return string.Equals(NormalizeLookupText(actual), NormalizeLookupText(expected), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLookupText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
