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
using FlaUI.Core.Definitions;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

public sealed class FlaUiControlResolverTests
{

    [Test]
    [Arguments(GridCellEditCommitMode.Commit)]
    [Arguments(GridCellEditCommitMode.Cancel)]
    [NotInParallel("DesktopUi")]
    public async Task ConfiguredGridCombo_CommitsThroughItemActivationAndPreservesCancel(GridCellEditCommitMode commitMode)
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
                ["APPAUTOMATION_FLAUI_GRID_COMPOSITE_COMBO"] = "1",
                ["APPAUTOMATION_GRID_COMBO_REQUIRE_POINTER_COMMIT"] = "1"
            },
            DisposeCallback = defaults.DisposeCallback,
            MainWindowTimeout = defaults.MainWindowTimeout,
            PollInterval = defaults.PollInterval,
            WindowPlacement = defaults.WindowPlacement
        });
        var catalog = new GridAutomationCatalog().Add(GridAutomationDefinition.ByAutomationIds(
                "ArmComplexDataGridControl", "ArmComplexDataGridControl", "ArmComplexDataGridControl")
            .WithColumns(GridColumnDefinition.Auto("Key").ReadIdentityFromRow(GridRowAutomationProperty.ItemStatus),
                GridColumnDefinition.Auto("State").AsValue(GridCellValueKind.Selection)
                    .EditWith(GridCellEditorKind.ComboBox, new GridCellEditorParts(
                        Input: new GridRelativeLocator("GridCombo_Input"),
                        Results: new GridRelativeLocator("GridCombo_Results", GridRelativeLocatorScope.DetachedPopup),
                        OpenButton: new GridRelativeLocator("GridCombo_Open"))))
            .IdentifyRowsBy("Key"));
        var page = MainWindowFlaUiPageFactory.Create(session, catalog);
        page.SelectTabItem(static candidate => candidate.DataGridTabItem);
        var row = GridRowSelector.ByCell("Key", "ARM-02");
        page.SelectGridCellComboItem(static candidate => candidate.ArmComplexDataGridControl,
            row, "State", "Closed", commitMode);
        await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl, row, "State"))
            .IsEqualTo(commitMode == GridCellEditCommitMode.Commit ? "Closed" : "Pending");
        await Assert.That(GridValueReader.ReadCellText(page.ArmComplexDataGridControl,
            GridRowSelector.ByCell("Key", "ARM-01"), "State")).IsEqualTo("Open");
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
            }));

        using (Assert.Multiple())
        {
            await Assert.That(value).IsEqualTo("OpenButton");
            await Assert.That(reads).IsEqualTo(2);
            await Assert.That(permanentReads).IsEqualTo(1);
            await Assert.That(permanent.HResult).IsEqualTo(unchecked((int)0x80070005));
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
        var page = MainWindowFlaUiPageFactory.Create(session);
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
                StableValues = [key], RuntimeId = key, ObservationIndex = observation
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
