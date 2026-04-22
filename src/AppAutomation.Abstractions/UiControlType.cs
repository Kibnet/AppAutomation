namespace AppAutomation.Abstractions;

/// <summary>
/// Specifies the type of UI control for source generation and control resolution.
/// </summary>
/// <remarks>
/// This enumeration is used with <see cref="UiControlAttribute"/> to specify which
/// control interface type should be generated for a property.
/// </remarks>
public enum UiControlType
{
    /// <summary>
    /// A generic automation element with basic properties only.
    /// </summary>
    AutomationElement = 0,

    /// <summary>
    /// A text input control. Maps to <see cref="ITextBoxControl"/>.
    /// </summary>
    TextBox = 1,

    /// <summary>
    /// A clickable button control. Maps to <see cref="IButtonControl"/>.
    /// </summary>
    Button = 2,

    /// <summary>
    /// A read-only text label. Maps to <see cref="ILabelControl"/>.
    /// </summary>
    Label = 3,

    /// <summary>
    /// A list box with selectable items. Maps to <see cref="IListBoxControl"/>.
    /// </summary>
    ListBox = 4,

    /// <summary>
    /// A check box with checked/unchecked state. Maps to <see cref="ICheckBoxControl"/>.
    /// </summary>
    CheckBox = 5,

    /// <summary>
    /// A combo box (drop-down) control. Maps to <see cref="IComboBoxControl"/>.
    /// </summary>
    ComboBox = 6,

    /// <summary>
    /// A radio button in a mutually exclusive group. Maps to <see cref="IRadioButtonControl"/>.
    /// </summary>
    RadioButton = 7,

    /// <summary>
    /// A toggle button with on/off state. Maps to <see cref="IToggleButtonControl"/>.
    /// </summary>
    ToggleButton = 8,

    /// <summary>
    /// A slider control for range selection. Maps to <see cref="ISliderControl"/>.
    /// </summary>
    Slider = 9,

    /// <summary>
    /// A progress indicator. Maps to <see cref="IProgressBarControl"/>.
    /// </summary>
    ProgressBar = 10,

    /// <summary>
    /// A calendar control for date selection. Maps to <see cref="ICalendarControl"/>.
    /// </summary>
    Calendar = 11,

    /// <summary>
    /// A date-time picker control. Maps to <see cref="IDateTimePickerControl"/>.
    /// </summary>
    DateTimePicker = 12,

    /// <summary>
    /// A numeric spinner (up/down) control. Maps to <see cref="ISpinnerControl"/>.
    /// </summary>
    Spinner = 13,

    /// <summary>
    /// A tab control containing tab items. Maps to <see cref="ITabControl"/>.
    /// </summary>
    Tab = 14,

    /// <summary>
    /// A hierarchical tree control. Maps to <see cref="ITreeControl"/>.
    /// </summary>
    Tree = 15,

    /// <summary>
    /// An individual tree node. Maps to <see cref="ITreeItemControl"/>.
    /// </summary>
    TreeItem = 16,

    /// <summary>
    /// A data grid view control (legacy).
    /// </summary>
    DataGridView = 17,

    /// <summary>
    /// A row within a data grid view (legacy).
    /// </summary>
    DataGridViewRow = 18,

    /// <summary>
    /// A cell within a data grid view (legacy).
    /// </summary>
    DataGridViewCell = 19,

    /// <summary>
    /// An individual tab within a tab control. Maps to <see cref="ITabItemControl"/>.
    /// </summary>
    TabItem = 20,

    /// <summary>
    /// A data grid control. Maps to <see cref="IGridControl"/>.
    /// </summary>
    Grid = 21,

    /// <summary>
    /// A row within a grid control. Maps to <see cref="IGridRowControl"/>.
    /// </summary>
    GridRow = 22,

    /// <summary>
    /// A cell within a grid control. Maps to <see cref="IGridCellControl"/>.
    /// </summary>
    GridCell = 23,

    /// <summary>
    /// A composite search picker control. Maps to <see cref="ISearchPickerControl"/>.
    /// </summary>
    SearchPicker = 24
}
