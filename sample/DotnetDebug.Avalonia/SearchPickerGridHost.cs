using System.Collections;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace DotnetDebug.Avalonia;

public sealed class SearchPickerGridHost : ContentControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SearchPickerGridHost, IEnumerable?>(nameof(ItemsSource));

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty || change.Property == ContentProperty)
        {
            SynchronizeContentDataContext();
        }
    }

    private void SynchronizeContentDataContext()
    {
        if (Content is Control content)
        {
            content.DataContext = ItemsSource?.Cast<object?>().FirstOrDefault();
        }
    }
}
