using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;

namespace FlaUI.EasyUse.PageObjects;

public enum UiLocatorKind
{
    AutomationId = 0,
    Name = 1
}

public enum UiControlType
{
    AutomationElement = 0,
    TextBox = 1,
    Button = 2,
    Label = 3,
    ListBox = 4,
    CheckBox = 5,
    ComboBox = 6,
    RadioButton = 7,
    ToggleButton = 8,
    Slider = 9,
    ProgressBar = 10,
    Calendar = 11,
    DateTimePicker = 12,
    Spinner = 13,
    Tab = 14,
    Tree = 15,
    TreeItem = 16,
    DataGridView = 17,
    DataGridViewRow = 18,
    DataGridViewCell = 19,
    TabItem = 20,
    Grid = 21,
    GridRow = 22,
    GridCell = 23
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class UiControlAttribute(string propertyName, UiControlType controlType, string locatorValue) : Attribute
{
    public string PropertyName { get; } = propertyName;

    public UiControlType ControlType { get; } = controlType;

    public string LocatorValue { get; } = locatorValue;

    public UiLocatorKind LocatorKind { get; init; } = UiLocatorKind.AutomationId;

    public bool FallbackToName { get; init; } = true;
}

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

        throw new InvalidOperationException($"Element with locator [{locatorKind}:{locatorValue}] was not found.");
    }

    protected TextBox FindTextBox(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsTextBox();
    }

    protected Button FindButton(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsButton();
    }

    protected Label FindLabel(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsLabel();
    }

    protected ListBox FindListBox(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsListBox();
    }

    protected CheckBox FindCheckBox(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsCheckBox();
    }

    protected ComboBox FindComboBox(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsComboBox();
    }

    protected RadioButton FindRadioButton(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsRadioButton();
    }

    protected ToggleButton FindToggleButton(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsToggleButton();
    }

    protected Slider FindSlider(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsSlider();
    }

    protected ProgressBar FindProgressBar(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsProgressBar();
    }

    protected Calendar FindCalendar(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsCalendar();
    }

    protected DateTimePicker FindDateTimePicker(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsDateTimePicker();
    }

    protected Spinner FindSpinner(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsSpinner();
    }

    protected Tab FindTab(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsTab();
    }

    protected TabItem FindTabItem(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsTabItem();
    }

    protected Tree FindTree(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsTree();
    }

    protected TreeItem FindTreeItem(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsTreeItem();
    }

    protected DataGridView FindDataGridView(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsDataGridView();
    }

    protected GridRow FindDataGridViewRow(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        throw new NotSupportedException("Finding DataGrid row by locator is not supported in headless adapter.");
    }

    protected GridCell FindDataGridViewCell(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        throw new NotSupportedException("Finding DataGrid cell by locator is not supported in headless adapter.");
    }

    protected Grid FindGrid(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        return FindElement(locatorValue, locatorKind, fallbackToName).AsGrid();
    }

    protected GridRow FindGridRow(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        throw new NotSupportedException("Finding grid row by locator is not supported in headless adapter.");
    }

    protected GridCell FindGridCell(string locatorValue, UiLocatorKind locatorKind = UiLocatorKind.AutomationId, bool fallbackToName = true)
    {
        throw new NotSupportedException("Finding grid cell by locator is not supported in headless adapter.");
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
        var normalized = locatorValue.Trim();
        return Window.FindAllDescendants()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.AutomationId, normalized, StringComparison.Ordinal)
                || string.Equals(candidate.Name, normalized, StringComparison.Ordinal)
                || string.Equals(candidate.Name, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private AutomationElement? SearchByName(string locatorValue)
    {
        var normalized = locatorValue.Trim();
        return Window.FindAllDescendants()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, normalized, StringComparison.Ordinal)
                || string.Equals(candidate.Name, normalized, StringComparison.OrdinalIgnoreCase));
    }
}