using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.UI;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderPopupObservationTests
{
    private static readonly string[] FilterItems = ["First", "Second"];

    [Test]
    [NotInParallel("RecorderPopupHeadless")]
    public async Task CheckHotkeyWithOpenFlyoutResolvesLogicalFilterWithoutChangingItsValue()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var result = headless.Dispatch(() =>
        {
            var logicalRoot = new FilterEditor
            {
                SelectedItem = "First",
                Child = new TextBlock { Text = "First" }
            };
            AutomationProperties.SetAutomationId(logicalRoot, "StatusFilterRoot");
            var results = new ListBox { Width = 160, Height = 100, ItemsSource = FilterItems };
            AutomationProperties.SetAutomationId(results, "StatusFilterItems");
            results.SelectedItem = "First";
            var flyout = new Flyout { Content = results };
            var openButton = new DropDownButton { Content = "Filters", Flyout = flyout };
            AutomationProperties.SetAutomationId(openButton, "StatusFilterOpenButton");
            var content = new StackPanel { Children = { logicalRoot, openButton } };
            var window = new Window { Width = 400, Height = 300, Content = content };
            window.Show();
            window.UpdateLayout();
            var options = new AppAutomationRecorderOptions { ShowOverlay = false };
            options.ComboBoxFilterHints.Add(new RecorderComboBoxFilterHint(
                "StatusFilterRoot",
                ComboBoxFilterParts.ByAutomationIds(
                    "StatusFilterRoot", "StatusFilterOpenButton", "StatusFilterItems")));
            using var session = new RecorderSession(window, options);
            session.Start();
            var recorderOverlay = new RecorderOverlay();
            recorderOverlay.Attach(session, options);
            flyout.ShowAt(openButton);
            window.UpdateLayout();
            session.RefreshObservedControlsForTesting();
            RecorderCheckTargetSelection? selection = null;
            session.CheckTargetSelected += (_, args) => selection = args.Selection;

            var keyEvent = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Q,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
                Source = window
            };
            window.RaiseEvent(keyEvent);
            var targetSelectionActive = session.IsCheckTargetSelectionActive;
            var popupOpenAfterHotkey = flyout.IsOpen;
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
                Source = window
            });
            var cancelledByEscape = !session.IsCheckTargetSelectionActive;
            var popupOpenAfterEscape = flyout.IsOpen;
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Q,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
                Source = window
            });
            var item = results.ContainerFromIndex(1) as ListBoxItem
                ?? throw new InvalidOperationException("The flyout item was not realized.");
            var targetSelected = session.SelectCheckTargetForTesting(item);
            if (selection is not null)
            {
                var checkMenu = recorderOverlay.CreateCheckMenuForTesting(selection);
                checkMenu.Items.OfType<MenuItem>()
                    .Single(item => string.Equals(item.Header?.ToString(), "Assert active filter", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            var captured = (
                targetSelectionActive,
                popupOpenAfterHotkey,
                cancelledByEscape,
                popupOpenAfterEscape,
                targetSelected,
                SelectedItem: results.SelectedItem,
                Locator: selection?.ValueSnapshot?.Prototype.Control.LocatorValue,
                ValueError: selection?.ValueDescriptionError,
                StepCount: session.StepCount,
                Preview: session.ExportPreview());
            flyout.Hide();
            window.Close();
            return captured;
        }, CancellationToken.None).GetAwaiter().GetResult();

        using (Assert.Multiple())
        {
            await Assert.That(result.targetSelectionActive).IsTrue();
            await Assert.That(result.popupOpenAfterHotkey).IsTrue();
            await Assert.That(result.cancelledByEscape).IsTrue();
            await Assert.That(result.popupOpenAfterEscape).IsTrue();
            await Assert.That(result.targetSelected).IsTrue();
            await Assert.That(result.SelectedItem).IsEqualTo("First");
            await Assert.That(result.Locator).IsEqualTo("StatusFilterRoot");
            await Assert.That(result.ValueError).IsNullOrEmpty();
            await Assert.That(result.StepCount).IsEqualTo(1);
            await Assert.That(result.Preview).Contains("WaitUntilSelectedItemsEqual");
            await Assert.That(result.Preview).Contains("WaitUntilTextEquals");
        }
    }

    [Test]
    [NotInParallel("RecorderPopupHeadless")]
    public async Task FilterResultsCreatedAfterFlyoutOpensAreRecorded()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var result = headless.Dispatch(() =>
        {
            var logicalRoot = new Border();
            AutomationProperties.SetAutomationId(logicalRoot, "StatusFilter");
            var host = new DeferredPopupContentHost();
            var flyoutContent = new StackPanel { Children = { logicalRoot, host } };
            var flyout = new Flyout { Content = flyoutContent };
            var button = new DropDownButton { Content = "Filters", Flyout = flyout };
            AutomationProperties.SetAutomationId(button, "StatusFilterOpenButton");
            var window = new Window { Width = 400, Height = 300, Content = button };
            window.Show();
            var options = new AppAutomationRecorderOptions { ShowOverlay = false };
            options.ComboBoxFilterHints.Add(new RecorderComboBoxFilterHint(
                "StatusFilter",
                ComboBoxFilterParts.ByAutomationIds(
                    "StatusFilter", "StatusFilterOpenButton", "StatusFilterItems")));
            var session = new RecorderSession(window, options);
            session.Start();
            try
            {
                flyout.ShowAt(button);
                var results = new ListBox { ItemsSource = FilterItems };
                AutomationProperties.SetAutomationId(results, "StatusFilterItems");
                host.PopupContent = results;
                host.IsPopupOpen = true;
                session.RegisterPointerInputForTesting(results);
                results.SelectedItem = "First";
                return (results.SelectedItem, session.StepJournal.ToArray(), session.LatestStatus);
            }
            finally
            {
                session.Dispose();
                flyout.Hide();
                window.Close();
            }
        }, CancellationToken.None).GetAwaiter().GetResult();

        await Assert.That(result.SelectedItem).IsEqualTo("First");
        await Assert.That(result.Item2.Length).IsEqualTo(1);
        if (!result.Item2[0].CanPersist)
        {
            throw new InvalidOperationException(result.Item2[0].StatusMessage);
        }
        await Assert.That(result.Item2[0].Preview).Contains(
            "Page.ApplyFilterSelection(static page => page.StatusFilter, new[] { \"First\" });");
    }

    [Test]
    [NotInParallel("RecorderPopupHeadless")]
    public async Task SingleSelectInFlyoutSelectionChangeRecordsFilterSelection()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var fixture = headless.Dispatch(() =>
        {
            var logicalRoot = new Border();
            AutomationProperties.SetAutomationId(logicalRoot, "StatusFilterRoot");
            var results = new ListBox { Width = 160, Height = 100, ItemsSource = FilterItems };
            AutomationProperties.SetAutomationId(results, "StatusFilterItems");
            var flyout = new Flyout { Content = results };
            var openButton = new DropDownButton { Content = "Filters", Flyout = flyout };
            AutomationProperties.SetAutomationId(openButton, "StatusFilterOpenButton");
            var content = new StackPanel { Children = { logicalRoot, openButton } };
            var window = new Window { Width = 400, Height = 300, Content = content };
            window.Show();
            window.UpdateLayout();
            var options = new AppAutomationRecorderOptions { ShowOverlay = false };
            options.ComboBoxFilterHints.Add(new RecorderComboBoxFilterHint(
                "StatusFilter",
                ComboBoxFilterParts.ByAutomationIds(
                    "StatusFilterRoot", "StatusFilterOpenButton", "StatusFilterItems")));
            var session = new RecorderSession(window, options);
            session.Start();
            flyout.ShowAt(openButton);
            window.UpdateLayout();
            session.RefreshObservedControlsForTesting();
            return (window, session, results, flyout);
        }, CancellationToken.None).GetAwaiter().GetResult();
        try
        {
            var result = headless.Dispatch(() =>
            {
                fixture.session.RegisterPointerInputForTesting(fixture.results);
                fixture.results.SelectedItem = "First";
                var recordedSteps = fixture.session.StepJournal.ToArray();
                fixture.flyout.Hide();
                fixture.session.RegisterPointerInputForTesting(fixture.results);
                fixture.results.SelectedItem = "Second";
                return (fixture.results.SelectedItem, recordedSteps, fixture.session.StepJournal.Count, fixture.flyout.IsOpen);
            }, CancellationToken.None).GetAwaiter().GetResult();

            using (Assert.Multiple())
            {
                await Assert.That(result.SelectedItem).IsEqualTo("Second");
                await Assert.That(result.recordedSteps.Length).IsEqualTo(1);
                await Assert.That(result.recordedSteps[0].Preview).Contains(
                    "Page.ApplyFilterSelection(static page => page.StatusFilter, new[] { \"First\" });");
                await Assert.That(result.Count).IsEqualTo(1);
                await Assert.That(result.IsOpen).IsFalse();
            }
        }
        finally
        {
            headless.Dispatch(() =>
            {
                fixture.session.Dispose();
                fixture.window.Close();
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    [Test]
    [NotInParallel("RecorderPopupHeadless")]
    public async Task SingleSelectInWindowOverlayOutsideContentRecordsFilterSelection()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var fixture = headless.Dispatch(CreateFixture, CancellationToken.None).GetAwaiter().GetResult();
        try
        {
            var result = headless.Dispatch(() =>
            {
                fixture.Session.RegisterPointerInputForTesting(fixture.Results);
                fixture.Results.SelectedItem = "First";
                return (fixture.Results.SelectedItem, fixture.Session.StepJournal.ToArray());
            }, CancellationToken.None).GetAwaiter().GetResult();

            using (Assert.Multiple())
            {
                await Assert.That(result.SelectedItem).IsEqualTo("First");
                await Assert.That(result.Item2.Length).IsEqualTo(1);
                await Assert.That(result.Item2[0].Preview).Contains(
                    "Page.ApplyFilterSelection(static page => page.StatusFilter, new[] { \"First\" });");
            }
        }
        finally
        {
            headless.Dispatch(() =>
            {
                fixture.Session.Dispose();
                fixture.Window.Close();
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    [Test]
    [NotInParallel("RecorderPopupHeadless")]
    public async Task ClosingAndReopeningOverlayDetachesOldSelectionAndRecordsOnce()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var fixture = headless.Dispatch(CreateFixture, CancellationToken.None).GetAwaiter().GetResult();
        try
        {
            var result = headless.Dispatch(() =>
            {
                fixture.Session.RegisterPointerInputForTesting(fixture.Results);
                fixture.Results.SelectedItem = "First";
                fixture.Overlay.Children.Remove(fixture.Results);
                fixture.Session.AttachInputHandlersForTesting();
                fixture.Session.RegisterPointerInputForTesting(fixture.Results);
                fixture.Results.SelectedItem = "Second";
                var countWhileClosed = fixture.Session.StepJournal.Count;
                var replacement = new ListBox
                {
                    Width = 160,
                    Height = 100,
                    ItemsSource = FilterItems
                };
                AutomationProperties.SetAutomationId(replacement, "StatusFilterItems");
                fixture.Overlay.Children.Add(replacement);
                fixture.Window.UpdateLayout();
                fixture.Session.AttachInputHandlersForTesting();
                fixture.Session.RegisterPointerInputForTesting(replacement);
                replacement.SelectedItem = "Second";
                return (countWhileClosed, fixture.Session.StepJournal.ToArray());
            }, CancellationToken.None).GetAwaiter().GetResult();

            using (Assert.Multiple())
            {
                await Assert.That(result.countWhileClosed).IsEqualTo(1);
                await Assert.That(result.Item2.Length).IsEqualTo(2);
                await Assert.That(result.Item2[1].Preview).Contains(
                    "Page.ApplyFilterSelection(static page => page.StatusFilter, new[] { \"Second\" });");
            }
        }
        finally
        {
            headless.Dispatch(() =>
            {
                fixture.Session.Dispose();
                fixture.Window.Close();
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    [Test]
    [NotInParallel("RecorderPopupHeadless")]
    public async Task PopupRecordsSelectionAndStopsObservingAfterClose()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var fixture = headless.Dispatch(() =>
        {
            var openButton = new Button { Content = "Open filter" };
            AutomationProperties.SetAutomationId(openButton, "StatusFilterOpenButton");
            var results = new ListBox { Width = 160, Height = 100, ItemsSource = FilterItems };
            AutomationProperties.SetAutomationId(results, "StatusFilterItems");
            var popup = new Popup { Child = results, PlacementTarget = openButton };
            results.SelectionChanged += (_, _) => popup.IsOpen = false;
            var content = new StackPanel();
            var logicalRoot = new Border();
            AutomationProperties.SetAutomationId(logicalRoot, "StatusFilterRoot");
            content.Children.Add(logicalRoot);
            content.Children.Add(openButton);
            content.Children.Add(popup);
            var window = new Window { Width = 400, Height = 300, Content = content };
            window.Show();
            window.UpdateLayout();
            var options = new AppAutomationRecorderOptions { ShowOverlay = false };
            options.ComboBoxFilterHints.Add(new RecorderComboBoxFilterHint(
                "StatusFilter",
                ComboBoxFilterParts.ByAutomationIds(
                    "StatusFilterRoot", "StatusFilterOpenButton", "StatusFilterItems")));
            var session = new RecorderSession(window, options);
            session.Start();
            popup.IsOpen = true;
            window.UpdateLayout();
            return (window, session, popup, results);
        }, CancellationToken.None).GetAwaiter().GetResult();
        try
        {
            var result = headless.Dispatch(() =>
            {
                fixture.session.RegisterPointerInputForTesting(fixture.results);
                fixture.results.SelectedItem = "First";
                var recordedSteps = fixture.session.StepJournal.ToArray();
                fixture.session.AttachInputHandlersForTesting();
                fixture.results.SelectedItem = "Second";
                return (recordedSteps, fixture.session.StepJournal.Count, fixture.popup.IsOpen);
            }, CancellationToken.None).GetAwaiter().GetResult();

            using (Assert.Multiple())
            {
                await Assert.That(result.recordedSteps.Length).IsEqualTo(1);
                await Assert.That(result.recordedSteps[0].Preview).Contains(
                    "Page.ApplyFilterSelection(static page => page.StatusFilter, new[] { \"First\" });");
                await Assert.That(result.Count).IsEqualTo(1);
                await Assert.That(result.IsOpen).IsFalse();
            }
        }
        finally
        {
            headless.Dispatch(() =>
            {
                fixture.session.Dispose();
                fixture.window.Close();
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    [Test]
    [NotInParallel("RecorderPopupHeadless")]
    public async Task MultiSelectInWindowOverlayRecordsApplyAndCancel()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var fixture = headless.Dispatch(CreateMultiSelectFixture, CancellationToken.None)
            .GetAwaiter().GetResult();
        try
        {
            var steps = headless.Dispatch(() =>
            {
                Click(fixture.Second);
                Click(fixture.Apply);
                Click(fixture.First);
                Click(fixture.Cancel);
                return fixture.Session.StepJournal.ToArray();
            }, CancellationToken.None).GetAwaiter().GetResult();

            using (Assert.Multiple())
            {
                await Assert.That(steps.Length).IsEqualTo(2);
                await Assert.That(steps[0].Preview).Contains(
                    "Page.ApplyFilterSelection(static page => page.StatusFilter, new[] { \"First\", \"Second\" });");
                await Assert.That(steps[1].Preview).Contains(
                    "Page.CancelFilterSelection(static page => page.StatusFilter, new[] { \"Second\" });");
            }
        }
        finally
        {
            headless.Dispatch(() =>
            {
                fixture.Session.Dispose();
                fixture.Window.Close();
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    private static PopupFixture CreateFixture()
    {
        var logicalRoot = new Border();
        AutomationProperties.SetAutomationId(logicalRoot, "StatusFilterRoot");
        var openButton = new Button { Content = "Open filter" };
        AutomationProperties.SetAutomationId(openButton, "StatusFilterOpenButton");
        var content = new StackPanel();
        content.Children.Add(logicalRoot);
        content.Children.Add(openButton);
        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = content
        };
        window.Show();
        window.UpdateLayout();

        var results = new ListBox
        {
            Width = 160,
            Height = 100,
            ItemsSource = FilterItems
        };
        AutomationProperties.SetAutomationId(results, "StatusFilterItems");
        var overlay = OverlayLayer.GetOverlayLayer(content)
            ?? throw new InvalidOperationException("The test window has no overlay layer.");
        overlay.Children.Add(results);
        window.UpdateLayout();
        if (content.GetVisualDescendants().Contains(results))
        {
            throw new InvalidOperationException("The test popup must be outside Window.Content.");
        }

        var options = new AppAutomationRecorderOptions { ShowOverlay = false };
        options.ComboBoxFilterHints.Add(new RecorderComboBoxFilterHint(
            "StatusFilter",
            ComboBoxFilterParts.ByAutomationIds(
                "StatusFilterRoot",
                "StatusFilterOpenButton",
                "StatusFilterItems")));
        var session = new RecorderSession(window, options);
        session.Start();
        return new PopupFixture(window, session, results, overlay);
    }

    private static MultiSelectPopupFixture CreateMultiSelectFixture()
    {
        var logicalRoot = new Border();
        AutomationProperties.SetAutomationId(logicalRoot, "StatusFilterRoot");
        var openButton = new Button { Content = "Open filter" };
        AutomationProperties.SetAutomationId(openButton, "StatusFilterOpenButton");
        var content = new StackPanel();
        content.Children.Add(logicalRoot);
        content.Children.Add(openButton);
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        window.UpdateLayout();

        var first = new CheckBox { Content = "First", IsChecked = true };
        var second = new CheckBox { Content = "Second", IsChecked = false };
        AutomationProperties.SetName(first, "First");
        AutomationProperties.SetName(second, "Second");
        var apply = new Button { Content = "OK" };
        var cancel = new Button { Content = "Cancel" };
        AutomationProperties.SetAutomationId(apply, "StatusFilterApplyButton");
        AutomationProperties.SetAutomationId(cancel, "StatusFilterCancelButton");
        cancel.Click += (_, _) =>
        {
            first.IsChecked = true;
            second.IsChecked = true;
        };
        var popupContent = new StackPanel { Width = 180 };
        AutomationProperties.SetAutomationId(popupContent, "StatusFilterItems");
        popupContent.Children.Add(first);
        popupContent.Children.Add(second);
        popupContent.Children.Add(apply);
        popupContent.Children.Add(cancel);
        var overlay = OverlayLayer.GetOverlayLayer(content)
            ?? throw new InvalidOperationException("The test window has no overlay layer.");
        overlay.Children.Add(popupContent);
        window.UpdateLayout();

        var options = new AppAutomationRecorderOptions { ShowOverlay = false };
        options.ComboBoxFilterHints.Add(new RecorderComboBoxFilterHint(
            "StatusFilter",
            ComboBoxFilterParts.ByAutomationIds(
                "StatusFilterRoot",
                "StatusFilterOpenButton",
                "StatusFilterItems",
                "StatusFilterApplyButton",
                "StatusFilterCancelButton")));
        var session = new RecorderSession(window, options);
        session.Start();
        return new MultiSelectPopupFixture(window, session, first, second, apply, cancel);
    }

    private static void Click(Control control)
    {
        var host = TopLevel.GetTopLevel(control)
            ?? throw new InvalidOperationException("The popup control has no TopLevel.");
        var clickPoint = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            host) ?? throw new InvalidOperationException("The popup control has no host position.");
        host.MouseDown(clickPoint, MouseButton.Left);
        host.MouseUp(clickPoint, MouseButton.Left);
    }

    [Test]
    [NotInParallel("RecorderPopupHeadless")]
    public async Task MultiSelectRecordsRenderedCaptionInsteadOfItemTypeName()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var result = headless.Dispatch(() =>
        {
            var option = new FilterOption("Отменён");
            var item = new CheckBox
            {
                Content = option,
                ContentTemplate = new FuncDataTemplate<FilterOption>(
                    static (value, _) => new TextBlock { Text = value?.Caption }),
                IsChecked = true
            };
            var items = new StackPanel { Children = { item } };
            AutomationProperties.SetAutomationId(items, "StatusFilterItems");
            var apply = new Button { Content = "OK" };
            AutomationProperties.SetAutomationId(apply, "StatusFilterApplyButton");
            var open = new Button { Content = "Open filter" };
            AutomationProperties.SetAutomationId(open, "StatusFilterOpenButton");
            var root = new Border();
            AutomationProperties.SetAutomationId(root, "StatusFilterRoot");
            var content = new StackPanel { Children = { root, open, items, apply } };
            var window = new Window { Width = 400, Height = 300, Content = content };
            window.Show();
            window.UpdateLayout();
            var options = new AppAutomationRecorderOptions { ShowOverlay = false };
            options.ComboBoxFilterHints.Add(new RecorderComboBoxFilterHint(
                "StatusFilter",
                ComboBoxFilterParts.ByAutomationIds(
                    "StatusFilterRoot", "StatusFilterOpenButton", "StatusFilterItems",
                    "StatusFilterApplyButton")));
            using var session = new RecorderSession(window, options);
            session.Start();
            session.CaptureButtonClickForTesting(apply);
            var steps = session.StepJournal.ToArray();
            window.Close();
            return steps;
        }, CancellationToken.None).GetAwaiter().GetResult();

        await Assert.That(result.Length).IsEqualTo(1);
        await Assert.That(result[0].Preview).Contains(
            "Page.ApplyFilterSelection(static page => page.StatusFilter, new[] { \"Отменён\" });");
    }

    [Test]
    [NotInParallel("RecorderPopupHeadless")]
    public async Task ListBoxFilterRecordsRenderedCaptionInsteadOfItemTypeName()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var result = headless.Dispatch(() =>
        {
            var option = new FilterOption("Просрочен");
            var results = new ListBox
            {
                Width = 180,
                Height = 100,
                ItemsSource = new[] { option },
                ItemTemplate = new FuncDataTemplate<FilterOption>(
                    static (value, _) => new TextBlock { Text = value?.Caption })
            };
            AutomationProperties.SetAutomationId(results, "StatusFilterItems");
            var open = new Button { Content = "Open filter" };
            AutomationProperties.SetAutomationId(open, "StatusFilterOpenButton");
            var root = new Border();
            AutomationProperties.SetAutomationId(root, "StatusFilterRoot");
            var content = new StackPanel { Children = { root, open, results } };
            var window = new Window { Width = 400, Height = 300, Content = content };
            window.Show();
            window.UpdateLayout();
            var options = new AppAutomationRecorderOptions { ShowOverlay = false };
            options.ComboBoxFilterHints.Add(new RecorderComboBoxFilterHint(
                "StatusFilter",
                ComboBoxFilterParts.ByAutomationIds(
                    "StatusFilterRoot", "StatusFilterOpenButton", "StatusFilterItems")));
            using var session = new RecorderSession(window, options);
            session.Start();
            session.RegisterPointerInputForTesting(results);
            results.SelectedItem = option;
            var steps = session.StepJournal.ToArray();
            window.Close();
            return steps;
        }, CancellationToken.None).GetAwaiter().GetResult();

        await Assert.That(result.Length).IsEqualTo(1);
        await Assert.That(result[0].Preview).Contains(
            "Page.ApplyFilterSelection(static page => page.StatusFilter, new[] { \"Просрочен\" });");
    }

    private sealed class DeferredPopupContentHost : Control
    {
        public static readonly StyledProperty<bool> IsPopupOpenProperty =
            AvaloniaProperty.Register<DeferredPopupContentHost, bool>(nameof(IsPopupOpen));

        public Control? PopupContent { get; set; }

        public bool IsPopupOpen
        {
            get => GetValue(IsPopupOpenProperty);
            set => SetValue(IsPopupOpenProperty, value);
        }
    }

    private sealed class FilterEditor : Border
    {
        public object? SelectedItem { get; set; }
    }

    private sealed record PopupFixture(
        Window Window,
        RecorderSession Session,
        ListBox Results,
        OverlayLayer Overlay);

    private sealed record MultiSelectPopupFixture(
        Window Window,
        RecorderSession Session,
        CheckBox First,
        CheckBox Second,
        Button Apply,
        Button Cancel);

    private sealed class TestApplication : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    private sealed record FilterOption(string Caption);
}
