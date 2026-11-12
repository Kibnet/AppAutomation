using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Automation.GridAutomation;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using AppAutomation.TestHost.Avalonia;
using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.Configuration;
using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using DotnetDebug.AppAutomation.TestHost;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

public sealed class FlaUiControlResolverTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task ClosedMultiSelectFilterReadsThePublishedSetNotItsConstantCaption()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = LaunchNativeGridRuntimeSession("closed-filter");
        var page = MainWindowFlaUiPageFactory.Create(session);
        await Assert.That(UiControlText.Read(page.ArmStatusFilter)).IsEqualTo("Archived");
        var missing = Assert.Throws<UiOperationException>(() =>
            page.WaitUntilSelectedItemsEqual(static p => p.ArmStatusFilter, ["Archived"], timeoutMs: 500));
        await Assert.That(missing!.InnerException!.Message).Contains("ItemStatus");

        Invoke("PublishSelection");
        page.WaitUntilSelectedItemsEqual(static p => p.ArmStatusFilter, ["Alpha; Beta", "Open"]);
        await Assert.That(page.ArmStatusFilter.SelectedItems).IsEquivalentTo(["Open", "Alpha; Beta"]);
        Invoke("ChangeSelection");
        page.WaitUntilSelectedItemsEqual(static p => p.ArmStatusFilter, ["Archived"]);
        Invoke("ClearSelection");
        page.WaitUntilSelectedItemsEqual(static p => p.ArmStatusFilter, []);
        await Assert.That(page.ArmStatusFilter.SelectedItems).IsEmpty();
        await Assert.That(UiControlText.Read(page.ArmStatusFilter)).IsEqualTo("Archived");
        Invoke("RemoveSelectionPublication");
        var unobserved = Assert.Throws<NotSupportedException>(() => _ = page.ArmStatusFilter.SelectedItems);
        await Assert.That(unobserved!.Message).Contains("ItemStatus");

        void Invoke(string id) => session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId(id))
            .Patterns.Invoke.Pattern.Invoke();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [NotInParallel("DesktopUi")]
    public async Task GridRowSetUsesTheCompleteNativeSelection(bool publishesSelection)
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = publishesSelection ? null : LaunchNativeGridRuntimeSession("row-set");
        using var fixture = publishesSelection ? new GridRowSetDesktopFixture() : null;
        var window = session?.MainWindow ?? fixture!.Window;
        var conditions = window.Automation.ConditionFactory;
        var catalog = new GridAutomationCatalog().Add(GridAutomationDefinition.ByAutomationIds(
                "ArmComplexDataGridControl", "ArmComplexDataGridControl", "ArmComplexDataGridControl")
            .WithColumns(GridColumnDefinition.Auto("Key").ReadIdentityFromRow(GridRowAutomationProperty.Name))
            .IdentifyRowsBy("Key"));
        var page = new MainWindowPage(new FlaUiControlResolver(window, conditions).WithGridAutomation(catalog));
        var a = GridRowSelector.ByCell("Key", "ROW-02");
        var b = GridRowSelector.ByCell("Key", "ROW-12");
        var c = GridRowSelector.ByCell("Key", "ROW-14");
        if (!publishesSelection)
        {
            var summary = window.FindFirstDescendant(conditions.ByAutomationId("GridSelectionSummary"));
            await Assert.That(summary?.Name).IsEqualTo("None");
            var unsupported = Assert.Throws<NotSupportedException>(() =>
                page.SelectGridRows(static candidate => candidate.ArmComplexDataGridControl, [a, b]));
            await Assert.That(unsupported!.Message).Contains("complete UI Automation multiple selection");
            await Assert.That(summary!.Name).IsEqualTo("None");
            return;
        }

        fixture!.SelectExtra();
        await Assert.That(fixture.Selection).IsEqualTo("ROW-03");
        page.SelectGridRows(static candidate => candidate.ArmComplexDataGridControl, [a, b], timeoutMs: 10_000);
        page.WaitUntilSelectedGridRowsEqual(static candidate => candidate.ArmComplexDataGridControl, [b, a]);
        await Assert.That(fixture.Selection).IsEqualTo("ROW-02,ROW-12");
        fixture.ScrollToLast();
        await Assert.That(fixture.IsRealized(1)).IsFalse();
        var artifacts = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "TestResults"));
        Capture.Element(window).ToFile(Path.Combine(artifacts.FullName, "grid-row-set-offscreen.png"));
        var mismatch = Assert.Throws<TimeoutException>(() => page.WaitUntilSelectedGridRowsEqual(
            static candidate => candidate.ArmComplexDataGridControl, [b], timeoutMs: 250));
        await Assert.That(mismatch!.Message).Contains("extra: [Key='ROW-02']");
        await Assert.That(fixture.IsRealized(1)).IsFalse();
        await Assert.That(fixture.Selection).IsEqualTo("ROW-02,ROW-12");
        page.SelectGridRows(static candidate => candidate.ArmComplexDataGridControl, [c, b], timeoutMs: 10_000);
        await Assert.That(fixture.Selection).IsEqualTo("ROW-12,ROW-14");
        page.SelectGridRows(static candidate => candidate.ArmComplexDataGridControl, []);
        await Assert.That(fixture.Selection).IsEmpty();
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task GridRowSetUsesPublishedCellKeysForVirtualizedNativeSelections()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var fixture = new GridRowSetDesktopFixture(compositeKey: true);
        var window = fixture.Window;
        var catalog = new GridAutomationCatalog().Add(GridAutomationDefinition.ByAutomationIds(
                "ArmComplexDataGridControl", "ArmComplexDataGridControl", "ArmComplexDataGridControl")
            .WithColumns(GridColumnDefinition.Auto("Key"), GridColumnDefinition.Auto("Suffix"))
            .IdentifyRowsBy("Key", "Suffix"));
        var page = new MainWindowPage(new FlaUiControlResolver(window, window.Automation.ConditionFactory).WithGridAutomation(catalog));
        fixture.SelectExtra();
        var key = GridRowSelector.ByCell("Key", "ROW-03").AndCell("Suffix", string.Empty);
        var horizontalOffset = fixture.HorizontalOffset;
        await Assert.That(() => page.WaitUntilSelectedGridRowsEqual(
            static candidate => candidate.ArmComplexDataGridControl, [key])).Throws<NotSupportedException>();
        await Assert.That(fixture.HorizontalOffset).IsEqualTo(horizontalOffset);
        fixture.ScrollToLast();
        await Assert.That(fixture.IsRealized(2)).IsFalse();
        fixture.PublishSelection(key);
        horizontalOffset = fixture.HorizontalOffset;
        page.WaitUntilSelectedGridRowsEqual(static candidate => candidate.ArmComplexDataGridControl, [key]);
        await Assert.That(fixture.HorizontalOffset).IsEqualTo(horizontalOffset);
        page.SelectGridRows(static candidate => candidate.ArmComplexDataGridControl, [key]);
        fixture.PublishSelection();
        await Assert.That(() => page.WaitUntilSelectedGridRowsEqual(static candidate => candidate.ArmComplexDataGridControl, []))
            .Throws<InvalidOperationException>();
        await Assert.That(fixture.Selection).IsEqualTo("ROW-03");
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task GridRowSetFixtureJoinsTheStaThreadWhenStartupTimesOut()
    {
        Thread? worker = null;
        using var entered = new ManualResetEventSlim();
        using var exit = new ManualResetEventSlim();
        var startup = Task.Run(() => Assert.Throws<TimeoutException>(() =>
            new GridRowSetDesktopFixture(TimeSpan.FromSeconds(1), () =>
            {
                worker = Thread.CurrentThread;
                entered.Set();
                exit.Wait(TimeSpan.FromSeconds(4));
            })));
        try
        {
            await Assert.That(entered.Wait(TimeSpan.FromSeconds(5))).IsTrue();
            await Task.Delay(1200);
        }
        finally { exit.Set(); }
        var timeout = await startup;
        await Assert.That(timeout).IsNotNull();
        await Assert.That(worker?.IsAlive).IsEqualTo(false);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [NotInParallel("DesktopUi")]
    public async Task GridRowSetUsesProtectedGesturesAndVerifiesTheCompletePublishedSelection(bool reverseRange)
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = LaunchNativeGridRuntimeSession("row-set-gestures");
        MainWindowFlaUiPageFactory.Create(session).SelectTabItem(static p => p.DataGridTabItem);
        var window = session.MainWindow;
        var conditions = window.Automation.ConditionFactory;
        var catalog = new GridAutomationCatalog().Add(GridAutomationDefinition.ByAutomationIds(
                nameof(GestureGridPage.Grid), "GridRowSetGestureTable", "GridRowSetGestureTable")
            .WithColumns(GridColumnDefinition.Auto("Key").ReadIdentityFromRow(GridRowAutomationProperty.ItemStatus))
            .IdentifyRowsBy("Key"));
        var page = new GestureGridPage(new FlaUiControlResolver(window, conditions).WithGridAutomation(catalog));
        await Assert.That(Find("ArmComplexDataGridControl")).IsNotNull();
        var root = Find("GridRowSetGestureTable");
        root.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        await Assert.That(root.Patterns.Selection.IsSupported).IsFalse();
        Find("SeedGridSelection").Patterns.Invoke.Pattern.Invoke();
        await Verify(0, 1);
        var beforeDisabled = Gestures();
        Find("DisableGestureRow").Patterns.Invoke.Pattern.Invoke();
        var disabled = Assert.Throws<InvalidOperationException>(() => Select(2, 12));
        await Assert.That(disabled!.Message).Contains("disabled");
        await Verify(0, 1);
        await Assert.That(Gestures()).IsEqualTo(beforeDisabled);
        Find("DisableGestureRow").Patterns.Invoke.Pattern.Invoke();
        Select(0);
        await Verify(0);
        var prefix = new List<int> { 0 };
        foreach (var next in new[] { 2, 5, 6, 12 })
        {
            var before = Gestures();
            prefix.Add(next);
            Select(prefix.ToArray());
            await Verify(prefix.ToArray());
            await Assert.That(Gestures()).IsEqualTo(before + "Control:1;");
        }
        var gestureCount = Gestures();
        Select(12, 6, 5, 2, 0);
        await Assert.That(Gestures()).IsEqualTo(gestureCount);
        await Assert.That(root.FindAllDescendants().Any(element =>
            element.Properties.ItemStatus.ValueOrDefault == "ARM-01" && !element.Properties.IsOffscreen.ValueOrDefault)).IsFalse();
        var missingExtra = Assert.Throws<TimeoutException>(() => page.WaitUntilSelectedGridRowsEqual(
            static p => p.Grid, [Row(12)], timeoutMs: 350));
        await Assert.That(missingExtra!.Message).Contains("ARM-01");

        Find("ChangeGridView").Patterns.Invoke.Pattern.Invoke();
        var beforeMissing = Gestures();
        var filteredOut = Assert.Throws<InvalidOperationException>(() => Select(17));
        await Assert.That(filteredOut!.Message).Contains("current view");
        await Assert.That(Gestures()).IsEqualTo(beforeMissing);
        var range = Enumerable.Range(3, 12).ToArray();
        // The view is reversed. First argument chooses the Shift anchor if it is one of the bounds.
        Select(reverseRange ? range : range.Reverse().ToArray());
        await Verify(range);
        await Assert.That(Gestures()).Contains("None:1;Shift:1;");
        await Assert.That(Gestures().Contains("Editor;", StringComparison.Ordinal)).IsFalse();
        var artifacts = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "TestResults"));
        Capture.Element(window).ToFile(Path.Combine(artifacts.FullName, $"grid-gesture-range-{(reverseRange ? "reverse" : "forward")}.png"));
        Select();
        await Verify();
        Select(0, 2, 5, 6, 12);
        await Verify(0, 2, 5, 6, 12);
        var beforeRefusal = Gestures();
        Find("WithholdGridSelection").Patterns.Invoke.Pattern.Invoke();
        var unavailable = Assert.Throws<NotSupportedException>(() => Select(0, 2));
        await Assert.That(unavailable!.Message).Contains("GridRowSelectionValue");
        await Assert.That(Gestures()).IsEqualTo(beforeRefusal);

        AutomationElement Find(string id) => window.FindFirstDescendant(conditions.ByAutomationId(id));
        string Gestures() => root.Properties.HelpText.ValueOrDefault ?? string.Empty;
        static GridRowSelector Row(int index) => GridRowSelector.ByCell("Key", $"ARM-{index + 1:D2}");
        void Select(params int[] indexes) => page.SelectGridRows(static p => p.Grid,
            indexes.Select(Row).ToArray(), timeoutMs: 12_000);
        async Task Verify(params int[] indexes)
        {
            page.WaitUntilSelectedGridRowsEqual(static p => p.Grid, indexes.Select(Row).ToArray());
            await Assert.That(Find("GridSelectionSummary").Properties.Name.ValueOrDefault ?? string.Empty)
                .IsEqualTo(string.Join(",", indexes.Order().Select(static index => $"ARM-{index + 1:D2}")));
        }
    }

    private sealed class GestureGridPage(IUiControlResolver resolver) : UiPage(resolver)
    {
        private static readonly UiControlDefinition Definition = new(nameof(Grid), UiControlType.Grid, "GridRowSetGestureTable");

        public IGridControl Grid => Resolve<IGridControl>(Definition);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [NotInParallel("DesktopUi")]
    public async Task StandaloneComboAppliesTypedValueWithoutLookingForClosedResults(bool alreadyOpen)
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        var defaults = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(buildBeforeLaunch: false);
        using var session = DesktopAppSession.Launch(new DesktopAppLaunchOptions
        {
            ExecutablePath = defaults.ExecutablePath,
            WorkingDirectory = defaults.WorkingDirectory,
            Arguments = defaults.Arguments,
            DisposeCallback = defaults.DisposeCallback,
            MainWindowTimeout = defaults.MainWindowTimeout,
            PollInterval = defaults.PollInterval,
            WindowPlacement = defaults.WindowPlacement,
            EnvironmentVariables = new Dictionary<string, string?>(defaults.EnvironmentVariables, StringComparer.Ordinal)
            { ["APPAUTOMATION_LOCAL_COMBO_FIXTURE"] = "1" }
        });
        var open = session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId("LocalChoice_OpenButton"))
            ?? throw new InvalidOperationException("The fixture does not expose its open button.");
        var toggle = open.Patterns.Toggle.Pattern;
        var lookups = new List<string>();
        var closedResultsLookups = 0;
        var page = new LocalComboBoxPage(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory), id =>
        {
            lookups.Add(id);
            if (id == "LocalChoice_Results" && toggle.ToggleState.Value == ToggleState.Off)
                closedResultsLookups++;
        });
        await Assert.That(page.Applied.Text).IsEqualTo("First");
        if (alreadyOpen) toggle.Toggle();
        await Assert.That(toggle.ToggleState.Value).IsEqualTo(alreadyOpen ? ToggleState.On : ToggleState.Off);
        var artifacts = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "TestResults"));
        Capture.Element(session.MainWindow).ToFile(Path.Combine(artifacts.FullName, $"local-combo-{alreadyOpen}-before.png"));
        lookups.Clear();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            page.SelectComboItem(static candidate => candidate.Choice, "Вариант B");
        }
        finally
        {
            Console.WriteLine($"Standalone ComboBox: {watch.ElapsedMilliseconds}ms; {string.Join(", ", lookups)}");
        }
        await Assert.That(page.Applied.Text).IsEqualTo("Second");
        await Assert.That(closedResultsLookups).IsEqualTo(0);
        await Assert.That(toggle.ToggleState.Value).IsEqualTo(ToggleState.Off);
        Capture.Element(session.MainWindow).ToFile(Path.Combine(artifacts.FullName, $"local-combo-{alreadyOpen}-after.png"));
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task ShellNavigationActivatesHiddenPaneThroughRealMenuFlyout()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        var defaults = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions();
        using var session = DesktopAppSession.Launch(new DesktopAppLaunchOptions
        {
            ExecutablePath = defaults.ExecutablePath,
            WorkingDirectory = defaults.WorkingDirectory,
            Arguments = defaults.Arguments,
            EnvironmentVariables = new Dictionary<string, string?>(defaults.EnvironmentVariables, StringComparer.Ordinal)
            { ["APPAUTOMATION_SHELL_OVERFLOW_FIXTURE"] = "1" },
            DisposeCallback = defaults.DisposeCallback,
            MainWindowTimeout = defaults.MainWindowTimeout,
            PollInterval = defaults.PollInterval,
            WindowPlacement = defaults.WindowPlacement
        });
        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
        var resolver = new FlaUiControlResolver(session.MainWindow, session.ConditionFactory).WithShellNavigation("ArmShellNavigation",
            ShellNavigationParts.ByAutomationIds("ArmShellNavigationList", "ArmShellPaneTabs", "ArmShellActivePaneLabel",
                ShellNavigationSourceKind.ListBox) with
            { OverflowButtonLocator = "ArmShellOverflowButton", OverflowMenuLocator = "ArmShellOverflowMenu", FallbackToName = false });
        var page = new MainWindowPage(resolver);
        page.SelectTabItem(static candidate => candidate.ArmDesktopTabItem);
        page.OpenOrActivateShellPane(static candidate => candidate.ArmShellNavigation, "Reports");
        page.ActivateShellPane(static candidate => candidate.ArmShellNavigation, "Reports");
        await Assert.That(page.ArmShellNavigation.ActivePaneName).IsEqualTo("Reports");
        await Assert.That(resolver.Resolve<ILabelControl>(new UiControlDefinition("Counter", UiControlType.Label,
            "ArmShellOverflowCounter")).Text).IsEqualTo("1:1");
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    [NotInParallel("DesktopUi")]
    public async Task NativeGridComboAppliesValueWithinOriginalBudgetAndPreservesCancel(bool cancel, bool enterOpensRow)
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = LaunchNativeGridRuntimeSession(enterOpensRow ? "enter" : "1");
        var page = CreateNativeGridRuntimePage(session);
        page.SelectTabItem(static candidate => candidate.DataGridTabItem);
        var row = GridRowSelector.ByCell("Key", "ARM-02");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        page.SelectGridCellComboItem(static candidate => candidate.ArmComplexDataGridControl, row, "State", "Closed",
            cancel ? GridCellEditCommitMode.Cancel : GridCellEditCommitMode.Commit);
        Console.WriteLine($"Native grid combo action completed in {watch.ElapsedMilliseconds}ms; cancel={cancel}.");
        await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl, row, "State"))
            .IsEqualTo(cancel ? "Pending" : "Closed");
        await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", "ARM-01"), "State")).IsEqualTo("Pending");
        await Assert.That(GridRowKeyReader.CaptureFirst(page.ArmComplexDataGridControl).Conditions.Single().Value).IsNotEmpty();
        await Assert.That(page.GridOpenMarkerLabel.Text).IsEqualTo("No row opened");
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task NativeGridCaptureUsesRowMetadataWithoutWaitingForCellProjection()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = LaunchNativeGridRuntimeSession("metadata");
        var page = CreateNativeGridRuntimePage(session);
        page.SelectTabItem(static candidate => candidate.DataGridTabItem);
        var native = session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId("ArmComplexDataGridControl"));
        var rows = native.FindAllDescendants(session.ConditionFactory.ByControlType(ControlType.DataItem));
        await Assert.That(rows.Any(row => !string.IsNullOrEmpty(row.ItemStatus))).IsTrue();
        await Assert.That(rows.All(row => row.FindAllChildren(session.ConditionFactory.ByAutomationId("State")).Length == 0)).IsTrue();
        var key = GridRowKeyReader.CaptureFirst(page.ArmComplexDataGridControl, timeoutMs: 2000);
        await Assert.That(key.Conditions.Single().Value).IsEqualTo(rows[0].ItemStatus);
    }

    [Test]
    [Arguments("RequiredAmount", GridCellEditorKind.Number, "997", "997", false, false, true)]
    [Arguments("RequiredAmount", GridCellEditorKind.Number, "1\u00a0200,5", "1200.5", false, false, true)]
    [Arguments("RequiredAmount", GridCellEditorKind.Number, "10 001", "10001", false, false, true)]
    [Arguments("RequiredAmount", GridCellEditorKind.Number, "-12,5", "-12.5", false, false, true)]
    [Arguments("RequiredAmount", GridCellEditorKind.Number, "500", "500", false, true, true)]
    [Arguments("RequiredAmount", GridCellEditorKind.Number, "500", "500", false, false, false)]
    [Arguments("RequiredAmount", GridCellEditorKind.Number, "997", "997", true, false, true)]
    [Arguments("Value", GridCellEditorKind.Text, "First line\nSecond line", "First line\nSecond line", false, false, true)]
    [Arguments("Value", GridCellEditorKind.Text, "Canceled text", "Canceled text", true, false, true)]
    [Arguments("IsApproved", GridCellEditorKind.CheckBox, "True", "True", false, false, true)]
    [Arguments("IsApproved", GridCellEditorKind.CheckBox, "True", "True", true, false, true)]
    [NotInParallel("DesktopUi")]
    public async Task NativeGridEditsCommitAndCancelWithoutParentEnter(
        string field, GridCellEditorKind kind, string input, string expected, bool cancel, bool commitTarget, bool keyboard)
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = LaunchNativeGridRuntimeSession("enter", "ru-RU");
        var page = CreateNativeGridRuntimePage(session, commitTarget, keyboard);
        page.SelectTabItem(static candidate => candidate.DataGridTabItem);
        var row = GridRowSelector.ByCell("Key", "ARM-02");
        var original = GridValueReader.ReadCellText(page.ArmComplexDataGridControl, row, field);
        var originalNumber = kind == GridCellEditorKind.Number
            ? GridValueReader.ReadCellNumber(page.ArmComplexDataGridControl, row, field) : 0;
        var mode = cancel ? GridCellEditCommitMode.Cancel : GridCellEditCommitMode.Commit;
        if (kind == GridCellEditorKind.Number)
        {
            var number = double.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
            if (input == expected)
                page.EditGridCellNumber(static candidate => candidate.ArmComplexDataGridControl, row, field, number, mode);
            else
                page.EditGridCellNumber(static candidate => candidate.ArmComplexDataGridControl, row, field,
                    number, input, "ru-RU", mode);
            await Assert.That(GridValueReader.ReadCellNumber(page.ArmComplexDataGridControl, row, field))
                .IsEqualTo(cancel ? originalNumber : number);
        }
        else
        {
            if (kind == GridCellEditorKind.CheckBox && cancel)
            {
                var failure = Assert.Throws<UiOperationException>(() => page.EditGridCell(
                    static candidate => candidate.ArmComplexDataGridControl, row, field, input, kind, mode));
                await Assert.That(failure.InnerException is NotSupportedException).IsTrue();
            }
            else
                page.EditGridCell(static candidate => candidate.ArmComplexDataGridControl, row, field, input, kind, mode);
            await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl, row, field))
                .IsEqualTo(cancel ? original : expected);
        }
        await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", "ARM-01"), "Value")).IsEqualTo("Value-0");
        await Assert.That(page.GridOpenMarkerLabel.Text).IsEqualTo("No row opened");
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task NativeGridRowSelectionDoesNotActivateItsFirstComboCell()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        using var session = LaunchNativeGridRuntimeSession("selection");
        var page = CreateNativeGridRuntimePage(session);
        page.SelectTabItem(static candidate => candidate.DataGridTabItem);
        var nativeGrid = session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId("ArmComplexDataGridControl"));
        var saveButton = session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByAutomationId("SaveArmGridProductButton"));
        var gridTitle = session.MainWindow.FindFirstDescendant(session.ConditionFactory.ByName("Three-row complex editor grid"));
        await Assert.That(nativeGrid.BoundingRectangle.Top).IsGreaterThanOrEqualTo(gridTitle.BoundingRectangle.Bottom);
        await Assert.That(nativeGrid.BoundingRectangle.Bottom).IsLessThanOrEqualTo(saveButton.BoundingRectangle.Top);
        var artifacts = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "TestResults"));
        Capture.Element(session.MainWindow).ToFile(Path.Combine(artifacts.FullName, "native-grid-layout.png"));
        page.SelectGridRow(static candidate => candidate.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", "ARM-02"));
        await Assert.That(page.GridOpenMarkerLabel.Text).IsEqualTo("No row opened");
        await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", "ARM-02"), "State")).IsEqualTo("Pending");
    }

    private static DesktopAppSession LaunchNativeGridRuntimeSession(string mode, string? culture = null)
    {
        var defaults = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(buildBeforeLaunch: false);
        var session = DesktopAppSession.Launch(new DesktopAppLaunchOptions
        {
            ExecutablePath = defaults.ExecutablePath,
            WorkingDirectory = defaults.WorkingDirectory,
            Arguments = defaults.Arguments,
            DisposeCallback = defaults.DisposeCallback,
            MainWindowTimeout = defaults.MainWindowTimeout,
            PollInterval = defaults.PollInterval,
            WindowPlacement = defaults.WindowPlacement,
            EnvironmentVariables = new Dictionary<string, string?>(defaults.EnvironmentVariables, StringComparer.Ordinal)
            {
                ["APPAUTOMATION_NATIVE_GRID_RUNTIME_FIXTURE"] = mode,
                ["APPAUTOMATION_NATIVE_GRID_CULTURE"] = culture
            }
        });
        try
        {
            session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private static MainWindowPage CreateNativeGridRuntimePage(DesktopAppSession session, bool commitTarget = false, bool keyboard = true)
    {
        var catalog = new GridAutomationCatalog().Add(GridAutomationDefinition.ByAutomationIds(
                "ArmComplexDataGridControl", "ArmComplexDataGridControl", "ArmComplexDataGridControl")
            .WithColumns(GridColumnDefinition.Auto("Key").ReadIdentityFromRow(GridRowAutomationProperty.ItemStatus),
                GridColumnDefinition.Auto("State").AsValue(GridCellValueKind.Selection).EditWith(GridCellEditorKind.ComboBox,
                    new GridCellEditorParts(new GridRelativeLocator("GridCombo_Input"),
                        Results: new GridRelativeLocator("GridCombo_Results", GridRelativeLocatorScope.DetachedPopup),
                        OpenButton: new GridRelativeLocator("GridCombo_Open", GridRelativeLocatorScope.GridRoot))),
                GridColumnDefinition.Auto("RequiredAmount").AsValue(GridCellValueKind.Number).FormatWith("G", "ru-RU")
                    .EditWith(GridCellEditorKind.Number, new GridCellEditorParts(new GridRelativeLocator("GridNumber_Input"),
                        CommitTarget: commitTarget ? new GridRelativeLocator("Key", GridRelativeLocatorScope.Row) : null,
                        UseKeyboardInput: keyboard)),
                GridColumnDefinition.Auto("Value").AsValue(GridCellValueKind.Text).EditWith(GridCellEditorKind.Text),
                GridColumnDefinition.Auto("IsApproved").AsValue(GridCellValueKind.Boolean).EditWith(GridCellEditorKind.CheckBox))
            .IdentifyRowsBy("Key"));
        return MainWindowFlaUiPageFactory.Create(session, catalog);
    }

    [Test]
    public async Task GridCommitTarget_DeterminesRevealDirectionFromTheRealTargetBounds()
    {
        var viewport = new System.Drawing.Rectangle(100, 100, 300, 200);
        await Assert.That(GridCellClickGeometry.HorizontalRevealDirection(
            new System.Drawing.Rectangle(-100, 120, 100, 20), viewport)).IsEqualTo(false);
        await Assert.That(GridCellClickGeometry.HorizontalRevealDirection(
            new System.Drawing.Rectangle(450, 120, 100, 20), viewport)).IsEqualTo(true);
        await Assert.That(GridCellClickGeometry.HorizontalRevealDirection(
            new System.Drawing.Rectangle(200, 120, 100, 20), viewport)).IsNull();
        await Assert.That(GridCellClickGeometry.HorizontalRevealDirection(
            System.Drawing.Rectangle.Empty, viewport)).IsNull();
    }

    [Test]
    public async Task GridColumnReveal_DoesNotTreatCatalogGapsAsFrozenBoundaries()
    {
        // A later catalog column is visible before an earlier one; the target is not materialized.
        await Assert.That(NativeGridHorizontalReveal.DirectionFromCatalog(6, [1, 2, 4, 3, 13, 5])).IsNull();
        await Assert.That(NativeGridHorizontalReveal.DirectionFromCatalog(6, [1, 2, 3, 4, 5, 13])).IsNull();
        await Assert.That(NativeGridHorizontalReveal.DirectionFromCatalog(3, [4, 5, 6])).IsEqualTo(false);
        await Assert.That(NativeGridHorizontalReveal.DirectionFromCatalog(7, [4, 5, 6])).IsEqualTo(true);
    }

    [Test]
    [Arguments(0, 2, false, true, "RR", true)]
    [Arguments(2, 0, true, true, "LL", true)]
    [Arguments(1, 0, true, true, "RLL", true)]
    [Arguments(1, 0, true, false, "RL", true)]
    [Arguments(1, 1, true, true, "", true)]
    [Arguments(0, -1, false, true, "RR", false)]
    public async Task GridColumnReveal_SearchesReachablePagesWithoutOscillating(
        int initialPage, int targetPage, bool hint, bool canMoveRight, string expectedMoves, bool expectedFound)
    {
        var page = initialPage;
        var moves = new List<char>();
        var found = NativeGridHorizontalReveal.TryReveal(
            () => new HorizontalGridViewport(page, 0, 2),
            () => hint,
            () => page == targetPage,
            forward =>
            {
                moves.Add(forward ? 'R' : 'L');
                if (forward && !canMoveRight) return false;
                page += forward ? 1 : -1;
                return true;
            },
            () => true);

        await Assert.That(found).IsEqualTo(expectedFound);
        await Assert.That(new string(moves.ToArray())).IsEqualTo(expectedMoves);
    }

    [Test]
    public async Task GridCommitTarget_RevealsAndReacquiresTheCurrentPartWithoutUsingTheOldProjection()
    {
        var oldTarget = new object();
        var currentTarget = new object();
        object? observed = oldTarget;
        var visible = false;
        var scrolls = 0;
        var result = FlaUiControlResolver.ResolveVisibleGridEditorPart(
            () => observed, _ => visible,
            isVisible =>
            {
                scrolls++;
                observed = currentTarget;
                visible = true;
                if (!isVisible()) throw new InvalidOperationException("Rebound target was not observed.");
            }, "Missing target");
        await Assert.That(ReferenceEquals(result, currentTarget)).IsTrue();
        await Assert.That(scrolls).IsEqualTo(1);
        FlaUiControlResolver.ResolveVisibleGridEditorPart(
            () => observed, _ => visible, _ => scrolls++, "Missing target");
        await Assert.That(scrolls).IsEqualTo(1);
        await Assert.That(() => FlaUiControlResolver.ResolveVisibleGridEditorPart<object>(
            () => null, _ => true, _ => { }, "Missing target"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => FlaUiControlResolver.ResolveVisibleGridEditorPart<object>(
            () => throw new InvalidOperationException("Ambiguous row part"), _ => true, _ => { }, "Missing target"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UiaLookup_RetriesOnlyKnownTransientComFailureBeforeInvokingAnAction()
    {
        var reads = 0;
        var value = FlaUiControlResolver.ReadTransientUiaLookup(() =>
        {
            if (++reads == 1)
            {
                throw new System.Runtime.InteropServices.COMException(
                    "Сбой чтения UIA",
                    unchecked((int)0x80004005));
            }

            return "OpenButton";
        });
        var permanentReads = 0;
        var permanent = Assert.Throws<System.Runtime.InteropServices.COMException>(() =>
            FlaUiControlResolver.ReadTransientUiaLookup<string>(() =>
            {
                permanentReads++;
                throw new System.Runtime.InteropServices.COMException("Access denied", unchecked((int)0x80070005));
            }, () => TimeSpan.FromSeconds(1), () => throw new InvalidOperationException("Permanent errors must not retry")));

        var root = "old";
        var refreshes = 0;
        var rebound = FlaUiControlResolver.ReadTransientUiaLookup(() =>
        {
            if (root == "old")
                throw new System.Runtime.InteropServices.COMException("Header enumeration failed", unchecked((int)0x80004005));
            return root;
        }, () => TimeSpan.FromSeconds(1), () => { refreshes++; root = "current"; });

        var transient = new System.Runtime.InteropServices.COMException("Original header read", unchecked((int)0x80004005));
        var budgetReads = 0;
        var boundedReads = 0;
        var exhausted = Assert.Throws<System.Runtime.InteropServices.COMException>(() =>
            FlaUiControlResolver.ReadTransientUiaLookup<string>(() =>
            {
                boundedReads++;
                throw transient;
            }, () => ++budgetReads == 1 ? TimeSpan.FromSeconds(1) : throw new TimeoutException("Original budget exhausted")));

        var lateReads = 0;
        var expired = false;
        var late = Assert.Throws<System.Runtime.InteropServices.COMException>(() =>
            FlaUiControlResolver.ReadTransientUiaLookup(() =>
            {
                if (++lateReads == 1) throw transient;
                expired = true;
                return "Late header snapshot";
            }, () => expired ? TimeSpan.Zero : TimeSpan.FromSeconds(1)));

        var failedReads = 0;
        var failedRefreshes = 0;
        var failed = Assert.Throws<System.Runtime.InteropServices.COMException>(() =>
            FlaUiControlResolver.ReadTransientUiaLookup<string>(() =>
            {
                failedReads++;
                throw transient;
            }, () => TimeSpan.FromSeconds(1), () => failedRefreshes++));

        var nestedReads = 0;
        var nestedExpired = false;
        var nestedTimeout = Assert.Throws<System.Runtime.InteropServices.COMException>(() =>
            FlaUiControlResolver.ReadTransientUiaLookup<string>(() =>
            {
                if (++nestedReads == 1) throw transient;
                nestedExpired = true;
                throw new TimeoutException("Grid root lookup exhausted the original budget");
            }, () => nestedExpired ? TimeSpan.Zero : TimeSpan.FromSeconds(1)));

        using (Assert.Multiple())
        {
            await Assert.That(value).IsEqualTo("OpenButton");
            await Assert.That(reads).IsEqualTo(2);
            await Assert.That(permanentReads).IsEqualTo(1);
            await Assert.That(permanent.HResult).IsEqualTo(unchecked((int)0x80070005));
            await Assert.That(rebound).IsEqualTo("current");
            await Assert.That(refreshes).IsEqualTo(1);
            await Assert.That(ReferenceEquals(exhausted, transient)).IsTrue();
            await Assert.That(exhausted.StackTrace).Contains(nameof(UiaLookup_RetriesOnlyKnownTransientComFailureBeforeInvokingAnAction));
            await Assert.That(boundedReads).IsEqualTo(1);
            await Assert.That(ReferenceEquals(late, transient)).IsTrue();
            await Assert.That(lateReads).IsEqualTo(2);
            await Assert.That(ReferenceEquals(failed, transient)).IsTrue();
            await Assert.That(failedReads).IsEqualTo(3);
            await Assert.That(failedRefreshes).IsEqualTo(2);
            await Assert.That(ReferenceEquals(nestedTimeout, transient)).IsTrue();
            await Assert.That(nestedReads).IsEqualTo(2);
        }
    }

    [Test]
    public async Task MultiSelectItemProjectionsMergeOverlappingCopiesWithoutHidingDistinctRows()
    {
        var sameRow = new[]
        {
            (Text: "Cancelled", Bounds: new System.Drawing.Rectangle(20, 40, 180, 24), Actionable: false),
            (Text: "Cancelled", Bounds: new System.Drawing.Rectangle(24, 42, 170, 20), Actionable: true)
        };
        var textReads = 0;
        var boundsReads = 0;
        var actionabilityReads = 0;
        var logical = FlaUiMultiSelectItemProjections.Normalize(
            sameRow,
            item => { textReads++; return item.Text; },
            item => { boundsReads++; return item.Bounds; },
            item => { actionabilityReads++; return item.Actionable; });
        var separateRows = new[]
        {
            sameRow[0],
            (Text: "Cancelled", Bounds: new System.Drawing.Rectangle(20, 72, 180, 24), Actionable: true)
        };

        using (Assert.Multiple())
        {
            await Assert.That(logical.Length).IsEqualTo(1);
            await Assert.That(logical[0].Actionable).IsTrue();
            await Assert.That(textReads).IsEqualTo(2);
            await Assert.That(boundsReads).IsEqualTo(2);
            await Assert.That(actionabilityReads).IsEqualTo(2);
            await Assert.That(() => FlaUiMultiSelectItemProjections.Normalize(
                    separateRows,
                    static item => item.Text,
                    static item => item.Bounds,
                    static item => item.Actionable))
                .Throws<InvalidOperationException>();
        }
    }

    [Test]
    public async Task MultiSelectItemProjectionsPreferRealItemsOverServiceCheckBox()
    {
        var preferListItems = FlaUiMultiSelectItemProjections.PreferSelectableItems(
            ["Select all"],
            ["Customer A", "Customer B"]);
        var preferCheckBoxes = FlaUiMultiSelectItemProjections.PreferSelectableItems(
            ["Open", "Closed"],
            ["Open", "Closed"]);

        using (Assert.Multiple())
        {
            await Assert.That(preferListItems).IsTrue();
            await Assert.That(preferCheckBoxes).IsFalse();
            await Assert.That(FlaUiMultiSelectItemProjections.PreferSelectableItems(
                ["Customer A"], ["Customer A", "Customer B"])).IsFalse();
        }
    }

    [Test]
    public async Task MultiSelectVisibleSelectionSkipsTraversalOnlyWhenSelectionIsComplete()
    {
        using (Assert.Multiple())
        {
            await Assert.That(FlaUiMultiSelectItemProjections.CanUseVisibleSelection(
                ["Target"], ["Other", "Target"], ["Other"])).IsTrue();
            await Assert.That(FlaUiMultiSelectItemProjections.CanUseVisibleSelection(
                ["Target"], ["Other", "Target"], ["Hidden selection"])).IsFalse();
            await Assert.That(FlaUiMultiSelectItemProjections.CanUseVisibleSelection(
                ["Hidden target"], ["Other", "Target"], [])).IsFalse();
        }
    }

    private const string CalendarFallbackFixtureEnvironmentVariable =
        "APPAUTOMATION_FLAUI_CALENDAR_FALLBACK_FIXTURE";

    private static readonly UiWaitOptions DesktopControlWaitOptions = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        PollInterval = TimeSpan.FromMilliseconds(200)
    };

    private static readonly string[] ExpectedMultiSelectItems =
    [
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta",
        "Eta", "Theta", "Iota", "Kappa", "Lambda", "Mu",
        "Nu", "Xi", "Omicron", "Pi", "Rho", "Sigma",
        "Tau", "Upsilon", "Phi", "Chi", "Psi", "Omega"
    ];

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task EremexMultiSelectPopup_ExposesInstrumentedPartsAndReadsAllItems()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions());
        var desktop = session.MainWindow.Automation.GetDesktop();
        var observed = new MultiSelectOpenObserver();
        var page = new MainWindowPage(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory)
            .WithAdapters(observed)
            .WithMultiSelect("MultiSelection", MultiSelectParts.ByAutomationIds(
                "MultiSelection", "MultiSelection_OpenButton", "MultiSelection_Results",
                "MultiSelection_ApplyButton", "MultiSelection_CancelButton")));
        page.SelectTabItem(static candidate => candidate.ControlMixTabItem);
        page.MultiSelection.Open();
        var popup = FindInstrumentedMultiSelectPopup(session, desktop);

        using (Assert.Multiple())
        {
            await Assert.That(popup.Results.AutomationId).IsEqualTo("MultiSelection_Results");
            await Assert.That(popup.ApplyButton.AutomationId).IsEqualTo("MultiSelection_ApplyButton");
            await Assert.That(popup.CancelButton.AutomationId).IsEqualTo("MultiSelection_CancelButton");
            await Assert.That(page.MultiSelection.Items).IsEquivalentTo(ExpectedMultiSelectItems);
        }

        global::AppAutomation.FlaUI.Input.DesktopPointer.Click(popup.CancelButton);
        page.SelectMultiItems(candidate => candidate.MultiSelection, ["Alpha", "Omega"]);
        var opensAfterSelection = observed.Opens;
        var lookupsAfterSelection = observed.ItemLookups;
        page.WaitUntilSelectedItemsEqual(candidate => candidate.MultiSelection, ["Alpha", "Omega"]);
        page.WaitUntilSelectedItemsEqual(candidate => candidate.MultiSelection, ["Omega", "Alpha"]);
        await Assert.That(observed.Opens).IsEqualTo(opensAfterSelection);
        await Assert.That(observed.ItemLookups).IsEqualTo(lookupsAfterSelection);
        page.CancelMultiSelection(candidate => candidate.MultiSelection, ["Beta"]);
        await Assert.That(observed.Opens).IsEqualTo(opensAfterSelection + 1);
        page.ClickButton(candidate => candidate.MultiSelectionResetButton)
            .WaitUntilSelectedItemsEqual(candidate => candidate.MultiSelection, []);
        await Assert.That(observed.Opens).IsEqualTo(opensAfterSelection + 1);
    }

    private sealed class MultiSelectOpenObserver : IUiControlAdapter
    {
        public int Opens { get; private set; }
        public int ItemLookups { get; private set; }

        public bool CanResolve(Type requestedType, UiControlDefinition definition) =>
            requestedType == typeof(IButtonControl) && definition.LocatorValue == "MultiSelection_OpenButton"
            || requestedType == typeof(IMultiSelectItemsControl) && definition.LocatorValue == "MultiSelection_Results";

        public object Resolve(Type requestedType, UiControlDefinition definition, IUiControlResolver innerResolver)
        {
            if (requestedType == typeof(IMultiSelectItemsControl))
            {
                ItemLookups++;
                return innerResolver.Resolve<IMultiSelectItemsControl>(definition);
            }
            return new ObservedOpenButton(innerResolver.Resolve<IButtonControl>(definition), () => Opens++);
        }

        private sealed class ObservedOpenButton(IButtonControl inner, Action invoked) : IButtonControl
        {
            public string AutomationId => inner.AutomationId;
            public string Name => inner.Name;
            public bool IsEnabled => inner.IsEnabled;
            public void Invoke() { invoked(); inner.Invoke(); }
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task MultiSelectMissingItem_DoesNotPartiallyChangeDesktopSelection()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions());
        var page = MainWindowFlaUiPageFactory.Create(session);
        page.SelectTabItem(static candidate => candidate.ControlMixTabItem);
        page.MultiSelection.Open();

        try
        {
            var resolver = new FlaUiControlResolver(session.MainWindow, session.ConditionFactory);
            var items = resolver.Resolve<IMultiSelectItemsControl>(new UiControlDefinition(
                "MultiSelectionItems",
                UiControlType.ListBox,
                "MultiSelection_Results",
                UiLocatorKind.AutomationId,
                FallbackToName: false));
            items.SetSelectedItems(["Alpha"]);

            await Assert.That(() => items.SetSelectedItems(["Beta", "Missing"]))
                .Throws<InvalidOperationException>();
            await Assert.That(items.SelectedItems).IsEquivalentTo(["Alpha"]);
        }
        finally
        {
            if (page.MultiSelection.IsOpen)
            {
                page.MultiSelection.Cancel();
            }
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task SelectListBoxItem_ByCapability_SelectsDesktopItem()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions());
        var page = new MainWindowPage(new FlaUiControlResolver(session.MainWindow, session.ConditionFactory));

        page
            .SelectTabItem(static candidate => candidate.HierarchyTabItem)
            .SelectTreeItem(static candidate => candidate.DemoTree, "Fibonacci")
            .WaitUntilHasItemsAtLeast(static candidate => candidate.HierarchySelectionList, 2);

        var ambiguous = Assert.Throws<InvalidOperationException>(() =>
            page.SelectListBoxItem(static candidate => candidate.HierarchySelectionList, "Fibonacci"));
        page.SelectListBoxItem(static candidate => candidate.HierarchySelectionList, "Path length: 1");

        var selectableList = page.HierarchySelectionList as ISelectableListBoxControl;

        using (Assert.Multiple())
        {
            await Assert.That(ambiguous.Message).Contains("ambiguous");
            await Assert.That(selectableList).IsNotNull();
            await Assert.That(selectableList!.SelectedItemText).IsEqualTo("Path length: 1");
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task SearchHistory_ReadsItemsFromItsPopupScope()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions());
        var page = MainWindowFlaUiPageFactory.Create(session);

        page.SelectTabItem(static candidate => candidate.ArmDesktopTabItem);
        page.ArmTableSearch.OpenHistory();

        var historyItems = UiWait.Until(
            () => page.ArmTableSearch.HistoryItems,
            static items => items.Count == 3,
            DesktopControlWaitOptions,
            "Search history items did not become available.");

        await Assert.That(historyItems)
            .IsEquivalentTo(["orders", "customers", "reports"]);

        var resolver = new FlaUiControlResolver(session.MainWindow, session.ConditionFactory);
        var repeatedButton = new UiControlDefinition("HistoryItem", UiControlType.Button,
            "ArmTableSearchHistoryItemButton")
        {
            Scope = new UiControlScope("ArmTableSearchHistoryRoot") { AnchorLocatorValue = "ArmTableSearchInput" }
        };
        UiControlResolutionException? ambiguity = null;
        try
        {
            resolver.Resolve<IButtonControl>(repeatedButton);
        }
        catch (UiControlResolutionException exception)
        {
            ambiguity = exception;
        }

        await Assert.That(ambiguity?.Failure).IsEqualTo(UiControlResolutionFailure.Ambiguous);
        await Assert.That(() => resolver.Resolve<IButtonControl>(repeatedButton with
        { LocatorValue = "MissingHistoryButton" }))
            .Throws<UiControlResolutionException>();
        var otherHistory = resolver.Resolve<ISearchHistoryItemsControl>(repeatedButton with
        {
            Scope = new UiControlScope("AnotherHistoryRoot") { AnchorLocatorValue = "ArmTableSearchInput" }
        });
        await Assert.That(otherHistory.Items).IsEmpty();
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task CompositeDatePicker_SelectsDateByVisibleCalendarCell_WhenNativeSelectionIsUnsupported()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        using var session = DesktopAppSession.Launch(CreateCalendarFallbackLaunchOptions());
        var calendarTab = session.MainWindow
            .FindFirstDescendant(session.ConditionFactory.ByAutomationId("CalendarTabItem"))
            ?.AsTabItem()
            ?? throw new InvalidOperationException("Calendar tab was not found.");
        calendarTab.Select();
        var resolver = new FlaUiControlResolver(session.MainWindow, session.ConditionFactory)
            .WithDateTimePickerProxy(
                "DatePicker",
                DatePickerParts.ByAutomationIds(
                    "FlaUiCalendarFallbackFixture",
                    "FlaUiCalendarFallbackValue",
                    "FlaUiCalendarFallbackOpen",
                    "FlaUiCalendarFallbackCalendar"));
        var page = new CalendarFallbackPage(resolver);
        var targetDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
            .AddMonths(1)
            .AddDays(2);

        page.SetDate(static candidate => candidate.DatePicker, targetDate);

        await Assert.That(page.DatePicker.SelectedDate?.Date).IsEqualTo(targetDate.Date);
    }

    [Test]
    public async Task CalendarMonthNavigation_RetriesWhenFirstClickDoesNotChangeMonth()
    {
        var initialMonth = new DateTime(2026, 9, 1);
        var displayedMonth = initialMonth;
        var clickCount = 0;

        var changed = FlaUiCalendarSelection.NavigateMonthWithRetry(
            () => displayedMonth,
            () =>
            {
                clickCount++;
                if (clickCount == 2)
                {
                    displayedMonth = initialMonth.AddMonths(1);
                }
            },
            initialMonth,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(1));

        using (Assert.Multiple())
        {
            await Assert.That(changed).IsTrue();
            await Assert.That(clickCount).IsEqualTo(2);
            await Assert.That(displayedMonth).IsEqualTo(initialMonth.AddMonths(1));
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task VisibleGridRowSelection_ConfirmsResolvedRowWithoutRedundantRefresh()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions());
        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
        var page = MainWindowFlaUiPageFactory.Create(session);
        page.SelectTabItem(static candidate => candidate.DataGridTabItem);
        var grid = session.MainWindow.FindFirstDescendant(
            session.ConditionFactory.ByAutomationId("ArmComplexDataGridControl"));

        foreach (var key in new[] { "ARM-02", "ARM-03", "ARM-01" })
        {
            var row = grid.FindAllDescendants(session.ConditionFactory.ByControlType(ControlType.DataItem))
                .Single(candidate => string.Equals(candidate.ItemStatus, key, StringComparison.Ordinal));
            var refreshes = 0;
            void SelectResolvedRow() =>
                FlaUiControlResolver.SelectAndConfirmGridRow(
                    row,
                    grid,
                    timeoutMs: 5000,
                    $"Sample grid row '{key}'",
                    () =>
                    {
                        refreshes++;
                        if (refreshes > 3)
                        {
                            throw new TimeoutException("Redundant row scans exhausted the selection budget before confirmation.");
                        }
                        return grid.FindAllDescendants(session.ConditionFactory.ByControlType(ControlType.DataItem))
                            .Single(candidate => string.Equals(candidate.ItemStatus, key, StringComparison.Ordinal));
                    },
                    isCurrentRowMatch: candidate => string.Equals(candidate.ItemStatus, key, StringComparison.Ordinal));

            SelectResolvedRow();

            await Assert.That(refreshes).IsEqualTo(0);
            page
                .WaitUntilIsEnabled(static candidate => candidate.SaveArmGridProductButton)
                .ClickButton(static candidate => candidate.SaveArmGridProductButton)
                .WaitUntilIsEnabled(static candidate => candidate.SaveArmGridProductButton, expected: false);
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task NarrowGridReadsOffscreenColumnThenVisibleColumnByStableRow()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        var defaults = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions();
        var environment = new Dictionary<string, string?>(defaults.EnvironmentVariables, StringComparer.Ordinal)
        {
            ["APPAUTOMATION_FLAUI_GRID_NARROW_LAYOUT"] = "1"
        };
        using var session = DesktopAppSession.Launch(new DesktopAppLaunchOptions
        {
            ExecutablePath = defaults.ExecutablePath,
            WorkingDirectory = defaults.WorkingDirectory,
            Arguments = defaults.Arguments,
            EnvironmentVariables = environment,
            DisposeCallback = defaults.DisposeCallback,
            MainWindowTimeout = defaults.MainWindowTimeout,
            PollInterval = defaults.PollInterval,
            WindowPlacement = defaults.WindowPlacement
        });
        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
        var page = MainWindowFlaUiPageFactory.Create(session);
        page.SelectTabItem(static candidate => candidate.DataGridTabItem);
        var grid = page.ArmComplexDataGridControl;
        var row = GridRowKeyReader.Capture(grid, 0);
        var cellKeyCatalog = new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds(
                    "ArmComplexDataGridControl",
                    "ArmComplexDataGridControl",
                    "ArmComplexDataGridControl")
                .WithColumns(GridColumnDefinition.Auto("Value"))
                .IdentifyRowsBy("Value"));
        var cellKeyGrid = MainWindowFlaUiPageFactory.Create(session, cellKeyCatalog)
            .ArmComplexDataGridControl;
        var cellKey = GridRowKeyReader.Capture(cellKeyGrid, 0);
        var product = GridValueReader.ReadCellText(grid, row, "Product");
        var value = GridValueReader.ReadCellText(grid, row, "Value");

        using (Assert.Multiple())
        {
            await Assert.That(row.Conditions.Single().Value).IsEqualTo("ARM-01");
            await Assert.That(product).Contains("Product");
            await Assert.That(value).IsEqualTo("Value-1");
            await Assert.That(cellKey.Conditions.Single().Value).IsEqualTo("Value-1");
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task PartiallyVisibleGridCell_EditsByStableRowWithinTheViewport()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        var defaults = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions();
        var environment = new Dictionary<string, string?>(defaults.EnvironmentVariables, StringComparer.Ordinal)
        {
            ["APPAUTOMATION_FLAUI_GRID_PARTIAL_CELL"] = "1"
        };
        using var session = DesktopAppSession.Launch(new DesktopAppLaunchOptions
        {
            ExecutablePath = defaults.ExecutablePath,
            WorkingDirectory = defaults.WorkingDirectory,
            Arguments = defaults.Arguments,
            EnvironmentVariables = environment,
            DisposeCallback = defaults.DisposeCallback,
            MainWindowTimeout = defaults.MainWindowTimeout,
            PollInterval = defaults.PollInterval,
            WindowPlacement = defaults.WindowPlacement
        });
        var page = MainWindowFlaUiPageFactory.Create(session);
        page.SelectTabItem(static candidate => candidate.DataGridTabItem);

        var gridElement = session.MainWindow.FindFirstDescendant(
            session.ConditionFactory.ByAutomationId("ArmComplexDataGridControl"))
            ?? throw new InvalidOperationException("The sample grid was not found.");
        var gridBounds = gridElement.BoundingRectangle;
        var clippedCell = gridElement.FindAllDescendants()
            .FirstOrDefault(candidate => candidate.ControlType == ControlType.Custom
                && candidate.BoundingRectangle.Left < gridBounds.Right
                && candidate.BoundingRectangle.Right > gridBounds.Right);
        await Assert.That(clippedCell).IsNotNull();
        await Assert.That(clippedCell!.BoundingRectangle.Left + clippedCell.BoundingRectangle.Width / 2)
            .IsGreaterThan(gridBounds.Right);

        page.EditGridCellText(
            static candidate => candidate.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", "ARM-02"),
            "Value",
            "Visible portion edit");

        await Assert.That(GridValueReader.ReadCellText(
            page.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", "ARM-02"),
            "Value")).IsEqualTo("Visible portion edit");
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task NativeGridCell_RebindsColumnAfterSelectionChangesMaterializedCells()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();
        var defaults = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions();
        using var session = DesktopAppSession.Launch(new DesktopAppLaunchOptions
        {
            ExecutablePath = defaults.ExecutablePath,
            WorkingDirectory = defaults.WorkingDirectory,
            Arguments = defaults.Arguments,
            EnvironmentVariables = new Dictionary<string, string?>(defaults.EnvironmentVariables, StringComparer.Ordinal)
            {
                ["APPAUTOMATION_GRID_COLUMNS_CHANGE_ON_SELECTION"] = "1"
            },
            DisposeCallback = defaults.DisposeCallback,
            MainWindowTimeout = defaults.MainWindowTimeout,
            PollInterval = defaults.PollInterval,
            WindowPlacement = defaults.WindowPlacement
        });
        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
        var page = MainWindowFlaUiPageFactory.Create(session);
        page.SelectTabItem(static candidate => candidate.DataGridTabItem);
        var grid = session.MainWindow.FindFirstDescendant(
            session.ConditionFactory.ByAutomationId("ArmComplexDataGridControl"))!;
        var before = grid.FindAllDescendants(session.ConditionFactory.ByControlType(ControlType.HeaderItem)).Length;

        page.EditGridCellText(static candidate => candidate.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", "ARM-02"), "Value", "Rebound value");

        var after = grid.FindAllDescendants(session.ConditionFactory.ByControlType(ControlType.HeaderItem)).Length;
        await Assert.That(after).IsLessThan(before);
        await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", "ARM-02"), "Value")).IsEqualTo("Rebound value");
        await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", "ARM-01"), "Value")).IsEqualTo("Value-1");
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task NestedGridOutsideViewport_CapturesAndOpensStableRow()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        var defaults = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions();
        var environment = new Dictionary<string, string?>(defaults.EnvironmentVariables, StringComparer.Ordinal)
        {
            ["APPAUTOMATION_NESTED_GRID_OUTSIDE_VIEWPORT"] = "1"
        };
        using var session = DesktopAppSession.Launch(new DesktopAppLaunchOptions
        {
            ExecutablePath = defaults.ExecutablePath,
            WorkingDirectory = defaults.WorkingDirectory,
            Arguments = defaults.Arguments,
            EnvironmentVariables = environment,
            DisposeCallback = defaults.DisposeCallback,
            MainWindowTimeout = defaults.MainWindowTimeout,
            PollInterval = defaults.PollInterval,
            WindowPlacement = defaults.WindowPlacement
        });
        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
        var page = MainWindowFlaUiPageFactory.Create(session);
        page.SelectTabItem(static candidate => candidate.DataGridTabItem);

        var diagnosticGrid = session.MainWindow.FindFirstDescendant(
            session.ConditionFactory.ByAutomationId("ArmComplexDataGridControl"));
        for (var candidate = diagnosticGrid; candidate is not null; candidate = candidate.Parent)
        {
            Console.WriteLine($"NESTED GRID UIA {candidate.ControlType} "
                + $"bounds={candidate.BoundingRectangle} offscreen={candidate.IsOffscreen} "
                + $"scrollItem={candidate.Patterns.ScrollItem.IsSupported} "
                + $"scroll={candidate.Patterns.Scroll.IsSupported} "
                + $"vertical={candidate.Patterns.Scroll.PatternOrDefault?.VerticallyScrollable.ValueOrDefault} "
                + $"percent={candidate.Patterns.Scroll.PatternOrDefault?.VerticalScrollPercent.ValueOrDefault} "
                + $"view={candidate.Patterns.Scroll.PatternOrDefault?.VerticalViewSize.ValueOrDefault}");
        }

        GridRowSelector row;
        try
        {
            row = GridRowKeyReader.Capture(page.ArmComplexDataGridControl, 0);
        }
        catch
        {
            Console.WriteLine($"NESTED GRID AFTER bounds={diagnosticGrid?.BoundingRectangle} "
                + $"outer percent={diagnosticGrid?.Parent?.Parent?.Parent?.Parent?.Patterns.Scroll.PatternOrDefault?.VerticalScrollPercent.ValueOrDefault}");
            throw;
        }
        page.OpenGridRow(static candidate => candidate.ArmComplexDataGridControl, row);

        using (Assert.Multiple())
        {
            await Assert.That(row.Conditions.Single().Value).IsEqualTo("ARM-01");
            await Assert.That(page.GridOpenMarkerLabel.Text).IsEqualTo("Opened row: ARM-01");
        }
    }

    [Test]
    public async Task NativeGridCellMatcherRequiresOnlyIdentityColumns()
    {
        var headers = new[]
        {
            new System.Drawing.Rectangle(0, 0, 100, 20),
            new System.Drawing.Rectangle(100, 0, 100, 20),
            new System.Drawing.Rectangle(200, 0, 100, 20)
        };
        var cells = new[]
        {
            new NativeGridCellCandidate(new System.Drawing.Rectangle(0, 20, 100, 20), IsText: false),
            new NativeGridCellCandidate(new System.Drawing.Rectangle(200, 20, 100, 20), IsText: false)
        };
        var rowBounds = new System.Drawing.Rectangle(0, 20, 300, 20);

        var selected = NativeGridCellMatcher.Match(headers, cells, rowBounds, [0]);
        var rowOnly = NativeGridCellMatcher.Match(headers, cells, rowBounds, []);
        var complete = NativeGridCellMatcher.Match(headers, cells, rowBounds, requiredIndexes: null);
        var missingIdentity = NativeGridCellMatcher.Match(headers, cells, rowBounds, [1]);

        using (Assert.Multiple())
        {
            await Assert.That(selected).IsNotNull();
            await Assert.That(selected![0]).IsEqualTo(0);
            await Assert.That(selected[1]).IsNull();
            await Assert.That(selected[2]).IsNull();
            await Assert.That(rowOnly).IsNotNull();
            await Assert.That(rowOnly!.All(static index => index is null)).IsTrue();
            await Assert.That(complete).IsNull();
            await Assert.That(missingIdentity).IsNull();
        }
    }

    [Test]
    public async Task NativeGridRowIndexPrefersRowMetadataAndRejectsConflict()
    {
        using (Assert.Multiple())
        {
            await Assert.That(FlaUiControlResolver.ResolveNativeGridRowIndex(7, null)).IsEqualTo(7);
            await Assert.That(FlaUiControlResolver.ResolveNativeGridRowIndex(null, 8)).IsEqualTo(8);
            await Assert.That(FlaUiControlResolver.ResolveNativeGridRowIndex(-1, 9)).IsEqualTo(9);
            await Assert.That(FlaUiControlResolver.ResolveNativeGridRowIndex(10, 10)).IsEqualTo(10);
            await Assert.That(Assert.Throws<InvalidOperationException>(() =>
                    FlaUiControlResolver.ResolveNativeGridRowIndex(3, 4)).Message)
                .Contains("row=3; firstCell=4");
        }
    }

    [Test]
    public async Task AutomationTextClassification_PreservesQualifiedBusinessValues()
    {
        using (Assert.Multiple())
        {
            await Assert.That(FlaUiControlResolver.IsUsefulAutomationText("Acme.Editor")).IsTrue();
            await Assert.That(FlaUiControlResolver.IsUsefulAutomationText("Product.ComboBox")).IsTrue();
            await Assert.That(FlaUiControlResolver.IsUsefulAutomationText("Avalonia.Controls.TextBox")).IsFalse();
        }
    }

    [Test]
    public async Task PrimaryAutomationText_DistinguishesEmptyValueFromMissingValue()
    {
        using (Assert.Multiple())
        {
            await Assert.That(FlaUiControlResolver.SelectPrimaryAutomationText(
                "Column caption", string.Empty, preferValue: true, preserveEmpty: true)).IsEqualTo(string.Empty);
            await Assert.That(FlaUiControlResolver.SelectPrimaryAutomationText(
                "Column caption", null, preferValue: true, preserveEmpty: true)).IsNull();
            await Assert.That(FlaUiControlResolver.SelectPrimaryAutomationText(
                "Column caption", string.Empty, preferValue: true, preserveEmpty: false)).IsNull();
        }
    }

    [Test]
    public async Task NativeDateCellProjection_IgnoresOnlyEmptyOrdinalName()
    {
        using (Assert.Multiple())
        {
            await Assert.That(FlaUiControlResolver.IsEmptyNativeDateCellProjection(
                "1", null, ControlType.Group, hasTextPattern: false, descendantCount: 0)).IsTrue();
            await Assert.That(FlaUiControlResolver.IsEmptyNativeDateCellProjection(
                "1000", null, ControlType.Group, hasTextPattern: false, descendantCount: 0)).IsTrue();
            await Assert.That(FlaUiControlResolver.IsEmptyNativeDateCellProjection(
                "07.10.2026 19:48", null, ControlType.Group, hasTextPattern: false, descendantCount: 0)).IsFalse();
            await Assert.That(FlaUiControlResolver.IsEmptyNativeDateCellProjection(
                "1", "07.10.2026 19:48", ControlType.Group, hasTextPattern: false, descendantCount: 0)).IsFalse();
            await Assert.That(FlaUiControlResolver.IsEmptyNativeDateCellProjection(
                "1", null, ControlType.Group, hasTextPattern: false, descendantCount: 1)).IsFalse();
        }
    }

    [Test]
    public async Task NativeGridCellSourceField_RebindsToItsOwnProjectionAndRejectsNeighbor()
    {
        var sourceIndex = FlaUiControlResolver.SelectNativeCellBySourceField(
            "DeletionReason", "ReadyDate", ["ReadyDate", "DeletionReason"],
            selectedIdBelongsToDifferentSourceField: true);
        var wrongNeighbor = Assert.Throws<InvalidOperationException>(() =>
            FlaUiControlResolver.SelectNativeCellBySourceField(
                "DeletionReason", "ReadyDate", ["ReadyDate"],
                selectedIdBelongsToDifferentSourceField: true));
        var duplicate = Assert.Throws<InvalidOperationException>(() =>
            FlaUiControlResolver.SelectNativeCellBySourceField(
                "DeletionReason", "ReadyDate", ["DeletionReason", "DeletionReason"],
                selectedIdBelongsToDifferentSourceField: true));
        var customAutomationId = FlaUiControlResolver.SelectNativeCellBySourceField(
            "DeletionReason", "CustomCell_01", ["CustomCell_01"],
            selectedIdBelongsToDifferentSourceField: false);

        using (Assert.Multiple())
        {
            await Assert.That(sourceIndex).IsEqualTo(1);
            await Assert.That(customAutomationId).IsEqualTo(-1);
            await Assert.That(wrongNeighbor.Message).Contains("ReadyDate");
            await Assert.That(duplicate.Message).Contains("multiple UIA cell projections");
        }
    }

    [Test]
    public async Task NativeGridTraversal_WaitsForInitiallyEmptyRows()
    {
        var observations = new Queue<string[]>(
        [
            [],
            [],
            ["row-1"]
        ]);
        var waits = 0;

        var rows = NativeGridTraversal.WaitForRows(
            () => observations.Dequeue(),
            () => observations.Count > 0,
            () => waits++);

        await Assert.That(rows).IsEquivalentTo(["row-1"]);
        await Assert.That(waits).IsEqualTo(2);
    }


    [Test]
    public async Task NativeRowSnapshots_DistinguishRepeatedAndRecycledRows()
    {
        var original = new NativeGridRowSnapshot(
            ["Repeated caption"], null, new System.Drawing.Rectangle(10, 10, 100, 20),
            new GridScrollPosition(null, null))
        {
            RuntimeId = "row-1",
            ViewportSignature = "viewport-1"
        };

        await Assert.That(original.HasSameStableRow(original with { })).IsTrue();
        await Assert.That(original.HasSameStableRow(original with { RuntimeId = "row-2" })).IsFalse();
        await Assert.That(original.HasSameStableRow(original with
        {
            CellTexts = ["Different item"],
            ViewportSignature = "viewport-2"
        })).IsFalse();
        var uncertain = Assert.Throws<InvalidOperationException>(() => original.HasSameStableRow(original with
        {
            ViewportSignature = "viewport-2"
        }));
        await Assert.That(uncertain.Message).Contains("identity");

        var positioned = original with { ScrollPosition = new GridScrollPosition(0, 0) };
        Assert.Throws<InvalidOperationException>(() => positioned.HasSameStableRow(positioned with
        {
            ScrollPosition = new GridScrollPosition(0, 20)
        }));

        var overlapping = positioned with
        {
            ObservationIndex = 1,
            ScrollPosition = new GridScrollPosition(null, 3),
            Bounds = new System.Drawing.Rectangle(10, -10, 100, 20),
            ViewportSignature = "viewport-2"
        };
        await Assert.That(overlapping.HasSameStableRow(positioned)).IsTrue();
        await Assert.That(positioned.HasSameStableRow(overlapping)).IsTrue();
        await Assert.That((overlapping with { Bounds = new System.Drawing.Rectangle(10, 30, 100, 20) })
            .HasSameStableRow(positioned)).IsFalse();
        Assert.Throws<InvalidOperationException>(() => (overlapping with { ObservationIndex = 3 }).HasSameStableRow(positioned));

        var indexed = original with { RowIndex = 4 };
        await Assert.That(indexed.HasSameStableRow(indexed with { RuntimeId = "recreated-peer" })).IsTrue();
        await Assert.That(indexed.HasSameStableRow(indexed with { RowIndex = 5 })).IsFalse();
    }

    [Test]
    public async Task NativeRowSnapshots_NormalizePhysicalProjectionsWithoutMergingRealRows()
    {
        var containerProjection = new NativeGridRowSnapshot(
            ["10", "Item 42"],
            null,
            new System.Drawing.Rectangle(10, 20, 400, 28),
            new GridScrollPosition(null, 0))
        {
            RuntimeId = "container-projection",
            ViewportSignature = "viewport-1",
            ObservationIndex = 3,
            RowAutomationValues = new Dictionary<GridRowAutomationProperty, string?>
            {
                [GridRowAutomationProperty.ItemStatus] = "10"
            }
        };
        var peerProjection = containerProjection with
        {
            RuntimeId = "uia-peer-projection",
            Bounds = new System.Drawing.Rectangle(11, 21, 398, 26)
        };
        var laterPeerProjection = peerProjection with
        {
            RuntimeId = "later-uia-peer-projection",
            ObservationIndex = 4,
            Bounds = new System.Drawing.Rectangle(0, 23, 450, 20)
        };
        var namedSelectorProjection = containerProjection with
        {
            RuntimeId = "named-selector-projection",
            RowAutomationValues = new Dictionary<GridRowAutomationProperty, string?>(),
            StableValues = ["10"]
        };
        var namedSelectorPeer = peerProjection with
        {
            CellTexts = ["10"],
            ObservationIndex = 4,
            Bounds = new System.Drawing.Rectangle(420, 21, 80, 26),
            RowAutomationValues = new Dictionary<GridRowAutomationProperty, string?>(),
            StableValues = ["10"]
        };
        var normalizedRows = new List<NativeGridRowSnapshot>();
        NativeGridRowNormalizer.Append(
            normalizedRows,
            [namedSelectorProjection, namedSelectorPeer],
            static (first, second) => first.HasSameStableRow(second));
        var separateBusinessRow = containerProjection with
        {
            RuntimeId = "separate-business-row",
            Bounds = new System.Drawing.Rectangle(10, 52, 400, 28)
        };

        using (Assert.Multiple())
        {
            await Assert.That(containerProjection.HasSameStableRow(peerProjection)).IsTrue();
            await Assert.That(containerProjection.HasSameStableRow(laterPeerProjection)).IsTrue();
            await Assert.That(normalizedRows).Count().IsEqualTo(1);
            await Assert.That(containerProjection.HasSameStableRow(separateBusinessRow)).IsFalse();
            await Assert.That(containerProjection.HasSameStableRow(peerProjection with
            {
                CellTexts = ["10", "Different item"]
            })).IsFalse();
            var identityOnly = containerProjection with
            {
                CellTexts = [],
                StableValues = ["10"],
                ScrollPosition = new GridScrollPosition(null, null)
            };
            await Assert.That(identityOnly.HasSameStableRow(identityOnly with
            {
                RuntimeId = peerProjection.RuntimeId,
                Bounds = peerProjection.Bounds
            })).IsTrue();
            await Assert.That(identityOnly.HasSameStableRow(identityOnly with
            {
                RuntimeId = separateBusinessRow.RuntimeId,
                Bounds = separateBusinessRow.Bounds
            })).IsFalse();
        }
    }

    [Test]
    public async Task NativeRowsAtKnownScrollStartResolveVisiblePositionsWithoutGridItemIndex()
    {
        var rows = new[]
        {
            new NativeGridRowSnapshot(["ORDER-22"], null,
                new System.Drawing.Rectangle(10, 80, 300, 25), new GridScrollPosition(0, null)),
            new NativeGridRowSnapshot(["ORDER-20"], null,
                new System.Drawing.Rectangle(10, 20, 300, 25), new GridScrollPosition(0, null)),
            new NativeGridRowSnapshot(["ORDER-21"], null,
                new System.Drawing.Rectangle(10, 50, 300, 25), new GridScrollPosition(0, null))
        };

        var selected = NativeGridRowNormalizer.AtVisibleStartPosition(rows, 1, atStart: true);
        var withoutBoundary = NativeGridRowNormalizer.AtVisibleStartPosition(rows, 1, atStart: false);
        var overlapping = NativeGridRowNormalizer.AtVisibleStartPosition(
            [rows[0] with { Bounds = new System.Drawing.Rectangle(10, 50, 300, 25) }, rows[2]],
            0,
            atStart: true);

        using (Assert.Multiple())
        {
            await Assert.That(selected!.CellTexts.Single()).IsEqualTo("ORDER-21");
            await Assert.That(withoutBoundary).IsNull();
            await Assert.That(overlapping).IsNull();
        }
    }

    [Test]
    public async Task NativeRowsResolveVisiblePositionWhenGridReportsCompleteLogicalRowCount()
    {
        var rows = new[]
        {
            new NativeGridRowSnapshot(["ORDER-22"], null,
                new System.Drawing.Rectangle(10, 80, 300, 25), new GridScrollPosition(null, null)),
            new NativeGridRowSnapshot(["ORDER-20"], null,
                new System.Drawing.Rectangle(10, 20, 300, 25), new GridScrollPosition(null, null)),
            new NativeGridRowSnapshot(["ORDER-21"], null,
                new System.Drawing.Rectangle(10, 50, 300, 25), new GridScrollPosition(null, null))
        };

        var complete = NativeGridRowNormalizer.AtCompleteVisiblePosition(rows, 2, reportedRowCount: 3);
        var incomplete = NativeGridRowNormalizer.AtCompleteVisiblePosition(rows, 2, reportedRowCount: 4);
        var unknown = NativeGridRowNormalizer.AtCompleteVisiblePosition(rows, 2, reportedRowCount: null);

        using (Assert.Multiple())
        {
            await Assert.That(complete?.CellTexts.Single()).IsEqualTo("ORDER-22");
            await Assert.That(incomplete).IsNull();
            await Assert.That(unknown).IsNull();
        }
    }

    [Test]
    public async Task NativeRowsWithoutGridItemIndexResolveLaterPositionOnlyAfterContiguousTraversal()
    {
        static NativeGridRowSnapshot Row(string key, int top, double scroll, int observation) =>
            new([key], null, new System.Drawing.Rectangle(10, top, 300, 25),
                new GridScrollPosition(scroll, null))
            {
                StableValues = [key],
                RuntimeId = key,
                ObservationIndex = observation
            };

        var first = new[] { Row("R1", 20, 0, 0), Row("R2", 50, 0, 0), Row("R3", 80, 0, 0) };
        var second = new[] { Row("R3", 20, 25, 1), Row("R4", 50, 25, 1), Row("R5", 80, 25, 1) };
        var third = new[] { Row("R5", 20, 50, 2), Row("R6", 50, 50, 2), Row("R7", 80, 50, 2) };
        var accumulated = new List<NativeGridRowSnapshot>();
        foreach (var observation in new[] { first, second, third })
        {
            NativeGridRowNormalizer.Append(accumulated, observation,
                static (current, previous) => current.HasSameStableRow(previous));
        }

        var contiguous = NativeGridRowNormalizer.HasContinuousObservation(first, second,
            static (current, previous) => current.HasSameStableRow(previous))
            && NativeGridRowNormalizer.HasContinuousObservation(second, third,
                static (current, previous) => current.HasSameStableRow(previous));
        var gap = NativeGridRowNormalizer.HasContinuousObservation(second,
            [Row("R8", 20, 50, 2), Row("R9", 50, 50, 2)],
            static (current, previous) => current.HasSameStableRow(previous));
        var missingInteriorRow = new[]
        {
            Row("R3", 20, 25, 1), Row("R5", 80, 25, 1), Row("R6", 110, 25, 1)
        };
        var incompleteObservation = NativeGridRowNormalizer.HasContinuousObservation(
            first, missingInteriorRow,
            static (current, previous) => current.HasSameStableRow(previous));
        var misplacedOverlap = NativeGridRowNormalizer.HasContinuousObservation(
            first,
            [Row("R4", 20, 25, 1), Row("R2", 50, 25, 1), Row("R5", 80, 25, 1)],
            static (current, previous) => current.StableValues.SequenceEqual(previous.StableValues));

        using (Assert.Multiple())
        {
            await Assert.That(contiguous).IsTrue();
            await Assert.That(accumulated.Count).IsEqualTo(7);
            await Assert.That(NativeGridRowNormalizer.AtScannedPosition(accumulated, 5, contiguous)!.CellTexts[0])
                .IsEqualTo("R6");
            await Assert.That(gap).IsFalse();
            await Assert.That(NativeGridRowNormalizer.AtScannedPosition(accumulated, 5, gap)).IsNull();
            await Assert.That(incompleteObservation).IsFalse();
            await Assert.That(misplacedOverlap).IsFalse();
            await Assert.That(NativeGridRowNormalizer.AtVisibleStartPosition(
                missingInteriorRow, 1, atStart: true)).IsNull();
        }
    }

    [Test]
    public async Task VirtualizedExactItemSelector_ReResolvesReusedContainerBeforeSelection()
    {
        const string expected = "Search result";
        var neighboring = new ReusableItemContainer("slot-1", "Search result extended");
        var recycled = new ReusableItemContainer("slot-2", expected);
        var rolled = new ReusableItemContainer("slot-3", "Search  result rolled");
        var replacement = new ReusableItemContainer("slot-4", expected);
        IReadOnlyList<ProjectedItemCandidate> candidates =
        [
            new("Search result extended", neighboring),
            new(expected, recycled),
            new("Search  result rolled", rolled)
        ];
        string? selected = null;
        var reads = 0;
        var resolutionsBySnapshot = new Dictionary<int, int>();
        var candidateCountsBySnapshot = new Dictionary<int, int>();

        VirtualizedExactItemSelector.Select(
            expected,
            TimeSpan.FromSeconds(1),
            () =>
            {
                reads++;
                candidateCountsBySnapshot[reads] = candidates.Count;
                return candidates;
            },
            static candidate => candidate.Caption,
            candidate =>
            {
                resolutionsBySnapshot[reads] = resolutionsBySnapshot.GetValueOrDefault(reads) + 1;
                return candidate.Container;
            },
            static container => container.Caption,
            static container => container.Identity,
            container =>
            {
                if (!ReferenceEquals(container, recycled))
                {
                    return;
                }

                recycled.Caption = neighboring.Caption;
                candidates =
                [
                    new("Search result extended", neighboring),
                    new("Search result extended", recycled),
                    new(expected, replacement),
                    new("Search  result rolled", rolled)
                ];
            },
            container =>
            {
                selected = container.Caption;
                return true;
            },
            container =>
            {
                if (ReferenceEquals(container, recycled))
                {
                    recycled.Caption = neighboring.Caption;
                }

                selected = container.Caption;
            });

        using (Assert.Multiple())
        {
            await Assert.That(selected).IsEqualTo(expected);
            await Assert.That(reads).IsGreaterThanOrEqualTo(2);
            await Assert.That(resolutionsBySnapshot.All(pair =>
                    candidateCountsBySnapshot[pair.Key] == pair.Value))
                .IsTrue();
        }
    }

    [Test]
    public async Task GridScrollBoundary_IgnoresUnusableScrollPatternWhenRangeCanMove()
    {
        var reached = GridScrollBoundary.IsReached(
            patternScrollable: false,
            patternPercent: 0,
            rangeMinimum: 0,
            rangeMaximum: 100,
            rangeValue: 25,
            forward: true);

        await Assert.That(reached).IsFalse();
    }

    [Test]
    public async Task GridScrollBoundary_DoesNotMistakeBottomHorizontalButtonsForVerticalScrolling()
    {
        var horizontal = GridScrollBoundary.AreHorizontalScrollButtons(
            new System.Drawing.Rectangle(32, 975, 487, 19),
            new System.Drawing.Rectangle(1888, 979, 12, 12));
        var vertical = GridScrollBoundary.AreHorizontalScrollButtons(
            new System.Drawing.Rectangle(1888, 100, 12, 12),
            new System.Drawing.Rectangle(1888, 975, 12, 12));

        using (Assert.Multiple())
        {
            await Assert.That(horizontal).IsTrue();
            await Assert.That(vertical).IsFalse();
        }
    }

    private static string[] ReadElementNames(AutomationElement root)
    {
        return root.FindAllDescendants()
            .Prepend(root)
            .Select(static element => TryRead(() => element.Name) ?? string.Empty)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static MultiSelectPopupParts FindInstrumentedMultiSelectPopup(
        DesktopAppSession session,
        AutomationElement desktop)
    {
        return new MultiSelectPopupParts(
            WaitForDesktopElement(session, desktop, "MultiSelection_Results", "results"),
            WaitForDesktopElement(session, desktop, "MultiSelection_ApplyButton", "Apply button"),
            WaitForDesktopElement(session, desktop, "MultiSelection_CancelButton", "Cancel button"));
    }

    private static AutomationElement WaitForDesktopElement(
        DesktopAppSession session,
        AutomationElement desktop,
        string automationId,
        string description)
    {
        var failureMessage = $"The instrumented multi-select popup part '{description}' was not exposed.";
        return UiWait.Until(
                () => desktop.FindFirstDescendant(session.ConditionFactory.ByAutomationId(automationId)),
                static element => element is not null && TryRead(() => element.IsAvailable),
                DesktopControlWaitOptions,
                failureMessage)
            ?? throw new InvalidOperationException(failureMessage);
    }

    private static bool ContainsText(IEnumerable<string> texts, string expected)
    {
        return texts.Any(text => text.Contains(expected, StringComparison.Ordinal));
    }

    private static DesktopAppLaunchOptions CreateCalendarFallbackLaunchOptions()
    {
        var baseOptions = DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions(buildConfiguration: "Debug");
        var environmentVariables = new Dictionary<string, string?>(baseOptions.EnvironmentVariables, StringComparer.Ordinal)
        {
            [CalendarFallbackFixtureEnvironmentVariable] = "1"
        };

        return new DesktopAppLaunchOptions
        {
            ExecutablePath = baseOptions.ExecutablePath,
            WorkingDirectory = baseOptions.WorkingDirectory,
            Arguments = baseOptions.Arguments,
            EnvironmentVariables = environmentVariables,
            DisposeCallback = baseOptions.DisposeCallback,
            MainWindowTimeout = baseOptions.MainWindowTimeout,
            PollInterval = baseOptions.PollInterval,
            WindowPlacement = baseOptions.WindowPlacement
        };
    }

    private static bool IsVisible(AutomationElement? element)
    {
        return element is not null
            && TryRead(() => element.IsAvailable && !element.IsOffscreen);
    }

    private static T? TryRead<T>(Func<T> accessor)
    {
        try
        {
            return accessor();
        }
        catch
        {
            return default;
        }
    }

    private sealed record MultiSelectPopupParts(
        AutomationElement Results,
        AutomationElement ApplyButton,
        AutomationElement CancelButton);

    private sealed record ProjectedItemCandidate(
        string Caption,
        ReusableItemContainer Container);

    private sealed class ReusableItemContainer(string identity, string caption)
    {
        public string Identity { get; } = identity;

        public string Caption { get; set; } = caption;
    }

    private sealed class CalendarFallbackPage : UiPage
    {
        private static readonly UiControlDefinition DatePickerDefinition = new(
            "DatePicker",
            UiControlType.DateTimePicker,
            "FlaUiCalendarFallbackFixture");

        public CalendarFallbackPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IDateTimePickerControl DatePicker => Resolve<IDateTimePickerControl>(DatePickerDefinition);
    }
}
