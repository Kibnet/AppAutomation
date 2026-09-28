using System.Globalization;
using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace AppAutomation.Recorder.Avalonia;

internal sealed partial class RecorderStepFactory
{
    public bool AreSameMultiItemSpinner(Control first, Control second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        foreach (var definition in _options.MultiItemControls)
        {
            var firstContext = TryResolveCaptureContext(first, definition);
            if (!firstContext.IsControlMatch)
            {
                continue;
            }

            var secondContext = TryResolveCaptureContext(second, definition);
            if (secondContext.IsControlMatch
                && ReferenceEquals(firstContext.ItemRoot, secondContext.ItemRoot))
            {
                return true;
            }
        }

        return false;
    }

    public MultiItemSpinnerCaptureResult TryCreateMultiItemSpinnerStep(Control source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var collectionCandidates = _options.MultiItemControls
            .Select(definition => TryResolveCaptureContext(source, definition))
            .Where(static result => result.IsCollectionMatch)
            .ToArray();
        if (collectionCandidates.Length == 0)
        {
            return new MultiItemSpinnerCaptureResult(
                IsConfigured: false,
                StepCreationResult.Unsupported("Control does not belong to a configured multi-item Spinner."));
        }

        var candidates = collectionCandidates
            .Where(static result => result.IsControlMatch)
            .ToArray();
        if (candidates.Length == 0)
        {
            var invalidCandidates = collectionCandidates
                .Where(static result => result.IsConfiguredInvalid)
                .ToArray();
            if (invalidCandidates.Length > 0)
            {
                var invalid = invalidCandidates[0];
                var message = invalidCandidates.Length == 1
                    ? invalid.Error!
                    : "The selected configured input has an invalid multi-item graph in multiple definitions: "
                        + string.Join(
                            "; ",
                            invalidCandidates.Select(static candidate =>
                                $"{candidate.Definition.PagePropertyName}: {candidate.Error}"));
                return InvalidCapture(
                    invalid.Definition,
                    source,
                    itemKey: null,
                    message);
            }

            return new MultiItemSpinnerCaptureResult(
                IsConfigured: false,
                StepCreationResult.Unsupported(
                    "Control is inside a configured collection but does not match a configured SpinnerRoot/SpinnerInput."));
        }

        if (candidates.Length > 1)
        {
            var names = string.Join(", ", candidates.Select(static candidate => candidate.Definition.PagePropertyName));
            return InvalidCapture(
                candidates[0].Definition,
                source,
                itemKey: null,
                $"The selected input matches multiple multi-item definitions: {names}.");
        }

        var context = candidates[0];
        if (context.CollectionRoot is null || context.ItemRoot is null)
        {
            return InvalidCapture(
                context.Definition,
                source,
                itemKey: null,
                "The configured collection or repeated item root could not be resolved from the selected input.");
        }

        var definition = context.Definition;
        var itemRoots = EnumerateDescendantControls(context.CollectionRoot)
            .Where(candidate => MatchesExact(candidate, definition.ItemContainerLocator!))
            .ToArray();
        if (itemRoots.Length == 0)
        {
            return InvalidCapture(
                definition,
                source,
                itemKey: null,
                "The configured collection does not contain any matching item containers.");
        }

        var keyedItems = new List<MultiItemKeyCandidate<Control>>(itemRoots.Length);
        foreach (var itemRoot in itemRoots)
        {
            if (!RecorderMultiItemSpinnerGraphResolver.TryReadItemKey(
                    itemRoot,
                    definition,
                    MatchesExact,
                    out var key,
                    out var keyError))
            {
                return InvalidCapture(
                    definition,
                    source,
                    itemKey: null,
                    keyError!);
            }

            keyedItems.Add(new MultiItemKeyCandidate<Control>(itemRoot, key));
        }

        var selected = MultiItemResolutionRules.ResolveSelectedItem(keyedItems, context.ItemRoot);
        if (!selected.Success)
        {
            return InvalidCapture(
                definition,
                source,
                selected.Key,
                selected.Error!);
        }

        if (!TryReadFiniteValue(source, out var value, out var displayedValue))
        {
            return InvalidCapture(
                definition,
                source,
                selected.Key,
                $"Spinner input value '{displayedValue}' is not a finite invariant number.");
        }

        var descriptor = new RecordedControlDescriptor(
            definition.PagePropertyName,
            UiControlType.MultiItemControlCollection,
            definition.CaptureLocatorValue,
            definition.CaptureLocatorKind,
            FallbackToName: false,
            source.GetType().FullName ?? source.GetType().Name,
            Warning: null);
        var step = new RecordedStep(
            RecordedActionKind.SetMultiItemSpinnerValue,
            descriptor,
            DoubleValue: value,
            RepeatedItemKey: selected.Key);
        return new MultiItemSpinnerCaptureResult(
            IsConfigured: true,
            CreateStep(source, step));
    }

    private MultiItemCaptureContext TryResolveCaptureContext(
        Control source,
        MultiItemControlDefinition definition) =>
        RecorderMultiItemSpinnerGraphResolver.Resolve(
            source,
            definition,
            static (candidate, configuredDefinition) => HasExactLocator(
                candidate,
                configuredDefinition.CaptureLocatorKind,
                configuredDefinition.CaptureLocatorValue),
            MatchesExact);

    private MultiItemSpinnerCaptureResult InvalidCapture(
        MultiItemControlDefinition definition,
        Control source,
        string? itemKey,
        string message)
    {
        var descriptor = new RecordedControlDescriptor(
            definition.PagePropertyName,
            UiControlType.MultiItemControlCollection,
            definition.CaptureLocatorValue,
            definition.CaptureLocatorKind,
            FallbackToName: false,
            source.GetType().FullName ?? source.GetType().Name,
            Warning: null);
        var step = new RecordedStep(
            RecordedActionKind.SetMultiItemSpinnerValue,
            descriptor,
            DoubleValue: TryReadFiniteValue(source, out var value, out _) ? value : null,
            ValidationStatus: RecorderValidationStatus.Invalid,
            ValidationMessage: message,
            CanPersist: false,
            RepeatedItemKey: itemKey);
        return new MultiItemSpinnerCaptureResult(
            IsConfigured: true,
            StepCreationResult.Created(step, message));
    }

    private static bool MatchesExact(Control control, MultiItemRelativeLocator locator)
    {
        if (HasExactLocator(control, locator.LocatorKind, locator.LocatorValue))
        {
            return true;
        }

        return locator.FallbackToName
            && locator.LocatorKind != UiLocatorKind.Name
            && HasExactLocator(control, UiLocatorKind.Name, locator.LocatorValue);
    }

    private static bool TryReadFiniteValue(Control source, out double value, out string? displayedValue)
    {
        displayedValue = source switch
        {
            TextBox textBox => textBox.Text?.Trim(),
            NumericUpDown { Value: { } numericValue } => numericValue.ToString(CultureInfo.InvariantCulture),
            _ => null
        };
        var parsed = source switch
        {
            NumericUpDown { Value: { } numericValue } => decimal.ToDouble(numericValue),
            _ when double.TryParse(
                displayedValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var textValue) => textValue,
            _ => double.NaN
        };
        value = parsed;
        return double.IsFinite(parsed);
    }

}

internal enum MultiItemCaptureClassification
{
    Unrelated,
    ConfiguredInvalid,
    ConfiguredValid
}

internal sealed record MultiItemCaptureContext(
    MultiItemControlDefinition Definition,
    Control? CollectionRoot,
    Control? ItemRoot,
    Control? SpinnerRoot,
    TextBox? SpinnerInput,
    MultiItemCaptureClassification Classification,
    string? Error)
{
    public bool IsCollectionMatch => CollectionRoot is not null;

    public bool IsControlMatch => Classification == MultiItemCaptureClassification.ConfiguredValid;

    public bool IsConfiguredInvalid => Classification == MultiItemCaptureClassification.ConfiguredInvalid;
}

internal static class RecorderMultiItemSpinnerGraphResolver
{
    public static bool HasValidCollectionGraph(
        Control collection,
        MultiItemControlDefinition definition,
        string? itemKey,
        Func<Control, MultiItemRelativeLocator, bool> matchesPart)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(matchesPart);
        if (string.IsNullOrWhiteSpace(itemKey))
        {
            return false;
        }

        var itemRoots = EnumerateSelfAndDescendants(collection)
            .Where(candidate => matchesPart(candidate, definition.ItemContainerLocator!))
            .ToArray();
        var keyedItems = new List<MultiItemKeyCandidate<Control>>(itemRoots.Length);
        foreach (var itemRoot in itemRoots)
        {
            if (!TryReadItemKey(
                    itemRoot,
                    definition,
                    matchesPart,
                    out var key,
                    out _))
            {
                return false;
            }

            keyedItems.Add(new MultiItemKeyCandidate<Control>(itemRoot, key));
        }

        var selected = MultiItemResolutionRules.ResolveRequestedItem(keyedItems, itemKey);
        if (!selected.Success)
        {
            return false;
        }

        var roots = EnumerateSelfAndDescendants(selected.Item!)
            .Where(candidate => matchesPart(candidate, definition.SpinnerParts!.Root))
            .Take(2)
            .ToArray();
        if (roots.Length != 1)
        {
            return false;
        }

        var inputs = EnumerateSelfAndDescendants(roots[0])
            .Where(candidate => matchesPart(candidate, definition.SpinnerParts!.Input))
            .Take(2)
            .ToArray();
        return inputs is [TextBox];
    }

    public static bool TryReadItemKey(
        Control item,
        MultiItemControlDefinition definition,
        Func<Control, MultiItemRelativeLocator, bool> matchesPart,
        out string? key,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(matchesPart);

        var keyDefinition = definition.ItemKey!;
        var source = item;
        if (keyDefinition.Source is not null)
        {
            var sources = EnumerateSelfAndDescendants(item)
                .Where(candidate => matchesPart(candidate, keyDefinition.Source))
                .Take(2)
                .ToArray();
            if (sources.Length != 1)
            {
                key = null;
                error = sources.Length == 0
                    ? "The configured stable-key source was not found inside a repeated item."
                    : "The configured stable-key source is ambiguous inside a repeated item.";
                return false;
            }

            source = sources[0];
        }

        key = (keyDefinition.Property switch
        {
            UiAutomationValueProperty.AutomationId => AutomationProperties.GetAutomationId(source),
            UiAutomationValueProperty.Name => AutomationProperties.GetName(source),
            UiAutomationValueProperty.HelpText => AutomationProperties.GetHelpText(source),
            UiAutomationValueProperty.ItemStatus => AutomationProperties.GetItemStatus(source),
            _ => null
        })?.Trim();
        error = null;
        return true;
    }

    public static MultiItemCaptureContext Resolve(
        Control source,
        MultiItemControlDefinition definition,
        Func<Control, MultiItemControlDefinition, bool> matchesCollection,
        Func<Control, MultiItemRelativeLocator, bool> matchesPart)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(matchesCollection);
        ArgumentNullException.ThrowIfNull(matchesPart);

        var ancestors = EnumerateAncestorsAndSelf(source).ToArray();
        var collections = ancestors
            .Where(candidate => matchesCollection(candidate, definition))
            .Take(2)
            .ToArray();
        if (collections.Length == 0)
        {
            return Result(MultiItemCaptureClassification.Unrelated);
        }

        var parts = definition.SpinnerParts!;
        var matchesConfiguredSource = source switch
        {
            TextBox => matchesPart(source, parts.Input),
            NumericUpDown => matchesPart(source, parts.Root),
            _ => false
        };
        if (!matchesConfiguredSource)
        {
            return Result(
                MultiItemCaptureClassification.Unrelated,
                collectionRoot: collections[0]);
        }

        if (collections.Length != 1)
        {
            return Invalid(
                "CollectionRoot",
                collections.Length,
                "The selected configured input belongs to multiple matching collection roots.",
                collectionRoot: collections[0]);
        }

        var collection = collections[0];
        var items = ancestors
            .TakeWhile(candidate => !ReferenceEquals(candidate, collection))
            .Where(candidate => matchesPart(candidate, definition.ItemContainerLocator!))
            .Take(2)
            .ToArray();
        if (items.Length != 1)
        {
            return Invalid(
                "ItemContainer",
                items.Length,
                "The configured repeated item could not be resolved from the selected input.",
                collectionRoot: collection,
                itemRoot: items.FirstOrDefault());
        }

        var item = items[0];
        var roots = ancestors
            .TakeWhile(candidate => !ReferenceEquals(candidate, item))
            .Where(candidate => matchesPart(candidate, parts.Root))
            .Take(2)
            .ToArray();
        if (roots.Length != 1)
        {
            return Invalid(
                "SpinnerRoot",
                roots.Length,
                "The configured Spinner root could not be resolved from the selected input.",
                collection,
                item,
                roots.FirstOrDefault());
        }

        var root = roots[0];
        var inputs = EnumerateSelfAndDescendants(root)
            .Where(candidate => matchesPart(candidate, parts.Input))
            .Take(2)
            .ToArray();
        if (inputs.Length != 1)
        {
            return Invalid(
                "SpinnerInput",
                inputs.Length,
                "The configured Spinner input must resolve uniquely inside its control root.",
                collection,
                item,
                root);
        }

        if (inputs[0] is not TextBox input)
        {
            return Invalid(
                "SpinnerInput",
                1,
                $"The configured Spinner input resolved to '{inputs[0].GetType().FullName}', not TextBox.",
                collection,
                item,
                root);
        }

        var sourceMatchesResolvedControl = source switch
        {
            TextBox => ReferenceEquals(source, input),
            NumericUpDown => ReferenceEquals(source, root),
            _ => false
        };
        return sourceMatchesResolvedControl
            ? Result(
                MultiItemCaptureClassification.ConfiguredValid,
                collection,
                item,
                root,
                input)
            : Invalid(
                "SpinnerInput",
                1,
                "The selected control does not match the uniquely resolved configured Spinner parts.",
                collection,
                item,
                root,
                input);

        MultiItemCaptureContext Invalid(
            string partName,
            int matchCount,
            string reason,
            Control? collectionRoot,
            Control? itemRoot = null,
            Control? spinnerRoot = null,
            TextBox? spinnerInput = null) =>
            Result(
                MultiItemCaptureClassification.ConfiguredInvalid,
                collectionRoot,
                itemRoot,
                spinnerRoot,
                spinnerInput,
                $"{partName}: matches={matchCount}. {reason}");

        MultiItemCaptureContext Result(
            MultiItemCaptureClassification classification,
            Control? collectionRoot = null,
            Control? itemRoot = null,
            Control? spinnerRoot = null,
            TextBox? spinnerInput = null,
            string? error = null) =>
            new(
                definition,
                collectionRoot,
                itemRoot,
                spinnerRoot,
                spinnerInput,
                classification,
                error);
    }

    private static IEnumerable<Control> EnumerateAncestorsAndSelf(Control source)
    {
        var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        for (Control? current = source; current is not null;)
        {
            if (!seen.Add(current))
            {
                yield break;
            }

            yield return current;
            current = current.GetVisualParent() as Control
                ?? (current as ILogical)?.LogicalParent as Control;
        }
    }

    private static IEnumerable<Control> EnumerateSelfAndDescendants(Control root) =>
        root.GetLogicalDescendants()
            .OfType<Control>()
            .Concat(root.GetVisualDescendants().OfType<Control>())
            .Prepend(root)
            .Distinct<Control>(ReferenceEqualityComparer.Instance);
}
