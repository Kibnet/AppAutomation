namespace Avalonia.Headless.EasyUse.Conditions;

public sealed class PropertyCondition
{
    internal PropertyCondition(global::FlaUI.Core.Conditions.PropertyCondition inner)
    {
        Inner = inner;
    }

    internal global::FlaUI.Core.Conditions.PropertyCondition Inner { get; }
}

public sealed class ConditionFactory
{
    private readonly global::FlaUI.Core.Conditions.ConditionFactory _inner = new();

    public PropertyCondition ByAutomationId(string value) => new(_inner.ByAutomationId(value));

    public PropertyCondition ByName(string value) => new(_inner.ByName(value));
}