using System;
using System.Globalization;
using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class ManifestContractTests
{
    [Test]
    public async Task LocatorManifest_PreservesDefinitions()
    {
        var control = new UiControlDefinition(
            "ResultText",
            UiControlType.Label,
            "ResultText",
            UiLocatorKind.Name,
            FallbackToName: false);
        var page = new UiPageDefinition(
            "Sample.Pages.MainWindowPage",
            "MainWindowPage",
            new[] { control });
        var manifest = new UiLocatorManifest(
            "1",
            "Sample.Authoring",
            new[] { page });
        IUiLocatorManifestProvider provider = new StubManifestProvider(manifest);

        var resolved = provider.GetManifest();

        using (Assert.Multiple())
        {
            await Assert.That(resolved.ContractVersion).IsEqualTo("1");
            await Assert.That(resolved.AssemblyName).IsEqualTo("Sample.Authoring");
            await Assert.That(resolved.Pages.Count).IsEqualTo(1);
            await Assert.That(resolved.Pages[0].Controls.Count).IsEqualTo(1);
            await Assert.That(resolved.Pages[0].Controls[0].PropertyName).IsEqualTo("ResultText");
            await Assert.That(resolved.Pages[0].Controls[0].LocatorKind).IsEqualTo(UiLocatorKind.Name);
            await Assert.That(resolved.Pages[0].Controls[0].FallbackToName).IsEqualTo(false);
        }
    }

    [Test]
    public async Task RuntimeCapabilities_DefaultFlagsAreDisabled()
    {
        var capabilities = new UiRuntimeCapabilities("flaui");
        var (
            adapterId,
            supportsGridCellAccess,
            supportsCalendarRangeSelection,
            supportsTreeNodeExpansionState,
            supportsRawNativeHandles,
            supportsScreenshots) = capabilities;

        using (Assert.Multiple())
        {
            await Assert.That(adapterId).IsEqualTo("flaui");
            await Assert.That(supportsGridCellAccess).IsEqualTo(false);
            await Assert.That(supportsCalendarRangeSelection).IsEqualTo(false);
            await Assert.That(supportsTreeNodeExpansionState).IsEqualTo(false);
            await Assert.That(supportsRawNativeHandles).IsEqualTo(false);
            await Assert.That(supportsScreenshots).IsEqualTo(false);
            await Assert.That(capabilities.SupportsClipboardText).IsEqualTo(false);
        }
    }

    [Test]
    public async Task UiOperationException_ExposesFailureContextAndInnerException()
    {
        var failureContext = new UiFailureContext(
            OperationName: "WaitUntilTextEquals",
            AdapterId: "avalonia-headless",
            Timeout: TimeSpan.FromSeconds(2),
            StartedAtUtc: DateTimeOffset.ParseExact("2026-03-07T10:00:00.0000000+00:00", "O", CultureInfo.InvariantCulture),
            FinishedAtUtc: DateTimeOffset.ParseExact("2026-03-07T10:00:02.0000000+00:00", "O", CultureInfo.InvariantCulture),
            Capabilities: new UiRuntimeCapabilities("avalonia-headless"),
            Artifacts: new[]
            {
                new UiFailureArtifact(
                    Kind: "logical-tree",
                    LogicalName: "logical-tree",
                    RelativePath: "artifacts/ui-failures/sample/logical-tree.txt",
                    ContentType: "text/plain",
                    IsRequiredByContract: true,
                    InlineTextPreview: "Window -> ResultText")
            },
            PageTypeFullName: "Sample.Pages.MainWindowPage",
            ControlPropertyName: "ResultText",
            LocatorValue: "ResultText",
            LocatorKind: UiLocatorKind.AutomationId,
            LastObservedValue: "Pending");
        var innerException = new InvalidOperationException("boom");

        var exception = new UiOperationException("timeout", failureContext, innerException);

        using (Assert.Multiple())
        {
            await Assert.That(exception.Message).IsEqualTo("timeout");
            await Assert.That(ReferenceEquals(exception.FailureContext, failureContext)).IsEqualTo(true);
            await Assert.That(exception.InnerException).IsNotNull();
            await Assert.That(exception.InnerException!.Message).IsEqualTo("boom");
            await Assert.That(exception.FailureContext.Artifacts.Count).IsEqualTo(1);
            await Assert.That(exception.FailureContext.Artifacts[0].LogicalName).IsEqualTo("logical-tree");
        }
    }

    private sealed class StubManifestProvider : IUiLocatorManifestProvider
    {
        private readonly UiLocatorManifest _manifest;

        public StubManifestProvider(UiLocatorManifest manifest)
        {
            _manifest = manifest;
        }

        public UiLocatorManifest GetManifest() => _manifest;
    }
}
