using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using TUnit.Assertions;
using TUnit.Core;
using static AppAutomation.TestHost.Avalonia.Tests.HeadlessTestRuntime;

namespace AppAutomation.TestHost.Avalonia.Tests;

public sealed class MultiItemControlRuntimeTests
{
    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessMultiItemSpinner_UsesStableKeyAfterItemsAreReordered()
    {
        using var headless = StartHeadlessRuntime();
        var context = HeadlessRuntime.Dispatch(() => CreateMultiItemSpinnerWindow());
        var page = new MultiItemSpinnerPage(
            new HeadlessControlResolver(context.Window)
                .WithMultiItemControls(CreateMultiItemSpinnerCatalog()));

        await Assert.That(page.UnitQuantityEditors.IsAvailable).IsTrue();
        HeadlessRuntime.Dispatch(() => context.Collection.IsVisible = false);
        await Assert.That(page.UnitQuantityEditors.IsAvailable).IsFalse();
        HeadlessRuntime.Dispatch(() => context.Collection.IsVisible = true);
        HeadlessRuntime.Dispatch(() => context.Collection.IsEnabled = false);
        await Assert.That(page.UnitQuantityEditors.IsEnabled).IsFalse();
        HeadlessRuntime.Dispatch(() => context.Collection.IsEnabled = true);
        await Assert.That(page.UnitQuantityEditors.IsEnabled).IsTrue();

        HeadlessRuntime.Dispatch(() => context.Items[0].IsVisible = false);
        var hiddenSpinner = page.UnitQuantityEditors.ResolveSpinner("unit-a", timeoutMs: 5000);
        await Assert.That(((IUiControlAvailability)hiddenSpinner).IsAvailable).IsFalse();
        HeadlessRuntime.Dispatch(() => context.Items[0].IsVisible = true);

        var delayedItem = context.Items[1];
        HeadlessRuntime.Dispatch(() => context.Collection.Children.Remove(delayedItem));
        var materialized = Task.Run(async () =>
        {
            await Task.Delay(100);
            HeadlessRuntime.Dispatch(() => context.Collection.Children.Insert(1, delayedItem));
        });

        page.SetMultiItemSpinnerValue(
            static candidate => candidate.UnitQuantityEditors,
            "unit-b",
            12.5);
        await materialized.WaitAsync(TimeSpan.FromSeconds(2));

        var staleInput = context.Inputs[1];
        HeadlessRuntime.Dispatch(() => staleInput.IsEnabled = false);
        var replaced = Task.Run(async () =>
        {
            await Task.Delay(100);
            HeadlessRuntime.Dispatch(() =>
            {
                var replacement = CreateMultiItemSpinnerItem("unit-b");
                context.Collection.Children.Remove(context.Items[1]);
                context.Collection.Children.Insert(1, replacement.Item);
                context.Items[1] = replacement.Item;
                context.Inputs[1] = replacement.Input;
            });
        });
        page.SetMultiItemSpinnerValue(
            static candidate => candidate.UnitQuantityEditors,
            "unit-b",
            14.5);
        await replaced.WaitAsync(TimeSpan.FromSeconds(2));
        HeadlessRuntime.Dispatch(() =>
        {
            context.Collection.Children.Remove(context.Items[2]);
            context.Collection.Children.Insert(0, context.Items[2]);
        });
        page.SetMultiItemSpinnerValue(
            static candidate => candidate.UnitQuantityEditors,
            "unit-b",
            18);

        using (Assert.Multiple())
        {
            await Assert.That(HeadlessRuntime.Dispatch(() => context.Inputs[0].Text)).IsEqualTo("0");
            await Assert.That(HeadlessRuntime.Dispatch(() => context.Inputs[1].Text)).IsEqualTo("18");
            await Assert.That(HeadlessRuntime.Dispatch(() => context.Inputs[2].Text)).IsEqualTo("0");
            await Assert.That(HeadlessRuntime.Dispatch(() => staleInput.Text)).IsEqualTo("12.5");
        }
    }

    [Test]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessMultiItemSpinner_RejectsDuplicateAndMissingStableKeys()
    {
        using var headless = StartHeadlessRuntime();
        var duplicateContext = HeadlessRuntime.Dispatch(() =>
            CreateMultiItemSpinnerWindow("unit-a", "unit-b", "unit-b"));
        var duplicatePage = new MultiItemSpinnerPage(
            new HeadlessControlResolver(duplicateContext.Window)
                .WithMultiItemControls(CreateMultiItemSpinnerCatalog()));

        var duplicateError = Assert.Throws<InvalidOperationException>(() =>
            duplicatePage.SetMultiItemSpinnerValue(
                static candidate => candidate.UnitQuantityEditors,
                "unit-b",
                12));
        HeadlessRuntime.Dispatch(duplicateContext.Window.Close);

        var missingContext = HeadlessRuntime.Dispatch(() =>
            CreateMultiItemSpinnerWindow("unit-a", null, "unit-c"));
        var missingPage = new MultiItemSpinnerPage(
            new HeadlessControlResolver(missingContext.Window)
                .WithMultiItemControls(CreateMultiItemSpinnerCatalog()));
        var missingError = Assert.Throws<InvalidOperationException>(() =>
            missingPage.SetMultiItemSpinnerValue(
                static candidate => candidate.UnitQuantityEditors,
                "unit-c",
                12));

        using (Assert.Multiple())
        {
            await Assert.That(duplicateError.Message).Contains("Stable item key 'unit-b'");
            await Assert.That(duplicateError.Message).Contains("multiple item containers");
            await Assert.That(missingError.Message).Contains("empty stable key");
        }
    }

    [Test]
    [Arguments(MultiItemPartFailure.MissingRoot, "SpinnerRoot", "matches=0")]
    [Arguments(MultiItemPartFailure.AmbiguousRoot, "SpinnerRoot", "matches=2")]
    [Arguments(MultiItemPartFailure.MissingInput, "SpinnerInput", "matches=0")]
    [Arguments(MultiItemPartFailure.AmbiguousInput, "SpinnerInput", "matches=2")]
    [Arguments(MultiItemPartFailure.MissingCommitTarget, "CommitTarget", "matches=0")]
    [Arguments(MultiItemPartFailure.AmbiguousCommitTarget, "CommitTarget", "matches=2")]
    [NotInParallel(HeadlessRuntimeConstraint)]
    public async Task HeadlessMultiItemSpinner_ReportsScopedPartErrors(
        MultiItemPartFailure failure,
        string expectedPart,
        string expectedMatchCount)
    {
        using var headless = StartHeadlessRuntime();
        var window = HeadlessRuntime.Dispatch(() => CreateMultiItemPartFailureWindow(failure));
        var requiresCommitTarget = failure is MultiItemPartFailure.MissingCommitTarget
            or MultiItemPartFailure.AmbiguousCommitTarget;
        var page = new MultiItemSpinnerPage(
            new HeadlessControlResolver(window)
                .WithMultiItemControls(CreateMultiItemSpinnerCatalog(
                    requiresCommitTarget
                        ? MultiItemRelativeLocator.ByAutomationId(
                            "QuantityCommit",
                            MultiItemRelativeLocatorScope.ItemRoot)
                        : null)));

        Exception error = failure is MultiItemPartFailure.MissingRoot
            or MultiItemPartFailure.MissingInput
            or MultiItemPartFailure.MissingCommitTarget
                ? Assert.Throws<TimeoutException>(() =>
                    page.SetMultiItemSpinnerValue(
                        static candidate => candidate.UnitQuantityEditors,
                        "unit-a",
                        12,
                        timeoutMs: 150))
                : Assert.Throws<InvalidOperationException>(() =>
                    page.SetMultiItemSpinnerValue(
                        static candidate => candidate.UnitQuantityEditors,
                        "unit-a",
                        12,
                        timeoutMs: 150));

        using (Assert.Multiple())
        {
            await Assert.That(error.Message).Contains(expectedPart);
            await Assert.That(error.Message).Contains(expectedMatchCount);
            await Assert.That(error.Message).Contains("unit-a");
        }
    }

    private static MultiItemSpinnerWindowContext CreateMultiItemSpinnerWindow(params string?[] keys)
    {
        if (keys.Length == 0)
        {
            keys = ["unit-a", "unit-b", "unit-c"];
        }

        var collection = new StackPanel();
        AutomationProperties.SetAutomationId(collection, "UnitQuantityCollection");
        var items = new List<Border>();
        var inputs = new List<TextBox>();
        foreach (var key in keys)
        {
            var created = CreateMultiItemSpinnerItem(key);
            collection.Children.Add(created.Item);
            items.Add(created.Item);
            inputs.Add(created.Input);
        }

        var window = new Window { Content = collection };
        window.Show();
        window.UpdateLayout();
        return new MultiItemSpinnerWindowContext(window, collection, items, inputs);
    }

    private static MultiItemSpinnerItem CreateMultiItemSpinnerItem(string? key)
    {
        var input = new TextBox { Text = "0" };
        AutomationProperties.SetAutomationId(input, "QuantityEditor_Input");
        var spinner = new Border { Child = input };
        AutomationProperties.SetAutomationId(spinner, "QuantityEditor");
        var item = new Border { Child = spinner };
        AutomationProperties.SetAutomationId(item, "UnitQuantityItem");
        AutomationProperties.SetItemStatus(item, key);
        return new MultiItemSpinnerItem(item, input);
    }

    private static Window CreateMultiItemPartFailureWindow(MultiItemPartFailure failure)
    {
        var collection = new StackPanel();
        AutomationProperties.SetAutomationId(collection, "UnitQuantityCollection");
        var item = new StackPanel();
        AutomationProperties.SetAutomationId(item, "UnitQuantityItem");
        AutomationProperties.SetItemStatus(item, "unit-a");
        collection.Children.Add(item);

        var rootCount = failure == MultiItemPartFailure.AmbiguousRoot ? 2 : 1;
        for (var rootIndex = 0; rootIndex < rootCount; rootIndex++)
        {
            var spinner = new StackPanel();
            AutomationProperties.SetAutomationId(
                spinner,
                failure == MultiItemPartFailure.MissingRoot ? "OtherEditor" : "QuantityEditor");
            var inputCount = failure == MultiItemPartFailure.AmbiguousInput ? 2 : 1;
            for (var inputIndex = 0; inputIndex < inputCount; inputIndex++)
            {
                var input = new TextBox { Text = "0" };
                AutomationProperties.SetAutomationId(
                    input,
                    failure == MultiItemPartFailure.MissingInput ? "OtherInput" : "QuantityEditor_Input");
                spinner.Children.Add(input);
            }

            item.Children.Add(spinner);
        }

        var commitCount = failure == MultiItemPartFailure.AmbiguousCommitTarget ? 2 : 0;
        for (var commitIndex = 0; commitIndex < commitCount; commitIndex++)
        {
            var commit = new Button();
            AutomationProperties.SetAutomationId(commit, "QuantityCommit");
            item.Children.Add(commit);
        }

        var window = new Window { Content = collection };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static MultiItemControlCatalog CreateMultiItemSpinnerCatalog(
        MultiItemRelativeLocator? commitTarget = null) =>
        new MultiItemControlCatalog().Add(
            MultiItemControlDefinition.ByAutomationIds(
                    "UnitQuantityEditors",
                    "UnitQuantityCollection",
                    "UnitQuantityCollection")
                .WithItems(
                    MultiItemRelativeLocator.ByAutomationId(
                        "UnitQuantityItem",
                        MultiItemRelativeLocatorScope.CollectionRoot),
                    RepeatedItemKeyDefinition.FromItem(UiAutomationValueProperty.ItemStatus))
                .WithSpinner(new MultiItemSpinnerParts(
                    MultiItemRelativeLocator.ByAutomationId(
                        "QuantityEditor",
                        MultiItemRelativeLocatorScope.ItemRoot),
                    MultiItemRelativeLocator.ByAutomationId(
                        "QuantityEditor_Input",
                        MultiItemRelativeLocatorScope.ControlRoot),
                    commitTarget)));

    private sealed record MultiItemSpinnerWindowContext(
        Window Window,
        StackPanel Collection,
        List<Border> Items,
        List<TextBox> Inputs);

    private sealed record MultiItemSpinnerItem(Border Item, TextBox Input);

    public enum MultiItemPartFailure
    {
        MissingRoot,
        AmbiguousRoot,
        MissingInput,
        AmbiguousInput,
        MissingCommitTarget,
        AmbiguousCommitTarget
    }

    private sealed class MultiItemSpinnerPage : UiPage
    {
        public MultiItemSpinnerPage(IUiControlResolver resolver)
            : base(resolver)
        {
        }

        public IMultiItemControlCollection UnitQuantityEditors =>
            Resolve<IMultiItemControlCollection>(MultiItemSpinnerPageDefinitions.UnitQuantityEditors);
    }

    private static class MultiItemSpinnerPageDefinitions
    {
        public static UiControlDefinition UnitQuantityEditors { get; } = new(
            "UnitQuantityEditors",
            UiControlType.MultiItemControlCollection,
            "UnitQuantityCollection");
    }

}
