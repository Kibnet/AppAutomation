using AppAutomation.Abstractions;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Abstractions.Tests;

public sealed class ColorPickerControlTests
{
    [Test]
    public async Task ConfirmedCustomValue_UsesCanonicalColorAndConfiguredParts()
    {
        var fixture = new ColorPickerFixture();

        fixture.Page.SetColor(static page => page.AccentColor, "#336699")
            .WaitUntilColorEquals(static page => page.AccentColor, "#FF336699");

        using (Assert.Multiple())
        {
            await Assert.That(fixture.Current.Text).IsEqualTo("#FF336699");
            await Assert.That(string.Join(" > ", fixture.Actions))
                .IsEqualTo("Open > Enter:#FF336699 > Confirm");
            await Assert.That(fixture.RootResolutionCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task InvalidColor_FailsBeforeOpeningOrChangingUi()
    {
        var fixture = new ColorPickerFixture();

        await Assert.That(() => fixture.Page.SetColor(static page => page.AccentColor, "blue"))
            .Throws<FormatException>();
        await Assert.That(fixture.Actions).IsEmpty();
        await Assert.That(fixture.Current.Text).IsEqualTo("#FF000000");
    }

    [Test]
    [Arguments("#010203", "#FF010203")]
    [Arguments("#7f010203", "#7F010203")]
    public async Task Normalize_PreservesAlphaAndUppercasesHex(string value, string expected)
    {
        await Assert.That(ColorValue.Normalize(value)).IsEqualTo(expected);
    }

    private sealed class ColorPickerFixture : IUiControlResolver
    {
        private readonly IUiControlResolver _resolver;

        public ColorPickerFixture()
        {
            Root = new FakeAvailability("AccentColor")
            {
                IsAvailable = true,
                IsEnabled = false
            };
            Popup = new FakeAvailability("AccentColorPopup");
            Current = new FakeTextBox("AccentColorValue", "#FF000000", Actions);
            Custom = new FakeTextBox("AccentColorCustom", string.Empty, Actions);
            Open = new FakeButton("AccentColorOpen", () =>
            {
                Actions.Add("Open");
                Popup.IsAvailable = true;
            });
            Confirm = new FakeButton("AccentColorConfirm", () =>
            {
                Actions.Add("Confirm");
                Current.Text = Custom.Text;
                Popup.IsAvailable = false;
            });
            _resolver = this.WithColorPicker(
                "AccentColor",
                ColorPickerParts.ByAutomationIds(
                    "AccentColor",
                    "AccentColorValue",
                    openButtonAutomationId: "AccentColorOpen",
                    popupRootAutomationId: "AccentColorPopup",
                    customValueAutomationId: "AccentColorCustom",
                    confirmButtonAutomationId: "AccentColorConfirm",
                    commitMode: ColorPickerCommitMode.Confirm));
            Page = new ColorPickerPage(_resolver);
        }

        public List<string> Actions { get; } = [];

        public FakeAvailability Root { get; }

        public FakeAvailability Popup { get; }

        public FakeTextBox Current { get; }

        public FakeTextBox Custom { get; }

        public FakeButton Open { get; }

        public FakeButton Confirm { get; }

        public ColorPickerPage Page { get; }

        public int RootResolutionCount { get; private set; }

        public UiRuntimeCapabilities Capabilities { get; } = new("color-picker-test");

        public TControl Resolve<TControl>(UiControlDefinition definition)
            where TControl : class
        {
            object control = definition.LocatorValue switch
            {
                "AccentColor" when typeof(TControl) == typeof(IUiControl) => ResolveRoot(),
                "AccentColorValue" when typeof(TControl) == typeof(ITextBoxControl) => Current,
                "AccentColorCustom" when typeof(TControl) == typeof(ITextBoxControl) => Custom,
                "AccentColorOpen" when typeof(TControl) == typeof(IButtonControl) => Open,
                "AccentColorConfirm" when typeof(TControl) == typeof(IButtonControl) => Confirm,
                "AccentColorPopup" when typeof(TControl) == typeof(IUiControl) => Popup,
                _ => throw new InvalidOperationException(
                    $"Unexpected control '{typeof(TControl).Name}:{definition.LocatorValue}'.")
            };
            return (TControl)control;
        }

        private FakeAvailability ResolveRoot()
        {
            RootResolutionCount++;
            return Root;
        }
    }

    private sealed class ColorPickerPage : UiPage
    {
        private static readonly UiControlDefinition Definition = new(
            "AccentColor",
            UiControlType.ColorPicker,
            "AccentColor");

        public ColorPickerPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IColorPickerControl AccentColor => Resolve<IColorPickerControl>(Definition);
    }

    private sealed class FakeButton(string automationId, Action invoke) : IButtonControl
    {
        public string AutomationId { get; } = automationId;

        public string Name => AutomationId;

        public bool IsEnabled => true;

        public void Invoke() => invoke();
    }

    private sealed class FakeTextBox(
        string automationId,
        string text,
        List<string> actions) : ITextBoxControl
    {
        public string AutomationId { get; } = automationId;

        public string Name => AutomationId;

        public bool IsEnabled => true;

        public string Text { get; set; } = text;

        public void Enter(string value)
        {
            Text = value;
            actions.Add($"Enter:{value}");
        }
    }

    private sealed class FakeAvailability(string automationId) : IUiControlAvailability
    {
        public string AutomationId { get; } = automationId;

        public string Name => AutomationId;

        public bool IsEnabled { get; set; } = true;

        public bool IsAvailable { get; set; }
    }
}
