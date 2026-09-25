using System.Collections;
using System.Security.Cryptography;
using System.Text;

namespace AppAutomation.Abstractions;

/// <summary>Contains repeated-control definitions shared by Recorder and runtime providers.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1710:Identifiers should have correct suffix",
    Justification = "Catalog is the public domain term shared with GridAutomationCatalog.")]
public sealed class MultiItemControlCatalog : IReadOnlyCollection<MultiItemControlDefinition>
{
    private readonly IReadOnlyList<MultiItemControlDefinition> _definitions;

    public MultiItemControlCatalog()
        : this(Array.Empty<MultiItemControlDefinition>())
    {
    }

    private MultiItemControlCatalog(IReadOnlyList<MultiItemControlDefinition> definitions)
    {
        _definitions = Validate(definitions);
        Fingerprint = ComputeFingerprint(_definitions);
    }

    public int Count => _definitions.Count;

    public string Fingerprint { get; }

    public MultiItemControlCatalog Add(MultiItemControlDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new MultiItemControlCatalog([.. _definitions, definition]);
    }

    public MultiItemControlCatalog AddRange(IEnumerable<MultiItemControlDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return new MultiItemControlCatalog([.. _definitions, .. definitions]);
    }

    public IEnumerator<MultiItemControlDefinition> GetEnumerator() => _definitions.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static IReadOnlyList<MultiItemControlDefinition> Validate(
        IReadOnlyList<MultiItemControlDefinition> definitions)
    {
        if (definitions.Any(static definition => definition is null))
        {
            throw new ArgumentException("Multi-item catalog cannot contain null definitions.", nameof(definitions));
        }

        foreach (var definition in definitions)
        {
            definition.Validate();
        }

        var duplicateProperty = definitions
            .GroupBy(static definition => definition.PagePropertyName, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateProperty is not null)
        {
            throw new ArgumentException(
                $"Multi-item Page property '{duplicateProperty.Key}' is configured more than once.",
                nameof(definitions));
        }

        var duplicateSignature = definitions
            .GroupBy(static definition => new
            {
                definition.CaptureLocatorKind,
                definition.CaptureLocatorValue,
                ItemKind = definition.ItemContainerLocator!.LocatorKind,
                ItemValue = definition.ItemContainerLocator.LocatorValue,
                RootKind = definition.SpinnerParts!.Root.LocatorKind,
                RootValue = definition.SpinnerParts.Root.LocatorValue
            })
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateSignature is not null)
        {
            throw new ArgumentException(
                $"Multi-item capture signature for collection '{duplicateSignature.Key.CaptureLocatorKind}:{duplicateSignature.Key.CaptureLocatorValue}' "
                + $"and control root '{duplicateSignature.Key.RootKind}:{duplicateSignature.Key.RootValue}' is configured more than once.",
                nameof(definitions));
        }

        return Array.AsReadOnly(definitions.ToArray());
    }

    private static string ComputeFingerprint(IEnumerable<MultiItemControlDefinition> definitions)
    {
        var builder = new StringBuilder();
        foreach (var definition in definitions.OrderBy(static item => item.PagePropertyName, StringComparer.Ordinal))
        {
            Append(builder, definition.PagePropertyName);
            Append(builder, definition.CaptureLocatorKind);
            Append(builder, definition.CaptureLocatorValue);
            Append(builder, definition.RuntimeLocatorKind);
            Append(builder, definition.RuntimeLocatorValue);
            Append(builder, definition.RuntimeFallbackToName);
            AppendLocator(builder, definition.ItemContainerLocator!);
            Append(builder, definition.ItemKey!.Property);
            Append(builder, definition.ItemKey.Source is not null);
            if (definition.ItemKey.Source is not null) AppendLocator(builder, definition.ItemKey.Source);
            Append(builder, definition.NestedControlType);
            AppendLocator(builder, definition.SpinnerParts!.Root);
            AppendLocator(builder, definition.SpinnerParts.Input);
            Append(builder, definition.SpinnerParts.CommitTarget is not null);
            if (definition.SpinnerParts.CommitTarget is not null) AppendLocator(builder, definition.SpinnerParts.CommitTarget);
            Append(builder, definition.SpinnerParts.UseKeyboardInput);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void AppendLocator(StringBuilder builder, MultiItemRelativeLocator locator)
    {
        Append(builder, locator.Scope);
        Append(builder, locator.LocatorKind);
        Append(builder, locator.LocatorValue);
        Append(builder, locator.FallbackToName);
    }

    private static void Append(StringBuilder builder, object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        builder.Append(text.Length).Append('#').Append(text);
    }
}
