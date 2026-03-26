using System.Text.Json;

namespace AppAutomation.Session.Contracts;

public sealed class AutomationLaunchContext
{
    public const string ScenarioNameEnvironmentVariable = "APPAUTOMATION_SCENARIO_NAME";
    public const string ScenarioPayloadPathEnvironmentVariable = "APPAUTOMATION_SCENARIO_PAYLOAD_PATH";

    private static readonly object AmbientSync = new();
    private static AutomationLaunchContext? _ambientOverride;

    public AutomationLaunchContext(string scenarioName, string? payloadPath = null, string source = "manual")
    {
        if (string.IsNullOrWhiteSpace(scenarioName))
        {
            throw new ArgumentException("Scenario name is required.", nameof(scenarioName));
        }

        ScenarioName = scenarioName.Trim();
        PayloadPath = string.IsNullOrWhiteSpace(payloadPath) ? null : Path.GetFullPath(payloadPath);
        Source = string.IsNullOrWhiteSpace(source) ? "manual" : source.Trim();
    }

    public string ScenarioName { get; }

    public string? PayloadPath { get; }

    public string Source { get; }

    public IReadOnlyDictionary<string, string?> ToEnvironmentVariables()
    {
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ScenarioNameEnvironmentVariable] = ScenarioName,
            [ScenarioPayloadPathEnvironmentVariable] = PayloadPath
        };
    }

    public TPayload? ReadPayload<TPayload>(JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(PayloadPath))
        {
            return default;
        }

        if (!File.Exists(PayloadPath))
        {
            throw new InvalidOperationException(
                $"Scenario '{ScenarioName}' expects payload '{PayloadPath}', but the file was not found.");
        }

        try
        {
            using var stream = File.OpenRead(PayloadPath);
            return JsonSerializer.Deserialize<TPayload>(stream, options);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to read scenario payload for '{ScenarioName}' from '{PayloadPath}' as '{typeof(TPayload).FullName}'.",
                ex);
        }
    }

    public TPayload ReadRequiredPayload<TPayload>(JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(PayloadPath))
        {
            throw new InvalidOperationException(
                $"Scenario '{ScenarioName}' does not provide payload path '{ScenarioPayloadPathEnvironmentVariable}'.");
        }

        var payload = ReadPayload<TPayload>(options);
        if (payload is null)
        {
            throw new InvalidOperationException(
                $"Scenario '{ScenarioName}' payload at '{PayloadPath}' was deserialized as null for '{typeof(TPayload).FullName}'.");
        }

        return payload;
    }

    public static AutomationLaunchContext? TryGetCurrent(Func<string, string?>? environmentReader = null)
    {
        lock (AmbientSync)
        {
            if (_ambientOverride is not null)
            {
                return _ambientOverride;
            }
        }

        environmentReader ??= Environment.GetEnvironmentVariable;
        var scenarioName = environmentReader(ScenarioNameEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(scenarioName))
        {
            return null;
        }

        var payloadPath = environmentReader(ScenarioPayloadPathEnvironmentVariable);
        return new AutomationLaunchContext(scenarioName, payloadPath, source: "environment");
    }

    public static AutomationLaunchContext GetRequired(Func<string, string?>? environmentReader = null)
    {
        return TryGetCurrent(environmentReader)
            ?? throw new InvalidOperationException(
                $"Automation launch context is not available. Provide '{ScenarioNameEnvironmentVariable}' or push an ambient override first.");
    }

    public static IDisposable PushAmbientOverride(AutomationLaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        lock (AmbientSync)
        {
            var previous = _ambientOverride;
            _ambientOverride = context;
            return new AmbientOverrideScope(previous);
        }
    }

    private sealed class AmbientOverrideScope : IDisposable
    {
        private readonly AutomationLaunchContext? _previous;
        private bool _disposed;

        public AmbientOverrideScope(AutomationLaunchContext? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            lock (AmbientSync)
            {
                _ambientOverride = _previous;
            }
        }
    }
}
