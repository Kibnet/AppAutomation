using System.ComponentModel;

namespace DotnetDebug.Avalonia;

public sealed class GridComboRowViewModel : INotifyPropertyChanged
{
    private string _state = "Draft";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; } = "ITEM-42";

    public IReadOnlyList<string> StateOptions { get; } = ["Draft", "Ready"];

    public string State
    {
        get => _state;
        set
        {
            if (string.Equals(_state, value, StringComparison.Ordinal))
            {
                return;
            }

            _state = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
        }
    }
}
