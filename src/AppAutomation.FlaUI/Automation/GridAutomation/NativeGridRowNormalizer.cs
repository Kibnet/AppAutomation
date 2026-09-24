namespace AppAutomation.FlaUI.Automation.GridAutomation;

internal static class NativeGridRowNormalizer
{
    public static void Append(
        List<NativeGridRowSnapshot> destination,
        IEnumerable<NativeGridRowSnapshot> observedRows,
        Func<NativeGridRowSnapshot, NativeGridRowSnapshot, bool> representSameRow)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(observedRows);
        ArgumentNullException.ThrowIfNull(representSameRow);

        foreach (var row in observedRows)
        {
            var existingIndex = destination.FindIndex(existing => representSameRow(row, existing));
            if (existingIndex < 0)
            {
                destination.Add(row);
            }
            else
            {
                destination[existingIndex] = row;
            }
        }
    }
}
