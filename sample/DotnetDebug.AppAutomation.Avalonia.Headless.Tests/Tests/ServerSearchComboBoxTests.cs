using AppAutomation.Avalonia.Headless.Session;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DotnetDebug.AppAutomation.TestHost;
using DotnetDebug.Avalonia;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.AppAutomation.Avalonia.Headless.Tests.Tests.UIAutomationTests;

public sealed class ServerSearchComboBoxTests
{
    [Test]
    [NotInParallel("DesktopUi")]
    public async Task Popup_ClosesWhenEditorLosesFocus()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var popupState = HeadlessRuntime.Dispatch(ObservePopupStateAfterFocusLeaves);

        using (Assert.Multiple())
        {
            await Assert.That(popupState.OpenWhileActive).IsTrue();
            await Assert.That(popupState.OpenAfterFocusLeaves).IsFalse();
        }
    }

    [Test]
    [NotInParallel("DesktopUi")]
    public async Task Popup_ClosesWhenContainingViewScrolls()
    {
        using var session = DesktopAppSession.Launch(DotnetDebugAppLaunchHost.CreateHeadlessLaunchOptions());
        var isPopupOpen = HeadlessRuntime.Dispatch(ObservePopupStateAfterScroll);

        await Assert.That(isPopupOpen).IsFalse();
    }

    private static (bool OpenWhileActive, bool OpenAfterFocusLeaves) ObservePopupStateAfterFocusLeaves()
    {
        var searchPicker = new ServerSearchComboBox { ItemList = new[] { "One", "Two" } };
        var nextControl = new TextBox();
        var window = new Window
        {
            Content = new StackPanel
            {
                Children = { searchPicker, nextControl }
            }
        };

        try
        {
            window.Show();
            searchPicker.Focus();
            Dispatcher.UIThread.RunJobs();
            searchPicker.ApplyTemplate();

            var searchInput = searchPicker.GetVisualDescendants().OfType<TextBox>().Single(textBox =>
                textBox.Name == "PART_RealEditor");
            searchInput.Focus();
            searchPicker.IsPopupOpen = true;
            Dispatcher.UIThread.RunJobs();
            var openWhileActive = searchPicker.IsPopupOpen;

            nextControl.Focus();
            Dispatcher.UIThread.RunJobs();
            return (openWhileActive, searchPicker.IsPopupOpen);
        }
        finally
        {
            window.Close();
        }
    }

    private static bool ObservePopupStateAfterScroll()
    {
        var searchPicker = new ServerSearchComboBox { ItemList = new[] { "One", "Two" } };
        var scrollViewer = new ScrollViewer
        {
            Height = 80,
            Content = new StackPanel
            {
                Children =
                {
                    searchPicker,
                    new Border { Height = 400 }
                }
            }
        };
        var window = new Window { Content = scrollViewer };

        try
        {
            window.Show();
            searchPicker.Focus();
            Dispatcher.UIThread.RunJobs();
            searchPicker.IsPopupOpen = true;
            Dispatcher.UIThread.RunJobs();

            scrollViewer.Offset = new Vector(0, 100);
            Dispatcher.UIThread.RunJobs();
            return searchPicker.IsPopupOpen;
        }
        finally
        {
            window.Close();
        }
    }
}
