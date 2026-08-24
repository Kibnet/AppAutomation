using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class TimePickerControlTests
{
    [Test]
    public async Task StandardTimePicker_ReplaysAndChecksExactTime()
    {
        var timePicker = new FakeTimePickerControl("StartTimePicker", new TimeSpan(8, 0, 0));
        var page = new TimePickerPage(new FakeResolver(("StartTimePicker", timePicker)));
        var expected = new TimeSpan(9, 45, 30);

        page.SetTime(static candidate => candidate.StartTimePicker, expected)
            .WaitUntilTimeEquals(static candidate => candidate.StartTimePicker, expected);

        await Assert.That(timePicker.SelectedTime).IsEqualTo(expected);
    }

    [Test]
    public async Task CompositeTimePicker_OpensSetsAndConfirmsOneLogicalValue()
    {
        var events = new List<string>();
        var input = new FakeTextBoxControl("DeliveryTimeInput", "08:00");
        var root = new FakeAvailabilityControl("DeliveryTimeEditor") { IsAvailable = true };
        var popup = new FakeAvailabilityControl("DeliveryTimePopup");
        var timePicker = new FakeTimePickerControl("DeliveryTimeSurface", new TimeSpan(8, 0, 0))
        {
            IsAvailable = false,
            OnSet = value => events.Add($"set:{value:c}")
        };
        var openButton = new FakeButtonControl("DeliveryTimeOpen")
        {
            OnInvoke = () =>
            {
                events.Add("open");
                timePicker.IsAvailable = true;
                popup.IsAvailable = true;
            }
        };
        var confirmButton = new FakeButtonControl("DeliveryTimeConfirm")
        {
            OnInvoke = () =>
            {
                events.Add("confirm");
                input.Text = timePicker.SelectedTime?.ToString("c") ?? string.Empty;
                timePicker.IsAvailable = false;
                popup.IsAvailable = false;
            }
        };
        var resolver = new FakeResolver(
                ("DeliveryTimeEditor", root),
                ("DeliveryTimeInput", input),
                ("DeliveryTimeSurface", timePicker),
                ("DeliveryTimePopup", popup),
                ("DeliveryTimeOpen", openButton),
                ("DeliveryTimeConfirm", confirmButton))
            .WithTimePicker(
                "DeliveryTime",
                TimePickerParts.ByAutomationIds(
                    "DeliveryTimeEditor",
                    "DeliveryTimeSurface",
                    inputAutomationId: "DeliveryTimeInput",
                    openButtonAutomationId: "DeliveryTimeOpen",
                    popupRootAutomationId: "DeliveryTimePopup",
                    confirmButtonAutomationId: "DeliveryTimeConfirm",
                    commitMode: TimePickerCommitMode.Confirm));
        var page = new TimePickerPage(resolver);
        var expected = new TimeSpan(14, 5, 0);

        page.SetTime(static candidate => candidate.DeliveryTime, expected, timeoutMs: 500)
            .WaitUntilTimeEquals(static candidate => candidate.DeliveryTime, expected, timeoutMs: 500);

        using (Assert.Multiple())
        {
            await Assert.That(string.Join('|', events)).IsEqualTo("open|set:14:05:00|confirm");
            await Assert.That(openButton.InvokeCount).IsEqualTo(1);
            await Assert.That(confirmButton.InvokeCount).IsEqualTo(1);
            await Assert.That(input.Text).IsEqualTo("14:05:00");
        }
    }

    [Test]
    public async Task SetTime_RejectsValuesOutsideOneDayBeforeMutation()
    {
        var timePicker = new FakeTimePickerControl("StartTimePicker", new TimeSpan(8, 0, 0));
        var page = new TimePickerPage(new FakeResolver(("StartTimePicker", timePicker)));

        await Assert.That(() => page.SetTime(
                static candidate => candidate.StartTimePicker,
                TimeSpan.FromDays(1)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(timePicker.SelectedTime).IsEqualTo(new TimeSpan(8, 0, 0));
    }

    [Test]
    public async Task CompositeTimePicker_RejectsUncommittedPendingValue()
    {
        var input = new FakeTextBoxControl("DeliveryTimeInput", "08:00:00");
        var root = new FakeAvailabilityControl("DeliveryTimeEditor") { IsAvailable = true };
        var popup = new FakeAvailabilityControl("DeliveryTimePopup");
        var timePicker = new FakeTimePickerControl("DeliveryTimeSurface", new TimeSpan(8, 0, 0));
        var openButton = new FakeButtonControl("DeliveryTimeOpen")
        {
            OnInvoke = () => popup.IsAvailable = true
        };
        var confirmButton = new FakeButtonControl("DeliveryTimeConfirm")
        {
            OnInvoke = () => popup.IsAvailable = false
        };
        var resolver = new FakeResolver(
                ("DeliveryTimeEditor", root),
                ("DeliveryTimeInput", input),
                ("DeliveryTimeSurface", timePicker),
                ("DeliveryTimePopup", popup),
                ("DeliveryTimeOpen", openButton),
                ("DeliveryTimeConfirm", confirmButton))
            .WithTimePicker(
                "DeliveryTime",
                TimePickerParts.ByAutomationIds(
                    "DeliveryTimeEditor",
                    "DeliveryTimeSurface",
                    inputAutomationId: "DeliveryTimeInput",
                    openButtonAutomationId: "DeliveryTimeOpen",
                    popupRootAutomationId: "DeliveryTimePopup",
                    confirmButtonAutomationId: "DeliveryTimeConfirm",
                    commitMode: TimePickerCommitMode.Confirm));
        var page = new TimePickerPage(resolver);

        var exception = Assert.Throws<TimeoutException>(() => page.SetTime(
            static candidate => candidate.DeliveryTime,
            new TimeSpan(14, 5, 0),
            timeoutMs: 100));

        using (Assert.Multiple())
        {
            await Assert.That(exception!.Message).Contains("did not reach expected time");
            await Assert.That(input.Text).IsEqualTo("08:00:00");
        }
    }

    private sealed class TimePickerPage : UiPage
    {
        private static readonly UiControlDefinition StartDefinition = new(
            "StartTimePicker",
            UiControlType.TimePicker,
            "StartTimePicker");
        private static readonly UiControlDefinition DeliveryDefinition = new(
            "DeliveryTime",
            UiControlType.TimePicker,
            "DeliveryTimeEditor");

        public TimePickerPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public ITimePickerControl StartTimePicker => Resolve<ITimePickerControl>(StartDefinition);

        public ITimePickerControl DeliveryTime => Resolve<ITimePickerControl>(DeliveryDefinition);
    }

    private sealed class FakeResolver(params (string Locator, object Control)[] controls) : IUiControlResolver
    {
        private readonly IReadOnlyDictionary<string, object> _controls = controls.ToDictionary(
            static entry => entry.Locator,
            static entry => entry.Control,
            StringComparer.Ordinal);

        public UiRuntimeCapabilities Capabilities { get; } = new("fake-runtime");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            if (_controls.TryGetValue(definition.LocatorValue, out var control)
                && control is TControl typed)
            {
                return typed;
            }

            throw new InvalidOperationException($"Unknown control '{definition.LocatorValue}'.");
        }
    }

    private sealed class FakeTimePickerControl(string automationId, TimeSpan? selectedTime) : ITimePickerControl, IUiControlAvailability
    {
        private TimeSpan? _selectedTime = selectedTime;

        public string AutomationId { get; } = automationId;

        public string Name => AutomationId;

        public bool IsEnabled { get; set; } = true;

        public bool IsAvailable { get; set; } = true;

        public Action<TimeSpan>? OnSet { get; init; }

        public TimeSpan? SelectedTime
        {
            get => _selectedTime;
            set
            {
                _selectedTime = value;
                if (value.HasValue)
                {
                    OnSet?.Invoke(value.Value);
                }
            }
        }
    }

    private sealed class FakeButtonControl(string automationId) : IButtonControl
    {
        public string AutomationId { get; } = automationId;

        public string Name => AutomationId;

        public bool IsEnabled { get; set; } = true;

        public int InvokeCount { get; private set; }

        public Action? OnInvoke { get; init; }

        public void Invoke()
        {
            InvokeCount++;
            OnInvoke?.Invoke();
        }
    }

    private sealed class FakeAvailabilityControl(string automationId) : IUiControl, IUiControlAvailability
    {
        public string AutomationId { get; } = automationId;

        public string Name => AutomationId;

        public bool IsEnabled { get; set; } = true;

        public bool IsAvailable { get; set; }
    }

    private sealed class FakeTextBoxControl(string automationId, string text) : ITextBoxControl
    {
        public string AutomationId { get; } = automationId;

        public string Name => AutomationId;

        public bool IsEnabled => true;

        public string Text { get; set; } = text;

        public void Enter(string value) => Text = value;
    }
}
