using System;
using System.Linq;
using DotnetDebug.UiTests.FlaUI.EasyUse.Pages;
using FlaUI.EasyUse.Extensions;
using FlaUI.EasyUse.Session;
using FlaUI.EasyUse.TUnit;
using TUnit.Assertions;
using TUnit.Core;

namespace DotnetDebug.UiTests.FlaUI.EasyUse.Tests.UIAutomationTests;

public sealed class MainWindowFlaUIEasyUseTests : DesktopUiTestBase<MainWindowPage>
{
    protected override DesktopProjectLaunchOptions CreateLaunchOptions()
    {
        return new DesktopProjectLaunchOptions
        {
            SolutionFileName = "DotnetDebug.sln",
            ProjectRelativePath = Path.Combine("src", "DotnetDebug.Avalonia", "DotnetDebug.Avalonia.csproj"),
            BuildConfiguration = "Debug",
            TargetFramework = "net9.0"
        };
    }

    protected override MainWindowPage CreatePage(DesktopAppSession session)
    {
        return new MainWindowPage(session.MainWindow, session.ConditionFactory);
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Calculate_Gcd_WithDefaultSettings_ShowsResultStepsAndHistory()
    {
        var initialHistoryItems = Page.HistoryList.Items.Length;

        Page
            .EnterText(p => p.NumbersInput, "48 18 30")
            .SelectComboItem(p => p.OperationCombo, "GCD")
            .SetChecked(p => p.ShowStepsCheck, true)
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameEquals(p => p.ResultText, "GCD = 6")
            .WaitUntilHasItemsAtLeast(p => p.StepsList, 1)
            .WaitUntilListBoxContains(p => p.HistoryList, "GCD");

        using (Assert.Multiple())
        {
            await UiAssert.TextEqualsAsync(() => Page.ResultText.Text, "GCD = 6");
            await UiAssert.TextContainsAsync(() => Page.ModeLabel.Text, "Mode: GCD");
            await UiAssert.TextContainsAsync(() => Page.ModeLabel.Text, "Steps: On");
            await UiAssert.NumberAtLeastAsync(() => Page.StepsList.Items.Length, 1);
            await UiAssert.NumberAtLeastAsync(() => Page.HistoryList.Items.Length, initialHistoryItems + 1);
            await UiAssert.TextEqualsAsync(() => Page.ErrorText.Text, string.Empty);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Calculate_Lcm_UsesNegativeAndAbsoluteOption()
    {
        var initialHistoryItems = Page.HistoryList.Items.Length;

        Page
            .EnterText(p => p.NumbersInput, "-4 8 12")
            .SelectComboItem(p => p.OperationCombo, "LCM")
            .SetChecked(p => p.UseAbsoluteValuesCheck, true)
            .SetChecked(p => p.ShowStepsCheck, false)
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameEquals(p => p.ResultText, "LCM = 24")
            .WaitUntilListBoxContains(p => p.HistoryList, "LCM");

        using (Assert.Multiple())
        {
            await UiAssert.TextEqualsAsync(() => Page.ResultText.Text, "LCM = 24");
            await UiAssert.TextContainsAsync(() => Page.ModeLabel.Text, "Mode: LCM");
            await UiAssert.TextContainsAsync(() => Page.ModeLabel.Text, "Absolute: On");
            await UiAssert.TextEqualsAsync(() => Page.ErrorText.Text, string.Empty);
            await UiAssert.NumberAtLeastAsync(() => Page.HistoryList.Items.Length, initialHistoryItems + 1);
        }
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Calculate_Min_RespectsAbsoluteCheckbox()
    {
        Page
            .SelectComboItem(p => p.OperationCombo, "MIN")
            .SetChecked(p => p.UseAbsoluteValuesCheck, false)
            .EnterText(p => p.NumbersInput, "-10 2 5")
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameEquals(p => p.ResultText, "MIN = -10");

        using (Assert.Multiple())
        {
            await UiAssert.TextEqualsAsync(() => Page.ResultText.Text, "MIN = -10");
            await UiAssert.TextContainsAsync(() => Page.ModeLabel.Text, "Mode: MIN");
            await UiAssert.TextContainsAsync(() => Page.ModeLabel.Text, "Absolute: Off");
        }

        Page
            .SetChecked(p => p.UseAbsoluteValuesCheck, true)
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameEquals(p => p.ResultText, "MIN = 2");

        await UiAssert.TextEqualsAsync(() => Page.ResultText.Text, "MIN = 2");
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task Calculate_InvalidInput_ShowsError_AndDoesNotPolluteHistory()
    {
        var initialHistoryItems = Page.HistoryList.Items.Length;

        Page
            .SelectComboItem(p => p.OperationCombo, "GCD")
            .EnterText(p => p.NumbersInput, "48 x 30")
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameContains(p => p.ErrorText, "Invalid integer: x");

        using (Assert.Multiple())
        {
            await UiAssert.TextContainsAsync(() => Page.ErrorText.Text, "Invalid integer: x");
            await UiAssert.TextEqualsAsync(() => Page.ResultText.Text, string.Empty);
        }

        await Assert.That(Page.HistoryList.Items.Length).IsEqualTo(initialHistoryItems);
    }

    [Test]
    [NotInParallel(DesktopUiConstraint)]
    public async Task FilterHistory_ByText_ShowsOnlyMatchingItems()
    {
        Page
            .EnterText(p => p.NumbersInput, "48 18 30")
            .SelectComboItem(p => p.OperationCombo, "GCD")
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameEquals(p => p.ResultText, "GCD = 6");

        Page
            .EnterText(p => p.NumbersInput, "4 8 12")
            .SelectComboItem(p => p.OperationCombo, "LCM")
            .SetChecked(p => p.UseAbsoluteValuesCheck, true)
            .ClickButton(p => p.CalculateButton)
            .WaitUntilNameEquals(p => p.ResultText, "LCM = 24");

        Page
            .EnterText(p => p.HistoryFilterInput, "LCM")
            .ClickButton(p => p.ApplyFilterButton)
            .WaitUntilHasItemsAtLeast(p => p.HistoryList, 1);

        var filteredHistory = Page.HistoryList.Items
            .Select(item => item.Text ?? item.Name ?? string.Empty)
            .ToArray();

        await Assert.That(filteredHistory.Length >= 1).IsEqualTo(true);
        await Assert.That(filteredHistory.All(item => item.Contains("LCM", StringComparison.Ordinal))).IsEqualTo(true);
        await Assert.That(filteredHistory.All(item => !item.Contains("GCD", StringComparison.Ordinal))).IsEqualTo(true);

        Page
            .EnterText(p => p.HistoryFilterInput, string.Empty)
            .ClickButton(p => p.ApplyFilterButton);

        await UiAssert.NumberAtLeastAsync(() => Page.HistoryList.Items.Length, 2);

        Page.ClickButton(p => p.ClearHistoryButton);

        await Assert.That(Page.HistoryList.Items.Length).IsEqualTo(0);
    }
}
