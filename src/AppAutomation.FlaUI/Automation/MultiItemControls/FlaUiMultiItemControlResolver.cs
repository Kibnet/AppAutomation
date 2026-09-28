using AppAutomation.Abstractions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace AppAutomation.FlaUI.Automation;

public sealed partial class FlaUiControlResolver : IMultiItemControlRuntimeResolver
{
    public MultiItemControlState ReadMultiItemCollectionState(MultiItemControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var matches = FindCollectionMatches(definition).Take(2).ToArray();
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

        var isAvailable = TryRead(() => matches[0].IsAvailable && !matches[0].IsOffscreen);
        return new MultiItemControlState(
            isAvailable,
            isAvailable && TryRead(() => matches[0].IsEnabled));
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
            () => ResolveMultiItemSpinnerOnce(definition, itemKey));
    }

    private ISpinnerControl ResolveMultiItemSpinnerOnce(
        MultiItemControlDefinition definition,
        string itemKey)
    {
        var collection = ResolveUnique(
            FindCollectionMatches(definition),
            CreateRuntimeCollectionLocator(definition),
            definition,
            itemKey,
            "CollectionRoot");
        var itemLocator = definition.ItemContainerLocator!;
        var items = FindScopedMatches(collection, itemLocator).ToArray();
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
            .Select(item => new MultiItemKeyCandidate<AutomationElement>(
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
            FindScopedMatches(selectedItem, parts.Root),
            parts.Root,
            definition,
            itemKey,
            "SpinnerRoot");
        var input = ResolveUnique(
            FindScopedMatches(root, parts.Input),
            parts.Input,
            definition,
            itemKey,
            "SpinnerInput");
        var inputTextBox = TryRead(input.AsTextBox)
            ?? throw Failure(
                definition,
                itemKey,
                "SpinnerInput",
                parts.Input,
                1,
                $"Resolved control type '{TryRead(() => input.ControlType)}' is not a writable Edit control.",
                isTransient: false);

        AutomationElement? commitTarget = null;
        if (parts.CommitTarget is not null)
        {
            var commitRoot = parts.CommitTarget.Scope switch
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
                FindScopedMatches(commitRoot, parts.CommitTarget),
                parts.CommitTarget,
                definition,
                itemKey,
                "CommitTarget");
        }

        return new SpinnerTextControl(
            definition.PagePropertyName,
            () => TryRead(() => inputTextBox.Name) ?? string.Empty,
            () => TryRead(() => inputTextBox.IsEnabled),
            () => TryRead(() => inputTextBox.IsAvailable && !inputTextBox.IsOffscreen),
            () => TryRead(() => inputTextBox.Text),
            text =>
            {
                inputTextBox.Focus();
                if (parts.UseKeyboardInput)
                {
                    Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                    Keyboard.Type(text);
                }
                else
                {
                    new FlaUiTextBoxControl(inputTextBox).Enter(text);
                }

                if (commitTarget is not null)
                {
                    commitTarget.Focus();
                }
                else
                {
                    Keyboard.Press(VirtualKeyShort.RETURN);
                }
            });
    }

    private IEnumerable<AutomationElement> FindCollectionMatches(MultiItemControlDefinition definition)
    {
        var locator = CreateRuntimeCollectionLocator(definition);
        return DistinctElements(GetProcessSearchRoots().SelectMany(root => FindScopedMatches(root, locator)));
    }

    private IEnumerable<AutomationElement> FindScopedMatches(
        AutomationElement root,
        MultiItemRelativeLocator locator)
    {
        var candidates = new List<AutomationElement>();
        if (Matches(root, locator))
        {
            candidates.Add(root);
        }

        candidates.AddRange(
            TryRead(() => root.FindAllDescendants(CreateCondition(locator.LocatorValue, locator.LocatorKind)))
            ?? Array.Empty<AutomationElement>());
        if (locator.FallbackToName && locator.LocatorKind != UiLocatorKind.Name)
        {
            candidates.AddRange(
                TryRead(() => root.FindAllDescendants(_conditionFactory.ByName(locator.LocatorValue)))
                ?? Array.Empty<AutomationElement>());
        }

        return DistinctElements(candidates.Where(candidate => TryRead(() => candidate.IsAvailable)));
    }

    private string? ReadItemKey(
        AutomationElement item,
        MultiItemControlDefinition definition,
        string requestedKey)
    {
        var keyDefinition = definition.ItemKey!;
        var source = keyDefinition.Source is null
            ? item
            : ResolveUnique(
                FindScopedMatches(item, keyDefinition.Source),
                keyDefinition.Source,
                definition,
                requestedKey,
                "ItemKey");
        return ReadAutomationValue(source, keyDefinition.Property)?.Trim();
    }

    private static string? ReadAutomationValue(
        AutomationElement element,
        UiAutomationValueProperty property) =>
        property switch
        {
            UiAutomationValueProperty.AutomationId => TryRead(() => element.AutomationId),
            UiAutomationValueProperty.Name => TryRead(() => element.Name),
            UiAutomationValueProperty.HelpText => TryRead(() => element.HelpText),
            UiAutomationValueProperty.ItemStatus => TryRead(() => element.ItemStatus),
            _ => throw new InvalidOperationException($"Unsupported automation property '{property}'.")
        };

    private static AutomationElement ResolveUnique(
        IEnumerable<AutomationElement> candidates,
        MultiItemRelativeLocator locator,
        MultiItemControlDefinition definition,
        string itemKey,
        string partName)
    {
        var matches = candidates.Take(2).ToArray();
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

    private static bool Matches(AutomationElement candidate, MultiItemRelativeLocator locator)
    {
        var primary = locator.LocatorKind switch
        {
            UiLocatorKind.AutomationId => TryRead(() => candidate.AutomationId),
            UiLocatorKind.Name => TryRead(() => candidate.Name),
            _ => null
        };
        return string.Equals(primary, locator.LocatorValue, StringComparison.Ordinal)
            || (locator.FallbackToName
                && locator.LocatorKind != UiLocatorKind.Name
                && string.Equals(TryRead(() => candidate.Name), locator.LocatorValue, StringComparison.Ordinal));
    }

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
