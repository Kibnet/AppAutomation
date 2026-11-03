using AppAutomation.Abstractions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;

namespace AppAutomation.FlaUI.Automation;

public sealed partial class FlaUiControlResolver
{
    private sealed partial class FlaUiVisualGridControl
    {
        public string ReadHeaderText(GridHeaderSelector selector, int timeoutMs = 5000,
            GridHeaderContextDefinition? context = null)
        {
            ArgumentNullException.ThrowIfNull(selector);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            context ??= new GridHeaderContextDefinition();
            var budget = UiOperationTimeoutBudget.Start(timeoutMs, "read grid header");
            var candidates = ReadHeaderMetadata(selector, context, budget);
            if (candidates.Count == 0 && selector.Kind != GridHeaderKind.Column)
                throw new InvalidOperationException($"Grid '{AutomationId}' header '{selector}' does not publish its complete UIA address. "
                    + "Expose the semantic address on the real header peer and declare HeaderContext.RuntimeAddressProperty; "
                    + "in-process group metadata alone cannot identify a UIA target.");
            return GridHeaderMetadataReader.ResolveText(candidates, selector, AutomationId);
        }

        public GridHeaderSelector CaptureFirstGroup(IReadOnlyList<string> fields, int timeoutMs = 5000,
            GridHeaderContextDefinition? context = null)
        {
            ArgumentNullException.ThrowIfNull(fields);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var budget = UiOperationTimeoutBudget.Start(timeoutMs, "capture available grid group");
            return UiWait.Until(() => GridHeaderMetadataReader.FirstGroup(
                    ReadHeaderMetadata(null, context ?? new GridHeaderContextDefinition(), budget), fields, AutomationId),
                static address => address is not null,
                new UiWaitOptions { Timeout = budget.Remaining, PollInterval = TimeSpan.FromMilliseconds(25) },
                $"Grid '{AutomationId}' did not expose an available group for '{string.Join(" / ", fields)}'.")!;
        }

        private IReadOnlyList<GridHeaderMetadata> ReadHeaderMetadata(GridHeaderSelector? selector,
            GridHeaderContextDefinition context, UiOperationTimeoutBudget budget)
        {
            var (root, elements) = ReadTransientUiaLookup(() =>
            {
                var currentRoot = FindGridRoot(budget)
                    ?? throw new InvalidOperationException($"Grid '{AutomationId}' was not found.");
                ConditionBase condition = selector?.Kind == GridHeaderKind.Column
                    ? currentRoot.Automation.ConditionFactory.ByControlType(ControlType.HeaderItem)
                    : new BoolCondition(false);
                if (context.RuntimeAddressProperty is { } property)
                {
                    var properties = currentRoot.Automation.PropertyLibrary.Element;
                    var addressProperty = property switch
                    {
                        GridRowAutomationProperty.AutomationId => properties.AutomationId,
                        GridRowAutomationProperty.ItemStatus => properties.ItemStatus,
                        GridRowAutomationProperty.HelpText => properties.HelpText,
                        _ => throw new InvalidOperationException($"Unsupported header address property '{property}'.")
                    };
                    ConditionBase published = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)
                        ? new PropertyCondition(addressProperty, GridHeaderSelector.AutomationValuePrefix, PropertyConditionFlags.MatchSubstring)
                        : new NotCondition(new PropertyCondition(addressProperty, string.Empty));
                    condition = condition.Or(published);
                }
                _ = budget.RemainingMilliseconds;
                return (currentRoot, currentRoot.FindAllDescendants(condition));
            }, () => budget.Remaining, () => _gridRoot = null);
            var candidates = new List<GridHeaderMetadata>();
            var captionCandidates = new List<GridHeaderMetadata>();
            foreach (var element in elements)
            {
                _ = budget.RemainingMilliseconds;
                if (selector is null && !HasVisibleBounds(element)) continue;
                // Не переносим in-process reflection в UIA и не угадываем родителя по соседней data-row.
                if (context.RuntimeAddressProperty is { } property)
                {
                    var serialized = ReadHeaderProperty(element, property);
                    var address = GridHeaderSelector.FromAutomationValue(serialized);
                    if (address is not null)
                    {
                        if (BelongsToHeaderGrid(element, root))
                            candidates.Add(new(element, address, TryRead(() => element.Name), null));
                        continue;
                    }
                }
                if (selector?.Kind != GridHeaderKind.Column
                    || TryRead(() => element.ControlType) != ControlType.HeaderItem) continue;
                var field = selector.Path[0].Name;
                var name = TryRead(() => element.Name);
                var caption = context.RuntimeColumnNames?.GetValueOrDefault(field) ?? field;
                if (!BelongsToHeaderGrid(element, root)) continue;
                if (string.Equals(TryRead(() => element.AutomationId), field, StringComparison.Ordinal))
                    candidates.Add(new(element, selector, name, null));
                else if (string.Equals(name, caption, StringComparison.Ordinal))
                    captionCandidates.Add(new(element, selector, name, null));
            }
            if (selector is not null && !candidates.Any(candidate => selector.Equals(candidate.Selector)))
                candidates.AddRange(captionCandidates);
            _ = budget.RemainingMilliseconds;
            return candidates;
        }

        private static bool BelongsToHeaderGrid(AutomationElement element, AutomationElement root)
        {
            for (var parent = TryRead(() => element.Parent); parent is not null; parent = TryRead(() => parent.Parent))
            {
                if (parent.Equals(root)) return true;
                if (TryRead(() => parent.ControlType) is ControlType.DataGrid or ControlType.Table) return false;
            }
            return false;
        }

        private static string? ReadHeaderProperty(AutomationElement element, GridRowAutomationProperty property) => property switch
        {
            GridRowAutomationProperty.AutomationId => TryRead(() => element.AutomationId),
            GridRowAutomationProperty.Name => throw new InvalidOperationException("The header Name is reserved for displayed text, not a serialized address."),
            GridRowAutomationProperty.ItemStatus => TryRead(() => element.FrameworkAutomationElement.ItemStatus.Value),
            GridRowAutomationProperty.HelpText => TryRead(() => element.FrameworkAutomationElement.HelpText.Value),
            _ => throw new InvalidOperationException($"Unsupported header property '{property}'.")
        };
    }
}
