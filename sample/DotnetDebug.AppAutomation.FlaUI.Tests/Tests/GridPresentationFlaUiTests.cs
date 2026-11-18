using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using DotnetDebug.AppAutomation.TestHost;
using DotnetDebug.Avalonia;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

[NotInParallel("DesktopUi")]
public sealed class GridPresentationFlaUiTests
{
    [Test]
    [Arguments("ru-RU", "12,3 kg/hour")]
    [Arguments("en-US", "12.3 kg/hour")]
    public async Task PublishedTextAndObjectRowsUseTheSameContractAsHeadless(string cultureName, string expectedRate)
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = Launch(cultureName);
        var page = CreatePage(session);
        var first = GridRowKeyReader.CaptureFirst(page.Table);
        var second = GridRowKeyReader.CaptureFirst(page.Table, [first]);
        await Assert.That(first.Conditions.Single().Value).IsNotEqualTo(second.Conditions.Single().Value);
        await Assert.That(GridValueReader.ReadCellText(page.Table, first, "Components")).IsEmpty();
        await Assert.That(GridValueReader.ReadCellText(page.Table, first, "Rate")).IsEmpty();
        page.ClickButton(static p => p.PopulateFirst);
        await Assert.That(GridValueReader.ReadCellText(page.Table, first, "Components"))
            .IsEqualTo($"Alpha{Environment.NewLine}Beta");
        await Assert.That(GridValueReader.ReadCellText(page.Table, first, "Rate")).IsEqualTo(expectedRate);
        var artifacts = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "TestResults"));
        Capture.Element(session.MainWindow).ToFile(Path.Combine(artifacts.FullName, $"grid-presentation-{cultureName}.png"));

        page.SelectGridRow(static p => p.Table, second);
        page.SelectGridCellComboItem(static p => p.Table, second, "Product", "Second");
        page.EditGridCellNumber(static p => p.Table, second, "Volume", 1000);
        page.ClickButton(static p => p.ReverseRows);
        page.OpenGridRow(static p => p.Table, second);
        await Assert.That(page.Opened.Text).IsEqualTo(second.Conditions.Single().Value);
        await Assert.That(GridValueReader.ReadCellNumber(page.Table, second, "Volume")).IsEqualTo(1000d);
        await Assert.That(GridValueReader.ReadCellText(page.Table, first, "Product")).IsEmpty();
    }

    [Test]
    public async Task FilteredClonedAndDuplicateObjectsFailBeforeOpeningARow()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = Launch("en-US");
        var page = CreatePage(session);
        var first = GridRowKeyReader.CaptureFirst(page.Table);
        var grid = (IAddressableGridControl)page.Table;
        page.ClickButton(static p => p.FilterFirstRow);
        await Assert.That(grid.ResolveRow(first, 5000).State).IsEqualTo(GridRowResolutionState.NotFound);
        await Assert.That(() => page.OpenGridRow(static p => p.Table, first)).Throws<UiOperationException>();
        await Assert.That(page.Opened.Text).IsEqualTo("None");
        page.ClickButton(static p => p.RestoreRows);
        page.ClickButton(static p => p.CloneRows);
        await Assert.That(grid.ResolveRow(first, 5000).State).IsEqualTo(GridRowResolutionState.NotFound);
        page.ClickButton(static p => p.RestoreRows);
        page.ClickButton(static p => p.DuplicateFirstRow);
        await Assert.That(() => page.OpenGridRow(static p => p.Table, first)).Throws<UiOperationException>();
        await Assert.That(() => page.SelectGridRow(static p => p.Table, first)).Throws<UiOperationException>();
        await Assert.That(page.Opened.Text).IsEqualTo("None");
    }

    private static DesktopAppSession Launch(string culture)
    {
        var defaults = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(buildBeforeLaunch: false);
        var session = DesktopAppSession.Launch(new DesktopAppLaunchOptions
        {
            ExecutablePath = defaults.ExecutablePath, WorkingDirectory = defaults.WorkingDirectory,
            Arguments = defaults.Arguments, DisposeCallback = defaults.DisposeCallback,
            MainWindowTimeout = defaults.MainWindowTimeout, PollInterval = defaults.PollInterval,
            WindowPlacement = defaults.WindowPlacement,
            EnvironmentVariables = new Dictionary<string, string?>(defaults.EnvironmentVariables, StringComparer.Ordinal)
            {
                ["APPAUTOMATION_NATIVE_GRID_RUNTIME_FIXTURE"] = "presentation",
                ["APPAUTOMATION_NATIVE_GRID_CULTURE"] = culture
            }
        });
        try
        {
            session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    private static Page CreatePage(DesktopAppSession session) => new(new FlaUiControlResolver(
        session.MainWindow, session.ConditionFactory).WithGridAutomation(GridPresentationFixture.CreateCatalog()));

    private sealed class Page(IUiControlResolver resolver) : UiPage(resolver)
    {
        public IGridControl Table => Resolve<IGridControl>(new("PresentationGrid", UiControlType.Grid, "PresentationGrid"));
        public ILabelControl Opened => Resolve<ILabelControl>(new("Opened", UiControlType.Label, "PresentationOpenedRow"));
        public IButtonControl PopulateFirst => Button("PopulateFirst");
        public IButtonControl ReverseRows => Button("ReverseRows");
        public IButtonControl FilterFirstRow => Button("FilterFirstRow");
        public IButtonControl RestoreRows => Button("RestoreRows");
        public IButtonControl CloneRows => Button("CloneRows");
        public IButtonControl DuplicateFirstRow => Button("DuplicateFirstRow");
        private IButtonControl Button(string id) => Resolve<IButtonControl>(new(id, UiControlType.Button, id));
    }
}
