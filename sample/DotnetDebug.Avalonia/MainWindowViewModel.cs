using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DotnetDebug.Avalonia;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private string _gridResultLabel = string.Empty;
    private string _gridSelectionLabel = "No row selected";
    private string _dataGridErrorText = string.Empty;
    private DataGridRowViewModel? _selectedDataGridRow;

    public MainWindowViewModel()
    {
        MultiSelectItems =
        [
            new MultiSelectItemViewModel("Alpha"),
            new MultiSelectItemViewModel("Beta"),
            new MultiSelectItemViewModel("Gamma"),
            new MultiSelectItemViewModel("Delta"),
            new MultiSelectItemViewModel("Epsilon"),
            new MultiSelectItemViewModel("Zeta"),
            new MultiSelectItemViewModel("Eta"),
            new MultiSelectItemViewModel("Theta"),
            new MultiSelectItemViewModel("Iota"),
            new MultiSelectItemViewModel("Kappa"),
            new MultiSelectItemViewModel("Lambda"),
            new MultiSelectItemViewModel("Mu"),
            new MultiSelectItemViewModel("Nu"),
            new MultiSelectItemViewModel("Xi"),
            new MultiSelectItemViewModel("Omicron"),
            new MultiSelectItemViewModel("Pi"),
            new MultiSelectItemViewModel("Rho"),
            new MultiSelectItemViewModel("Sigma"),
            new MultiSelectItemViewModel("Tau"),
            new MultiSelectItemViewModel("Upsilon"),
            new MultiSelectItemViewModel("Phi"),
            new MultiSelectItemViewModel("Chi"),
            new MultiSelectItemViewModel("Psi"),
            new MultiSelectItemViewModel("Omega")
        ];
        SelectedMultiSelectItems.Add(MultiSelectItems[0]);
        SearchPickerGridRows = [SearchPickerGridRow];
    }

    public ObservableCollection<DataGridRowViewModel> DataGridRows { get; } = [];

    public SearchPickerGridRowViewModel SearchPickerGridRow { get; } = new();

    public IReadOnlyList<SearchPickerGridRowViewModel> SearchPickerGridRows { get; }
    
    public ObservableCollection<MultiSelectItemViewModel> MultiSelectItems { get; }

    public ObservableCollection<MultiSelectItemViewModel> SelectedMultiSelectItems { get; } = [];

    public string GridResultLabel
    {
        get => _gridResultLabel;
        set => SetProperty(ref _gridResultLabel, value);
    }

    public string GridSelectionLabel
    {
        get => _gridSelectionLabel;
        set => SetProperty(ref _gridSelectionLabel, value);
    }

    public string DataGridErrorText
    {
        get => _dataGridErrorText;
        set => SetProperty(ref _dataGridErrorText, value);
    }

    public DataGridRowViewModel? SelectedDataGridRow
    {
        get => _selectedDataGridRow;
        set
        {
            if (SetProperty(ref _selectedDataGridRow, value))
            {
                GridSelectionLabel = value is null ? "No row selected" : $"Selected row: {value.Row}";
            }
        }
    }

    public void BuildGrid(int requestedRows)
    {
        if (requestedRows <= 0)
        {
            DataGridErrorText = "Rows must be greater than zero.";
            return;
        }

        DataGridRows.Clear();
        for (var rowIndex = 0; rowIndex < requestedRows; rowIndex++)
        {
            var value = rowIndex * 3 + 7;
            DataGridRows.Add(new DataGridRowViewModel(rowIndex, value));
        }

        DataGridErrorText = string.Empty;
        GridResultLabel = $"Grid rows: {DataGridRows.Count}";
        SelectedDataGridRow = null;
    }

    public void ClearGrid()
    {
        DataGridRows.Clear();
        DataGridErrorText = string.Empty;
        GridResultLabel = string.Empty;
        SelectedDataGridRow = null;
        GridSelectionLabel = "No row selected";
    }

    public void SelectRowByIndex(int selectedIndex)
    {
        DataGridErrorText = string.Empty;

        if (DataGridRows.Count == 0)
        {
            DataGridErrorText = "Build grid before selecting a row.";
            return;
        }

        if (selectedIndex < 0 || selectedIndex >= DataGridRows.Count)
        {
            DataGridErrorText = $"Row index {selectedIndex} is out of range.";
            return;
        }

        SelectedDataGridRow = DataGridRows[selectedIndex];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class MultiSelectItemViewModel
{
    public MultiSelectItemViewModel(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public override string ToString() => Name;
}
