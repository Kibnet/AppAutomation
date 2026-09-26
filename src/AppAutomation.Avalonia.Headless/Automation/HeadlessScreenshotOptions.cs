namespace AppAutomation.Avalonia.Headless.Automation;

/// <summary>
/// Configures where automatic headless failure screenshots are saved.
/// </summary>
public sealed class HeadlessScreenshotOptions
{
    /// <summary>
    /// Directory for failure screenshots. Must be on the same filesystem root as
    /// <see cref="AppContext.BaseDirectory"/> so artifact paths remain relative.
    /// </summary>
    public string ArtifactDirectory { get; init; } = Path.Combine(
        AppContext.BaseDirectory,
        "artifacts",
        "ui-failures",
        "avalonia-headless");
}
