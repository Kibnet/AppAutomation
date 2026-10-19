using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderTextInputProvenanceTests
{
    [Test]
    public async Task DisabledBoundTextAfterNearbyPointerInput_DoesNotRecordEnterText()
    {
        var previews = Capture(() => new TextBox { IsEnabled = false }, (session, textBox) =>
        {
            session.RegisterPointerInputForTesting(textBox);
            textBox.Text = "385 за шт";
        });

        await Assert.That(previews).IsEmpty();
    }

    [Test]
    public async Task ReadOnlyBoundTextAfterKeyboardInput_DoesNotRecordEnterText()
    {
        var previews = Capture(() => new TextBox { IsReadOnly = true }, (session, textBox) =>
        {
            session.RegisterKeyboardInputForTesting(textBox);
            textBox.Text = "385 за шт";
        });

        await Assert.That(previews).IsEmpty();
    }

    [Test]
    public async Task FocusedEditableTextChangedByBinding_DoesNotRecordEnterText()
    {
        var previews = Capture(() => new TextBox(), (session, textBox) =>
        {
            textBox.Focus();
            session.RegisterPointerInputForTesting(textBox);
            textBox.Text = "Updated by model";
        });

        await Assert.That(previews).IsEmpty();
    }

    [Test]
    public async Task TextInputIntoEditableTextBox_RecordsEnterText()
    {
        var previews = Capture(() => new TextBox(), (_, textBox) =>
        {
            textBox.Focus();
            textBox.RaiseEvent(new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Text = "Alpha"
            });
            textBox.Text = "Alpha";
        });

        await Assert.That(previews).Contains("Page.EnterText(static page => page.ValueInput, \"Alpha\");");
    }

    [Test]
    public async Task DeleteKeyInEditableTextBox_RecordsEmptyText()
    {
        var previews = Capture(() => new TextBox { Text = "Seed" }, (_, textBox) =>
        {
            textBox.Focus();
            textBox.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Delete
            });
            textBox.Text = string.Empty;
        });

        await Assert.That(previews).Contains("Page.EnterText(static page => page.ValueInput, \"\");");
    }

    [Test]
    [Arguments(Key.Z)]
    [Arguments(Key.Y)]
    public async Task UndoAndRedoInEditableTextBox_RecordResultingText(Key key)
    {
        var previews = Capture(() => new TextBox { Text = "Before" }, (_, textBox) =>
        {
            textBox.Focus();
            textBox.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = key,
                KeyModifiers = KeyModifiers.Control
            });
            textBox.Text = "After";
        });

        await Assert.That(previews).Contains("Page.EnterText(static page => page.ValueInput, \"After\");");
    }

    [Test]
    public async Task ClipboardPasteIntoEditableTextBox_RecordsEnterText()
    {
        var previews = Capture(() => new TextBox(), (_, textBox) =>
        {
            textBox.Focus();
            textBox.RaiseEvent(new RoutedEventArgs(TextBox.PastingFromClipboardEvent));
            textBox.Text = "Pasted";
        });

        await Assert.That(previews).Contains("Page.EnterText(static page => page.ValueInput, \"Pasted\");");
    }

    [Test]
    public async Task TextBoxDisabledBeforePendingEditFlush_DoesNotRecordEnterText()
    {
        var previews = Capture(() => new TextBox(), (_, textBox) =>
        {
            textBox.Focus();
            textBox.RaiseEvent(new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Text = "Draft"
            });
            textBox.Text = "Draft";
            textBox.IsEnabled = false;
        });

        await Assert.That(previews).IsEmpty();
    }

    [Test]
    public async Task TextInputInNewlyMaterializedTextBox_RecordsEnterText()
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        var previews = headless.Dispatch(() =>
        {
            var root = new StackPanel();
            var window = new Window { Content = root, Width = 300, Height = 120 };
            window.Show();
            window.UpdateLayout();
            using var session = new RecorderSession(
                window, new AppAutomationRecorderOptions { ShowOverlay = false });
            session.Start();

            var input = new TextBox();
            AutomationProperties.SetAutomationId(input, "ValueInput");
            root.Children.Add(input);
            window.UpdateLayout();
            input.Focus();
            input.RaiseEvent(new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Text = "Late input"
            });
            input.Text = "Late input";
            session.FlushPendingStateForTesting();
            var result = session.StepJournal.Select(step => step.Preview).ToArray();
            window.Close();
            return result;
        }, CancellationToken.None).GetAwaiter().GetResult();

        await Assert.That(previews).Contains("Page.EnterText(static page => page.ValueInput, \"Late input\");");
    }

    private static string[] Capture(Func<TextBox> createTextBox, Action<RecorderSession, TextBox> interaction)
    {
        using var headless = HeadlessUnitTestSession.StartNew(
            typeof(TestApplication), AvaloniaTestIsolationLevel.PerTest);
        return headless.Dispatch(() =>
        {
            var textBox = createTextBox();
            AutomationProperties.SetAutomationId(textBox, "ValueInput");
            var window = new Window { Content = textBox, Width = 300, Height = 120 };
            window.Show();
            window.UpdateLayout();
            using var session = new RecorderSession(
                window, new AppAutomationRecorderOptions { ShowOverlay = false });
            session.Start();
            interaction(session, textBox);
            session.FlushPendingStateForTesting();
            var result = session.StepJournal.Select(step => step.Preview).ToArray();
            window.Close();
            return result;
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    private sealed class TestApplication : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }
}
