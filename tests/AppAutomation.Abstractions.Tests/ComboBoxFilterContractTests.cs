using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class ComboBoxFilterContractTests
{
    [Test]
    public async Task PublicApi_ExposesCardinalityNeutralFilterAndItemsKind()
    {
        var parts = ComboBoxFilterParts.ByAutomationIds(
            "StatusFilter",
            "StatusFilterOpenButton",
            "StatusFilterItems",
            itemsKind: MultiSelectItemsKind.ComboBox);

        using (Assert.Multiple())
        {
            await Assert.That(typeof(IComboBoxFilterControl).GetInterfaces()).Contains(typeof(IMultiSelectControl));
            await Assert.That(parts.ItemsKind).IsEqualTo(MultiSelectItemsKind.ComboBox);
        }
    }
}
