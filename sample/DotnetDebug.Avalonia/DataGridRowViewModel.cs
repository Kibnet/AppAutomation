using System.Globalization;

namespace DotnetDebug.Avalonia;

public sealed class DataGridRowViewModel(int index, int value)
{
    private const string EremexAutomationBridgeId = "EremexDemoDataGridAutomationBridge";

    public int Index => index;

    public string Row => $"R{index + 1}";

    public string Value => value.ToString(CultureInfo.InvariantCulture);

    public string Parity => value % 2 == 0 ? "Even" : "Odd";

    public string EremexRow => $"EX-{Row}";

    public string EremexValue => $"EX-{Value}";

    public string EremexParity => $"EX-{Parity}";

    public string EremexAutomationRowId => $"{EremexAutomationBridgeId}_Row{Index}";

    public string EremexAutomationCell0Id => $"{EremexAutomationRowId}_Cell0";

    public string EremexAutomationCell1Id => $"{EremexAutomationRowId}_Cell1";

    public string EremexAutomationCell2Id => $"{EremexAutomationRowId}_Cell2";
}
