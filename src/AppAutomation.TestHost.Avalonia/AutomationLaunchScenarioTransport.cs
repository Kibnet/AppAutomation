using System.Text.Json;
using AppAutomation.Session.Contracts;

namespace AppAutomation.TestHost.Avalonia;

internal static class AutomationLaunchScenarioTransport
{
    public static ScenarioTransport Create<TPayload>(AutomationLaunchScenario<TPayload> scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var directory = TemporaryDirectory.Create("AppAutomationScenario");
        var payloadPath = directory.WriteTextFile(
            "scenario\\payload.json",
            JsonSerializer.Serialize(scenario.Payload, scenario.SerializerOptions));

        return new ScenarioTransport(
            scenario.CreateContext(payloadPath, source: "transport"),
            directory);
    }

    public static Action? CombineCallbacks(params Action?[] callbacks)
    {
        var activeCallbacks = callbacks
            .Where(static callback => callback is not null)
            .Cast<Action>()
            .ToArray();

        if (activeCallbacks.Length == 0)
        {
            return null;
        }

        return () =>
        {
            List<Exception>? exceptions = null;
            foreach (var callback in activeCallbacks)
            {
                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    exceptions ??= [];
                    exceptions.Add(ex);
                }
            }

            if (exceptions is null)
            {
                return;
            }

            throw exceptions.Count == 1 ? exceptions[0] : new AggregateException(exceptions);
        };
    }

    internal sealed class ScenarioTransport : IDisposable
    {
        private readonly TemporaryDirectory _directory;
        private bool _disposed;

        public ScenarioTransport(AutomationLaunchContext context, TemporaryDirectory directory)
        {
            Context = context;
            _directory = directory;
        }

        public AutomationLaunchContext Context { get; }

        public IDisposable PushAmbientOverride()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return AutomationLaunchContext.PushAmbientOverride(Context);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _directory.Dispose();
        }
    }
}
