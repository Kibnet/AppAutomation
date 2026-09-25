using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using AvaloniaControl = Avalonia.Controls.Control;
using AvaloniaTextBox = Avalonia.Controls.TextBox;

namespace AppAutomation.Avalonia.Headless.Automation;

public sealed partial class HeadlessControlResolver : IMultiItemControlRuntimeResolver
{
    public MultiItemControlState ReadMultiItemCollectionState(MultiItemControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
        {
            var matches = EnumerateSelfAndDescendants(_window.Native)
                .Where(candidate => HasLocator(
                    candidate,
                    definition.RuntimeLocatorKind,
                    definition.RuntimeLocatorValue,
                    definition.RuntimeFallbackToName))
                .Take(2)
                .ToArray();
            if (matches.Length == 0)
            {
                return new MultiItemControlState(IsAvailable: false, IsEnabled: false);
            }

            if (matches.Length > 1)
            {
                throw Failure(
                    definition,
                    string.Empty,
                    "CollectionRoot",
                    CreateRuntimeCollectionLocator(definition),
                    matches.Length,
                    "Configured part is ambiguous.",
                    isTransient: false);
            }

            var collection = matches[0];
            var isAvailable = collection.IsAttachedToVisualTree() && collection.IsEffectivelyVisible;
            return new MultiItemControlState(
                isAvailable,
                isAvailable && collection.IsEffectivelyEnabled);
        });
    }

    public ISpinnerControl ResolveMultiItemSpinner(
        MultiItemControlDefinition definition,
        string itemKey,
        int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemKey);
        return MultiItemResolutionRules.ResolveWithRetry(
            definition,
            itemKey,
            timeoutMs,
            () => AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(
                () => ResolveMultiItemSpinnerOnce(definition, itemKey)));
    }

    private ISpinnerControl ResolveMultiItemSpinnerOnce(
        MultiItemControlDefinition definition,
        string itemKey)
    {
        var collection = ResolveUnique(
            EnumerateSelfAndDescendants(_window.Native),
            CreateRuntimeCollectionLocator(definition),
            definition,
            itemKey,
            "CollectionRoot");
        var itemLocator = definition.ItemContainerLocator!;
        var items = EnumerateSelfAndDescendants(collection)
            .Where(candidate => Matches(candidate, itemLocator))
            .Distinct()
            .ToArray();
        if (items.Length == 0)
        {
            throw Failure(
                definition,
                itemKey,
                "ItemContainer",
                itemLocator,
                0,
                "No repeated item containers were found inside the collection root.",
                isTransient: true);
        }

        var keyedItems = items
            .Select(item => new MultiItemKeyCandidate<AvaloniaControl>(
                item,
                ReadItemKey(item, definition, itemKey)))
            .ToArray();
        var itemResolution = MultiItemResolutionRules.ResolveRequestedItem(keyedItems, itemKey);
        if (!itemResolution.Success)
        {
            throw Failure(
                definition,
                itemKey,
                "ItemKey",
                definition.ItemKey!.Source,
                itemResolution.MatchCount,
                itemResolution.Error!,
                itemResolution.IsTransient);
        }

        var selectedItem = itemResolution.Item!;
        var parts = definition.SpinnerParts!;
        var root = ResolveUnique(
            EnumerateSelfAndDescendants(selectedItem),
            parts.Root,
            definition,
            itemKey,
            "SpinnerRoot");
        var input = ResolveUnique(
            EnumerateSelfAndDescendants(root),
            parts.Input,
            definition,
            itemKey,
            "SpinnerInput");
        if (input is not AvaloniaTextBox textBox)
        {
            throw Failure(
                definition,
                itemKey,
                "SpinnerInput",
                parts.Input,
                1,
                $"Resolved control type '{input.GetType().FullName}' is not a writable TextBox.",
                isTransient: false);
        }

        AvaloniaControl? commitTarget = null;
        if (parts.CommitTarget is not null)
        {
            var commitScope = parts.CommitTarget.Scope switch
            {
                MultiItemRelativeLocatorScope.CollectionRoot => collection,
                MultiItemRelativeLocatorScope.ItemRoot => selectedItem,
                _ => throw Failure(
                    definition,
                    itemKey,
                    "CommitTarget",
                    parts.CommitTarget,
                    0,
                    "Unsupported commit-target scope.",
                    isTransient: false)
            };
            commitTarget = ResolveUnique(
                EnumerateSelfAndDescendants(commitScope),
                parts.CommitTarget,
                definition,
                itemKey,
                "CommitTarget");
        }

        return new SpinnerTextControl(
            definition.PagePropertyName,
            () => AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(
                () => AutomationProperties.GetName(textBox) ?? string.Empty),
            () => AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(
                () => textBox.IsEffectivelyEnabled),
            () => AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(
                () => textBox.IsAttachedToVisualTree() && textBox.IsEffectivelyVisible),
            () => AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(
                () => textBox.Text),
            text => AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
            {
                textBox.Focus();
                textBox.Text = text;
                textBox.Dispatcher.RunJobs();
                if (commitTarget is not null)
                {
                    commitTarget.Focus();
                }
                else
                {
                    textBox.RaiseEvent(new KeyEventArgs
                    {
                        RoutedEvent = InputElement.KeyDownEvent,
                        Key = Key.Enter,
                        Source = textBox
                    });
                    textBox.RaiseEvent(new KeyEventArgs
                    {
                        RoutedEvent = InputElement.KeyUpEvent,
                        Key = Key.Enter,
                        Source = textBox
                    });
                }

                textBox.Dispatcher.RunJobs();
                return true;
            }));
    }

    private static string? ReadItemKey(
        AvaloniaControl item,
        MultiItemControlDefinition definition,
        string requestedKey)
    {
        var keyDefinition = definition.ItemKey!;
        var source = keyDefinition.Source is null
            ? item
            : ResolveUnique(
                EnumerateSelfAndDescendants(item),
                keyDefinition.Source,
                definition,
                requestedKey,
                "ItemKey");
        return ReadAutomationValue(source, keyDefinition.Property)?.Trim();
    }

    private static string? ReadAutomationValue(
        AvaloniaControl control,
        UiAutomationValueProperty property) =>
        property switch
        {
            UiAutomationValueProperty.AutomationId => AutomationProperties.GetAutomationId(control),
            UiAutomationValueProperty.Name => AutomationProperties.GetName(control),
            UiAutomationValueProperty.HelpText => AutomationProperties.GetHelpText(control),
            UiAutomationValueProperty.ItemStatus => AutomationProperties.GetItemStatus(control),
            _ => throw new InvalidOperationException($"Unsupported automation property '{property}'.")
        };

    private static AvaloniaControl ResolveUnique(
        IEnumerable<AvaloniaControl> candidates,
        MultiItemRelativeLocator locator,
        MultiItemControlDefinition definition,
        string itemKey,
        string partName)
    {
        var matches = candidates
            .Where(candidate => Matches(candidate, locator))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            throw Failure(
                definition,
                itemKey,
                partName,
                locator,
                matches.Length,
                matches.Length == 0 ? "Configured part was not found." : "Configured part is ambiguous.",
                isTransient: matches.Length == 0);
        }

        return matches[0];
    }

    private static bool Matches(AvaloniaControl candidate, MultiItemRelativeLocator locator) =>
        HasLocator(candidate, locator.LocatorKind, locator.LocatorValue, locator.FallbackToName);

    private static bool HasLocator(
        AvaloniaControl control,
        UiLocatorKind locatorKind,
        string locatorValue,
        bool fallbackToName)
    {
        var primary = locatorKind switch
        {
            UiLocatorKind.AutomationId => AutomationProperties.GetAutomationId(control),
            UiLocatorKind.Name => AutomationProperties.GetName(control),
            _ => null
        };
        return string.Equals(primary, locatorValue, StringComparison.Ordinal)
            || (fallbackToName
                && locatorKind != UiLocatorKind.Name
                && string.Equals(AutomationProperties.GetName(control), locatorValue, StringComparison.Ordinal));
    }

    private static IEnumerable<AvaloniaControl> EnumerateSelfAndDescendants(AvaloniaControl root) =>
        root.GetLogicalDescendants()
            .OfType<AvaloniaControl>()
            .Concat(root.GetVisualDescendants().OfType<AvaloniaControl>())
            .Prepend(root)
            .Distinct<AvaloniaControl>(ReferenceEqualityComparer.Instance);

    private static MultiItemResolutionException Failure(
        MultiItemControlDefinition definition,
        string itemKey,
        string partName,
        MultiItemRelativeLocator? locator,
        int matchCount,
        string reason,
        bool isTransient) =>
        MultiItemResolutionRules.Failure(
            definition,
            itemKey,
            partName,
            locator,
            matchCount,
            reason,
            isTransient);

    private static MultiItemRelativeLocator CreateRuntimeCollectionLocator(MultiItemControlDefinition definition) =>
        new(
            definition.RuntimeLocatorValue,
            MultiItemRelativeLocatorScope.CollectionRoot,
            definition.RuntimeLocatorKind,
            definition.RuntimeFallbackToName);

}
