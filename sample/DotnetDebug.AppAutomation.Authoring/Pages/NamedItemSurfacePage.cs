using AppAutomation.Abstractions;
using TUnit.Assertions;

namespace DotnetDebug.AppAutomation.Authoring.Pages;

[UiControl("EquipmentSurface", UiControlType.NamedItemSurface, "EquipmentSurface", FallbackToName = false)]
[UiControl("OpenedEquipment", UiControlType.Label, "OpenedEquipment")]
[UiControl("ReverseEquipment", UiControlType.Button, "ReverseEquipment")]
[UiControl("DuplicateEquipment", UiControlType.Button, "DuplicateEquipment")]
[UiControl("OmitEquipmentPeer", UiControlType.Button, "OmitEquipmentPeer")]
public sealed partial class NamedItemSurfacePage(IUiControlResolver resolver) : UiPage(resolver)
{
    public async Task VerifyReplayAfterReorder()
    {
        this.ClickButton(static page => page.ReverseEquipment);
        this.DoubleClickNamedItem(static page => page.EquipmentSurface, "  LASER 02  ");
        await Assert.That(OpenedEquipment.Text).IsEqualTo("line-laser:1");
    }

    public async Task VerifyInvalidTargetsDoNotOpenAnItem()
    {
        await Assert.That(() => this.DoubleClickNamedItem(static page => page.EquipmentSurface, " "))
            .Throws<ArgumentException>();
        await Assert.That(() => this.DoubleClickNamedItem(static page => page.EquipmentSurface, "Laser"))
            .Throws<InvalidOperationException>().WithMessageContaining("no item named");
        this.ClickButton(static page => page.DuplicateEquipment);
        await Assert.That(() => this.DoubleClickNamedItem(static page => page.EquipmentSurface, "Laser 02"))
            .Throws<InvalidOperationException>().WithMessageContaining("ambiguous");
        this.ClickButton(static page => page.OmitEquipmentPeer);
        await Assert.That(() => this.DoubleClickNamedItem(static page => page.EquipmentSurface, "Press 05"))
            .Throws<InvalidOperationException>().WithMessageContaining("incomplete item set");
        await Assert.That(OpenedEquipment.Text).IsEqualTo("None");
    }
}
