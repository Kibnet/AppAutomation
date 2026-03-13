using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AppAutomation.Abstractions;

public static class UiPageExtensions
{
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
            $"TextBox '{textBox.AutomationId}' did not reach '{value}'.",
            () => textBox.Text);
        return page;
    }

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
            () => button.IsEnabled.ToString(CultureInfo.InvariantCulture));
        button.Invoke();
        return page;
    }

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
            () => checkBox.IsEnabled.ToString(CultureInfo.InvariantCulture));
        checkBox.IsChecked = isChecked;
        WaitUntil(
            page,
            selector,
            () => checkBox.IsChecked == isChecked,
            timeoutMs,
            $"CheckBox '{checkBox.AutomationId}' did not become '{isChecked}'.",
            () => checkBox.IsChecked?.ToString());
        return page;
    }

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
            () => radioButton.IsEnabled.ToString(CultureInfo.InvariantCulture));
        radioButton.IsChecked = isChecked;
        WaitUntil(
            page,
            selector,
            () => radioButton.IsChecked == isChecked,
            timeoutMs,
            $"RadioButton '{radioButton.AutomationId}' did not become '{isChecked}'.",
            () => radioButton.IsChecked?.ToString());
        return page;
    }

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
            () => toggle.IsEnabled.ToString(CultureInfo.InvariantCulture));
        if (toggle.IsToggled != isToggled)
        {
            toggle.Toggle();
        }

        WaitUntil(
            page,
            selector,
            () => toggle.IsToggled == isToggled,
            timeoutMs,
            $"Toggle '{toggle.AutomationId}' did not become '{isToggled}'.",
            () => toggle.IsToggled.ToString(CultureInfo.InvariantCulture));
        return page;
    }

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
            () => comboBox.IsEnabled.ToString(CultureInfo.InvariantCulture));
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
            throw new InvalidOperationException($"ComboBox item '{itemText}' was not found.");
        }

        comboBox.Select(index.Value);
        WaitUntil(
            page,
            selector,
            () => comboBox.SelectedIndex == index.Value
                || string.Equals(NormalizeLookupText(comboBox.SelectedItem?.Text), target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeLookupText(comboBox.SelectedItem?.Name), target, StringComparison.OrdinalIgnoreCase),
            timeoutMs,
            $"ComboBox '{comboBox.AutomationId}' did not select '{itemText}'.",
            () => comboBox.SelectedItem?.Text ?? comboBox.SelectedItem?.Name ?? comboBox.SelectedIndex.ToString(CultureInfo.InvariantCulture));
        return page;
    }

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
            () => slider.IsEnabled.ToString(CultureInfo.InvariantCulture));
        slider.Value = value;
        WaitUntil(
            page,
            selector,
            () => Math.Abs(slider.Value - value) < 0.001,
            timeoutMs,
            $"Slider '{slider.AutomationId}' did not reach '{value}'.",
            () => slider.Value.ToString(CultureInfo.InvariantCulture));
        return page;
    }

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
            $"Spinner-like text box '{textBox.AutomationId}' did not reach '{expected}'.",
            () => textBox.Text);
        return page;
    }

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
            () => tab.IsEnabled.ToString(CultureInfo.InvariantCulture));
        tab.SelectTabItem(itemText);

        WaitUntil(
            page,
            selector,
            () => tab.Items.Any(item =>
                item.IsSelected &&
                TextMatches(item.Name, itemText)),
            timeoutMs,
            $"Tab '{tab.AutomationId}' did not select '{itemText}'.",
            () => tab.Items.FirstOrDefault(static item => item.IsSelected)?.Name);
        return page;
    }

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
            () => searchPicker.IsEnabled.ToString(CultureInfo.InvariantCulture));

        searchPicker.Search(searchText);
        WaitUntil(
            page,
            selector,
            () => string.Equals(searchPicker.SearchText, searchText, StringComparison.Ordinal),
            timeoutMs,
            $"Search picker '{searchPicker.AutomationId}' did not accept search text '{searchText}'.",
            () => searchPicker.SearchText);

        searchPicker.Select(itemText);
        WaitUntil(
            page,
            selector,
            () => string.Equals(searchPicker.SelectedItemText, itemText, StringComparison.OrdinalIgnoreCase),
            timeoutMs,
            $"Search picker '{searchPicker.AutomationId}' did not select '{itemText}'.",
            () => searchPicker.SelectedItemText);
        return page;
    }

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
            () => tabItem.IsEnabled.ToString(CultureInfo.InvariantCulture));
        tabItem.Select();
        WaitUntil(
            page,
            selector,
            () => tabItem.IsSelected,
            timeoutMs,
            $"Tab item '{tabItem.AutomationId}' was not selected.",
            () => tabItem.IsSelected.ToString(CultureInfo.InvariantCulture));
        return page;
    }

    public static TSelf SelectTreeItem<TSelf>(
        this TSelf page,
        Expression<Func<TSelf, ITreeControl>> selector,
        string itemText,
        int timeoutMs = 5000)
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
            () => tree.IsEnabled.ToString(CultureInfo.InvariantCulture));

        var target = FindTreeItem(tree.Items, itemText);
        if (target is null)
        {
            throw new InvalidOperationException($"Tree item '{itemText}' was not found.");
        }

        target.Select();
        WaitUntil(
            page,
            selector,
            () => target.IsSelected
                || TextMatches(tree.SelectedTreeItem?.Text, itemText)
                || TextMatches(tree.SelectedTreeItem?.Name, itemText),
            timeoutMs,
            $"Tree item '{itemText}' was not selected.",
            () => tree.SelectedTreeItem?.Text ?? tree.SelectedTreeItem?.Name ?? target.Text);
        return page;
    }

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
            () => datePicker.IsEnabled.ToString(CultureInfo.InvariantCulture));
        datePicker.SelectedDate = date.Date;
        WaitUntil(
            page,
            selector,
            () => datePicker.SelectedDate?.Date == date.Date,
            timeoutMs,
            $"Date picker '{datePicker.AutomationId}' did not reach '{date:yyyy-MM-dd}'.",
            () => datePicker.SelectedDate?.ToString("O", CultureInfo.InvariantCulture));
        return page;
    }

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
            () => calendar.IsEnabled.ToString(CultureInfo.InvariantCulture));
        calendar.SelectDate(date.Date);
        WaitUntil(
            page,
            selector,
            () => calendar.SelectedDates.Any(candidate => candidate.Date == date.Date),
            timeoutMs,
            $"Calendar '{calendar.AutomationId}' did not reach '{date:yyyy-MM-dd}'.",
            () => string.Join(", ", calendar.SelectedDates.Select(static candidate => candidate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
        return page;
    }

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
            $"Progress bar '{progressBar.AutomationId}' did not reach '{expectedMin}'.",
            () => progressBar.Value.ToString(CultureInfo.InvariantCulture));
        return page;
    }

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
            $"Radio button '{radioButton.AutomationId}' did not become '{expected}'.",
            () => radioButton.IsChecked?.ToString());
        return page;
    }

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
            $"Tab item '{tabItem.AutomationId}' did not become '{expected}'.",
            () => tabItem.IsSelected.ToString(CultureInfo.InvariantCulture));
        return page;
    }

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
            $"Toggle '{toggle.AutomationId}' did not become '{expected}'.",
            () => toggle.IsToggled.ToString(CultureInfo.InvariantCulture));
        return page;
    }

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
            $"ListBox '{listBox.AutomationId}' did not contain '{expectedText}'.",
            () => string.Join(", ", listBox.Items.Select(static item => item.Text ?? item.Name ?? string.Empty)));
        return page;
    }

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
            $"Control '{control.AutomationId}' did not reach '{expectedText}'.",
            () => control.Name);
        return page;
    }

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
            $"Control '{control.AutomationId}' did not contain '{expectedPart}'.",
            () => control.Name);
        return page;
    }

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
            $"ListBox '{listBox.AutomationId}' did not reach '{minCount}' items.",
            () => listBox.Items.Count.ToString(CultureInfo.InvariantCulture));
        return page;
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

    private static void WaitUntil<TSelf, TControl>(
        TSelf page,
        Expression<Func<TSelf, TControl>> selector,
        Func<bool> condition,
        int timeoutMs,
        string timeoutMessage,
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
        Func<string?>? lastObservedValueFactory,
        string operationName,
        Exception exception)
        where TSelf : UiPage
    {
        var propertyName = TryGetPropertyName(selector);
        var definition = TryGetControlDefinition(page.GetType(), propertyName);
        var failureContext = new UiFailureContext(
            OperationName: string.IsNullOrWhiteSpace(operationName) ? "UiOperation" : operationName,
            AdapterId: page.Capabilities.AdapterId,
            Timeout: timeout,
            StartedAtUtc: startedAtUtc,
            FinishedAtUtc: DateTimeOffset.UtcNow,
            Capabilities: page.Capabilities,
            Artifacts: Array.Empty<UiFailureArtifact>(),
            PageTypeFullName: page.GetType().FullName,
            ControlPropertyName: propertyName,
            LocatorValue: definition?.LocatorValue,
            LocatorKind: definition?.LocatorKind,
            LastObservedValue: TryReadLastObservedValue(lastObservedValueFactory));
        failureContext = AttachArtifacts(page, failureContext);
        return new UiOperationException(
            CreateUiOperationMessage(failureMessage, exception),
            failureContext,
            exception);
    }

    private static string CreateUiOperationMessage(string failureMessage, Exception exception)
    {
        return exception is TimeoutException
            ? failureMessage
            : $"{failureMessage} Operation failed before timeout: {exception.Message}";
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
