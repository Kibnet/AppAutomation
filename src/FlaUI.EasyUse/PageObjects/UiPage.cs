using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Exceptions;

namespace FlaUI.EasyUse.PageObjects;

public abstract class UiPage(Window window, ConditionFactory conditionFactory)
{
    protected Window Window { get; } = window ?? throw new ArgumentNullException(nameof(window));

    protected ConditionFactory ConditionFactory { get; } = conditionFactory ?? throw new ArgumentNullException(nameof(conditionFactory));

    protected AutomationElement FindElement(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locatorValue);

        var element = Window.FindFirstDescendant(CreateCondition(locatorValue, locatorKind));
        if (element is not null)
        {
            return element;
        }

        if (fallbackToName && locatorKind != UiLocatorKind.Name)
        {
            element = Window.FindFirstDescendant(CreateCondition(locatorValue, UiLocatorKind.Name));
            if (element is not null)
            {
                return element;
            }
        }

        var rootSearch = locatorKind switch
        {
            UiLocatorKind.AutomationId => SearchByAutomationId(locatorValue),
            UiLocatorKind.Name => SearchByName(locatorValue),
            _ => SearchByAutomationId(locatorValue)
        };

        if (rootSearch is not null)
        {
            return rootSearch;
        }

        throw new ElementNotAvailableException($"Element with locator [{locatorKind}:{locatorValue}] was not found.");
    }

    protected TextBox FindTextBox(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsTextBox();
    }

    protected Button FindButton(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsButton();
    }

    protected Label FindLabel(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsLabel();
    }

    protected ListBox FindListBox(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsListBox();
    }

    protected CheckBox FindCheckBox(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsCheckBox();
    }

    protected ComboBox FindComboBox(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsComboBox();
    }

    protected RadioButton FindRadioButton(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsRadioButton();
    }

    protected ToggleButton FindToggleButton(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsToggleButton();
    }

    protected Slider FindSlider(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsSlider();
    }

    protected ProgressBar FindProgressBar(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsProgressBar();
    }

    protected Calendar FindCalendar(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsCalendar();
    }

    protected DateTimePicker FindDateTimePicker(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsDateTimePicker();
    }

    protected Spinner FindSpinner(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsSpinner();
    }

    protected Tab FindTab(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsTab();
    }

    protected TabItem FindTabItem(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsTabItem();
    }

    protected Tree FindTree(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsTree();
    }

    protected TreeItem FindTreeItem(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsTreeItem();
    }

    protected DataGridView FindDataGridView(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsDataGridView();
    }

    protected GridRow FindDataGridViewRow(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsGridRow();
    }

    protected GridCell FindDataGridViewCell(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsGridCell();
    }

    protected Grid FindGrid(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsGrid();
    }

    protected GridRow FindGridRow(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsGridRow();
    }

    protected GridCell FindGridCell(
        string locatorValue,
        UiLocatorKind locatorKind = UiLocatorKind.AutomationId,
        bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsGridCell();
    }

    private PropertyCondition CreateCondition(string locatorValue, UiLocatorKind locatorKind)
    {
        return locatorKind switch
        {
            UiLocatorKind.AutomationId => ConditionFactory.ByAutomationId(locatorValue),
            UiLocatorKind.Name => ConditionFactory.ByName(locatorValue),
            _ => throw new ArgumentOutOfRangeException(nameof(locatorKind), locatorKind, "Unsupported locator kind.")
        };
    }

    private AutomationElement? SearchByAutomationId(string locatorValue)
    {
        var direct = Window.FindAllDescendants(factory => factory.ByAutomationId(locatorValue));

        if (direct.Length > 0)
        {
            return direct.FirstOrDefault(candidate => candidate?.IsAvailable == true);
        }

        var normalized = locatorValue.Trim().ToLowerInvariant();
        return Window.FindAllDescendants()
            .FirstOrDefault(candidate =>
            {
                if (!candidate.IsAvailable)
                {
                    return false;
                }

                var automationId = TryReadAutomationId(candidate)?.ToLowerInvariant();
                return automationId is not null && (automationId == normalized || automationId.StartsWith(normalized));
            });
    }

    private AutomationElement? SearchByName(string locatorValue)
    {
        var direct = Window.FindAllDescendants(factory => factory.ByName(locatorValue));
        if (direct.Length > 0)
        {
            return direct.FirstOrDefault(candidate => candidate?.IsAvailable == true);
        }

        var normalized = locatorValue.Trim().ToLowerInvariant();
        return Window.FindAllDescendants()
            .FirstOrDefault(candidate =>
            {
                if (!candidate.IsAvailable)
                {
                    return false;
                }

                var name = TryReadName(candidate)?.ToLowerInvariant();
                return name is not null && (name == normalized || name.Contains(normalized));
            });
    }

    private static string? TryReadAutomationId(AutomationElement candidate)
    {
        try
        {
            return candidate.AutomationId;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadName(AutomationElement candidate)
    {
        try
        {
            return candidate.Name;
        }
        catch
        {
            return null;
        }
    }
}
