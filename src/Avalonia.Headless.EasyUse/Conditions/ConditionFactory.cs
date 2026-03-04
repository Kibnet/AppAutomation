using Avalonia.Automation;
using Avalonia.Controls;

namespace FlaUI.Core.Conditions;

public sealed class PropertyCondition
{
    internal PropertyCondition(Func<Control, bool> predicate)
    {
        Predicate = predicate;
    }

    internal Func<Control, bool> Predicate { get; }

    internal bool Match(Control control) => Predicate(control);
}

public sealed class ConditionFactory
{
    public PropertyCondition ByAutomationId(string value)
    {
        return new PropertyCondition(control =>
        {
            var automationId = AutomationProperties.GetAutomationId(control) ?? string.Empty;
            return string.Equals(automationId, value, StringComparison.Ordinal);
        });
    }

    public PropertyCondition ByName(string value)
    {
        return new PropertyCondition(control =>
        {
            var automationName = AutomationProperties.GetName(control);
            var name = automationName ?? control.Name ?? string.Empty;
            return string.Equals(name, value, StringComparison.Ordinal);
        });
    }
}