using System.Globalization;

namespace DotnetDebug.Avalonia;

public sealed class DataGridRowViewModel(int index, int value)
{
    public string Row => $"R{index + 1}";

    public string Value => value.ToString(CultureInfo.InvariantCulture);

    public string Parity => value % 2 == 0 ? "Even" : "Odd";
}
