namespace AppAutomation.TestHost.Avalonia;

public sealed class AutomationPreflight
{
    private readonly string _name;
    private readonly List<AutomationPreflightFinding> _findings = [];

    private AutomationPreflight(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Preflight name is required.", nameof(name));
        }

        _name = name.Trim();
    }

    public static AutomationPreflight Create(string name)
    {
        return new AutomationPreflight(name);
    }

    public AutomationPreflight RequireEnvironmentVariable(string environmentVariableName, bool secret = false)
    {
        if (string.IsNullOrWhiteSpace(environmentVariableName))
        {
            throw new ArgumentException("Environment variable name is required.", nameof(environmentVariableName));
        }

        return RequireValue(
            environmentVariableName,
            Environment.GetEnvironmentVariable(environmentVariableName),
            $"env:{environmentVariableName}",
            secret);
    }

    public AutomationPreflight RequireValue(string label, string? value, string source, bool secret = false)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Label is required.", nameof(label));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Source is required.", nameof(source));
        }

        var normalizedValue = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var status = normalizedValue is null ? "missing" : "set";
        var displayValue = secret
            ? status
            : normalizedValue ?? "missing";

        _findings.Add(new AutomationPreflightFinding(
            Label: label.Trim(),
            Source: source.Trim(),
            Status: status,
            DisplayValue: displayValue,
            Secret: secret,
            IsSatisfied: normalizedValue is not null));
        return this;
    }

    public AutomationPreflight RequireExistingFile(string label, string? path, string source)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Label is required.", nameof(label));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Source is required.", nameof(source));
        }

        var normalizedPath = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        var exists = normalizedPath is not null && File.Exists(normalizedPath);

        _findings.Add(new AutomationPreflightFinding(
            Label: label.Trim(),
            Source: source.Trim(),
            Status: exists ? "exists" : "missing",
            DisplayValue: normalizedPath ?? "missing",
            Secret: false,
            IsSatisfied: exists));
        return this;
    }

    public void ThrowIfInvalid()
    {
        var failures = _findings.Where(static finding => !finding.IsSatisfied).ToArray();
        if (failures.Length == 0)
        {
            return;
        }

        throw new AutomationPreflightException(_name, _findings);
    }
}

public sealed record AutomationPreflightFinding(
    string Label,
    string Source,
    string Status,
    string DisplayValue,
    bool Secret,
    bool IsSatisfied);

public sealed class AutomationPreflightException : InvalidOperationException
{
    public AutomationPreflightException(string preflightName, IReadOnlyList<AutomationPreflightFinding> findings)
        : base(BuildMessage(preflightName, findings))
    {
        PreflightName = preflightName;
        Findings = findings;
    }

    public string PreflightName { get; }

    public IReadOnlyList<AutomationPreflightFinding> Findings { get; }

    private static string BuildMessage(string preflightName, IReadOnlyList<AutomationPreflightFinding> findings)
    {
        var lines = new List<string>
        {
            $"Automation preflight '{preflightName}' failed.",
            "Resolved inputs:"
        };

        foreach (var finding in findings)
        {
            lines.Add($"  - {finding.Label} [{finding.Source}] = {finding.DisplayValue}");
        }

        lines.Add("Fix the missing entries and retry.");
        return string.Join(Environment.NewLine, lines);
    }
}
