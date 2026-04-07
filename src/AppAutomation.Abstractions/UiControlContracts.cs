namespace AppAutomation.Abstractions;

/// <summary>
/// Base interface for all UI controls in the automation framework.
/// </summary>
/// <remarks>
/// All control types derive from this interface, providing common properties
/// for identification and state. Implementations should wrap platform-specific
/// automation elements (e.g., FlaUI, Avalonia Headless).
/// </remarks>
public interface IUiControl
{
    /// <summary>
    /// Gets the automation identifier of the control.
    /// </summary>
    /// <value>The unique automation ID used to locate the control.</value>
    string AutomationId { get; }

    /// <summary>
    /// Gets the display name of the control.
    /// </summary>
    /// <value>The human-readable name of the control, often used as a fallback locator.</value>
    string Name { get; }

    /// <summary>
    /// Gets a value indicating whether the control is enabled and can accept user input.
    /// </summary>
    /// <value><see langword="true"/> if the control is enabled; otherwise, <see langword="false"/>.</value>
    bool IsEnabled { get; }
}

/// <summary>
/// Represents a text input control such as a TextBox or text field.
/// </summary>
public interface ITextBoxControl : IUiControl
{
    /// <summary>
    /// Gets or sets the text content of the control.
    /// </summary>
    /// <value>The current text value of the text box.</value>
    string Text { get; set; }

    /// <summary>
    /// Enters text into the control, clearing any existing content.
    /// </summary>
    /// <param name="value">The text to enter into the control.</param>
    void Enter(string value);
}

/// <summary>
/// Represents a clickable button control.
/// </summary>
public interface IButtonControl : IUiControl
{
    /// <summary>
    /// Invokes (clicks) the button.
    /// </summary>
    void Invoke();
}

/// <summary>
/// Represents a read-only label control that displays text.
/// </summary>
public interface ILabelControl : IUiControl
{
    /// <summary>
    /// Gets the text content displayed by the label.
    /// </summary>
    /// <value>The label's display text.</value>
    string Text { get; }
}

/// <summary>
/// Represents an individual item within a <see cref="IListBoxControl"/>.
/// </summary>
public interface IListBoxItem
{
    /// <summary>
    /// Gets the text content of the list item.
    /// </summary>
    /// <value>The item's text, or <see langword="null"/> if not available.</value>
    string? Text { get; }

    /// <summary>
    /// Gets the name of the list item.
    /// </summary>
    /// <value>The item's name, or <see langword="null"/> if not available.</value>
    string? Name { get; }
}

/// <summary>
/// Represents a list box control containing selectable items.
/// </summary>
public interface IListBoxControl : IUiControl
{
    /// <summary>
    /// Gets the collection of items in the list box.
    /// </summary>
    /// <value>A read-only list of <see cref="IListBoxItem"/> instances.</value>
    IReadOnlyList<IListBoxItem> Items { get; }
}

/// <summary>
/// Represents a list box control that supports interactive item selection.
/// </summary>
public interface ISelectableListBoxControl : IListBoxControl
{
    /// <summary>
    /// Gets the text of the currently selected item, if any.
    /// </summary>
    string? SelectedItemText { get; }

    /// <summary>
    /// Selects an item by its display text.
    /// </summary>
    /// <param name="itemText">The text of the item to select.</param>
    void SelectItem(string itemText);
}

/// <summary>
/// Represents a check box control with a tri-state checked value.
/// </summary>
public interface ICheckBoxControl : IUiControl
{
    /// <summary>
    /// Gets or sets the checked state of the check box.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if checked, <see langword="false"/> if unchecked,
    /// or <see langword="null"/> for indeterminate state.
    /// </value>
    bool? IsChecked { get; set; }
}

/// <summary>
/// Represents an individual item within a <see cref="IComboBoxControl"/>.
/// </summary>
public interface IComboBoxItem
{
    /// <summary>
    /// Gets the display text of the combo box item.
    /// </summary>
    /// <value>The item's display text.</value>
    string Text { get; }

    /// <summary>
    /// Gets the name of the combo box item.
    /// </summary>
    /// <value>The item's name identifier.</value>
    string Name { get; }
}

/// <summary>
/// Represents a combo box (drop-down) control with selectable items.
/// </summary>
public interface IComboBoxControl : IUiControl
{
    /// <summary>
    /// Gets the collection of items in the combo box.
    /// </summary>
    /// <value>A read-only list of <see cref="IComboBoxItem"/> instances.</value>
    IReadOnlyList<IComboBoxItem> Items { get; }

    /// <summary>
    /// Gets the currently selected item.
    /// </summary>
    /// <value>The selected <see cref="IComboBoxItem"/>, or <see langword="null"/> if nothing is selected.</value>
    IComboBoxItem? SelectedItem { get; }

    /// <summary>
    /// Gets or sets the zero-based index of the selected item.
    /// </summary>
    /// <value>The selected item index, or -1 if nothing is selected.</value>
    int SelectedIndex { get; set; }

    /// <summary>
    /// Selects an item by its zero-based index.
    /// </summary>
    /// <param name="index">The index of the item to select.</param>
    void SelectByIndex(int index);

    /// <summary>
    /// Expands the combo box to show its drop-down list.
    /// </summary>
    void Expand();
}

/// <summary>
/// Represents a composite search picker control that combines a search input with a results list.
/// </summary>
/// <remarks>
/// This interface abstracts a common UI pattern where users type to search and then select from filtered results.
/// Use <see cref="SearchPickerParts"/> and <see cref="UiControlResolverExtensions.WithSearchPicker"/> to compose
/// this control from individual primitive controls.
/// </remarks>
public interface ISearchPickerControl : IUiControl
{
    /// <summary>
    /// Gets the current text in the search input field.
    /// </summary>
    /// <value>The search query text.</value>
    string SearchText { get; }

    /// <summary>
    /// Gets the text of the currently selected item.
    /// </summary>
    /// <value>The selected item's text, or <see langword="null"/> if nothing is selected.</value>
    string? SelectedItemText { get; }

    /// <summary>
    /// Gets the collection of available items in the results list.
    /// </summary>
    /// <value>A read-only list of item display texts.</value>
    IReadOnlyList<string> Items { get; }

    /// <summary>
    /// Enters a search query into the search input field.
    /// </summary>
    /// <param name="value">The search text to enter.</param>
    void Search(string value);

    /// <summary>
    /// Expands the results list to show available items.
    /// </summary>
    void Expand();

    /// <summary>
    /// Selects an item from the results list by its display text.
    /// </summary>
    /// <param name="itemText">The text of the item to select.</param>
    void SelectItem(string itemText);
}

/// <summary>
/// Represents a radio button control within a mutually exclusive group.
/// </summary>
public interface IRadioButtonControl : IUiControl
{
    /// <summary>
    /// Gets or sets the checked state of the radio button.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if selected, <see langword="false"/> if not selected,
    /// or <see langword="null"/> for indeterminate state.
    /// </value>
    bool? IsChecked { get; set; }
}

/// <summary>
/// Represents a toggle button control that switches between on/off states.
/// </summary>
public interface IToggleButtonControl : IUiControl
{
    /// <summary>
    /// Gets a value indicating whether the toggle is currently in the "on" state.
    /// </summary>
    /// <value><see langword="true"/> if toggled on; otherwise, <see langword="false"/>.</value>
    bool IsToggled { get; }

    /// <summary>
    /// Toggles the button to the opposite state.
    /// </summary>
    void Toggle();
}

/// <summary>
/// Represents a slider control for selecting a value within a range.
/// </summary>
public interface ISliderControl : IUiControl
{
    /// <summary>
    /// Gets or sets the current value of the slider.
    /// </summary>
    /// <value>The slider's current position value.</value>
    double Value { get; set; }
}

/// <summary>
/// Represents a progress bar control that displays completion progress.
/// </summary>
public interface IProgressBarControl : IUiControl
{
    /// <summary>
    /// Gets the current progress value.
    /// </summary>
    /// <value>The progress value, typically between 0 and 100.</value>
    double Value { get; }
}

/// <summary>
/// Represents a calendar control for date selection.
/// </summary>
public interface ICalendarControl : IUiControl
{
    /// <summary>
    /// Gets the collection of currently selected dates.
    /// </summary>
    /// <value>A read-only list of selected <see cref="DateTime"/> values.</value>
    IReadOnlyList<DateTime> SelectedDates { get; }

    /// <summary>
    /// Selects a specific date on the calendar.
    /// </summary>
    /// <param name="selectedDate">The date to select.</param>
    void SelectDate(DateTime selectedDate);
}

/// <summary>
/// Represents a date-time picker control for selecting a single date.
/// </summary>
public interface IDateTimePickerControl : IUiControl
{
    /// <summary>
    /// Gets or sets the selected date.
    /// </summary>
    /// <value>The selected <see cref="DateTime"/>, or <see langword="null"/> if no date is selected.</value>
    DateTime? SelectedDate { get; set; }
}

/// <summary>
/// Represents a numeric spinner (up/down) control.
/// </summary>
public interface ISpinnerControl : IUiControl
{
    /// <summary>
    /// Gets or sets the current numeric value of the spinner.
    /// </summary>
    /// <value>The spinner's current value.</value>
    double Value { get; set; }
}

/// <summary>
/// Represents an individual tab within a <see cref="ITabControl"/>.
/// </summary>
public interface ITabItemControl : IUiControl
{
    /// <summary>
    /// Gets a value indicating whether this tab is currently selected.
    /// </summary>
    /// <value><see langword="true"/> if the tab is selected; otherwise, <see langword="false"/>.</value>
    bool IsSelected { get; }

    /// <summary>
    /// Selects this tab, making it the active tab.
    /// </summary>
    void SelectTab();
}

/// <summary>
/// Represents a tab control containing multiple tab items.
/// </summary>
public interface ITabControl : IUiControl
{
    /// <summary>
    /// Gets the collection of tab items.
    /// </summary>
    /// <value>A read-only list of <see cref="ITabItemControl"/> instances.</value>
    IReadOnlyList<ITabItemControl> Items { get; }

    /// <summary>
    /// Selects a tab by its display text.
    /// </summary>
    /// <param name="itemText">The text of the tab to select.</param>
    void SelectTabItem(string itemText);
}

/// <summary>
/// Represents an individual node within a tree control.
/// </summary>
public interface ITreeItemControl : IUiControl
{
    /// <summary>
    /// Gets or sets a value indicating whether this tree node is selected.
    /// </summary>
    /// <value><see langword="true"/> if selected; otherwise, <see langword="false"/>.</value>
    bool IsSelected { get; set; }

    /// <summary>
    /// Gets the display text of the tree node.
    /// </summary>
    /// <value>The node's text label.</value>
    string Text { get; }

    /// <summary>
    /// Gets the child items of this tree node.
    /// </summary>
    /// <value>A read-only list of child <see cref="ITreeItemControl"/> instances.</value>
    IReadOnlyList<ITreeItemControl> Items { get; }

    /// <summary>
    /// Expands the tree node to reveal its children.
    /// </summary>
    void Expand();

    /// <summary>
    /// Selects this tree node.
    /// </summary>
    void SelectNode();
}

/// <summary>
/// Represents a hierarchical tree control.
/// </summary>
public interface ITreeControl : IUiControl
{
    /// <summary>
    /// Gets the root-level items of the tree.
    /// </summary>
    /// <value>A read-only list of root <see cref="ITreeItemControl"/> instances.</value>
    IReadOnlyList<ITreeItemControl> Items { get; }

    /// <summary>
    /// Gets the currently selected tree item.
    /// </summary>
    /// <value>The selected <see cref="ITreeItemControl"/>, or <see langword="null"/> if nothing is selected.</value>
    ITreeItemControl? SelectedTreeItem { get; }
}

/// <summary>
/// Represents an individual cell within a grid row.
/// </summary>
public interface IGridCellControl
{
    /// <summary>
    /// Gets the value displayed in the cell.
    /// </summary>
    /// <value>The cell's string value.</value>
    string Value { get; }
}

/// <summary>
/// Represents a row within a grid control.
/// </summary>
public interface IGridRowControl
{
    /// <summary>
    /// Gets the collection of cells in this row.
    /// </summary>
    /// <value>A read-only list of <see cref="IGridCellControl"/> instances.</value>
    IReadOnlyList<IGridCellControl> Cells { get; }
}

/// <summary>
/// Represents a data grid control with rows and cells.
/// </summary>
public interface IGridControl : IUiControl
{
    /// <summary>
    /// Gets all rows in the grid.
    /// </summary>
    /// <value>A read-only list of <see cref="IGridRowControl"/> instances.</value>
    IReadOnlyList<IGridRowControl> Rows { get; }

    /// <summary>
    /// Gets a row by its zero-based index.
    /// </summary>
    /// <param name="index">The row index.</param>
    /// <returns>The <see cref="IGridRowControl"/> at the specified index, or <see langword="null"/> if the index is out of range.</returns>
    IGridRowControl? GetRowByIndex(int index);
}
