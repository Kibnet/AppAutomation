namespace AppAutomation.TestHost.Avalonia;

public sealed class AvaloniaDesktopAppDescriptor
{
    public AvaloniaDesktopAppDescriptor(
        IEnumerable<string> solutionFileNames,
        IEnumerable<string> desktopProjectRelativePaths,
        string desktopTargetFramework,
        string executableName)
    {
        SolutionFileNames = ValidateSequence(solutionFileNames, nameof(solutionFileNames));
        DesktopProjectRelativePaths = ValidateSequence(desktopProjectRelativePaths, nameof(desktopProjectRelativePaths));
        DesktopTargetFramework = ValidateValue(desktopTargetFramework, nameof(desktopTargetFramework));
        ExecutableName = ValidateValue(executableName, nameof(executableName));
    }

    public IReadOnlyList<string> SolutionFileNames { get; }

    public IReadOnlyList<string> DesktopProjectRelativePaths { get; }

    public string DesktopTargetFramework { get; }

    public string ExecutableName { get; }

    private static IReadOnlyList<string> ValidateSequence(IEnumerable<string> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);

        var items = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (items.Length == 0)
        {
            throw new ArgumentException("At least one non-empty value is required.", parameterName);
        }

        return items;
    }

    private static string ValidateValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
    }
}
