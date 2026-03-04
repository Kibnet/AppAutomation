namespace Avalonia.Headless.EasyUse.PageObjects;

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
public sealed class UiControlAttribute(string propertyName, UiControlType controlType, string locatorValue) : Attribute
{
    public string PropertyName { get; } = propertyName;

    public UiControlType ControlType { get; } = controlType;

    public string LocatorValue { get; } = locatorValue;

    public UiLocatorKind LocatorKind { get; init; } = UiLocatorKind.AutomationId;

    public bool FallbackToName { get; init; } = true;
}