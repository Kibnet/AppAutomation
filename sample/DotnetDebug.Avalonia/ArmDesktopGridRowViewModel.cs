using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DotnetDebug.Avalonia;

public sealed class ArmDesktopGridRowViewModel : INotifyPropertyChanged
{
    private string _value;
    private string _state;
    private string _color;
    private decimal _requiredAmount;
    private bool _isApproved;
    private string _product;
    private DateTime? _scheduledDate;
    private TimeSpan? _scheduledTime;

    public ArmDesktopGridRowViewModel(int index, string value, string state, string color)
    {
        Index = index;
        _value = value;
        _state = state;
        _color = color;
        _requiredAmount = 10 + index;
        _isApproved = index % 2 == 0;
        _product = index switch
        {
            0 => "Product 42",
            1 => "Service Contract",
            _ => "Warehouse North"
        };
        _scheduledDate = new DateTime(2026, 9, 10 + index);
        _scheduledTime = new TimeSpan(8 + index, 30, 0);
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

    public string Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }

    public decimal RequiredAmount
    {
        get => _requiredAmount;
        set => SetProperty(ref _requiredAmount, value);
    }

    public bool IsApproved
    {
        get => _isApproved;
        set => SetProperty(ref _isApproved, value);
    }

    public string Product
    {
        get => _product;
        set => SetProperty(ref _product, value);
    }

    public DateTime? ScheduledDate
    {
        get => _scheduledDate;
        set => SetProperty(ref _scheduledDate, value);
    }

    public TimeSpan? ScheduledTime
    {
        get => _scheduledTime;
        set => SetProperty(ref _scheduledTime, value);
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
