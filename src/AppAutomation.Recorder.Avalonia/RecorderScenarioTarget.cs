using AppAutomation.Recorder.Avalonia.SourceScanning;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderScenarioTarget
{
    private readonly string? _projectDirectory;
    private readonly string? _namespaceRoot;
    private readonly string _outputRoot;
    private readonly string? _configuredNamespace;
    private readonly string? _configuredClass;
    private bool _hasExplicitName;
    private string? _scanError;
    private string? _configuredDestinationError;

    internal RecorderScenarioTarget(AppAutomationRecorderOptions options)
    {
        IsEnabled = options.ScenarioSelection.IsEnabled;
        _projectDirectory = options.AuthoringProjectDirectory;
        _namespaceRoot = options.ScenarioSelection.ScenarioNamespaceRoot;
        _outputRoot = options.ScenarioSelection.OutputSubdirectoryRoot;
        _configuredNamespace = options.ScenarioNamespace;
        _configuredClass = options.ScenarioClassName;
        Name = options.ScenarioName?.Trim() ?? string.Empty;
        IsScanning = IsEnabled;
    }

    internal bool IsEnabled { get; }
    internal bool IsScanning { get; private set; }
    internal string? DiscoveryError => _scanError ?? _configuredDestinationError;
    internal string? Error => DiscoveryError ?? (Selected is null ? null : ValidateName(Name));
    internal IReadOnlyList<RecordedScenarioDestination> Destinations { get; private set; } = Array.Empty<RecordedScenarioDestination>();
    internal RecordedScenarioDestination? Selected { get; private set; }
    internal string Name { get; private set; }
    internal string DraftIdentity { get; private set; } = Guid.NewGuid().ToString("N");
    internal string? LastScenarioFilePath { get; set; }

    internal async Task DiscoverAsync(AuthoringProjectScanner scanner)
    {
        ScenarioDestinationDiscoveryResult result;
        try
        {
            result = await Task.Run(() => scanner.DiscoverScenarioDestinations(_projectDirectory, _namespaceRoot, _outputRoot));
        }
        catch (Exception error)
        {
            result = ScenarioDestinationDiscoveryResult.Failed($"Scenario destination scan failed: {error.Message}");
        }
        Destinations = result.Destinations;
        _scanError = result.Error;
        _configuredDestinationError = null;
        if (result.Success && !string.IsNullOrWhiteSpace(_configuredNamespace) && !string.IsNullOrWhiteSpace(_configuredClass))
        {
            Selected = Destinations.SingleOrDefault(destination =>
                string.Equals(destination.ScenarioNamespace, _configuredNamespace, StringComparison.Ordinal)
                && string.Equals(destination.ScenarioClassName, _configuredClass, StringComparison.Ordinal));
            _hasExplicitName |= Selected is not null && !string.IsNullOrWhiteSpace(Name);
            if (Selected is null)
                _configuredDestinationError = $"Configured scenario destination '{_configuredNamespace}.{_configuredClass}' was not found.";
        }
        IsScanning = false;
    }

    internal bool Select(RecordedScenarioDestination? destination, string? suggestedName)
    {
        if (destination is not null && !Destinations.Contains(destination)) return false;
        Selected = destination;
        if (destination is not null) _configuredDestinationError = null;
        if (!_hasExplicitName && suggestedName is not null) Name = suggestedName;
        ResetDraft();
        return true;
    }

    internal void SetName(string? name)
    {
        Name = name ?? string.Empty;
        _hasExplicitName = true;
        ResetDraft();
    }

    internal void Reset()
    {
        Selected = null;
        Name = string.Empty;
        _hasExplicitName = false;
        ResetDraft();
    }

    internal void PrepareRecording() => Name = Name.Trim();

    internal void ResetDraft()
    {
        LastScenarioFilePath = null;
        DraftIdentity = Guid.NewGuid().ToString("N");
    }

    internal void RestoreDraft(string identity) => DraftIdentity = identity;

    internal RecorderScenarioSaveContext? CaptureSaveContext() =>
        !IsEnabled || Selected is null || IsScanning || Error is not null
            ? null
            : new RecorderScenarioSaveContext(Selected, Name.Trim(), DraftIdentity);

    internal static string? ValidateName(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length == 0) return "Scenario name is required.";
        if (value.Any(static character => char.IsControl(character))
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar)
            || value.Contains("..", StringComparison.Ordinal))
            return "Scenario name contains characters that cannot be used safely in a generated file.";
        if (string.Equals(RecorderNaming.CreateFileSafeName(value, "scenario"), "scenario", StringComparison.Ordinal)
            && !string.Equals(value, "scenario", StringComparison.OrdinalIgnoreCase))
            return "Scenario name cannot be converted to a generated method and file name.";
        return null;
    }
}
