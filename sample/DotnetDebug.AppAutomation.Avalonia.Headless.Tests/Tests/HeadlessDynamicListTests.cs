using System.Collections.ObjectModel;
using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DotnetDebug.AppAutomation.TestHost;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

[NotInParallel("DesktopUi")]
public sealed class HeadlessDynamicListTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Count_DoesNotMaterializeCaptionsScrollOrChangeSelection(bool hasSelection)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var fixture = HeadlessRuntime.Dispatch(() => CreateList(session.MainWindow));
        var before = HeadlessRuntime.Dispatch(() =>
        {
            fixture.List.SelectedIndex = hasSelection ? 2 : -1;
            fixture.List.Dispatcher.RunJobs();
            fixture.List.UpdateLayout();
            return (fixture.TemplateBuilds, Offset: fixture.List.GetVisualDescendants().OfType<ScrollViewer>().Single().Offset);
        });
        var selectionChanges = 0;
        HeadlessRuntime.Dispatch(() => fixture.List.SelectionChanged += (_, _) => selectionChanges++);

        var count = Resolve(fixture).Items.Count;

        using (Assert.Multiple())
        {
            await Assert.That(count).IsEqualTo(64);
            await Assert.That(fixture.TemplateBuilds).IsEqualTo(before.TemplateBuilds);
            await Assert.That(HeadlessRuntime.Dispatch(() => fixture.List.GetVisualDescendants().OfType<ScrollViewer>().Single().Offset))
                .IsEqualTo(before.Offset);
            await Assert.That(HeadlessRuntime.Dispatch(() => fixture.List.SelectedIndex)).IsEqualTo(hasSelection ? 2 : -1);
            await Assert.That(selectionChanges).IsEqualTo(0);
        }

        var captions = Resolve(fixture).Items.Select(item => item.Text).ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(captions.Length).IsEqualTo(64);
            await Assert.That(HeadlessRuntime.Dispatch(() => fixture.List.SelectedIndex)).IsEqualTo(hasSelection ? 2 : -1);
            await Assert.That(selectionChanges).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(0, true)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    [Arguments(3, true)]
    public async Task Items_RestartsTheWholeReadWhenResultsChangeDuringMaterialization(int updateKind, bool multiSelect)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var fixture = HeadlessRuntime.Dispatch(() => CreateList(session.MainWindow));
        var replacement = updateKind switch
        {
            0 => [],
            1 => fixture.Entries.Take(2).ToArray(),
            2 => Enumerable.Range(1, 64).Select(index => new Entry($"Replacement {index}")).ToArray(),
            _ => fixture.Entries.TakeLast(1).Concat(fixture.Entries.Take(63)).ToArray()
        };
        HeadlessRuntime.Dispatch(() => fixture.OnMaterializingLaterItem = () =>
        {
            switch (updateKind)
            {
                case 0:
                    fixture.Source.Clear();
                    break;
                case 1:
                    while (fixture.Source.Count > replacement.Length)
                        fixture.Source.RemoveAt(fixture.Source.Count - 1);
                    break;
                case 2:
                    fixture.List.ItemsSource = replacement;
                    break;
                default:
                    fixture.Source.Move(63, 0);
                    break;
            }
        });
        var resolver = new HeadlessControlResolver(session.MainWindow);

        var captions = multiSelect
            ? resolver.Resolve<IMultiSelectItemsControl>(Definition).Items.ToArray()
            : resolver.Resolve<IListBoxControl>(Definition).Items.Select(item => item.Text).ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(fixture.UpdateApplied).IsTrue();
            await Assert.That(string.Join("|", captions)).IsEqualTo(string.Join("|", replacement.Select(entry => entry.Caption)));
            await Assert.That(HeadlessRuntime.Dispatch(() => fixture.List.SelectedIndex)).IsEqualTo(-1);
            await Assert.That(Resolve(fixture).Items.Count).IsEqualTo(replacement.Length);
        }
    }

    [Test]
    public async Task Items_PreservesMissingCaptionDiagnosticsButCountDoesNotRequireCaptions()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var fixture = HeadlessRuntime.Dispatch(() => CreateList(session.MainWindow, missingCaption: true));
        var control = Resolve(fixture);

        await Assert.That(control.Items.Count).IsEqualTo(64);
        var failure = Assert.Throws<UiControlResolutionException>(() => control.Items.Select(item => item.Text).ToArray());
        await Assert.That(failure.Message).Contains("does not expose a readable UI caption");
        await Assert.That(failure.Message).Contains("DynamicResults");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelectedCaption_RechecksSelectionWhenTheSelectedItemDisappears(bool multiSelect)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var fixture = HeadlessRuntime.Dispatch(() => CreateList(session.MainWindow));
        HeadlessRuntime.Dispatch(() =>
        {
            fixture.List.AutoScrollToSelectedItem = false;
            fixture.List.SelectionMode = multiSelect ? SelectionMode.Multiple : SelectionMode.Single;
            fixture.List.SelectedIndex = 40;
            fixture.List.Dispatcher.RunJobs();
            fixture.List.ScrollIntoView(0);
            fixture.List.UpdateLayout();
            fixture.List.Dispatcher.RunJobs();
            if (fixture.List.ContainerFromIndex(40) is not null)
                throw new InvalidOperationException("The selected item must be virtualized before reading its caption.");
            fixture.MaterializationCaption = "Item 41";
            fixture.OnMaterializingLaterItem = () => fixture.List.ItemsSource = Array.Empty<Entry>();
        });
        var resolver = new HeadlessControlResolver(session.MainWindow);

        var selected = multiSelect
            ? string.Join("|", resolver.Resolve<IMultiSelectItemsControl>(Definition).SelectedItems)
            : resolver.Resolve<ISelectableListBoxControl>(Definition).SelectedItemText;

        using (Assert.Multiple())
        {
            await Assert.That(fixture.UpdateApplied).IsTrue();
            await Assert.That(string.IsNullOrEmpty(selected)).IsTrue();
            await Assert.That(Resolve(fixture).Items.Count).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task SelectingByCaption_UsesTheNewItemsAndIndicesAfterReplacement(int selectionKind)
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var fixture = HeadlessRuntime.Dispatch(() => CreateList(session.MainWindow));
        Entry[] replacement = [new("Replacement 1"), new("Replacement 2")];
        HeadlessRuntime.Dispatch(() =>
        {
            fixture.List.SelectionMode = selectionKind == 2 ? SelectionMode.Multiple : SelectionMode.Single;
            fixture.OnMaterializingLaterItem = () => fixture.List.ItemsSource = replacement;
        });
        var resolver = new HeadlessControlResolver(session.MainWindow);

        if (selectionKind == 0)
            resolver.Resolve<IExactSelectableListBoxControl>(Definition).SelectItemExact("Replacement 2");
        else
            resolver.Resolve<IMultiSelectItemsControl>(Definition).SetSelectedItems(["Replacement 2"]);

        using (Assert.Multiple())
        {
            await Assert.That(fixture.UpdateApplied).IsTrue();
            await Assert.That(HeadlessRuntime.Dispatch(() => fixture.List.SelectedItem)).IsSameReferenceAs(replacement[1]);
            await Assert.That(Resolve(fixture).SelectedItemText).IsEqualTo("Replacement 2");
        }
    }

    [Test]
    public async Task Items_DoesNotHideUnrelatedMaterializationErrors()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var fixture = HeadlessRuntime.Dispatch(() => CreateList(session.MainWindow));
        HeadlessRuntime.Dispatch(() => fixture.FailMaterialization = true);

        var failure = Assert.Throws<InvalidOperationException>(() => Resolve(fixture).Items.Select(item => item.Text).ToArray());

        await Assert.That(failure.Message).IsEqualTo("Template materialization failed.");
    }

    private static readonly UiControlDefinition Definition = new("DynamicResults", UiControlType.ListBox, "DynamicResults");

    private static ISelectableListBoxControl Resolve(ListFixture fixture) =>
        new HeadlessControlResolver(fixture.Window).Resolve<ISelectableListBoxControl>(Definition);

    private static ListFixture CreateList(Window window, bool missingCaption = false)
    {
        var fixture = new ListFixture(window);
        fixture.List.ItemsSource = fixture.Source;
        fixture.List.ItemTemplate = new FuncDataTemplate<Entry>((entry, _) =>
        {
            fixture.TemplateBuilds++;
            if (entry?.Caption == fixture.MaterializationCaption && fixture.FailMaterialization)
                throw new InvalidOperationException("Template materialization failed.");
            if (entry?.Caption == fixture.MaterializationCaption && fixture.OnMaterializingLaterItem is { } update)
            {
                fixture.OnMaterializingLaterItem = null;
                fixture.List.Dispatcher.Post(() =>
                {
                    update();
                    fixture.UpdateApplied = true;
                }, DispatcherPriority.Background);
            }
            return missingCaption ? new Border() : new TextBlock { Text = entry?.Caption };
        });
        AutomationProperties.SetAutomationId(fixture.List, "DynamicResults");
        window.Content = fixture.List;
        window.Show();
        window.Dispatcher.RunJobs();
        window.UpdateLayout();
        return fixture;
    }

    private sealed class ListFixture
    {
        public ListFixture(Window window)
        {
            Window = window;
            Source = new ObservableCollection<Entry>(Entries);
        }

        public Window Window { get; }
        public ListBox List { get; } = new() { Height = 96 };
        public Entry[] Entries { get; } = Enumerable.Range(1, 64).Select(index => new Entry($"Item {index}")).ToArray();
        public ObservableCollection<Entry> Source { get; }
        public int TemplateBuilds { get; set; }
        public Action? OnMaterializingLaterItem { get; set; }
        public bool UpdateApplied { get; set; }
        public string MaterializationCaption { get; set; } = "Item 24";
        public bool FailMaterialization { get; set; }
    }

    private sealed class Entry(string caption)
    {
        public string Caption { get; } = caption;
    }
}
