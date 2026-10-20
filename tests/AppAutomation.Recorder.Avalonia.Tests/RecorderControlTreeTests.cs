using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderControlTreeTests
{
    [Test]
    public async Task RelatedControls_VisitSharedVisualAndLogicalParentOnce()
    {
        var parent = new StackPanel();
        var child = new Button();
        parent.Children.Add(child);

        var related = RecorderControlTree.EnumerateRelatedControls(child).ToArray();

        await Assert.That(related.Length).IsEqualTo(2);
        await Assert.That(ReferenceEquals(related[0], child)).IsTrue();
        await Assert.That(ReferenceEquals(related[1], parent)).IsTrue();
    }

    [Test]
    public async Task RelatedControls_OnlyFollowPopupPlacementWhenRequested()
    {
        var target = new Button();
        var popup = new Popup { PlacementTarget = target };

        var defaultRelated = RecorderControlTree.EnumerateRelatedControls(popup).ToArray();
        var placementRelated = RecorderControlTree.EnumerateRelatedControls(
            popup, includePopupPlacementTarget: true).ToArray();

        await Assert.That(defaultRelated.Length).IsEqualTo(1);
        await Assert.That(ReferenceEquals(defaultRelated[0], popup)).IsTrue();
        await Assert.That(placementRelated.Length).IsEqualTo(2);
        await Assert.That(ReferenceEquals(placementRelated[0], popup)).IsTrue();
        await Assert.That(ReferenceEquals(placementRelated[1], target)).IsTrue();
    }
}
