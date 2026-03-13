using System.Xml.Linq;

namespace AppAutomation.Tooling;

internal enum DoctorIssueSeverity
{
    Info,
    Warning,
    Error
}

internal sealed record DoctorIssue(DoctorIssueSeverity Severity, string Message);

internal sealed record ProjectInspection(
    string Name,
    string Path,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<string> ProjectReferences)
{
    public bool UsesAppAutomationPackage =>
        PackageReferences.Any(static reference => reference.StartsWith("AppAutomation.", StringComparison.Ordinal));

    public bool UsesAppAutomationSourceDependency =>
        ProjectReferences.Any(static reference =>
            reference.Contains("src\\AppAutomation.", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("src/AppAutomation.", StringComparison.OrdinalIgnoreCase));

    public bool IsAuthoring => Name.Contains("Authoring", StringComparison.OrdinalIgnoreCase);

    public bool IsHeadless => Name.Contains("Headless", StringComparison.OrdinalIgnoreCase);

    public bool IsFlaUi => Name.Contains("FlaUI", StringComparison.OrdinalIgnoreCase);

    public bool IsTestHost => Name.Contains("TestHost", StringComparison.OrdinalIgnoreCase);

    public static ProjectInspection Load(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var propertyGroups = document.Root?.Elements().Where(static element => element.Name.LocalName == "PropertyGroup").ToArray()
            ?? Array.Empty<XElement>();
        var targetFrameworks = propertyGroups.Elements()
            .Where(static element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .Select(static element => element.Value)
            .SelectMany(static value => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var packageReferences = document.Root?.Descendants()
            .Where(static element => element.Name.LocalName == "PackageReference")
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        var projectReferences = document.Root?.Descendants()
            .Where(static element => element.Name.LocalName == "ProjectReference")
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        return new ProjectInspection(
            Name: System.IO.Path.GetFileNameWithoutExtension(projectPath),
            Path: projectPath,
            TargetFrameworks: targetFrameworks,
            PackageReferences: packageReferences,
            ProjectReferences: projectReferences);
    }
}
