using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DotnetDebug.Avalonia;

public sealed class ArmDesktopGridRowViewModel : INotifyPropertyChanged
{
    private const string BridgeAutomationId = "ArmGridAutomationBridge";
    private string _value;
    private string _state;

    public ArmDesktopGridRowViewModel(int index, string value, string state)
    {
        Index = index;
        _value = value;
        _state = state;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; }

    public string Key => $"ARM-{Index + 1:00}";

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public string State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public string RowAutomationId => $"{BridgeAutomationId}_Row{Index}";

    public string KeyCellAutomationId => $"{RowAutomationId}_Cell0";

    public string ValueCellAutomationId => $"{RowAutomationId}_Cell1";

    public string StateCellAutomationId => $"{RowAutomationId}_Cell2";

    private void SetProperty(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
