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
            $"TextBox '{textBox.AutomationId}' did not reach expected value.",
            expectedValue: value,
            lastObservedValueFactory: () => textBox.Text);
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
            expectedValue: "IsEnabled=true",
            lastObservedValueFactory: () => $"IsEnabled={button.IsEnabled}");
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
            () => target.IsSelected
                || TextMatches(tree.SelectedTreeItem?.Text, itemText)
                || TextMatches(tree.SelectedTreeItem?.Name, itemText),
            timeoutMs,
            $"Tree '{tree.AutomationId}' failed to select item.",
            expectedValue: itemText,
            lastObservedValueFactory: () => tree.SelectedTreeItem?.Text ?? tree.SelectedTreeItem?.Name ?? target.Text);
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
