using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class SearchControlContractTests
{
    [Test]
    public async Task PublicApi_ExposesOneSearchControlWithHistory()
    {
        var parts = SearchControlParts.ByAutomationIds(
            "TableSearchInput",
            "SearchHistoryItemButton",
            historyResultsKind: SearchHistoryResultsKind.Buttons);

        using (Assert.Multiple())
        {
            await Assert.That((int)UiControlType.Search).IsEqualTo(33);
            await Assert.That(parts.HistoryResultsKind).IsEqualTo(SearchHistoryResultsKind.Buttons);
            await Assert.That(typeof(ISearchControl).GetMethod(nameof(ISearchControl.EnterSearch))).IsNotNull();
            await Assert.That(typeof(ISearchControl).GetMethod(nameof(ISearchControl.ClearSearch))).IsNotNull();
            await Assert.That(typeof(ISearchControl).GetMethod(nameof(ISearchControl.ApplySearchFromHistory))).IsNotNull();
        }
    }
}
