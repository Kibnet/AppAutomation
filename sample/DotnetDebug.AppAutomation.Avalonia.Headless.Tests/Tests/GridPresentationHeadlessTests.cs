using System.Globalization;
using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DotnetDebug.AppAutomation.TestHost;
using DotnetDebug.Avalonia;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

[NotInParallel("DesktopUi")]
public sealed class GridPresentationHeadlessTests
{
    [Test]
    public async Task ObjectRowRemainsTheSameAfterFillReorderAndDoubleClick()
    {
        using var fixture = Launch();
        var page = fixture.Page;
        var originals = fixture.Rows;
        await Assert.That(originals[0].Equals(originals[1])).IsTrue();
        var first = GridRowKeyReader.CaptureFirst(page.Table);
        var second = GridRowKeyReader.CaptureFirst(page.Table, [first]);
        await Assert.That(second.Conditions.Single().Value).IsEqualTo(GridRowObjectKey.Get(originals[1]));

        page.SelectGridRows(static p => p.Table, [first, second]);
        page.WaitUntilSelectedGridRowsEqual(static p => p.Table, [second, first]);
        page.SelectGridRows(static p => p.Table, [first]);
        page.WaitUntilSelectedGridRowsEqual(static p => p.Table, [first]);

        page.SelectGridRow(static p => p.Table, first);
        page.SelectGridRow(static p => p.Table, second);
        await Assert.That(HeadlessRuntime.Dispatch(() => ReferenceEquals(
            fixture.Grid.GetSourceItemByRowIndex(fixture.Grid.FocusedRowIndex), originals[1]))).IsTrue();
        await Assert.That(HeadlessRuntime.Dispatch(() => fixture.Grid.SelectedItems!.Cast<object>()
            .Any(item => ReferenceEquals(item, originals[1])))).IsTrue();
        page.SelectGridCellComboItem(static p => p.Table, second, "Product", "Second");
        page.EditGridCellNumber(static p => p.Table, second, "Volume", 1000);
        page.ClickButton(static p => p.ReverseRows);
        page.OpenGridRow(static p => p.Table, second);

        await Assert.That(page.Opened.Text).IsEqualTo(second.Conditions.Single().Value);
        await Assert.That(GridValueReader.ReadCellNumber(page.Table, second, "Volume")).IsEqualTo(1000d);
        await Assert.That(originals[1].Product).IsEqualTo("Second");
        await Assert.That(originals[1].Volume).IsEqualTo(1000m);
        await Assert.That(originals[0].Product).IsEqualTo(string.Empty);
        await Assert.That(originals[0].Volume).IsEqualTo(0m);

        // Expected keys come from the fixture's original references, not captured business IDs.
        var previousRows = originals.Select(item => GridRowSelector.ByCell("RowKey", GridRowObjectKey.Get(item))).ToArray();
        page.ClickButton(static p => p.AddEmptyRow);
        var added = GridRowKeyReader.CaptureFirst(page.Table, previousRows);
        await Assert.That(previousRows.Any(previous => previous.Conditions.Single().Value == added.Conditions.Single().Value)).IsFalse();
        page.EditGridCellNumber(static p => p.Table, added, "Volume", 7);
        await Assert.That(GridValueReader.ReadCellNumber(page.Table, added, "Volume")).IsEqualTo(7d);
    }

    [Test]
    [Arguments("ru-RU", "12,3 kg/hour")]
    [Arguments("en-US", "12.3 kg/hour")]
    public async Task PublishedTextIsReadWithoutChangingTheRawValue(string cultureName, string expectedRate)
    {
        using var fixture = Launch(cultureName);
        var page = fixture.Page;
        var row = GridRowKeyReader.CaptureFirst(page.Table);
        await Assert.That(GridValueReader.ReadCellText(page.Table, row, "Components")).IsEmpty();
        await Assert.That(GridValueReader.ReadCellText(page.Table, row, "Rate")).IsEmpty();
        page.ClickButton(static p => p.PopulateFirst);

        var componentSnapshot = ((IAddressableGridControl)page.Table).ReadCell(new GridCellAddress(row, "Components"), 5000);
        var rateSnapshot = ((IAddressableGridControl)page.Table).ReadCell(new GridCellAddress(row, "Rate"), 5000);
        await Assert.That(componentSnapshot.DisplayText).IsEqualTo($"Alpha{Environment.NewLine}Beta");
        await Assert.That(rateSnapshot.DisplayText).IsEqualTo(expectedRate);
        await Assert.That(rateSnapshot.RawValue).IsEqualTo(12.345m);
        await Assert.That(ReferenceEquals(componentSnapshot.RawValue, fixture.Rows[0].Components)).IsTrue();
        await Assert.That(fixture.Rows[0].Rate).IsEqualTo(12.345m);
        await Assert.That(rateSnapshot.HasAuthoritativeDisplayText).IsTrue();
        HeadlessRuntime.Dispatch(() => { fixture.Rows[0].Rate = -12.345m; fixture.Grid.RefreshData(); });
        var negative = ((IAddressableGridControl)page.Table).ReadCell(new GridCellAddress(row, "Rate"), 5000);
        await Assert.That(negative.DisplayText).IsEqualTo("-" + expectedRate);
        await Assert.That(negative.RawValue).IsEqualTo(-12.345m);
        await Assert.That(fixture.Rows[0].Rate).IsEqualTo(-12.345m);
    }

    [Test]
    public async Task FilteredAndReplacedObjectsAreNotReadOrActedOn()
    {
        using var fixture = Launch();
        var page = fixture.Page;
        var row = GridRowKeyReader.CaptureFirst(page.Table);
        page.ClickButton(static p => p.FilterFirstRow);
        await Assert.That(HeadlessRuntime.Dispatch(() => fixture.Grid.ItemsSource!.Cast<GridPresentationFixture.Row>()
            .Any(r => ReferenceEquals(r, fixture.Rows[0])))).IsTrue();
        var grid = (IAddressableGridControl)page.Table;
        await Assert.That(grid.ResolveRow(row, 500).State).IsEqualTo(GridRowResolutionState.NotFound);
        await Assert.That(() => grid.ReadCell(new GridCellAddress(row, "Volume"), 100)).Throws<InvalidOperationException>();
        await Assert.That(() => page.EditGridCellNumber(static p => p.Table, row, "Volume", 99, timeoutMs: 100))
            .Throws<UiOperationException>();
        await Assert.That(fixture.Rows[0].Volume).IsEqualTo(0m);
        await Assert.That(() => grid.OpenRow(row, 100)).Throws<InvalidOperationException>();
        await Assert.That(() => ((IGridRowSelectionControl)page.Table).SelectRow(row, 100)).Throws<InvalidOperationException>();
        await Assert.That(page.Opened.Text).IsEqualTo("None");
        page.ClickButton(static p => p.RestoreRows);
        page.ClickButton(static p => p.CloneRows);
        await Assert.That(grid.ResolveRow(row, 500).State).IsEqualTo(GridRowResolutionState.NotFound);
        var newRow = GridRowKeyReader.CaptureFirst(page.Table);
        await Assert.That(newRow.Conditions.Single().Value).IsNotEqualTo(row.Conditions.Single().Value);
    }

    [Test]
    public async Task RepeatedReferenceAcrossTheViewIsAmbiguousBeforeTheGesture()
    {
        using var fixture = Launch();
        var page = fixture.Page;
        var row = GridRowKeyReader.CaptureFirst(page.Table);
        page.ClickButton(static p => p.DuplicateFirstRow);
        await Assert.That(((IAddressableGridControl)page.Table).ResolveRow(row, 500).State).IsEqualTo(GridRowResolutionState.Ambiguous);
        await Assert.That(() => ((IAddressableGridControl)page.Table).OpenRow(row, 100)).Throws<InvalidOperationException>();
        await Assert.That(() => page.SelectGridRows(static p => p.Table, [row], timeoutMs: 100)).Throws<InvalidOperationException>();
        await Assert.That(page.Opened.Text).IsEqualTo("None");
    }

    private static Fixture Launch(string cultureName = "en-US")
    {
        const string variable = "APPAUTOMATION_NATIVE_GRID_RUNTIME_FIXTURE";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "presentation");
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        try
        {
            var options = DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions();
            // Session owns its dispatcher; culture and fixture construction must run on that dispatcher.
            var desktop = DesktopAppSession.Launch(options);
            HeadlessRuntime.Dispatch(() =>
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                desktop.MainWindow.Show();
                desktop.MainWindow.Dispatcher.RunJobs();
                desktop.MainWindow.UpdateLayout();
            });
            return new Fixture(desktop, new Page(new HeadlessControlResolver(desktop.MainWindow)
                .WithGridAutomation(GridPresentationFixture.CreateCatalog())), previousCulture);
        }
        catch
        {
            CultureInfo.CurrentCulture = previousCulture;
            throw;
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); }
    }

    private sealed class Fixture(DesktopAppSession session, Page page, CultureInfo previousCulture) : IDisposable
    {
        public Page Page { get; } = page;
        public Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl Grid { get; } = HeadlessRuntime.Dispatch(() =>
            session.MainWindow.GetVisualDescendants().OfType<Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl>()
                .Single(grid => AutomationProperties.GetAutomationId(grid) == "PresentationGrid"));
        public GridPresentationFixture.Row[] Rows { get; } = HeadlessRuntime.Dispatch(() =>
            session.MainWindow.GetVisualDescendants().OfType<Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl>()
                .Single(grid => AutomationProperties.GetAutomationId(grid) == "PresentationGrid").ItemsSource!.Cast<GridPresentationFixture.Row>().ToArray());
        public void Dispose()
        {
            try { session.Dispose(); }
            finally { CultureInfo.CurrentCulture = previousCulture; }
        }
    }

    private sealed class Page(IUiControlResolver resolver) : UiPage(resolver)
    {
        public IGridControl Table => Resolve<IGridControl>(new("PresentationGrid", UiControlType.Grid, "PresentationGrid"));
        public ILabelControl Opened => Resolve<ILabelControl>(new("Opened", UiControlType.Label, "PresentationOpenedRow"));
        public IButtonControl ReverseRows => Button("ReverseRows");
        public IButtonControl PopulateFirst => Button("PopulateFirst");
        public IButtonControl FilterFirstRow => Button("FilterFirstRow");
        public IButtonControl RestoreRows => Button("RestoreRows");
        public IButtonControl CloneRows => Button("CloneRows");
        public IButtonControl DuplicateFirstRow => Button("DuplicateFirstRow");
        public IButtonControl AddEmptyRow => Button("AddEmptyRow");
        private IButtonControl Button(string id) => Resolve<IButtonControl>(new(id, UiControlType.Button, id));
    }
}
