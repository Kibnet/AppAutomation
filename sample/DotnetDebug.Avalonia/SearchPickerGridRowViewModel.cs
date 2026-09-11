using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DotnetDebug.Avalonia;

public sealed class SearchPickerGridRowViewModel : INotifyPropertyChanged
{
    private string _searchText = string.Empty;
    private string _selectedValue = "Alpha";

    public SearchPickerGridRowViewModel(string key = "Row-1")
    {
        Key = key;
    }

    public string Key { get; }

    public IReadOnlyList<string> AvailableValues { get; } = ["Alpha", "Beta", "Gamma"];

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string SelectedValue
    {
        get => _selectedValue;
        set => SetProperty(ref _selectedValue, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetProperty(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (string.Equals(field, value, System.StringComparison.Ordinal))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
