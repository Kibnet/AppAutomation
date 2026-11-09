using AppAutomation.Abstractions;

namespace DotnetDebug.AppAutomation.Authoring.Pages;

public sealed class LocalComboBoxPage(IUiControlResolver resolver, Action<string> onLookup)
    : UiPage(new ObservedResolver(resolver, onLookup).WithSingleSelect(nameof(Choice),
        SingleSelectParts.ByAutomationIds("LocalChoice", "LocalChoice_Results", "LocalChoice_Input",
            "LocalChoice_OpenButton", "LocalChoice_Input", resultsKind: SingleSelectResultsKind.ListBox)))
{
    public IComboBoxControl Choice => Resolve<IComboBoxControl>(new(nameof(Choice), UiControlType.ComboBox, "LocalChoice"));

    public ILabelControl Applied => Resolve<ILabelControl>(new(nameof(Applied), UiControlType.Label, "LocalChoiceApplied"));

    private sealed class ObservedResolver(IUiControlResolver inner, Action<string> onLookup) : IUiControlResolver
    {
        public UiRuntimeCapabilities Capabilities => inner.Capabilities;

        public TControl Resolve<TControl>(UiControlDefinition definition) where TControl : class
        {
            onLookup(definition.LocatorValue);
            return inner.Resolve<TControl>(definition);
        }
    }
}
