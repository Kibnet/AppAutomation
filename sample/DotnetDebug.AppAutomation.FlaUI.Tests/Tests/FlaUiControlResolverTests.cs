using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Automation;
using AppAutomation.FlaUI.Automation.GridAutomation;
using AppAutomation.FlaUI.Session;
using AppAutomation.Session.Contracts;
using AppAutomation.TestHost.Avalonia;
using DotnetDebug.AppAutomation.Authoring.Pages;
using DotnetDebug.AppAutomation.FlaUI.Tests.Infrastructure;
using DotnetDebug.AppAutomation.TestHost;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.FlaUI.Tests.Tests.UIAutomationTests;

public sealed class FlaUiControlResolverTests
{
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

        popup.CancelButton.Click();
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
            .WaitUntilHasItemsAtLeast(static candidate => candidate.HierarchySelectionList, 2)
            .SelectListBoxItem(static candidate => candidate.HierarchySelectionList, "Fibonacci");

        var selectableList = page.HierarchySelectionList as ISelectableListBoxControl;

        using (Assert.Multiple())
        {
            await Assert.That(selectableList).IsNotNull();
            await Assert.That(selectableList!.SelectedItemText).IsEqualTo("Fibonacci");
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
    public async Task ComplexDataGrid_ThreeRowsResolveAllConfiguredColumns()
    {
        DesktopUiAvailabilityGuard.SkipIfUnavailable();

        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateDesktopLaunchOptions());
        session.MainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
        var page = MainWindowFlaUiPageFactory.Create(session);
        var firstRow = GridRowSelector.ByCell("Key", "ARM-01");
        var secondRow = GridRowSelector.ByCell("Key", "ARM-02");
        var thirdRow = GridRowSelector.ByCell("Key", "ARM-03");

        page
            .SelectTabItem(static candidate => candidate.DataGridTabItem);

        string[] columns =
        [
            "Key",
            "Value",
            "RequiredAmount",
            "IsApproved",
            "State",
            "Product",
            "ScheduledDate",
            "ScheduledTime"
        ];
        (GridRowSelector Row, string[] Values)[] initialRows =
        [
            (firstRow, ["ARM-01", "Value-1", "10", "True", "Open", "Product 42", "2026-09-10", "08:30:00"]),
            (secondRow, ["ARM-02", "Value-2", "11", "False", "Pending", "Service Contract", "2026-09-11", "09:30:00"]),
            (thirdRow, ["ARM-03", "Value-3", "12", "True", "Open", "Warehouse North", "2026-09-12", "10:30:00"])
        ];

        using (Assert.Multiple())
        {
            foreach (var (row, values) in initialRows)
            {
                for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
                {
                    await Assert.That(GridValueReader.ReadCellText(
                            page.ArmComplexDataGridControl,
                            row,
                            columns[columnIndex]))
                        .IsEqualTo(values[columnIndex]);
                }
            }
        }

        page.EditGridCellNumber(
            static candidate => candidate.ArmComplexDataGridControl,
            firstRow,
            "RequiredAmount",
            1000);

        await Assert.That(GridValueReader.ReadCellNumber(
                page.ArmComplexDataGridControl,
                firstRow,
                "RequiredAmount"))
            .IsEqualTo(1000);

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
            RuntimeId = "row-1", ViewportSignature = "viewport-1"
        };

        await Assert.That(original.HasSameStableRow(original with { })).IsTrue();
        await Assert.That(original.HasSameStableRow(original with { RuntimeId = "row-2" })).IsFalse();
        await Assert.That(original.HasSameStableRow(original with
        {
            CellTexts = ["Different item"], ViewportSignature = "viewport-2"
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
