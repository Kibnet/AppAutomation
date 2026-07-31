using System.Runtime.Serialization;
using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderMultiSelectCaptureTests
{
    private static readonly string[] AvailableItems = ["Alpha", "Beta", "Gamma"];

    [Test]
    public async Task Apply_RecordsOneSemanticStep()
    {
        using var fixture = MultiSelectFixture.Create("Alpha", "Gamma");
        fixture.Session.Start();

        fixture.Capture(fixture.OpenButton);
        fixture.Capture(fixture.Item("Alpha"));
        fixture.Capture(fixture.Item("Gamma"));
        fixture.Capture(fixture.ApplyButton);

        await AssertSingleSemanticStep(
            fixture.Session,
            "Page.SelectMultiItems(static page => page.Categories, new[] { \"Alpha\", \"Gamma\" });");
    }

    [Test]
    public async Task Cancel_RecordsOneSemanticStep()
    {
        using var fixture = MultiSelectFixture.Create("Beta");
        fixture.Session.Start();

        fixture.Capture(fixture.OpenButton);
        fixture.Capture(fixture.Item("Beta"));
        fixture.Capture(fixture.CancelButton);

        await AssertSingleSemanticStep(
            fixture.Session,
            "Page.CancelMultiSelection(static page => page.Categories, new[] { \"Beta\" });");
    }

    [Test]
    public async Task ItemsContainerSelection_IsSuppressed()
    {
        var options = CreateOptions();
        var itemsContainer = new ListBox
        {
            ItemsSource = new[] { "Alpha", "Beta", "Gamma" },
            SelectedItem = "Alpha"
        };
        AutomationProperties.SetAutomationId(itemsContainer, "CategoriesItems");

        using var session = CreateSession(options, itemsContainer);
        session.Start();
        session.RegisterPointerInputForTesting(itemsContainer);
        session.CaptureListBoxSelectionForTesting(itemsContainer);

        await Assert.That(session.StepJournal).IsEmpty();
    }

    [Test]
    public async Task AmbiguousCommitHint_IsRejectedWithoutPrimitiveFallback()
    {
        var options = CreateOptions();
        options.MultiSelectHints.Add(new RecorderMultiSelectHint("SecondaryCategories", CreateParts()));
        using var fixture = MultiSelectFixture.Create(options);
        fixture.Session.Start();

        fixture.Capture(fixture.ApplyButton);

        using (Assert.Multiple())
        {
            await Assert.That(fixture.Session.StepJournal).IsEmpty();
            await Assert.That(fixture.Session.LatestPreview).IsEmpty();
            await Assert.That(fixture.Session.LatestStatus).Contains("ambiguous");
        }
    }

    private static async Task AssertSingleSemanticStep(RecorderSession session, string expectedPreview)
    {
        using (Assert.Multiple())
        {
            await Assert.That(session.StepJournal.Count).IsEqualTo(1);
            await Assert.That(session.StepJournal[0].Preview).Contains(expectedPreview);
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("Page.SetChecked");
            await Assert.That(session.StepJournal[0].Preview).DoesNotContain("Page.ClickButton");
        }
    }

    private static AppAutomationRecorderOptions CreateOptions()
    {
        var options = new AppAutomationRecorderOptions { ShowOverlay = false };
        options.MultiSelectHints.Add(new RecorderMultiSelectHint("Categories", CreateParts()));
        return options;
    }

    private static MultiSelectParts CreateParts()
    {
        return MultiSelectParts.ByAutomationIds(
            "CategoriesEditor",
            "CategoriesOpenButton",
            "CategoriesItems",
            "CategoriesApplyButton",
            "CategoriesCancelButton");
    }

    private static RecorderSession CreateSession(AppAutomationRecorderOptions options, Control root)
    {
        return new RecorderSession(
            CreateWindowStub(),
            options,
            () => root,
            attachWindowHandlers: false);
    }

    private static Window CreateWindowStub()
    {
#pragma warning disable SYSLIB0050
        return (Window)FormatterServices.GetUninitializedObject(typeof(TestRecorderWindow));
#pragma warning restore SYSLIB0050
    }

    private sealed class MultiSelectFixture : IDisposable
    {
        private readonly IReadOnlyDictionary<string, CheckBox> _items;

        private MultiSelectFixture(
            RecorderSession session,
            Button openButton,
            Button applyButton,
            Button cancelButton,
            IReadOnlyDictionary<string, CheckBox> items)
        {
            Session = session;
            OpenButton = openButton;
            ApplyButton = applyButton;
            CancelButton = cancelButton;
            _items = items;
        }

        public RecorderSession Session { get; }

        public Button OpenButton { get; }

        public Button ApplyButton { get; }

        public Button CancelButton { get; }

        public static MultiSelectFixture Create(params string[] selectedItems)
        {
            return Create(CreateOptions(), selectedItems);
        }

        public static MultiSelectFixture Create(
            AppAutomationRecorderOptions options,
            params string[] selectedItems)
        {
            var selected = selectedItems.ToHashSet(StringComparer.Ordinal);
            var items = AvailableItems
                .ToDictionary(
                    static item => item,
                    item => CreateCheckBox(item, selected.Contains(item)),
                    StringComparer.Ordinal);
            var editorRoot = new Border();
            var openButton = CreateButton("CategoriesOpenButton");
            var itemsContainer = new StackPanel();
            var applyButton = CreateButton("CategoriesApplyButton");
            var cancelButton = CreateButton("CategoriesCancelButton");
            AutomationProperties.SetAutomationId(editorRoot, "CategoriesEditor");
            AutomationProperties.SetAutomationId(itemsContainer, "CategoriesItems");

            foreach (var item in items.Values)
            {
                itemsContainer.Children.Add(item);
            }

            var popupRoot = new StackPanel();
            popupRoot.Children.Add(itemsContainer);
            popupRoot.Children.Add(applyButton);
            popupRoot.Children.Add(cancelButton);

            var windowRoot = new StackPanel();
            windowRoot.Children.Add(editorRoot);
            windowRoot.Children.Add(openButton);

            return new MultiSelectFixture(
                CreateSession(options, windowRoot),
                openButton,
                applyButton,
                cancelButton,
                items);
        }

        public CheckBox Item(string name)
        {
            return _items[name];
        }

        public void Capture(Control control)
        {
            Session.CaptureButtonClickForTesting(control);
        }

        public void Dispose()
        {
            Session.Dispose();
        }

        private static Button CreateButton(string automationId)
        {
            var button = new Button { Content = automationId };
            AutomationProperties.SetAutomationId(button, automationId);
            return button;
        }

        private static CheckBox CreateCheckBox(string text, bool isChecked)
        {
            var checkBox = new CheckBox
            {
                Content = text,
                IsChecked = isChecked
            };
            AutomationProperties.SetAutomationId(checkBox, $"Categories{text}");
            AutomationProperties.SetName(checkBox, text);
            return checkBox;
        }
    }

    private sealed class TestRecorderWindow : Window
    {
    }
}
