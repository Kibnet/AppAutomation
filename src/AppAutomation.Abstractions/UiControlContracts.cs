namespace AppAutomation.Abstractions;

public interface IUiControl
{
    string AutomationId { get; }

    string Name { get; }

    bool IsEnabled { get; }
}

public interface ITextBoxControl : IUiControl
{
    string Text { get; set; }

    void Enter(string value);
}

public interface IButtonControl : IUiControl
{
    void Invoke();
}

public interface ILabelControl : IUiControl
{
    string Text { get; }
}

public interface IListBoxItem
{
    string? Text { get; }

    string? Name { get; }
}

public interface IListBoxControl : IUiControl
{
    IReadOnlyList<IListBoxItem> Items { get; }
}

public interface ICheckBoxControl : IUiControl
{
    bool? IsChecked { get; set; }
}

public interface IComboBoxItem
{
    string Text { get; }

    string Name { get; }
}

public interface IComboBoxControl : IUiControl
{
    IReadOnlyList<IComboBoxItem> Items { get; }

    IComboBoxItem? SelectedItem { get; }

    int SelectedIndex { get; set; }

    void SelectByIndex(int index);

    void Expand();
}

public interface ISearchPickerControl : IUiControl
{
    string SearchText { get; }

    string? SelectedItemText { get; }

    IReadOnlyList<string> Items { get; }

    void Search(string value);

    void Expand();

    void SelectItem(string itemText);
}

public interface IRadioButtonControl : IUiControl
{
    bool? IsChecked { get; set; }
}

public interface IToggleButtonControl : IUiControl
{
    bool IsToggled { get; }

    void Toggle();
}

public interface ISliderControl : IUiControl
{
    double Value { get; set; }
}

public interface IProgressBarControl : IUiControl
{
    double Value { get; }
}

public interface ICalendarControl : IUiControl
{
    IReadOnlyList<DateTime> SelectedDates { get; }

    void SelectDate(DateTime selectedDate);
}

public interface IDateTimePickerControl : IUiControl
{
    DateTime? SelectedDate { get; set; }
}

public interface ISpinnerControl : IUiControl
{
    double Value { get; set; }
}

public interface ITabItemControl : IUiControl
{
    bool IsSelected { get; }

    void SelectTab();
}

public interface ITabControl : IUiControl
{
    IReadOnlyList<ITabItemControl> Items { get; }

    void SelectTabItem(string itemText);
}

public interface ITreeItemControl : IUiControl
{
    bool IsSelected { get; set; }

    string Text { get; }

    IReadOnlyList<ITreeItemControl> Items { get; }

    void Expand();

    void SelectNode();
}

public interface ITreeControl : IUiControl
{
    IReadOnlyList<ITreeItemControl> Items { get; }

    ITreeItemControl? SelectedTreeItem { get; }
}

public interface IGridCellControl
{
    string Value { get; }
}

public interface IGridRowControl
{
    IReadOnlyList<IGridCellControl> Cells { get; }
}

public interface IGridControl : IUiControl
{
    IReadOnlyList<IGridRowControl> Rows { get; }

    IGridRowControl? GetRowByIndex(int index);
}
