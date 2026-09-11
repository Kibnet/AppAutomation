using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.CodeGeneration;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using Avalonia.Automation;
using Avalonia.Controls;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Recorder.Avalonia.Tests;

public sealed class RecorderNotificationCaptureTests
{
    [Test]
    public async Task SelectingEachVisibleNotificationText_CapturesItsOwningNotificationMessage()
    {
        var fixture = new RepeatedNotificationsFixture(
            "Позиция заказа успешно создана",
            "Заказ успешно обновлен");

        var first = fixture.Capture(fixture.FirstText);
        var second = fixture.Capture(fixture.SecondText);

        await AssertCapturedNotification(first, fixture.FirstMessage);
        await AssertCapturedNotification(second, fixture.SecondMessage);
        await AssertRevalidationAllowsRepeatedNotificationParts(fixture, first.Step!, second.Step!);
        await AssertGeneratedCodeUsesOneLogicalNotification(first.Step!, second.Step!);
    }

    [Test]
    public async Task SelectingNotificationTextForExists_CapturesTheLogicalNotification()
    {
        var fixture = new RepeatedNotificationsFixture(
            "First message",
            "Second message");

        var result = fixture.CaptureExists(fixture.SecondText);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilExists);
            await Assert.That(result.Step.Control.ControlType).IsEqualTo(UiControlType.Notification);
            await Assert.That(result.Step.Control.LocatorValue).IsEqualTo(RepeatedNotificationsFixture.NotificationId);
            await Assert.That(result.Step.CanPersist).IsTrue();
            await Assert.That(fixture.ResolveExisting(result.Step).CanPersist).IsTrue();
        }
    }

    [Test]
    public async Task LabelOutsideNotificationRoot_PreservesPrimitiveAssertion()
    {
        var root = new StackPanel();
        var label = new Label { Content = "Standalone status" };
        AutomationProperties.SetAutomationId(label, RepeatedNotificationsFixture.NotificationTextId);
        root.Children.Add(label);
        var factory = new RecorderStepFactory(CreateOptions(), () => root);

        var result = factory.TryCreateAssertionStep(label, RecorderAssertionMode.Text);

        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilTextEquals);
            await Assert.That(result.Step.Control.ControlType).IsEqualTo(UiControlType.Label);
            await Assert.That(result.Step.Control.LocatorValue).IsEqualTo(RepeatedNotificationsFixture.NotificationTextId);
        }
    }

    private static async Task AssertCapturedNotification(StepCreationResult result, string expectedMessage)
    {
        using (Assert.Multiple())
        {
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.Step).IsNotNull();
            await Assert.That(result.Step!.ActionKind).IsEqualTo(RecordedActionKind.WaitUntilNotificationContains);
            await Assert.That(result.Step.StringValue).IsEqualTo(expectedMessage);
            await Assert.That(result.Step.Control.ControlType).IsEqualTo(UiControlType.Notification);
            await Assert.That(result.Step.Control.LocatorValue).IsEqualTo(RepeatedNotificationsFixture.NotificationId);
            await Assert.That(result.Step.CanPersist).IsTrue();
        }
    }

    private static async Task AssertRevalidationAllowsRepeatedNotificationParts(
        RepeatedNotificationsFixture fixture,
        params RecordedStep[] steps)
    {
        foreach (var step in steps)
        {
            var validation = fixture.ResolveExisting(step);

            using (Assert.Multiple())
            {
                await Assert.That(validation.CanPersist).IsTrue();
                await Assert.That(validation.ValidationStatus).IsEqualTo(RecorderValidationStatus.Valid);
                await Assert.That(validation.ValidationMessage).IsNull();
                await Assert.That(validation.MatchedControl).IsNotNull();
            }
        }
    }

    private static async Task AssertGeneratedCodeUsesOneLogicalNotification(params RecordedStep[] steps)
    {
        var generator = new AuthoringCodeGenerator(new AuthoringProjectScanner(), logger: null);
        var preview = generator.GeneratePreview(steps);

        using (Assert.Multiple())
        {
            await Assert.That(CountOccurrences(preview, "Page.WaitUntilNotificationContains(")).IsEqualTo(2);
            await Assert.That(preview.Contains("page.ToastNotificationText", StringComparison.Ordinal)).IsFalse();
            await Assert.That(preview.Contains("page.ToastNotification", StringComparison.Ordinal)).IsTrue();
            await Assert.That(preview.Contains(steps[0].StringValue!, StringComparison.Ordinal)).IsTrue();
            await Assert.That(preview.Contains(steps[1].StringValue!, StringComparison.Ordinal)).IsTrue();
        }
    }

    private static int CountOccurrences(string source, string value)
    {
        return source.Split(value, StringSplitOptions.None).Length - 1;
    }

    private static AppAutomationRecorderOptions CreateOptions()
    {
        var options = new AppAutomationRecorderOptions();
        options.NotificationHints.Add(new RecorderNotificationHint(
            RepeatedNotificationsFixture.NotificationId,
            NotificationControlParts.ByAutomationIds(RepeatedNotificationsFixture.NotificationTextId)));
        return options;
    }

    private sealed class RepeatedNotificationsFixture
    {
        public const string NotificationId = "ToastNotification";
        public const string NotificationTextId = "ToastNotificationText";

        private readonly RecorderStepFactory _factory;
        private readonly RecorderSelectorResolver _resolver;

        public RepeatedNotificationsFixture(string firstMessage, string secondMessage)
        {
            FirstMessage = firstMessage;
            SecondMessage = secondMessage;

            var options = CreateOptions();

            var root = new StackPanel();
            FirstText = AddNotification(root, firstMessage);
            SecondText = AddNotification(root, secondMessage);
            _factory = new RecorderStepFactory(options, () => root);
            _resolver = new RecorderSelectorResolver(options, validationRoot: root);
        }

        public string FirstMessage { get; }

        public string SecondMessage { get; }

        public Label FirstText { get; }

        public Label SecondText { get; }

        public StepCreationResult Capture(Label text)
        {
            return _factory.TryCreateAssertionStep(text, RecorderAssertionMode.Text);
        }

        public StepCreationResult CaptureExists(Label text)
        {
            return _factory.TryCreateAssertionStep(text, RecorderAssertionMode.Exists);
        }

        public RecorderSelectorResolver.ExistingControlResolutionResult ResolveExisting(RecordedStep step)
        {
            return _resolver.ResolveExisting(step);
        }

        private static Label AddNotification(StackPanel root, string message)
        {
            var notification = new Border();
            var text = new Label { Content = message };
            AutomationProperties.SetAutomationId(notification, NotificationId);
            AutomationProperties.SetAutomationId(text, NotificationTextId);
            notification.Child = text;
            root.Children.Add(notification);
            return text;
        }
    }
}
