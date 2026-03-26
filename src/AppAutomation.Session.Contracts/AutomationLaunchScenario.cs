using System.Text.Json;

namespace AppAutomation.Session.Contracts;

public sealed class AutomationLaunchScenario<TPayload>
{
    public AutomationLaunchScenario(string name, TPayload payload)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Scenario name is required.", nameof(name));
        }

        Name = name.Trim();
        Payload = payload;
    }

    public string Name { get; }

    public TPayload Payload { get; }

    public JsonSerializerOptions? SerializerOptions { get; init; }

    public AutomationLaunchContext CreateContext(string? payloadPath = null, string source = "manual")
    {
        return new AutomationLaunchContext(Name, payloadPath, source);
    }
}
