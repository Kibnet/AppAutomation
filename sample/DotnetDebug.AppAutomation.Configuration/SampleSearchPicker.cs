using AppAutomation.Abstractions;

namespace DotnetDebug.AppAutomation.Configuration;

/// <summary>Shared registration of a real committed-value surface for both sample runtimes.</summary>
public static class SampleSearchPicker
{
    public static IUiControlResolver WithSampleSearchPicker(
        this IUiControlResolver resolver, string propertyName, string committedValueLocator)
    {
        return resolver
            .WithSingleSelect($"{propertyName}Results", new SingleSelectParts(
                RootLocator: propertyName,
                ResultsLocator: $"{propertyName}_Results",
                OpenButtonLocator: $"{propertyName}_OpenButton",
                SelectedValueLocator: committedValueLocator,
                ResultsKind: SingleSelectResultsKind.ListBox))
            .WithSearchPicker(propertyName, new SearchPickerParts(
                SearchInputLocator: $"{propertyName}_Input",
                ResultsLocator: propertyName,
                OpensOnSearch: true));
    }
}
