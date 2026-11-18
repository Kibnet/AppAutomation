using System.Runtime.CompilerServices;
using System.Text.Json;
using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class GridObjectIdentityAndDisplayTests
{
    [Test]
    public async Task ObjectKey_UsesReferenceIdentityWithoutChangingEqualityOrSerialization()
    {
        var row = new RowModel { Name = "Pump", Amount = 12.345678901234567890123456789m };
        var equalRow = row with { };
        var json = JsonSerializer.Serialize(row);
        var hash = row.GetHashCode();
        var key = GridRowObjectKey.Get(row);
        var equalKey = GridRowObjectKey.Get(equalRow);

        using (Assert.Multiple())
        {
            await Assert.That(row.Equals(equalRow)).IsTrue();
            await Assert.That(key.Length > 0).IsTrue();
            await Assert.That(GridRowObjectKey.Get(row)).IsEqualTo(key);
            await Assert.That(equalKey).IsNotEqualTo(key);
            await Assert.That(row.GetHashCode()).IsEqualTo(hash);
            await Assert.That(JsonSerializer.Serialize(row)).IsEqualTo(json);
        }

        row.Name = "Edited";
        row.Amount = 999m;
        await Assert.That(GridRowObjectKey.Get(row)).IsEqualTo(key);
    }

    [Test]
    public async Task ObjectKey_ConcurrentReadsReturnOneKeyAndRejectUnstableInputs()
    {
        var row = new RowModel();
        var keys = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => GridRowObjectKey.Get(row))));

        using (Assert.Multiple())
        {
            await Assert.That(keys.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
            await Assert.That(() => GridRowObjectKey.Get(null!)).Throws<ArgumentNullException>();
            await Assert.That(() => GridRowObjectKey.Get(42)).Throws<ArgumentException>();
            await Assert.That(() => GridRowObjectKey.Get(DateTime.UnixEpoch)).Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task ObjectKey_DoesNotKeepRowAliveEvenWhileItsKeyIsRetained()
    {
        var (weakRow, key) = CreateWeakRow();
        for (var attempt = 0; attempt < 10 && weakRow.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        await Assert.That(weakRow.IsAlive).IsFalse();
        await Assert.That(key.Length > 0).IsTrue();
    }

    [Test]
    public async Task Catalog_ObjectKeyIsHiddenSoleIdentityAndRejectsFieldDisplayAndEditorOverrides()
    {
        var objectKey = GridColumnDefinition.ObjectKey(" RowKey ");
        var valid = Definition(objectKey).IdentifyRowsBy("RowKey");
        _ = new GridAutomationCatalog().Add(valid);
        using (Assert.Multiple())
        {
            await Assert.That(objectKey.LogicalName).IsEqualTo("RowKey");
            await Assert.That(objectKey.SourceFieldName).IsEqualTo("RowKey");
            await Assert.That(objectKey.IsObjectKey).IsTrue();
            await Assert.That(objectKey.RowIdentityAutomationProperty).IsEqualTo(GridRowAutomationProperty.ItemStatus);
            await Assert.That(GridColumnDefinition.Auto("Id").IsObjectKey).IsFalse();
            await Assert.That(GridColumnDefinition.Auto("Id").Equals(GridColumnDefinition.Auto("Id").FromField("Id"))).IsTrue();
            await Assert.That(() => new GridAutomationCatalog().Add(Definition(objectKey))).Throws<ArgumentException>();
            await Assert.That(() => new GridAutomationCatalog().Add(
                    Definition(objectKey, GridColumnDefinition.Auto("Id")).IdentifyRowsBy("RowKey", "Id")))
                .Throws<ArgumentException>();
            await Assert.That(() => new GridAutomationCatalog().Add(
                    Definition(objectKey, GridColumnDefinition.Auto("Id")).IdentifyRowsBy("Id")))
                .Throws<ArgumentException>();
        }

        GridColumnDefinition[] invalidColumns =
        [
            objectKey.FromField("RowKey"),
            objectKey.FromField("Id"),
            objectKey.AtRuntime("Visible key"),
            objectKey.DisplayValueFrom("Name"),
            objectKey.FormatWith("N2"),
            objectKey.ReadDisplayedText(),
            objectKey.EditWith(GridCellEditorKind.Text),
            objectKey.AsValue(GridCellValueKind.Number)
        ];
        foreach (var invalid in invalidColumns)
        {
            await Assert.That(() => new GridAutomationCatalog().Add(Definition(invalid).IdentifyRowsBy("RowKey")))
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task Catalog_DisplayedTextAllowsCultureButRejectsProjectionFormattingAndIdentity()
    {
        var displayed = GridColumnDefinition.Auto("Amount").AsValue(GridCellValueKind.Number)
            .ReadDisplayedText("ru-RU");
        _ = new GridAutomationCatalog().Add(Definition(displayed));
        using (Assert.Multiple())
        {
            await Assert.That(displayed.ReadsDisplayedText).IsTrue();
            await Assert.That(displayed.CultureName).IsEqualTo("ru-RU");
            await Assert.That(GridColumnDefinition.Auto("Amount").ReadsDisplayedText).IsFalse();
            await Assert.That(() => GridColumnDefinition.Auto("Amount").ReadDisplayedText(" "))
                .Throws<ArgumentException>();
        }

        GridColumnDefinition[] invalidColumns =
        [
            displayed.DisplayValueFrom("Amount"),
            GridColumnDefinition.Auto("Amount").DisplayValueFrom("Amount").ReadDisplayedText(),
            displayed.FormatWith("N2"),
            GridColumnDefinition.Auto("Amount").FormatWith("N2").ReadDisplayedText(),
            displayed.ReadIdentityFromRow(GridRowAutomationProperty.ItemStatus),
            displayed.AsStableIdentityCandidate()
        ];
        foreach (var invalid in invalidColumns)
        {
            await Assert.That(() => new GridAutomationCatalog().Add(Definition(invalid)))
                .Throws<ArgumentException>();
        }

        await Assert.That(() => new GridAutomationCatalog().Add(Definition(displayed).IdentifyRowsBy("Amount")))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Catalog_FingerprintDistinguishesObjectIdentityDisplaySourceAndCulture()
    {
        var legacy = Fingerprint(Definition(GridColumnDefinition.Auto("Id")).IdentifyRowsBy("Id"));
        var legacyIdentity = Fingerprint(Definition(GridColumnDefinition.Auto("RowKey")
            .ReadIdentityFromRow(GridRowAutomationProperty.ItemStatus)).IdentifyRowsBy("RowKey"));
        var objectIdentity = Fingerprint(Definition(GridColumnDefinition.ObjectKey("RowKey")).IdentifyRowsBy("RowKey"));
        var plain = Fingerprint(Definition(GridColumnDefinition.Auto("Amount")));
        var displayed = Fingerprint(Definition(GridColumnDefinition.Auto("Amount").ReadDisplayedText()));
        var cultured = Fingerprint(Definition(GridColumnDefinition.Auto("Amount").ReadDisplayedText("ru-RU")));

        using (Assert.Multiple())
        {
            // Fixed pre-extension HEAD hash: one plain Id column, default contexts, and IdentifyRowsBy("Id").
            await Assert.That(legacy).IsEqualTo("A4CC365D84CB2E32C449A8E5C47A0A054D110459BBCBF0DB6DBE6BAFCAC58B84");
            await Assert.That(objectIdentity).IsNotEqualTo(legacyIdentity);
            await Assert.That(displayed).IsNotEqualTo(plain);
            await Assert.That(cultured).IsNotEqualTo(displayed);
            await Assert.That(objectIdentity).IsEqualTo(Fingerprint(
                Definition(GridColumnDefinition.ObjectKey("RowKey")).IdentifyRowsBy("RowKey")));
        }
    }

    [Test]
    [Arguments(GridRowAutomationProperty.ItemStatus)]
    [Arguments(GridRowAutomationProperty.HelpText)]
    public async Task Mapping_PreservesObjectAndDisplayFlagsWithoutAssumingCurrentViewUniqueness(
        GridRowAutomationProperty automationProperty)
    {
        var provider = new ProbeGrid();
        var definition = Definition(GridColumnDefinition.ObjectKey("RowKey").ReadIdentityFromRow(automationProperty),
                GridColumnDefinition.Map("Rate").FromField("Amount").AtRuntime("Rate caption").ReadDisplayedText("ru-RU"))
            .IdentifyRowsBy("RowKey");
        var configured = Configure(provider, definition);
        var key = GridRowSelector.ByCell("RowKey", "current-object-key");
        _ = configured.ReadCell(new GridCellAddress(key, "Rate"), 1000);

        using (Assert.Multiple())
        {
            await Assert.That(provider.LastIndexedRow!.HasDeclaredUniqueIdentity).IsFalse();
            await Assert.That(provider.LastIndexedRow.Conditions[0].Column.IsObjectKey).IsTrue();
            await Assert.That(provider.LastIndexedRow.Conditions[0].Column.UsesObjectRowIdentity).IsTrue();
            await Assert.That(provider.LastIndexedRow.Conditions[0].Column.ObjectRowKeyAutomationProperty).IsEqualTo(automationProperty);
            await Assert.That(provider.LastIndexedRow.Conditions[0].Column.RuntimeColumnIndex).IsNull();
            await Assert.That(provider.LastIndexedColumn!.IsObjectKey).IsFalse();
            await Assert.That(provider.LastIndexedColumn.UsesObjectRowIdentity).IsTrue();
            await Assert.That(provider.LastIndexedColumn.ObjectRowKeyAutomationProperty).IsEqualTo(automationProperty);
            await Assert.That(provider.LastIndexedColumn.RowIdentityAutomationProperty).IsNull();
            await Assert.That(provider.LastIndexedColumn.ReadsDisplayedText).IsTrue();
            await Assert.That(provider.LastIndexedColumn.CultureName).IsEqualTo("ru-RU");
            await Assert.That(provider.LastIndexedColumn.SourceFieldName).IsEqualTo("Amount");
            await Assert.That(string.Join("|", provider.LastIndexedColumn.DeclaredRuntimeColumnNames))
                .IsEqualTo("Rate caption");
        }

        _ = configured.ReadCell(new GridCellAddress(GridRowSelector.ByCell("Id", "item-1"), "Rate"), 1000);
        using (Assert.Multiple())
        {
            await Assert.That(provider.LastIndexedRow!.Conditions[0].Column.IsObjectKey).IsFalse();
            await Assert.That(provider.LastIndexedRow.Conditions[0].Column.ObjectRowKeyAutomationProperty).IsEqualTo(automationProperty);
            await Assert.That(provider.LastIndexedRow.Conditions[0].Column.RowIdentityAutomationProperty).IsNull();
        }

        var businessProvider = new ProbeGrid();
        var businessGrid = Configure(businessProvider, Definition(GridColumnDefinition.Auto("Id"),
            GridColumnDefinition.Auto("Rate caption")).IdentifyRowsBy("Id"));
        _ = businessGrid.ResolveRow(GridRowSelector.ByCell("Id", "item-1"), 1000);
        await Assert.That(businessProvider.LastAddressableRow!.HasDeclaredUniqueIdentity).IsTrue();
    }

    [Test]
    public async Task Normalizer_PreservesAuthoritativeUiTextAndPreciseRawValueAcrossBothMappings()
    {
        const decimal raw = 12.345678901234567890123456789m;
        var snapshot = new GridCellValueSnapshot("12,3 units/hour", raw, GridCellValueKind.Number)
        {
            HasAuthoritativeDisplayText = true
        };
        var runtime = new GridRuntimeColumn(0, "Amount", null, null, "ru-RU", GridCellValueKind.Number, null, null)
        {
            ReadsDisplayedText = true
        };
        var normalized = GridCellValueNormalizer.Normalize("Orders", snapshot, runtime);
        normalized = GridCellValueNormalizer.Normalize("Orders", normalized,
            GridColumnDefinition.Auto("Amount").AsValue(GridCellValueKind.Number).ReadDisplayedText("ru-RU"));
        var formattedColumn = GridColumnDefinition.Auto("Amount").DisplayValueFrom("Missing.Path").FormatWith("N0");
        var authoritative = GridCellValueNormalizer.Normalize("Orders", snapshot, formattedColumn);
        using (Assert.Multiple())
        {
            await Assert.That(normalized.DisplayText).IsEqualTo("12,3 units/hour");
            await Assert.That(normalized.RawValue).IsEqualTo((object)raw);
            await Assert.That(normalized.IsDisplayOnly).IsFalse();
            await Assert.That(normalized.HasAuthoritativeDisplayText).IsTrue();
            await Assert.That(normalized.CultureName).IsEqualTo("ru-RU");
            await Assert.That(authoritative.DisplayText).IsEqualTo(snapshot.DisplayText);
            await Assert.That(authoritative.RawValue).IsEqualTo((object)raw);
        }
    }

    [Test]
    public async Task Normalizer_PreservesEmptyPresentationAndRawCollectionsAndRequiresProviderEvidence()
    {
        var raw = new[] { "Pump", "Valve" };
        var column = GridColumnDefinition.Auto("Components").ReadDisplayedText();
        var empty = GridCellValueNormalizer.Normalize("Orders",
            new GridCellValueSnapshot(string.Empty, raw) { HasAuthoritativeDisplayText = true }, column);
        var displayOnly = GridCellValueNormalizer.Normalize("Orders",
            new GridCellValueSnapshot("12,3", null, GridCellValueKind.Number)
            { HasAuthoritativeDisplayText = true, IsDisplayOnly = true },
            GridColumnDefinition.Auto("Amount").AsValue(GridCellValueKind.Number).ReadDisplayedText("ru-RU"));
        using (Assert.Multiple())
        {
            await Assert.That(empty.DisplayText).IsEqualTo(string.Empty);
            await Assert.That(ReferenceEquals(empty.RawValue, raw)).IsTrue();
            await Assert.That(displayOnly.RawValue).IsEqualTo((object)12.3m);
            await Assert.That(displayOnly.IsDisplayOnly).IsTrue();
            await Assert.That(() => GridCellValueNormalizer.Normalize("Orders", new GridCellValueSnapshot("model fallback", raw), column))
                .Throws<InvalidOperationException>();
        }
    }

    [Test]
    public async Task RowSet_ObjectKeysRequireNonemptyMetadataAndNeverDeclareUniquenessWithoutViewProof()
    {
        var keyColumn = new GridRuntimeColumn(0, "RowKey", null, null, null, GridCellValueKind.Text, null, null)
        { IsObjectKey = true, UsesObjectRowIdentity = true };
        var objectIdentity = new GridRowSetIdentity(["RowKey"], [keyColumn], string.Empty);
        var businessIdentity = new GridRowSetIdentity(["Id"],
            [new GridRuntimeColumn(0, "Id", null, null, null, GridCellValueKind.Text, null, null)], string.Empty);
        var emptyObjectPublication = GridRowSelectionValue.ToAutomationValue([], [GridRowSelector.ByCell("RowKey", " ")]);
        var emptyBusinessPublication = GridRowSelectionValue.ToAutomationValue([], [GridRowSelector.ByCell("Id", string.Empty)]);
        var runtimeSelector = objectIdentity.ToRuntimeSelector(GridRowSelector.ByCell("RowKey", "current-object-key"));
        var logicalSelector = GridRowSelector.ByCell("RowKey", "current-object-key").WithDeclaredUniqueIdentity()
            .WithColumnDefinitions(new Dictionary<string, GridColumnDefinition>(StringComparer.Ordinal)
            { ["RowKey"] = GridColumnDefinition.ObjectKey("RowKey") });
        var published = GridRowSelectionValue.Read(GridRowSelectionValue.ToAutomationValue(
            [], [GridRowSelector.ByCell("RowKey", "current-object-key")]), objectIdentity);
        using (Assert.Multiple())
        {
            await Assert.That(runtimeSelector.HasDeclaredUniqueIdentity).IsFalse();
            await Assert.That(runtimeSelector.WithDeclaredUniqueIdentity().HasDeclaredUniqueIdentity).IsFalse();
            await Assert.That(logicalSelector.HasDeclaredUniqueIdentity).IsFalse();
            await Assert.That(logicalSelector.WithDeclaredUniqueIdentity().HasDeclaredUniqueIdentity).IsFalse();
            await Assert.That(businessIdentity.ToRuntimeSelector(GridRowSelector.ByCell("Id", "id-1")).HasDeclaredUniqueIdentity).IsTrue();
            await Assert.That(objectIdentity.TryRead(_ => " ")).IsNull();
            await Assert.That(() => objectIdentity.Read(_ => string.Empty)).Throws<InvalidOperationException>();
            await Assert.That(() => GridRowSelectionValue.Read(emptyObjectPublication, objectIdentity)).Throws<InvalidOperationException>();
            await Assert.That(GridRowSelectionValue.Read(emptyBusinessPublication, businessIdentity)).IsNotNull();
            await Assert.That(published!.OrderedView!.Length).IsEqualTo(1);
        }
    }

    private static GridAutomationDefinition Definition(params GridColumnDefinition[] columns) =>
        GridAutomationDefinition.ByAutomationIds("Orders", "OrdersVisual", "Orders").WithColumns(columns);

    private static string Fingerprint(GridAutomationDefinition definition) => new GridAutomationCatalog().Add(definition).Fingerprint;

    private static IAddressableGridControl Configure(ProbeGrid provider, GridAutomationDefinition definition)
    {
        var adapter = new GridAutomationAdapter(definition);
        return (IAddressableGridControl)adapter.Resolve(typeof(IGridControl),
            new UiControlDefinition("Orders", UiControlType.Grid, "Orders"), new ProbeResolver(provider));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Row, string Key) CreateWeakRow()
    {
        var row = new RowModel();
        return (new WeakReference(row), GridRowObjectKey.Get(row));
    }

    private sealed record RowModel
    {
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    private sealed class ProbeResolver(IGridControl grid) : IUiControlResolver
    {
        public UiRuntimeCapabilities Capabilities { get; } = new("object-grid-contract-test");
        public TControl Resolve<TControl>(UiControlDefinition definition) where TControl : class =>
            grid as TControl ?? throw new NotSupportedException();
    }

    private sealed class ProbeGrid : IGridControl, IGridColumnMetadataControl, IIndexedAddressableGridControl, IAddressableGridControl
    {
        public string AutomationId => "Orders";
        public string Name => AutomationId;
        public bool IsEnabled => true;
        public IReadOnlyList<IGridRowControl> Rows => [];
        public IReadOnlyList<string> ColumnNames => ["Id", "Rate caption"];
        public GridIndexedRowSelector? LastIndexedRow { get; private set; }
        public GridRuntimeColumn? LastIndexedColumn { get; private set; }
        public GridRowSelector? LastAddressableRow { get; private set; }
        public IGridRowControl? GetRowByIndex(int index) => null;
        public bool TryGetColumnIndex(string columnName, out int columnIndex)
        {
            columnIndex = ColumnNames.ToList().IndexOf(columnName);
            return columnIndex >= 0;
        }
        public GridRowResolution ResolveRow(GridIndexedRowSelector row, int timeoutMs)
        {
            LastIndexedRow = row;
            return GridRowResolution.Unique("probe");
        }
        public GridRowResolution ResolveRow(GridRowSelector row, int timeoutMs)
        {
            LastAddressableRow = row;
            return GridRowResolution.Unique("probe");
        }
        public GridCellValueSnapshot ReadCell(GridIndexedRowSelector row, GridRuntimeColumn column, int timeoutMs)
        {
            LastIndexedRow = row;
            LastIndexedColumn = column;
            return new GridCellValueSnapshot("Published UI", 12.345m) { HasAuthoritativeDisplayText = true };
        }
        public GridCellValueSnapshot ReadCell(GridCellAddress address, int timeoutMs) =>
            new("Published UI", 12.345m) { HasAuthoritativeDisplayText = true };
        public string CopyCell(GridIndexedRowSelector row, GridRuntimeColumn column, int timeoutMs) => "Published UI";
        public string CopyCell(GridCellAddress address, int timeoutMs) => "Published UI";
        public void EditCell(GridIndexedRowSelector row, GridRuntimeColumn column, GridCellValueEditRequest request, int timeoutMs) => throw new NotSupportedException();
        public void EditCell(GridCellAddress address, GridCellValueEditRequest request, int timeoutMs) => throw new NotSupportedException();
        public void OpenRow(GridIndexedRowSelector row, int timeoutMs) => throw new NotSupportedException();
        public void OpenRow(GridRowSelector row, int timeoutMs) => throw new NotSupportedException();
    }
}
