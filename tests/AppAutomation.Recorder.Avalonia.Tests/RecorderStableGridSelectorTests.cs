using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using Avalonia.Automation;
using Avalonia.Controls;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderStableGridSelectorTests
{
    [Test]
    public async Task Generator_RendersCompositeSelectorAndNamedTargetColumn()
    {
        var step = new RecordedStep(
            RecordedActionKind.WaitUntilGridCellEquals,
            GridDescriptor(),
            StringValue: "Ready",
            RowIndex: 7,
            ColumnIndex: 2)
        {
            GridRowConditions =
            [
                new RecordedGridRowCondition("OrderId", "ORD-42"),
                new RecordedGridRowCondition("Item", "Pump")
            ],
            GridTargetColumnName = "Status"
        };

        var preview = CreateGenerator().GeneratePreview(step);

        await Assert.That(preview).Contains(
            "Page.WaitUntilGridCellEquals(static page => page.OrdersGrid, "
            + "GridRowSelector.ByCell(\"OrderId\", \"ORD-42\").AndCell(\"Item\", \"Pump\"), "
            + "\"Status\", \"Ready\");");
    }

    [Test]
    public async Task Generator_KeepsLegacyIndexesWithoutNamedPayload()
    {
        var step = new RecordedStep(
            RecordedActionKind.CopyGridCell,
            GridDescriptor(),
            RowIndex: 7,
            ColumnIndex: 2);

        var preview = CreateGenerator().GeneratePreview(step);

        await Assert.That(preview).Contains(
            "Page.CopyGridCell(static page => page.OrdersGrid, 7, 2);");
    }

    [Test]
    public async Task Recorder_UsesOnlyConfiguredIdentityColumns()
    {
        var fixture = new GridCaptureFixture(
            [
                new OrderRow("ORD-41", "North", "Draft"),
                new OrderRow("ORD-42", "North", "Ready")
            ],
            identityColumns: ["OrderId", "Customer"]);

        var result = fixture.CaptureCell(rowIndex: 1, columnIndex: 2);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step!.GridTargetColumnName).IsEqualTo("Status");
            await Assert.That(result.Step.GridRowConditions).IsEquivalentTo(
            [
                new RecordedGridRowCondition("OrderId", "ORD-42"),
                new RecordedGridRowCondition("Customer", "North")
            ]);
        }
    }

    [Test]
    public async Task Recorder_UsesDisplayedIdentityValueInsteadOfModelToString()
    {
        var fixture = new GridCaptureFixture(
            [new OrderRow("ORD-42", "North", "Ready")],
            identityColumns: ["OrderId"],
            displayValue: static (row, columnIndex) => columnIndex == 0 ? "Order #42" : ValueAt(row, columnIndex));

        var result = fixture.CaptureCell(rowIndex: 0, columnIndex: 2);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step!.GridRowConditions).IsEquivalentTo(
            [
                new RecordedGridRowCondition("OrderId", "Order #42")
            ]);
        }
    }

    [Test]
    public async Task Recorder_FollowsDisplayedRowDataAfterSorting()
    {
        var fixture = new GridCaptureFixture(
            [
                new OrderRow("ORD-42", "North", "Ready"),
                new OrderRow("ORD-41", "South", "Draft")
            ],
            identityColumns: ["OrderId"],
            displayedAutomationRowIndex: static rowIndex => 1 - rowIndex);

        var result = fixture.CaptureCell(rowIndex: 0, columnIndex: 2);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step!.GridRowConditions).IsEquivalentTo(
            [
                new RecordedGridRowCondition("OrderId", "ORD-42")
            ]);
        }
    }

    [Test]
    public async Task Recorder_RecordsRowAssertionWithStableIdentity()
    {
        var fixture = new GridCaptureFixture(
            [
                new OrderRow("ORD-41", "North", "Draft"),
                new OrderRow("ORD-42", "South", "Ready")
            ],
            identityColumns: ["OrderId"]);

        var result = fixture.CaptureRow(rowIndex: 1);
        var preview = CreateGenerator().GeneratePreview(result.Step!);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilGridContainsRow);
            await Assert.That(result.Step.GridRowConditions).IsEquivalentTo(
            [
                new RecordedGridRowCondition("OrderId", "ORD-42")
            ]);
            await Assert.That(preview).Contains(
                "Page.WaitUntilGridContainsRow(static page => page.OrdersGrid, "
                + "GridRowSelector.ByCell(\"OrderId\", \"ORD-42\"));");
        }
    }

    [Test]
    public async Task Recorder_NamedGridStepRequiresRuntimeColumnMetadata()
    {
        var fixture = new GridCaptureFixture(
            [new OrderRow("ORD-42", "North", "Ready")],
            identityColumns: ["OrderId"]);

        var result = fixture.CaptureCell(rowIndex: 0, columnIndex: 2);
        var validated = new RecorderCommandRuntimeValidator(new AppAutomationRecorderOptions())
            .Validate(result.Step!);
        var findings = validated.RuntimeValidationFindings ?? [];

        using (Assert.Multiple())
        {
            await Assert.That(findings.Count(static finding =>
                finding.Code.EndsWith("grid-column-metadata-adapter-required", StringComparison.Ordinal))).IsEqualTo(2);
            await Assert.That(findings.All(static finding =>
                finding.Severity == RecorderRuntimeValidationSeverity.Warning)).IsTrue();
        }
    }

    [Test]
    public async Task Recorder_RejectsNonUniqueConfiguredIdentity()
    {
        var fixture = new GridCaptureFixture(
            [
                new OrderRow("ORD-42", "North", "Draft"),
                new OrderRow("ORD-42", "South", "Ready")
            ],
            identityColumns: ["OrderId"]);

        var result = fixture.CaptureCell(rowIndex: 1, columnIndex: 2);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Message).Contains("matches 2 rows");
        }
    }

    [Test]
    public async Task Recorder_RejectsIdentityMadeEmptyByTargetColumn()
    {
        var fixture = new GridCaptureFixture(
            [new OrderRow("ORD-42", "North", "Ready")],
            identityColumns: ["Status"]);

        var result = fixture.CaptureCell(rowIndex: 0, columnIndex: 2);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Message).Contains("identity is empty after excluding target column");
        }
    }

    [Test]
    public async Task Recorder_KeepsIndexesWhenIdentityIsNotConfigured()
    {
        var fixture = new GridCaptureFixture(
            [new OrderRow("ORD-42", "North", "Ready")],
            identityColumns: []);

        var result = fixture.CaptureCell(rowIndex: 0, columnIndex: 2);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step!.RowIndex).IsEqualTo(0);
            await Assert.That(result.Step.ColumnIndex).IsEqualTo(2);
            await Assert.That(result.Step.GridRowConditions).IsNull();
            await Assert.That(result.Step.GridTargetColumnName).IsNull();
        }
    }

    private static AuthoringCodeGenerator CreateGenerator()
    {
        return new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
    }

    private static RecordedControlDescriptor GridDescriptor()
    {
        return new RecordedControlDescriptor(
            "OrdersGrid",
            UiControlType.Grid,
            "OrdersGrid",
            UiLocatorKind.AutomationId,
            FallbackToName: false,
            AvaloniaTypeName: typeof(Border).FullName!,
            Warning: null);
    }

    private sealed class GridCaptureFixture
    {
        private readonly StackPanel _root = new();
        private readonly GridHost _grid;
        private readonly Panel _bridge;
        private readonly IReadOnlyList<OrderRow> _rows;
        private readonly RecorderStepFactory _factory;

        public GridCaptureFixture(
            IReadOnlyList<OrderRow> rows,
            IReadOnlyList<string> identityColumns,
            Func<OrderRow, int, string>? displayValue = null,
            Func<int, int>? displayedAutomationRowIndex = null)
        {
            _rows = rows;
            _grid = new GridHost { ItemsSource = rows };
            _bridge = new StackPanel();
            AutomationProperties.SetAutomationId(_grid, "OrdersGridVisual");
            AutomationProperties.SetAutomationId(_bridge, "OrdersGrid");
            _root.Children.Add(_grid);
            _root.Children.Add(_bridge);

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                for (var columnIndex = 0; columnIndex < 3; columnIndex++)
                {
                    var displayedCell = new TextBlock
                    {
                        Text = displayValue?.Invoke(rows[rowIndex], columnIndex)
                            ?? ValueAt(rows[rowIndex], columnIndex),
                        DataContext = rows[rowIndex]
                    };
                    AutomationProperties.SetAutomationId(
                        displayedCell,
                        $"OrdersGrid_Row{displayedAutomationRowIndex?.Invoke(rowIndex) ?? rowIndex}_Cell{columnIndex}");
                    _bridge.Children.Add(displayedCell);

                    var sourceCell = new TextBlock
                    {
                        Text = ValueAt(rows[rowIndex], columnIndex),
                        DataContext = rows[rowIndex]
                    };
                    AutomationProperties.SetAutomationId(
                        sourceCell,
                        $"OrdersGridVisual_Row{rowIndex}_Cell{columnIndex}");
                    _grid.Children.Add(sourceCell);
                }
            }

            var options = new AppAutomationRecorderOptions();
            options.GridHints.Add(new RecorderGridHint(
                "OrdersGridVisual",
                "OrdersGrid",
                ["OrderId", "Customer", "Status"])
            {
                RowIdentityColumnPropertyNames = identityColumns
            });
            _factory = new RecorderStepFactory(options, () => _root);
        }

        public StepCreationResult CaptureCell(int rowIndex, int columnIndex)
        {
            var cell = _grid.Children
                .OfType<TextBlock>()
                .Single(candidate => string.Equals(
                    AutomationProperties.GetAutomationId(candidate),
                    $"OrdersGridVisual_Row{rowIndex}_Cell{columnIndex}",
                    StringComparison.Ordinal));
            return _factory.TryCreateAssertionStep(cell, RecorderAssertionMode.Text);
        }

        public StepCreationResult CaptureRow(int rowIndex)
        {
            var row = new Border { DataContext = _rows[rowIndex] };
            _grid.Children.Add(row);
            return _factory.TryCreateAssertionStep(row, RecorderAssertionMode.Text);
        }
    }

    private static string ValueAt(OrderRow row, int columnIndex)
    {
        return columnIndex switch
        {
            0 => row.OrderId,
            1 => row.Customer,
            2 => row.Status,
            _ => throw new ArgumentOutOfRangeException(nameof(columnIndex))
        };
    }

    private sealed class GridHost : Panel
    {
        public object? ItemsSource { get; init; }
    }

    private sealed record OrderRow(string OrderId, string Customer, string Status);
}
