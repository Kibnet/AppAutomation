using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using AppAutomation.Session.Contracts;
using AppAutomation.TestHost.Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using TUnit.Assertions;
using TUnit.Core;
using static AppAutomation.TestHost.Avalonia.Tests.HeadlessTestRuntime;

namespace AppAutomation.TestHost.Avalonia.Tests;

public sealed class LaunchContractTests
{
    [Test]
    public async Task AutomationLaunchContext_PrefersAmbientOverride_OverEnvironment()
    {
        var ambient = new AutomationLaunchContext("AmbientScenario", @"C:\ambient.json", source: "ambient-test");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [AutomationLaunchContext.ScenarioNameEnvironmentVariable] = "EnvironmentScenario",
            [AutomationLaunchContext.ScenarioPayloadPathEnvironmentVariable] = @"C:\environment.json"
        };

        using var scope = AutomationLaunchContext.PushAmbientOverride(ambient);
        var resolved = AutomationLaunchContext.GetRequired(name => environment.TryGetValue(name, out var value) ? value : null);

        using (Assert.Multiple())
        {
            await Assert.That(resolved.ScenarioName).IsEqualTo("AmbientScenario");
            await Assert.That(resolved.PayloadPath).IsEqualTo(@"C:\ambient.json");
            await Assert.That(resolved.Source).IsEqualTo("ambient-test");
        }
    }

    [Test]
    public async Task AutomationLaunchContext_ReadRequiredPayload_ReportsScenarioPathAndType()
    {
        using var workspace = TemporaryWorkspace.Create();
        var payloadPath = Path.Combine(workspace.FullPath, "payload.json");
        File.WriteAllText(payloadPath, "{ invalid json");

        var context = new AutomationLaunchContext("BrokenScenario", payloadPath, source: "test");

        Exception? exception = null;
        try
        {
            _ = context.ReadRequiredPayload<LaunchPayload>();
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.Message).Contains("BrokenScenario");
            await Assert.That(exception.Message).Contains(payloadPath);
            await Assert.That(exception.Message).Contains(typeof(LaunchPayload).FullName!);
        }
    }

    [Test]
    public async Task AutomationPreflight_MasksSecrets_AndAggregatesSources()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Settings.json");
        Exception? exception = null;

        try
        {
            AutomationPreflight.Create("Login smoke")
                .RequireValue("ServerUrl", "http://localhost:5000", "env:SERVER_URL")
                .RequireValue("Login", "alice@example.com", "env:LOGIN", secret: true)
                .RequireValue("Password", null, "env:PASSWORD", secret: true)
                .RequireExistingFile("SettingsPath", missingPath, "launchSettings")
                .ThrowIfInvalid();
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception is AutomationPreflightException).IsEqualTo(true);
            await Assert.That(exception!.Message).Contains("Login smoke");
            await Assert.That(exception.Message).Contains("[env:LOGIN] = set");
            await Assert.That(exception.Message).DoesNotContain("alice@example.com");
            await Assert.That(exception.Message).Contains("[env:PASSWORD] = missing");
            await Assert.That(exception.Message).Contains("[launchSettings] = " + missingPath);
        }
    }

    [Test]
    public async Task AvaloniaHeadlessLaunchHost_CreateWithScenario_SetsAmbientContext_AndCleansUp()
    {
        var scenario = new AutomationLaunchScenario<LaunchPayload>(
            "SignedInSmoke",
            new LaunchPayload("alice@example.com"));

        AutomationLaunchContext? contextFromBeforeLaunch = null;
        AutomationLaunchContext? contextFromWindowFactory = null;
        Exception? factoryException = null;

        var options = AvaloniaHeadlessLaunchHost.Create(
            () =>
            {
                contextFromWindowFactory = AutomationLaunchContext.GetRequired();
                throw new InvalidOperationException("Factory executed.");
            },
            scenario,
            beforeLaunchAsync: cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                contextFromBeforeLaunch = AutomationLaunchContext.GetRequired();
                return ValueTask.CompletedTask;
            });

        await options.BeforeLaunchAsync!(CancellationToken.None);
        try
        {
            _ = options.CreateMainWindow!.Invoke();
        }
        catch (Exception ex)
        {
            factoryException = ex;
        }

        var resolvedContext = contextFromWindowFactory
            ?? throw new InvalidOperationException("Window factory did not capture the launch context.");
        var payloadPath = resolvedContext.PayloadPath!;
        var payload = resolvedContext.ReadRequiredPayload<LaunchPayload>();

        using (Assert.Multiple())
        {
            await Assert.That(contextFromBeforeLaunch).IsNotNull();
            await Assert.That(factoryException).IsNotNull();
            await Assert.That(factoryException!.Message).Contains("Factory executed.");
            await Assert.That(resolvedContext.ScenarioName).IsEqualTo("SignedInSmoke");
            await Assert.That(payload.UserName).IsEqualTo("alice@example.com");
            await Assert.That(File.Exists(payloadPath)).IsEqualTo(true);
        }

        options.DisposeCallback!.Invoke();

        using (Assert.Multiple())
        {
            await Assert.That(AutomationLaunchContext.TryGetCurrent(static _ => null)).IsNull();
            await Assert.That(File.Exists(payloadPath)).IsEqualTo(false);
        }
    }

    [Test]
    public async Task AutomationLaunchContext_ParallelHeadlessLaunchCallbacks_DoNotLeakAcrossTasks()
    {
        using var barrier = new Barrier(2);
        var scenarioA = new AutomationLaunchScenario<LaunchPayload>("ScenarioA", new LaunchPayload("alice@example.com"));
        var scenarioB = new AutomationLaunchScenario<LaunchPayload>("ScenarioB", new LaunchPayload("bob@example.com"));
        string? observedScenarioA = null;
        string? observedScenarioB = null;

        var optionsA = AvaloniaHeadlessLaunchHost.Create(
            static () => throw new NotSupportedException("Window factory is not used in this test."),
            scenarioA,
            beforeLaunchAsync: _ =>
            {
                barrier.SignalAndWait();
                observedScenarioA = AutomationLaunchContext.GetRequired().ScenarioName;
                return ValueTask.CompletedTask;
            });

        var optionsB = AvaloniaHeadlessLaunchHost.Create(
            static () => throw new NotSupportedException("Window factory is not used in this test."),
            scenarioB,
            beforeLaunchAsync: _ =>
            {
                barrier.SignalAndWait();
                observedScenarioB = AutomationLaunchContext.GetRequired().ScenarioName;
                return ValueTask.CompletedTask;
            });

        try
        {
            await Task.WhenAll(
                Task.Run(() => optionsA.BeforeLaunchAsync!(CancellationToken.None).AsTask()),
                Task.Run(() => optionsB.BeforeLaunchAsync!(CancellationToken.None).AsTask()));
        }
        finally
        {
            optionsA.DisposeCallback!.Invoke();
            optionsB.DisposeCallback!.Invoke();
        }

        using (Assert.Multiple())
        {
            await Assert.That(observedScenarioA).IsEqualTo("ScenarioA");
            await Assert.That(observedScenarioB).IsEqualTo("ScenarioB");
            await Assert.That(AutomationLaunchContext.TryGetCurrent(static _ => null)).IsNull();
        }
    }

    [Test]
    public async Task HeadlessDesktopSession_LaunchPreservesPrimaryException_WhenCleanupFails()
    {
        Exception? exception = null;

        try
        {
            _ = DesktopAppSession.Launch(new HeadlessAppLaunchOptions
            {
                BeforeLaunchAsync = _ => throw new InvalidOperationException("BeforeLaunch failed."),
                CreateMainWindow = static () => throw new NotSupportedException("CreateMainWindow should not run."),
                DisposeCallback = () => throw new ApplicationException("Dispose cleanup failed.")
            });
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        var cleanupException = exception?.Data["AppAutomation.CleanupException"] as ApplicationException;

        using (Assert.Multiple())
        {
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception is InvalidOperationException).IsEqualTo(true);
            await Assert.That(exception!.Message).Contains("BeforeLaunch failed.");
            await Assert.That(cleanupException).IsNotNull();
            await Assert.That(cleanupException!.Message).Contains("Dispose cleanup failed.");
        }
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessVisualGrid_EditGridCellText_CommitsValue()
    {
        using var headless = StartHeadlessRuntime();
        var window = HeadlessRuntime.Dispatch(() => CreateVisualGridWindow("OldValue"));
        var page = new VisualGridPage(new HeadlessControlResolver(window));

        page.EditGridCellText(
            static candidate => candidate.EremexDemoDataGridAutomationBridge,
            0,
            1,
            "EditedValue");

        var editedValue = page.EremexDemoDataGridAutomationBridge.GetRowByIndex(0)!.Cells[1].Value;
        await Assert.That(editedValue).IsEqualTo("EditedValue");
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessVisualGrid_EditGridCellText_CancelKeepsOriginalValue()
    {
        using var headless = StartHeadlessRuntime();
        var window = HeadlessRuntime.Dispatch(() => CreateVisualGridWindow("OriginalValue"));
        var page = new VisualGridPage(new HeadlessControlResolver(window));

        page.EditGridCellText(
            static candidate => candidate.EremexDemoDataGridAutomationBridge,
            0,
            1,
            "ChangedValue",
            GridCellEditCommitMode.Cancel);

        var editedValue = page.EremexDemoDataGridAutomationBridge.GetRowByIndex(0)!.Cells[1].Value;
        await Assert.That(editedValue).IsEqualTo("OriginalValue");
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessVisualGrid_EditGridCellTypedValues_Commits()
    {
        using var headless = StartHeadlessRuntime();
        var window = HeadlessRuntime.Dispatch(CreateVisualGridWindowWithEditors);
        var page = new VisualGridPage(new HeadlessControlResolver(window));

        page
            .EditGridCellDate(
                static candidate => candidate.TypedEditorsGrid,
                0,
                1,
                new DateTime(2026, 4, 22))
            .SelectGridCellComboItem(
                static candidate => candidate.TypedEditorsGrid,
                0,
                2,
                "Ready")
            .EditGridCellTime(
                static candidate => candidate.TypedEditorsGrid,
                0,
                3,
                new TimeSpan(13, 45, 30))
            .EditGridCellColor(
                static candidate => candidate.TypedEditorsGrid,
                0,
                4,
                "#336699");

        var cells = page.TypedEditorsGrid.GetRowByIndex(0)!.Cells;
        using (Assert.Multiple())
        {
            await Assert.That(cells[1].Value).IsEqualTo("2026-04-22");
            await Assert.That(cells[2].Value).IsEqualTo("Ready");
            await Assert.That(cells[3].Value).IsEqualTo("13:45:30");
            await Assert.That(cells[4].Value).IsEqualTo("#FF336699");
        }
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessCatalogGrid_ReadsPublicItemsSourceWithoutItemsControl()
    {
        using var headless = StartHeadlessRuntime();
        var rows = new List<RuntimeGridRow>
        {
            new(10, new RuntimeReference("Repeated item"), 100m, RuntimeGridState.Pending),
            new(20, new RuntimeReference("Repeated item"), 200m, RuntimeGridState.Ready),
            new(30, new RuntimeReference("Other item"), 300m, RuntimeGridState.Pending)
        };
        var window = HeadlessRuntime.Dispatch(() => CreateRuntimeGridWindow(rows));
        var catalog = CreateRuntimeGridCatalog();
        var page = new RuntimeGridPage(
            new HeadlessControlResolver(window).WithGridAutomation(catalog));
        var targetRow = GridRowSelector.ByCell("PositionNumber", "20");

        var movedRow = rows[2];
        rows.RemoveAt(2);
        rows.Insert(0, movedRow);

        var resolution = ((IAddressableGridControl)page.RuntimeGrid).ResolveRow(targetRow, 1000);
        var product = GridValueReader.ReadCellText(page.RuntimeGrid, targetRow, "Product");
        var required = GridValueReader.ReadCellNumber(page.RuntimeGrid, targetRow, "Required");
        var state = GridValueReader.ReadCellText(page.RuntimeGrid, targetRow, "State");
        page.EditGridCellNumber(
            static candidate => candidate.RuntimeGrid,
            targetRow,
            "Required",
            275d);
        var editedRequired = GridValueReader.ReadCellNumber(page.RuntimeGrid, targetRow, "Required");
        var grid = HeadlessRuntime.Dispatch(() => (RuntimeGridHost)window.Content!);

        using (Assert.Multiple())
        {
            await Assert.That(resolution.State).IsEqualTo(GridRowResolutionState.Unique);
            await Assert.That(product).IsEqualTo("Repeated item");
            await Assert.That(required).IsEqualTo(200d);
            await Assert.That(state).IsEqualTo("Ready for processing");
            await Assert.That(editedRequired).IsEqualTo(275d);
            await Assert.That(rows[1].RequiredVolume).IsEqualTo(100m);
            await Assert.That(grid.Columns.Any(column => column.FieldName == "PositionNumber")).IsFalse();
            await Assert.That(grid.ScrollIntoViewCalls).IsEqualTo(1);
            await Assert.That(grid.ShowEditorCalls).IsEqualTo(1);
            await Assert.That(grid.PostEditorCalls).IsEqualTo(1);
            await Assert.That(grid.CommitEditingCalls).IsEqualTo(1);
        }
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessDataGrid_UsesHiddenIdentityAndRealEditTransaction()
    {
        using var headless = StartHeadlessRuntime();
        var rows = new List<RuntimeGridRow>
        {
            new(10, new RuntimeReference("First item"), 100m, RuntimeGridState.Pending),
            new(20, new RuntimeReference("Second item"), 200m, RuntimeGridState.Ready)
        };
        var window = HeadlessRuntime.Dispatch(() => CreateNativeDataGridWindow(rows));
        var page = new RuntimeGridPage(
            new HeadlessControlResolver(window).WithGridAutomation(
                CreateRuntimeGridCatalog(useHiddenRowMetadata: true)));

        var selector = GridRowSelector.ByCell("PositionNumber", "20");
        var value = GridValueReader.ReadCellNumber(
            page.RuntimeGrid,
            selector,
            "Required");
        var grid = (IAddressableGridControl)page.RuntimeGrid;

        grid.EditCell(
            new GridCellAddress(selector, "Required"),
            new GridCellValueEditRequest("325", GridCellEditorKind.Number, GridCellEditCommitMode.Commit),
            timeoutMs: 1000);
        var committed = GridValueReader.ReadCellNumber(page.RuntimeGrid, selector, "Required");

        grid.EditCell(
            new GridCellAddress(selector, "Required"),
            new GridCellValueEditRequest("999", GridCellEditorKind.Number, GridCellEditCommitMode.Cancel),
            timeoutMs: 1000);
        var afterCancel = GridValueReader.ReadCellNumber(page.RuntimeGrid, selector, "Required");

        var failedEdit = await Assert.That(() => grid.EditCell(
                new GridCellAddress(selector, "Required"),
                new GridCellValueEditRequest("ignored", GridCellEditorKind.SearchPicker),
                timeoutMs: 1000))
            .Throws<InvalidOperationException>();
        var afterFailedEdit = GridValueReader.ReadCellNumber(page.RuntimeGrid, selector, "Required");

        using (Assert.Multiple())
        {
            await Assert.That(value).IsEqualTo(200d);
            await Assert.That(committed).IsEqualTo(325d);
            await Assert.That(afterCancel).IsEqualTo(325d);
            await Assert.That(afterFailedEdit).IsEqualTo(325d);
            await Assert.That(failedEdit!.Message).Contains("SearchPicker");
            await Assert.That(rows[1].BeginEditCount).IsEqualTo(3);
            await Assert.That(rows[1].EndEditCount).IsEqualTo(1);
            await Assert.That(rows[1].CancelEditCount).IsEqualTo(2);
        }
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessCatalogGrid_RejectsNoOpEditorActivation()
    {
        using var headless = StartHeadlessRuntime();
        var rows = new List<RuntimeGridRow>
        {
            new(10, new RuntimeReference("Item"), 200m, RuntimeGridState.Pending)
        };
        var window = HeadlessRuntime.Dispatch(() => CreateRuntimeGridWindow(rows, opensEditor: false));
        var page = new RuntimeGridPage(
            new HeadlessControlResolver(window).WithGridAutomation(CreateRuntimeGridCatalog()));
        var selector = GridRowSelector.ByCell("PositionNumber", "10");
        var grid = (IAddressableGridControl)page.RuntimeGrid;

        var exception = await Assert.That(() => grid.EditCell(
                new GridCellAddress(selector, "Required"),
                new GridCellValueEditRequest(
                    "321",
                    GridCellEditorKind.Number,
                    GridCellEditCommitMode.Commit),
                timeoutMs: 250))
            .Throws<InvalidOperationException>();

        using (Assert.Multiple())
        {
            await Assert.That(exception!.Message).Contains("writable");
            await Assert.That(rows[0].RequiredVolume).IsEqualTo(200m);
        }
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessCatalogGrid_SearchPickerUsesOpenButtonWhenTextDoesNotOpenResults()
    {
        using var headless = StartHeadlessRuntime();
        var context = HeadlessRuntime.Dispatch(() => CreateDelayedSearchGridWindow(resultsOpenFromText: false));
        var catalog = CreateDelayedSearchGridCatalog();
        var page = new DelayedSearchGridPage(
            new HeadlessControlResolver(context.Window).WithGridAutomation(catalog));

        page.SearchAndSelectGridCell(
            static candidate => candidate.CatalogGrid,
            GridRowSelector.ByCell("StableKey", "10"),
            "SelectedItem",
            "item",
            "Item 42",
            timeoutMs: 2000);

        using (Assert.Multiple())
        {
            await Assert.That(context.Row.SelectedItem).IsEqualTo("Item 42");
            await Assert.That(string.Join("|", context.Events))
                .IsEqualTo("search:item|open|select:Item 42|commit");
        }
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessCatalogGrid_SequentialSearchPickerAndNumberUsesActiveEditorWhenCellStaysVirtualized()
    {
        using var headless = StartHeadlessRuntime();
        var context = HeadlessRuntime.Dispatch(() => CreateDelayedSearchGridWindow(
            resultsOpenFromText: true,
            keepNumberCellVirtualizedAfterSearchCommit: true));
        var page = new DelayedSearchGridPage(
            new HeadlessControlResolver(context.Window).WithGridAutomation(CreateDelayedSearchGridCatalog()));
        var row = GridRowSelector.ByCell("StableKey", "10");

        page
            .SearchAndSelectGridCell(
                static candidate => candidate.CatalogGrid,
                row,
                "SelectedItem",
                "item",
                "Item 42",
                timeoutMs: 5000)
            .EditGridCellNumber(
                static candidate => candidate.CatalogGrid,
                row,
                "RequiredVolume",
                321,
                timeoutMs: 5000);

        using (Assert.Multiple())
        {
            await Assert.That(context.Row.SelectedItem).IsEqualTo("Item 42");
            await Assert.That(context.Row.RequiredVolume).IsEqualTo(321m);
            await Assert.That(string.Join("|", context.Events))
                .IsEqualTo("search:item|select:Item 42|commit|show:RequiredVolume|commit-number");
        }
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessProxyTextBox_ResolvesLogicalWrapperThroughInnerPart()
    {
        using var headless = StartHeadlessRuntime();
        var window = HeadlessRuntime.Dispatch(CreateProxyEditorWindow);
        var page = new ProxyEditorPage(
            new HeadlessControlResolver(window)
                .WithTextBoxProxy("ServerFilterEditor", "ServerFilterEditorInput", fallbackToName: false));

        page.EnterText(static candidate => candidate.ServerFilterEditor, "Updated");

        var updatedValue = page.ServerFilterEditor.Text;
        await Assert.That(updatedValue).IsEqualTo("Updated");
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessSearchPicker_ResolvesListResultsFromDetachedPopupContent()
    {
        using var headless = StartHeadlessRuntime();
        var window = HeadlessRuntime.Dispatch(() => CreatePopupContentSearchPickerWindow(loadResultsOnToggle: false));
        var page = new PopupSearchPickerPage(
            new HeadlessControlResolver(window)
                .WithSearchPicker(
                    "OrderCustomerSearch",
                    SearchPickerParts.ByAutomationIds(
                        "OrderCustomerSearch_Input",
                        "OrderCustomerSearch_Results",
                        resultsKind: SearchPickerResultsKind.ListBox)));

        page.SearchAndSelect(
            static candidate => candidate.OrderCustomerSearch,
            "АЭРОСКАН ООО",
            "АЭРОСКАН ООО");

        await Assert.That(page.OrderCustomerSearch.SelectedItemText).IsEqualTo("АЭРОСКАН ООО");
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessSearchPicker_InvokesToggleExpandButtonBeforeResolvingListResults()
    {
        using var headless = StartHeadlessRuntime();
        var window = HeadlessRuntime.Dispatch(() => CreatePopupContentSearchPickerWindow(loadResultsOnToggle: true));
        var page = new PopupSearchPickerPage(
            new HeadlessControlResolver(window)
                .WithSearchPicker(
                    "OrderCustomerSearch",
                    SearchPickerParts.ByAutomationIds(
                        "OrderCustomerSearch_Input",
                        "OrderCustomerSearch_Results",
                        expandButtonAutomationId: "OrderCustomerSearch_OpenButton",
                        resultsKind: SearchPickerResultsKind.ListBox)));

        page.SearchAndSelect(
            static candidate => candidate.OrderCustomerSearch,
            "АЭРОСКАН ООО",
            "АЭРОСКАН ООО");

        await Assert.That(page.OrderCustomerSearch.SelectedItemText).IsEqualTo("АЭРОСКАН ООО");
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessSearchHistory_IsExactAndScopedToItsSearchControl()
    {
        using var headless = StartHeadlessRuntime();
        var context = HeadlessRuntime.Dispatch(CreateSearchHistoryWindow);
        var page = new SearchControlPage(
            new HeadlessControlResolver(context.Window)
                .WithSearchControl(
                    "TableSearch",
                    SearchControlParts.ByAutomationIds(
                        "TableSearchInput",
                        "SearchHistoryItemButton",
                        historyRootAutomationId: "TableSearchHistoryRoot")));

        var historyItems = page.TableSearch.HistoryItems;
        page.TableSearch.ApplySearchFromHistory("orders");

        using (Assert.Multiple())
        {
            await Assert.That(historyItems).IsEquivalentTo(["Orders", "orders"]);
            await Assert.That(context.ClickCounts).IsEquivalentTo([0, 1, 0]);
        }
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessShellNavigation_ActivatesExistingPaneByAutomationId()
    {
        using var headless = StartHeadlessRuntime();
        var shell = HeadlessRuntime.Dispatch(CreateShellWindow);
        var page = new ShellPage(new HeadlessControlResolver(shell.Window));

        page.ActivateShellPane(static candidate => candidate.MainShell, "newOrder");

        using (Assert.Multiple())
        {
            await Assert.That(shell.Orders.IsActive).IsEqualTo(false);
            await Assert.That(shell.NewOrder.IsActive).IsEqualTo(true);
            await Assert.That(page.MainShell.ActivePaneName).IsEqualTo("newOrder");
            await Assert.That(page.MainShell.OpenPaneNames).Contains("newOrder");
        }
    }

    [Test]
    public async Task AvaloniaDesktopLaunchHost_CreateLaunchOptions_WithScenario_AddsEnvironmentVariables_AndCleansUp()
    {
        using var workspace = TemporaryWorkspace.Create();
        WriteFakeDesktopRepo(workspace.FullPath, includeExecutable: true, includeSource: false);

        var descriptor = new AvaloniaDesktopAppDescriptor(
            solutionFileNames: ["FakeDesktop.sln"],
            desktopProjectRelativePaths: ["src\\FakeDesktop\\FakeDesktop.csproj"],
            desktopTargetFramework: "net8.0",
            executableName: "FakeDesktop.exe");
        var scenario = new AutomationLaunchScenario<LaunchPayload>(
            "SignedInSmoke",
            new LaunchPayload("bob@example.com"));

        var options = AvaloniaDesktopLaunchHost.CreateLaunchOptions(
            descriptor,
            scenario,
            new AvaloniaDesktopLaunchOptions
            {
                BuildBeforeLaunch = false,
                EnvironmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["EXISTING_VALUE"] = "42"
                }
            },
            repositoryRoot: workspace.FullPath);

        var payloadPath = options.EnvironmentVariables[AutomationLaunchContext.ScenarioPayloadPathEnvironmentVariable]!;

        using (Assert.Multiple())
        {
            await Assert.That(options.EnvironmentVariables[AutomationLaunchContext.ScenarioNameEnvironmentVariable]).IsEqualTo("SignedInSmoke");
            await Assert.That(options.EnvironmentVariables["EXISTING_VALUE"]).IsEqualTo("42");
            await Assert.That(File.Exists(payloadPath)).IsEqualTo(true);
        }

        options.DisposeCallback!.Invoke();
        await Assert.That(File.Exists(payloadPath)).IsEqualTo(false);
    }

    [Test]
    public async Task AvaloniaDesktopLaunchHost_CreateLaunchOptions_CopiesWindowPlacement()
    {
        using var workspace = TemporaryWorkspace.Create();
        WriteFakeDesktopRepo(workspace.FullPath, includeExecutable: true, includeSource: false);

        var descriptor = new AvaloniaDesktopAppDescriptor(
            solutionFileNames: ["FakeDesktop.sln"],
            desktopProjectRelativePaths: ["src\\FakeDesktop\\FakeDesktop.csproj"],
            desktopTargetFramework: "net8.0",
            executableName: "FakeDesktop.exe");
        var placement = DesktopWindowPlacement.Centered(
            DesktopMonitorSelector.FromIndex(1),
            width: 1280,
            height: 900);

        var options = AvaloniaDesktopLaunchHost.CreateLaunchOptions(
            descriptor,
            new AvaloniaDesktopLaunchOptions
            {
                BuildBeforeLaunch = false,
                WindowPlacement = placement
            },
            repositoryRoot: workspace.FullPath);

        await Assert.That(options.WindowPlacement).IsEqualTo(placement);
    }

    [Test]
    public async Task AvaloniaDesktopLaunchHost_IsolatedBuildOutput_PreservesBuildOnceSemantics_AcrossLaunches()
    {
        using var workspace = TemporaryWorkspace.Create();
        WriteFakeDesktopRepo(workspace.FullPath, includeExecutable: false, includeSource: true);

        var descriptor = new AvaloniaDesktopAppDescriptor(
            solutionFileNames: ["FakeDesktop.sln"],
            desktopProjectRelativePaths: ["src\\FakeDesktop\\FakeDesktop.csproj"],
            desktopTargetFramework: "net8.0",
            executableName: "FakeDesktop.exe");
        var projectSourcePath = Path.Combine(workspace.FullPath, "src", "FakeDesktop", "Program.cs");

        var firstOptions = AvaloniaDesktopLaunchHost.CreateLaunchOptions(
            descriptor,
            new AvaloniaDesktopLaunchOptions
            {
                UseIsolatedBuildOutput = true,
                BuildConfiguration = "Debug",
                BuildBeforeLaunch = true,
                BuildOncePerProcess = true
            },
            repositoryRoot: workspace.FullPath);

        var firstIsolatedRoot = Path.GetFullPath(Path.Combine(firstOptions.ExecutablePath, "..", "..", ".."));
        firstOptions.DisposeCallback!.Invoke();

        File.WriteAllText(projectSourcePath, "this is not valid csharp");

        DesktopAppLaunchOptions? secondOptions = null;
        try
        {
            secondOptions = AvaloniaDesktopLaunchHost.CreateLaunchOptions(
                descriptor,
                new AvaloniaDesktopLaunchOptions
                {
                    UseIsolatedBuildOutput = true,
                    BuildConfiguration = "Debug",
                    BuildBeforeLaunch = true,
                    BuildOncePerProcess = true
                },
                repositoryRoot: workspace.FullPath);

            var secondIsolatedRoot = Path.GetFullPath(Path.Combine(secondOptions.ExecutablePath, "..", "..", ".."));

            using (Assert.Multiple())
            {
                await Assert.That(File.Exists(secondOptions.ExecutablePath)).IsEqualTo(true);
                await Assert.That(secondIsolatedRoot).IsEqualTo(firstIsolatedRoot);
                await Assert.That(Directory.Exists(firstIsolatedRoot)).IsEqualTo(true);
            }
        }
        finally
        {
            secondOptions?.DisposeCallback?.Invoke();
            if (Directory.Exists(firstIsolatedRoot))
            {
                Directory.Delete(firstIsolatedRoot, recursive: true);
            }
        }
    }

    [Test]
    public async Task AvaloniaDesktopLaunchHost_IsolatedBuildOutput_WritesOutsideProjectBin_AndCleansAutoTemp()
    {
        using var workspace = TemporaryWorkspace.Create();
        WriteFakeDesktopRepo(workspace.FullPath, includeExecutable: false, includeSource: true);

        var descriptor = new AvaloniaDesktopAppDescriptor(
            solutionFileNames: ["FakeDesktop.sln"],
            desktopProjectRelativePaths: ["src\\FakeDesktop\\FakeDesktop.csproj"],
            desktopTargetFramework: "net8.0",
            executableName: "FakeDesktop.exe");

        var options = AvaloniaDesktopLaunchHost.CreateLaunchOptions(
            descriptor,
            new AvaloniaDesktopLaunchOptions
            {
                UseIsolatedBuildOutput = true,
                BuildConfiguration = "Debug",
                BuildBeforeLaunch = true,
                BuildOncePerProcess = false
            },
            repositoryRoot: workspace.FullPath);

        var defaultOutputPath = Path.Combine(
            workspace.FullPath,
            "src",
            "FakeDesktop",
            "bin",
            "Debug",
            "net8.0",
            "FakeDesktop.exe");
        var isolatedRoot = Path.GetFullPath(Path.Combine(options.ExecutablePath, "..", "..", ".."));

        using (Assert.Multiple())
        {
            await Assert.That(File.Exists(options.ExecutablePath)).IsEqualTo(true);
            await Assert.That(options.ExecutablePath.Contains("AppAutomationDesktopBuild-", StringComparison.Ordinal)).IsEqualTo(true);
            await Assert.That(File.Exists(defaultOutputPath)).IsEqualTo(false);
        }

        options.DisposeCallback!.Invoke();
        await Assert.That(Directory.Exists(isolatedRoot)).IsEqualTo(false);
    }

    [Test]
    public async Task AvaloniaDesktopLaunchHost_BuildUsesDotnetHostPath_WhenPathDoesNotContainDotnet()
    {
        using var workspace = TemporaryWorkspace.Create();
        WriteFakeDesktopRepo(workspace.FullPath, includeExecutable: false, includeSource: true);

        var descriptor = new AvaloniaDesktopAppDescriptor(
            solutionFileNames: ["FakeDesktop.sln"],
            desktopProjectRelativePaths: ["src\\FakeDesktop\\FakeDesktop.csproj"],
            desktopTargetFramework: "net8.0",
            executableName: "FakeDesktop.exe");
        var dotnetHostPath = ResolveAvailableDotnetHostPath();
        var unreachablePathEntry = Path.Combine(workspace.FullPath, "missing-dotnet");

        using var pathScope = TemporaryEnvironmentVariableScope.Override("PATH", unreachablePathEntry);
        using var dotnetHostScope = TemporaryEnvironmentVariableScope.Override("DOTNET_HOST_PATH", dotnetHostPath);
        using var dotnetRootScope = TemporaryEnvironmentVariableScope.Override(
            "DOTNET_ROOT",
            Path.GetDirectoryName(dotnetHostPath));

        var options = AvaloniaDesktopLaunchHost.CreateLaunchOptions(
            descriptor,
            new AvaloniaDesktopLaunchOptions
            {
                UseIsolatedBuildOutput = true,
                BuildConfiguration = "Debug",
                BuildBeforeLaunch = true,
                BuildOncePerProcess = false
            },
            repositoryRoot: workspace.FullPath);

        var isolatedRoot = Path.GetFullPath(Path.Combine(options.ExecutablePath, "..", "..", ".."));

        using (Assert.Multiple())
        {
            await Assert.That(File.Exists(options.ExecutablePath)).IsEqualTo(true);
            await Assert.That(options.ExecutablePath.Contains("AppAutomationDesktopBuild-", StringComparison.Ordinal)).IsEqualTo(true);
        }

        options.DisposeCallback!.Invoke();
        await Assert.That(Directory.Exists(isolatedRoot)).IsEqualTo(false);
    }

    private static void WriteFakeDesktopRepo(string repositoryRoot, bool includeExecutable, bool includeSource)
    {
        Directory.CreateDirectory(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "FakeDesktop.sln"), string.Empty);

        var projectDirectory = Path.Combine(repositoryRoot, "src", "FakeDesktop");
        Directory.CreateDirectory(projectDirectory);

        File.WriteAllText(Path.Combine(projectDirectory, "FakeDesktop.csproj"), """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""");

        if (includeSource)
        {
            File.WriteAllText(Path.Combine(projectDirectory, "Program.cs"), """
Console.WriteLine("Fake desktop");
""");
        }

        if (!includeExecutable)
        {
            return;
        }

        var executablePath = Path.Combine(projectDirectory, "bin", "Debug", "net8.0", "FakeDesktop.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, "fake");
    }

    private static string ResolveAvailableDotnetHostPath()
    {
        var dotnetHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(dotnetHostPath) && File.Exists(dotnetHostPath))
        {
            return Path.GetFullPath(dotnetHostPath);
        }

        var executableName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var pathEntry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(pathEntry, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not resolve a dotnet host path for the test process.");
    }

    private static Window CreateVisualGridWindow(string editableCellValue)
    {
        var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal };
        var firstCell = new TextBlock { Text = "EX-R1" };
        var editableCell = new TextBox { Text = editableCellValue };
        AutomationProperties.SetAutomationId(row, "EremexDemoDataGridAutomationBridge_Row0");
        AutomationProperties.SetAutomationId(firstCell, "EremexDemoDataGridAutomationBridge_Row0_Cell0");
        AutomationProperties.SetAutomationId(editableCell, "EremexDemoDataGridAutomationBridge_Row0_Cell1");
        row.Children.Add(firstCell);
        row.Children.Add(editableCell);

        var bridge = new StackPanel();
        AutomationProperties.SetAutomationId(bridge, "EremexDemoDataGridAutomationBridge");
        bridge.Children.Add(row);

        return new Window { Content = bridge };
    }

    private static Window CreateVisualGridWindowWithEditors()
    {
        var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal };
        var firstCell = new TextBlock { Text = "EX-R1" };
        var dateCell = new DatePicker { SelectedDate = new DateTimeOffset(new DateTime(2026, 1, 1)) };
        var comboCell = new ComboBox
        {
            ItemsSource = new[] { "Draft", "Ready" },
            SelectedIndex = 0
        };
        var timeCell = new TimePicker { SelectedTime = new TimeSpan(8, 0, 0) };
        var colorCell = new TextBox { Text = "#FF000000" };
        AutomationProperties.SetAutomationId(row, "TypedEditorsGrid_Row0");
        AutomationProperties.SetAutomationId(firstCell, "TypedEditorsGrid_Row0_Cell0");
        AutomationProperties.SetAutomationId(dateCell, "TypedEditorsGrid_Row0_Cell1");
        AutomationProperties.SetAutomationId(comboCell, "TypedEditorsGrid_Row0_Cell2");
        AutomationProperties.SetAutomationId(timeCell, "TypedEditorsGrid_Row0_Cell3");
        AutomationProperties.SetAutomationId(colorCell, "TypedEditorsGrid_Row0_Cell4");
        row.Children.Add(firstCell);
        row.Children.Add(dateCell);
        row.Children.Add(comboCell);
        row.Children.Add(timeCell);
        row.Children.Add(colorCell);

        var bridge = new StackPanel();
        AutomationProperties.SetAutomationId(bridge, "TypedEditorsGrid");
        bridge.Children.Add(row);

        return new Window { Content = bridge };
    }

    private static Window CreateRuntimeGridWindow(
        IEnumerable<RuntimeGridRow> rows,
        bool opensEditor = true)
    {
        var grid = new RuntimeGridHost(rows, opensEditor)
        {
            Columns =
            [
                new RuntimeGridColumn("Product", "Product"),
                new RuntimeGridColumn("RequiredVolume", "Required"),
                new RuntimeGridColumn("State", "State")
            ]
        };
        grid.MaterializeFirstRow();
        AutomationProperties.SetAutomationId(grid, "RuntimeGrid");
        return new Window { Content = grid };
    }

    private static GridAutomationCatalog CreateRuntimeGridCatalog(bool useHiddenRowMetadata = false)
    {
        var identityColumn = GridColumnDefinition.Auto("PositionNumber")
            .AsValue(GridCellValueKind.Number);
        if (useHiddenRowMetadata)
        {
            identityColumn = identityColumn.ReadIdentityFromRow(GridRowAutomationProperty.ItemStatus);
        }

        return new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds(
                    "RuntimeGrid",
                    "RuntimeGrid",
                    "RuntimeGrid")
                .WithColumns(
                    identityColumn,
                    GridColumnDefinition.Auto("Product")
                        .DisplayValueFrom("Product.Name")
                        .AsValue(GridCellValueKind.Reference),
                    GridColumnDefinition.Map("Required")
                        .FromField("RequiredVolume")
                        .AtRuntime("Required")
                        .AsValue(GridCellValueKind.Number),
                    GridColumnDefinition.Auto("State")
                        .AsValue(GridCellValueKind.Selection))
                .IdentifyRowsBy("PositionNumber"));
    }

    private static Window CreateNativeDataGridWindow(IEnumerable<RuntimeGridRow> rows)
    {
        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            Columns =
            {
                new DataGridTextColumn
                {
                    Header = "Product",
                    SortMemberPath = "Product.Name",
                    Binding = new global::Avalonia.Data.Binding("Product.Name")
                },
                new DataGridTemplateColumn
                {
                    Header = null,
                    IsVisible = false,
                    CellTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<RuntimeGridRow>(
                        static (row, _) => new NativeNumberEditor { Value = row.PositionNumber })
                },
                new DataGridTemplateColumn
                {
                    Header = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = "_Required" }
                        }
                    },
                    CellTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<RuntimeGridRow>(
                        (row, _) => new TextBlock { Text = row.RequiredVolume.ToString(System.Globalization.CultureInfo.InvariantCulture) }),
                    CellEditingTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<RuntimeGridRow>(
                        static (_, _) =>
                        {
                            var editor = new NativeNumberEditor();
                            editor.Bind(
                                NativeNumberEditor.ValueProperty,
                                new global::Avalonia.Data.Binding(nameof(RuntimeGridRow.RequiredVolume))
                                {
                                    Mode = global::Avalonia.Data.BindingMode.TwoWay
                                });
                            return editor;
                        })
                },
                new DataGridTextColumn
                {
                    Header = "State",
                    SortMemberPath = "State",
                    Binding = new global::Avalonia.Data.Binding("State")
                }
            }
        };
        AutomationProperties.SetAutomationId(grid, "RuntimeGrid");
        var window = new Window
        {
            Width = 640,
            Height = 320,
            Content = grid
        };
        window.Show();
        return window;
    }

    private static GridAutomationCatalog CreateDelayedSearchGridCatalog()
    {
        return new GridAutomationCatalog().Add(
            GridAutomationDefinition.ByAutomationIds(
                    "CatalogGrid",
                    "CatalogGrid",
                    "CatalogGrid")
                .WithColumns(
                    GridColumnDefinition.Auto("StableKey"),
                    GridColumnDefinition.Auto("SelectedItem")
                        .AsValue(GridCellValueKind.Reference)
                        .EditWith(
                            GridCellEditorKind.SearchPicker,
                            new GridCellEditorParts(
                                Input: new GridRelativeLocator("ItemPicker_Input"),
                                Results: new GridRelativeLocator(
                                    "ItemPicker_Results",
                                    GridRelativeLocatorScope.DetachedPopup),
                                OpenButton: new GridRelativeLocator("ItemPicker_Open"))),
                    GridColumnDefinition.Auto("RequiredVolume")
                        .AsValue(GridCellValueKind.Number)
                        .EditWith(
                            GridCellEditorKind.Number,
                            new GridCellEditorParts(
                                Input: new GridRelativeLocator("RequiredVolume_Input"),
                                CommitTarget: new GridRelativeLocator(
                                    "RequiredVolume_CommitTarget",
                                    GridRelativeLocatorScope.Row),
                                UseKeyboardInput: true)))
                .IdentifyRowsBy("StableKey"));
    }

    private static DelayedSearchGridContext CreateDelayedSearchGridWindow(
        bool resultsOpenFromText,
        int numberCellMaterializationDelayMs = 0,
        bool keepNumberCellVirtualizedAfterSearchCommit = false)
    {
        var row = new DelayedSearchGridRow("10", "Original item", 200m);
        var popupLayer = new StackPanel();
        var grid = new DelayedSearchGridHost(
            row,
            popupLayer,
            resultsOpenFromText,
            numberCellMaterializationDelayMs,
            keepNumberCellVirtualizedAfterSearchCommit);
        AutomationProperties.SetAutomationId(grid, "CatalogGrid");
        var root = new StackPanel();
        root.Children.Add(grid);
        root.Children.Add(popupLayer);
        var window = new Window { Content = root };
        window.Show();
        return new DelayedSearchGridContext(window, row, grid.Events);
    }

    private static Window CreateProxyEditorWindow()
    {
        var wrapper = new Border();
        var innerEditor = new TextBox { Text = "Initial" };
        AutomationProperties.SetAutomationId(wrapper, "ServerFilterEditor");
        AutomationProperties.SetAutomationId(innerEditor, "ServerFilterEditorInput");
        wrapper.Child = innerEditor;

        return new Window { Content = wrapper };
    }

    private static Window CreatePopupContentSearchPickerWindow(bool loadResultsOnToggle)
    {
        var root = new StackPanel();
        var input = new TextBox();
        AutomationProperties.SetAutomationId(input, "OrderCustomerSearch_Input");
        root.Children.Add(input);

        var resultItems = new[]
        {
            "АЭРОСКАН ООО",
            "БЕТА ООО"
        };
        var results = new ListBox
        {
            ItemsSource = loadResultsOnToggle ? Array.Empty<string>() : resultItems
        };
        AutomationProperties.SetAutomationId(results, "OrderCustomerSearch_Results");

        if (loadResultsOnToggle)
        {
            var expandButton = new ToggleButton();
            AutomationProperties.SetAutomationId(expandButton, "OrderCustomerSearch_OpenButton");
            expandButton.PropertyChanged += (_, args) =>
            {
                if (args.Property == ToggleButton.IsCheckedProperty && expandButton.IsChecked == true)
                {
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => results.ItemsSource = resultItems);
                }
            };
            root.Children.Add(expandButton);
        }

        root.Children.Add(new PopupContentHost { PopupContent = results });
        return new Window { Content = root };
    }

    private static SearchHistoryWindowContext CreateSearchHistoryWindow()
    {
        var clickCounts = new int[3];
        var root = new StackPanel();
        var searchRoot = new StackPanel();
        var unrelatedRoot = new StackPanel();
        AutomationProperties.SetAutomationId(searchRoot, "TableSearchHistoryRoot");
        AutomationProperties.SetAutomationId(unrelatedRoot, "OtherSearchHistoryRoot");

        var input = new TextBox();
        AutomationProperties.SetAutomationId(input, "TableSearchInput");
        root.Children.Add(input);

        AddHistoryButton(searchRoot, "Orders", () => clickCounts[0]++);
        AddHistoryButton(searchRoot, "orders", () => clickCounts[1]++);
        AddHistoryButton(unrelatedRoot, "orders", () => clickCounts[2]++);
        root.Children.Add(searchRoot);
        root.Children.Add(unrelatedRoot);
        return new SearchHistoryWindowContext(new Window { Content = root }, clickCounts);
    }

    private static void AddHistoryButton(Panel root, string text, Action onClick)
    {
        var button = new Button { Content = text };
        AutomationProperties.SetAutomationId(button, "SearchHistoryItemButton");
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => onClick();
        root.Children.Add(button);
    }

    private static ShellWindowContext CreateShellWindow()
    {
        var orders = new ShellPaneModel("ordersViewModel", "Заказы") { IsActive = true };
        var newOrder = new ShellPaneModel("newOrder", "Заказ");

        var ordersPane = new ContentControl { Name = orders.ViewModelId, DataContext = orders };
        var newOrderPane = new ContentControl { Name = newOrder.ViewModelId, DataContext = newOrder };
        AutomationProperties.SetAutomationId(ordersPane, orders.ViewModelId);
        AutomationProperties.SetAutomationId(newOrderPane, newOrder.ViewModelId);

        var shell = new ShellHost { ItemsSource = new[] { orders, newOrder } };
        AutomationProperties.SetAutomationId(shell, "MainShell");
        shell.Children.Add(ordersPane);
        shell.Children.Add(newOrderPane);

        return new ShellWindowContext(new Window { Content = shell }, orders, newOrder);
    }

    private sealed record LaunchPayload(string UserName);

    private sealed record SearchHistoryWindowContext(Window Window, int[] ClickCounts);

    private sealed class VisualGridPage : UiPage
    {
        public VisualGridPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IGridControl EremexDemoDataGridAutomationBridge =>
            Resolve<IGridControl>(VisualGridPageDefinitions.EremexDemoDataGridAutomationBridge);

        public IGridControl TypedEditorsGrid =>
            Resolve<IGridControl>(VisualGridPageDefinitions.TypedEditorsGrid);
    }

    private sealed class RuntimeGridPage : UiPage
    {
        public RuntimeGridPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IGridControl RuntimeGrid => Resolve<IGridControl>(RuntimeGridPageDefinitions.RuntimeGrid);
    }

    private sealed class DelayedSearchGridPage : UiPage
    {
        public DelayedSearchGridPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IGridControl CatalogGrid => Resolve<IGridControl>(DelayedSearchGridPageDefinitions.CatalogGrid);
    }

    private static class RuntimeGridPageDefinitions
    {
        public static UiControlDefinition RuntimeGrid { get; } = new(
            "RuntimeGrid",
            UiControlType.Grid,
            "RuntimeGrid");
    }

    private static class DelayedSearchGridPageDefinitions
    {
        public static UiControlDefinition CatalogGrid { get; } = new(
            "CatalogGrid",
            UiControlType.Grid,
            "CatalogGrid");
    }

    private sealed record DelayedSearchGridContext(
        Window Window,
        DelayedSearchGridRow Row,
        IReadOnlyList<string> Events);

    private sealed class DelayedSearchGridHost : StackPanel
    {
        private readonly DelayedSearchGridRow _row;
        private readonly StackPanel _popupLayer;
        private readonly bool _resultsOpenFromText;
        private readonly int _numberCellMaterializationDelayMs;
        private readonly bool _keepNumberCellVirtualizedAfterSearchCommit;
        private string? _pendingSelection;
        private decimal? _pendingRequiredVolume;
        private bool _numberCellPending;
        private bool _numberCellMaterializationScheduled;

        public DelayedSearchGridHost(
            DelayedSearchGridRow row,
            StackPanel popupLayer,
            bool resultsOpenFromText,
            int numberCellMaterializationDelayMs,
            bool keepNumberCellVirtualizedAfterSearchCommit)
        {
            _row = row;
            _popupLayer = popupLayer;
            _resultsOpenFromText = resultsOpenFromText;
            _numberCellMaterializationDelayMs = numberCellMaterializationDelayMs;
            _keepNumberCellVirtualizedAfterSearchCommit = keepNumberCellVirtualizedAfterSearchCommit;
            ItemsSource = new[] { row };
            Columns =
            [
                new DelayedSearchGridColumn("StableKey", "Key"),
                new DelayedSearchGridColumn("SelectedItem", "Item"),
                new DelayedSearchGridColumn("RequiredVolume", "Required")
            ];
            Materialize();
        }

        public IEnumerable<DelayedSearchGridRow> ItemsSource { get; }

        public IReadOnlyList<DelayedSearchGridColumn> Columns { get; }

        public object? FocusedItem { get; set; }

        public int FocusedRowIndex { get; set; }

        public DelayedSearchGridColumn? FocusedColumn { get; set; }

        public Control? ActiveEditor { get; private set; }

        public List<string> Events { get; } = [];

        public void ShowEditor()
        {
            if (!ReferenceEquals(FocusedItem, _row) || FocusedColumn is null)
            {
                return;
            }

            if (FocusedColumn.FieldName == "RequiredVolume")
            {
                Events.Add("show:RequiredVolume");
                var numberInput = new TextBox
                {
                    Text = _row.RequiredVolume.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
                AutomationProperties.SetAutomationId(numberInput, "RequiredVolume_Input");
                var numberEditor = new DelayedNumberEditor
                {
                    Value = _row.RequiredVolume,
                    Content = numberInput
                };
                var numberCell = Children.OfType<DelayedSearchGridCell>()
                    .SingleOrDefault(candidate => candidate.Context.Column.FieldName == FocusedColumn.FieldName);
                if (numberCell is not null)
                {
                    numberCell.Child = numberEditor;
                }

                ActiveEditor = numberEditor;
                return;
            }

            if (FocusedColumn.FieldName != "SelectedItem")
            {
                return;
            }

            var cell = Children.OfType<DelayedSearchGridCell>()
                .Single(candidate => candidate.Context.Column.FieldName == FocusedColumn.FieldName);

            var editor = new StackPanel();
            var input = new TextBox();
            var open = new MenuItem { Header = "Open" };
            AutomationProperties.SetAutomationId(input, "ItemPicker_Input");
            AutomationProperties.SetAutomationId(open, "ItemPicker_Open");
            input.TextChanged += (_, _) =>
            {
                Events.Add($"search:{input.Text}");
                if (_resultsOpenFromText)
                {
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(
                        EnsureResults,
                        global::Avalonia.Threading.DispatcherPriority.Background);
                }
            };
            open.Click += (_, _) =>
            {
                Events.Add("open");
                global::Avalonia.Threading.Dispatcher.UIThread.Post(
                    EnsureResults,
                    global::Avalonia.Threading.DispatcherPriority.Background);
            };
            editor.Children.Add(input);
            editor.Children.Add(open);
            cell.Child = editor;
            ActiveEditor = editor;
        }

        public void PostEditor()
        {
            if (FocusedColumn?.FieldName == "RequiredVolume"
                && ActiveEditor is DelayedNumberEditor { Value: { } value })
            {
                _pendingRequiredVolume = value;
            }
        }

        public void CommitEditing()
        {
            if (_pendingSelection is not null)
            {
                _row.SelectedItem = _pendingSelection;
                _pendingSelection = null;
                ActiveEditor = null;
                Events.Add("commit");
                _numberCellPending = _keepNumberCellVirtualizedAfterSearchCommit
                    || _numberCellMaterializationDelayMs > 0;
                Materialize();
                if (_numberCellPending && !_keepNumberCellVirtualizedAfterSearchCommit)
                {
                    ScheduleNumberCellMaterialization();
                }

                return;
            }

            if (_pendingRequiredVolume is { } requiredVolume)
            {
                _row.RequiredVolume = requiredVolume;
                _pendingRequiredVolume = null;
                ActiveEditor = null;
                Events.Add("commit-number");
                Materialize();
            }
        }

        private void EnsureResults()
        {
            if (_popupLayer.Children.OfType<ListBox>().Any())
            {
                return;
            }

            var results = new ListBox { ItemsSource = new[] { "Item 42", "Item 84" } };
            AutomationProperties.SetAutomationId(results, "ItemPicker_Results");
            results.SelectionChanged += (_, _) =>
            {
                if (results.SelectedItem is not string selected)
                {
                    return;
                }

                _pendingSelection = selected;
                Events.Add($"select:{selected}");
                _popupLayer.Children.Remove(results);
            };
            _popupLayer.Children.Add(results);
        }

        private void Materialize()
        {
            Children.Clear();
            foreach (var column in Columns)
            {
                if (_numberCellPending && column.FieldName == "RequiredVolume")
                {
                    continue;
                }

                object? value = column.FieldName switch
                {
                    "StableKey" => _row.StableKey,
                    "SelectedItem" => _row.SelectedItem,
                    "RequiredVolume" => _row.RequiredVolume,
                    _ => null
                };
                Children.Add(new DelayedSearchGridCell(
                    new DelayedSearchGridCellContext(_row, column, value)));
            }
        }

        private void ScheduleNumberCellMaterialization()
        {
            if (_numberCellMaterializationScheduled)
            {
                return;
            }

            _numberCellMaterializationScheduled = true;
            _ = Task.Run(async () =>
            {
                await Task.Delay(_numberCellMaterializationDelayMs).ConfigureAwait(false);
                HeadlessRuntime.Dispatch(() =>
                {
                    _numberCellPending = false;
                    _numberCellMaterializationScheduled = false;
                    Materialize();
                });
            });
        }
    }

    private sealed class DelayedNumberEditor : ContentControl
    {
        public decimal? Value { get; set; }
    }

    private sealed record DelayedSearchGridColumn(string FieldName, string Header);

    private sealed class DelayedSearchGridRow(
        string stableKey,
        string selectedItem,
        decimal requiredVolume)
    {
        public string StableKey { get; } = stableKey;

        public string SelectedItem { get; set; } = selectedItem;

        public decimal RequiredVolume { get; set; } = requiredVolume;
    }

    private sealed record DelayedSearchGridCellContext(
        DelayedSearchGridRow Row,
        DelayedSearchGridColumn Column,
        object? Value);

    private sealed class DelayedSearchGridCell : Border
    {
        public DelayedSearchGridCell(DelayedSearchGridCellContext context)
        {
            Context = context;
            DataContext = context;
            Child = new TextBlock
            {
                Text = Convert.ToString(
                    context.Value,
                    System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        public DelayedSearchGridCellContext Context { get; }
    }

    private sealed class RuntimeGridHost : StackPanel
    {
        private readonly bool _opensEditor;

        public RuntimeGridHost(IEnumerable<RuntimeGridRow> itemsSource, bool opensEditor)
        {
            ItemsSource = itemsSource;
            _opensEditor = opensEditor;
        }

        public IEnumerable<RuntimeGridRow> ItemsSource { get; }

        public IReadOnlyList<RuntimeGridColumn> Columns { get; init; } = [];

        public object? FocusedItem { get; set; }

        public int FocusedRowIndex { get; set; }

        public RuntimeGridColumn? FocusedColumn { get; set; }

        public Control? ActiveEditor { get; private set; }

        public int ScrollIntoViewCalls { get; private set; }

        public int ShowEditorCalls { get; private set; }

        public int CommitEditingCalls { get; private set; }

        public int PostEditorCalls { get; private set; }

        private decimal? _postedRequiredVolume;

        public void MaterializeFirstRow()
        {
            var first = ItemsSource.FirstOrDefault();
            if (first is not null)
            {
                Materialize(first);
            }
        }

        public void ScrollIntoView(RuntimeGridRow row)
        {
            ScrollIntoViewCalls++;
            Materialize(row);
        }

        public void ShowEditor()
        {
            ShowEditorCalls++;
            if (!_opensEditor)
            {
                return;
            }

            if (FocusedItem is not RuntimeGridRow row || FocusedColumn is null)
            {
                return;
            }

            var cell = Children
                .OfType<RuntimeGridCell>()
                .SingleOrDefault(candidate =>
                    ReferenceEquals(candidate.Context.Row, row)
                    && ReferenceEquals(candidate.Context.Column, FocusedColumn));
            if (cell is null)
            {
                return;
            }

            var editor = new TextBox
            {
                Text = Convert.ToString(cell.Context.Value, System.Globalization.CultureInfo.InvariantCulture)
            };
            cell.Child = editor;
            ActiveEditor = editor;
        }

        public void PostEditor()
        {
            PostEditorCalls++;
            if (FocusedItem is not RuntimeGridRow row
                || FocusedColumn?.FieldName != "RequiredVolume"
                || ActiveEditor is not TextBox editor
                || !decimal.TryParse(
                    editor.Text,
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                return;
            }

            _postedRequiredVolume = value;
        }

        public void CommitEditing()
        {
            CommitEditingCalls++;
            if (FocusedItem is not RuntimeGridRow row
                || _postedRequiredVolume is not { } value)
            {
                return;
            }

            row.RequiredVolume = value;
            _postedRequiredVolume = null;
            ActiveEditor = null;
            Materialize(row);
        }

        public void CancelEditing()
        {
            ActiveEditor = null;
        }

        private void Materialize(RuntimeGridRow row)
        {
            Children.Clear();
            foreach (var column in Columns)
            {
                object? value = column.FieldName switch
                {
                    "PositionNumber" => row.PositionNumber,
                    "Product" => row.Product,
                    "RequiredVolume" => row.RequiredVolume,
                    "State" => row.State,
                    _ => null
                };
                Children.Add(new RuntimeGridCell(new RuntimeCellContext(row, column, value)));
            }
        }
    }

    private sealed record RuntimeGridColumn(string FieldName, string Header);

    private sealed class NativeNumberEditor : ContentControl
    {
        public static readonly global::Avalonia.StyledProperty<decimal> ValueProperty =
            global::Avalonia.AvaloniaProperty.Register<NativeNumberEditor, decimal>(nameof(Value));

        public NativeNumberEditor()
        {
            Content = new TextBox();
        }

        public decimal Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }

    private sealed class RuntimeGridRow : System.ComponentModel.IEditableObject
    {
        private decimal _editSnapshot;

        public RuntimeGridRow(
            int positionNumber,
            RuntimeReference product,
            decimal requiredVolume,
            RuntimeGridState state)
        {
            PositionNumber = positionNumber;
            Product = product;
            RequiredVolume = requiredVolume;
            State = state;
        }

        public int PositionNumber { get; }

        public RuntimeReference Product { get; }

        public decimal RequiredVolume { get; set; }

        public RuntimeGridState State { get; }

        public int BeginEditCount { get; private set; }

        public int EndEditCount { get; private set; }

        public int CancelEditCount { get; private set; }

        public void BeginEdit()
        {
            BeginEditCount++;
            _editSnapshot = RequiredVolume;
        }

        public void EndEdit()
        {
            EndEditCount++;
        }

        public void CancelEdit()
        {
            CancelEditCount++;
            RequiredVolume = _editSnapshot;
        }
    }

    private enum RuntimeGridState
    {
        [System.ComponentModel.DataAnnotations.Display(Name = "Pending")]
        Pending,

        [System.ComponentModel.DataAnnotations.Display(Name = "Ready for processing")]
        Ready
    }

    private sealed record RuntimeReference(string Name);

    private sealed record RuntimeCellContext(
        RuntimeGridRow Row,
        RuntimeGridColumn Column,
        object? Value);

    private sealed class RuntimeGridCell : Border
    {
        public RuntimeGridCell(RuntimeCellContext context)
        {
            Context = context;
            DataContext = context;
            Child = new TextBlock
            {
                Text = Convert.ToString(
                    context.Value,
                    System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        public RuntimeCellContext Context { get; }
    }

    public static class VisualGridPageDefinitions
    {
        public static UiControlDefinition EremexDemoDataGridAutomationBridge { get; } = new(
            "EremexDemoDataGridAutomationBridge",
            UiControlType.Grid,
            "EremexDemoDataGridAutomationBridge");

        public static UiControlDefinition TypedEditorsGrid { get; } = new(
            "TypedEditorsGrid",
            UiControlType.Grid,
            "TypedEditorsGrid");
    }

    private sealed class ProxyEditorPage : UiPage
    {
        public ProxyEditorPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ITextBoxControl ServerFilterEditor =>
            Resolve<ITextBoxControl>(ProxyEditorPageDefinitions.ServerFilterEditor);
    }

    public static class ProxyEditorPageDefinitions
    {
        public static UiControlDefinition ServerFilterEditor { get; } = new(
            "ServerFilterEditor",
            UiControlType.TextBox,
            "ServerFilterEditor");
    }

    private sealed class PopupSearchPickerPage : UiPage
    {
        public PopupSearchPickerPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ISearchPickerControl OrderCustomerSearch =>
            Resolve<ISearchPickerControl>(PopupSearchPickerPageDefinitions.OrderCustomerSearch);
    }

    public static class PopupSearchPickerPageDefinitions
    {
        public static UiControlDefinition OrderCustomerSearch { get; } = new(
            "OrderCustomerSearch",
            UiControlType.SearchPicker,
            "OrderCustomerSearch");
    }

    private sealed class SearchControlPage : UiPage
    {
        public SearchControlPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ISearchControl TableSearch =>
            Resolve<ISearchControl>(SearchControlPageDefinitions.TableSearch);
    }

    public static class SearchControlPageDefinitions
    {
        public static UiControlDefinition TableSearch { get; } = new(
            "TableSearch",
            UiControlType.Search,
            "TableSearch");
    }

    private sealed class ShellPage : UiPage
    {
        public ShellPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IShellNavigationControl MainShell =>
            Resolve<IShellNavigationControl>(ShellPageDefinitions.MainShell);
    }

    public static class ShellPageDefinitions
    {
        public static UiControlDefinition MainShell { get; } = new(
            "MainShell",
            UiControlType.ShellNavigation,
            "MainShell");
    }

    private sealed record ShellWindowContext(Window Window, ShellPaneModel Orders, ShellPaneModel NewOrder);

    private sealed record ShellPaneModel(string ViewModelId, string Title)
    {
        public bool IsActive { get; set; }
    }

    private sealed class ShellHost : StackPanel
    {
        public IEnumerable<ShellPaneModel> ItemsSource { get; init; } = Array.Empty<ShellPaneModel>();
    }

    private sealed class PopupContentHost : Control
    {
        public object? PopupContent { get; init; }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string fullPath)
        {
            FullPath = fullPath;
        }

        public string FullPath { get; }

        public static TemporaryWorkspace Create()
        {
            var fullPath = Path.Combine(
                Path.GetTempPath(),
                "AppAutomationTestHostTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fullPath);
            return new TemporaryWorkspace(fullPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(FullPath))
            {
                Directory.Delete(FullPath, recursive: true);
            }
        }
    }

    private sealed class TemporaryEnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        private TemporaryEnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public static TemporaryEnvironmentVariableScope Override(string name, string? value)
        {
            return new TemporaryEnvironmentVariableScope(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }
}
