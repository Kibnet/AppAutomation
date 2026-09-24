using System.Globalization;
using System.ComponentModel.DataAnnotations;

namespace DotnetDebug.Avalonia;

public sealed class DataGridRowViewModel(int index, int value)
{
    public int Index => index;

    [Key]
    public string Row => $"R{index + 1}";

    public string Value => value.ToString(CultureInfo.InvariantCulture);

    public string Parity => value % 2 == 0 ? "Even" : "Odd";

    public string HiddenIdentity => $"item-{Index + 1}";

}
