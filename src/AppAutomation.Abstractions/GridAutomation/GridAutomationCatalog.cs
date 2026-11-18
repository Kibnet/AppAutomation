using System.Collections;
using System.Security.Cryptography;
using System.Text;

namespace AppAutomation.Abstractions;

/// <summary>Contains validated grid definitions shared by Recorder and runtime adapters.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1710:Identifiers should have correct suffix",
    Justification = "Catalog is the public domain term used by the Recorder and runtime configuration API.")]
public sealed class GridAutomationCatalog : IReadOnlyCollection<GridAutomationDefinition>
{
    private readonly IReadOnlyList<GridAutomationDefinition> _definitions;

    public GridAutomationCatalog()
        : this(Array.Empty<GridAutomationDefinition>())
    {
    }

    private GridAutomationCatalog(IReadOnlyList<GridAutomationDefinition> definitions)
    {
        _definitions = Validate(definitions);
        Fingerprint = ComputeFingerprint(_definitions);
    }

    public int Count => _definitions.Count;

    public string Fingerprint { get; }

    public GridAutomationCatalog Add(GridAutomationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new GridAutomationCatalog([.. _definitions, definition]);
    }

    public GridAutomationCatalog AddRange(IEnumerable<GridAutomationDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return new GridAutomationCatalog([.. _definitions, .. definitions]);
    }

    public IEnumerator<GridAutomationDefinition> GetEnumerator() => _definitions.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static IReadOnlyList<GridAutomationDefinition> Validate(
        IReadOnlyList<GridAutomationDefinition> definitions)
    {
        if (definitions.Any(static definition => definition is null))
        {
            throw new ArgumentException("Grid catalog cannot contain null definitions.", nameof(definitions));
        }

        var duplicateProperty = definitions
            .GroupBy(static definition => definition.PagePropertyName, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateProperty is not null)
        {
            throw new ArgumentException(
                $"Grid Page property '{duplicateProperty.Key}' is configured more than once.",
                nameof(definitions));
        }

        var duplicateCaptureLocator = definitions
            .GroupBy(static definition => new CaptureLocatorKey(
                definition.CaptureLocatorKind,
                definition.CaptureLocatorValue))
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateCaptureLocator is not null)
        {
            throw new ArgumentException(
                $"Grid capture locator '{duplicateCaptureLocator.Key.CaptureLocatorKind}:{duplicateCaptureLocator.Key.CaptureLocatorValue}' is configured more than once.",
                nameof(definitions));
        }

        var duplicateRuntimeLocator = definitions
            .GroupBy(static definition => new RuntimeLocatorKey(
                definition.RuntimeLocatorKind,
                definition.RuntimeLocatorValue))
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateRuntimeLocator is not null)
        {
            throw new ArgumentException(
                $"Grid runtime locator '{duplicateRuntimeLocator.Key.RuntimeLocatorKind}:{duplicateRuntimeLocator.Key.RuntimeLocatorValue}' is configured more than once.",
                nameof(definitions));
        }

        foreach (var definition in definitions)
        {
            ValidateEnum(definition.CaptureLocatorKind, nameof(definition.CaptureLocatorKind));
            ValidateEnum(definition.RuntimeLocatorKind, nameof(definition.RuntimeLocatorKind));
            foreach (var column in definition.Columns)
            {
                if (column.IsObjectKey)
                {
                    if (definition.RowIdentityColumns.Count != 1
                        || !string.Equals(definition.RowIdentityColumns[0], column.LogicalName, StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            $"Grid object-key column '{column.LogicalName}' must be the only identity declared by IdentifyRowsBy(...).",
                            nameof(definitions));
                    }

                    if (column.HasObjectKeyFieldOverride || column.RuntimeColumnName is not null
                        || column.DisplayValuePath is not null || column.FormatString is not null
                        || column.CultureName is not null || column.ReadsDisplayedText
                        || column.ValueKind is { } objectKeyKind && objectKeyKind != GridCellValueKind.Text
                        || column.EditorKind is not null || column.EditorParts is not null
                        || column.BooleanTrueDisplayText is not null || column.BooleanFalseDisplayText is not null)
                    {
                        throw new ArgumentException(
                            $"Grid object-key column '{column.LogicalName}' is hidden identity metadata and cannot configure a model field, runtime cell, display projection, formatting, or editor.",
                            nameof(definitions));
                    }
                }

                if (column.ReadsDisplayedText
                    && (column.DisplayValuePath is not null || column.FormatString is not null
                        || column.RowIdentityAutomationProperty is not null || column.IsStableIdentityCandidate
                        || definition.RowIdentityColumns.Contains(column.LogicalName, StringComparer.Ordinal)))
                {
                    throw new ArgumentException(
                        $"Grid column '{column.LogicalName}' reads published display text and cannot also configure display projection, formatting, or row identity.",
                        nameof(definitions));
                }

                if (column.ValueKind is { } valueKind)
                {
                    ValidateEnum(valueKind, nameof(column.ValueKind));
                }

                if (column.EditorKind is { } editorKind)
                {
                    ValidateEnum(editorKind, nameof(column.EditorKind));
                }

                if (column.RowIdentityAutomationProperty is { } rowIdentityAutomationProperty)
                {
                    ValidateEnum(rowIdentityAutomationProperty, nameof(column.RowIdentityAutomationProperty));
                }

                if (column.ValueKind is { } configuredValueKind
                    && column.EditorKind is { } configuredEditorKind
                    && !IsSupportedCombination(configuredValueKind, configuredEditorKind))
                {
                    throw new ArgumentException(
                        $"Grid column '{column.LogicalName}' combines value kind '{configuredValueKind}' "
                        + $"with incompatible editor kind '{configuredEditorKind}'.",
                        nameof(definitions));
                }

                if (column.BooleanTrueDisplayText is not null
                    && (column.ValueKind ?? GridCellValueNormalizer.InferValueKind(column.EditorKind))
                    != GridCellValueKind.Boolean)
                {
                    throw new ArgumentException(
                        $"Grid column '{column.LogicalName}' configures Boolean display text but is not a Boolean value.",
                        nameof(definitions));
                }

                foreach (var locator in EnumerateEditorLocators(column.EditorParts))
                {
                    ValidateEnum(locator.Scope, nameof(locator.Scope));
                    ValidateEnum(locator.LocatorKind, nameof(locator.LocatorKind));
                }

                if (column.EditorParts is { ConfirmButton: not null, CommitTarget: not null })
                {
                    throw new ArgumentException(
                        $"Grid column '{column.LogicalName}' cannot configure both a confirm button and a focus-loss commit target.",
                        nameof(definitions));
                }
            }

            var nonIdentityRowSource = definition.Columns.FirstOrDefault(column =>
                column.RowIdentityAutomationProperty is not null
                && !definition.RowIdentityColumns.Contains(column.LogicalName, StringComparer.Ordinal));
            if (nonIdentityRowSource is not null)
            {
                throw new ArgumentException(
                    $"Grid column '{nonIdentityRowSource.LogicalName}' reads a row automation property but is not part of the stable row identity.",
                    nameof(definitions));
            }
        }

        return Array.AsReadOnly(definitions.ToArray());
    }

    private static bool IsSupportedCombination(
        GridCellValueKind valueKind,
        GridCellEditorKind editorKind)
    {
        return valueKind switch
        {
            GridCellValueKind.Text => editorKind is GridCellEditorKind.Text
                or GridCellEditorKind.ComboBox
                or GridCellEditorKind.SearchPicker,
            GridCellValueKind.Number => editorKind is GridCellEditorKind.Number
                or GridCellEditorKind.Text,
            GridCellValueKind.Date => editorKind is GridCellEditorKind.Date
                or GridCellEditorKind.Text,
            GridCellValueKind.Time => editorKind is GridCellEditorKind.Time
                or GridCellEditorKind.Text,
            GridCellValueKind.DateTime => editorKind is GridCellEditorKind.Text,
            GridCellValueKind.Boolean => editorKind == GridCellEditorKind.CheckBox,
            GridCellValueKind.Selection or GridCellValueKind.Reference =>
                editorKind is GridCellEditorKind.ComboBox or GridCellEditorKind.SearchPicker,
            GridCellValueKind.Color => editorKind is GridCellEditorKind.Color
                or GridCellEditorKind.Text,
            _ => false
        };
    }

    private static IEnumerable<GridRelativeLocator> EnumerateEditorLocators(GridCellEditorParts? parts)
    {
        if (parts is null)
        {
            yield break;
        }

        if (parts.Input is not null) yield return parts.Input;
        if (parts.Results is not null) yield return parts.Results;
        if (parts.OpenButton is not null) yield return parts.OpenButton;
        if (parts.ConfirmButton is not null) yield return parts.ConfirmButton;
        if (parts.CancelButton is not null) yield return parts.CancelButton;
        if (parts.CommitTarget is not null) yield return parts.CommitTarget;
    }

    private static void ValidateEnum<TEnum>(TEnum value, string name)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentException($"Unsupported {name} value '{value}'.", nameof(value));
        }
    }

    private static string ComputeFingerprint(IEnumerable<GridAutomationDefinition> definitions)
    {
        var builder = new StringBuilder();
        var sourceModes = new List<(string GridName, GridColumnDefinition Column)>();
        foreach (var definition in definitions.OrderBy(static item => item.PagePropertyName, StringComparer.Ordinal))
        {
            AppendField(builder, definition.PagePropertyName);
            AppendField(builder, definition.CaptureLocatorKind);
            AppendField(builder, definition.CaptureLocatorValue);
            AppendField(builder, definition.RuntimeLocatorKind);
            AppendField(builder, definition.RuntimeLocatorValue);
            AppendField(builder, definition.RuntimeFallbackToName);
            AppendField(builder, definition.CellContext.RowPath);
            AppendField(builder, definition.CellContext.FieldNamePath);
            AppendField(builder, definition.CellContext.ValuePath);
            AppendField(builder, definition.HeaderContext.GroupFieldPath);
            AppendField(builder, definition.HeaderContext.GroupValuePath);
            AppendField(builder, definition.HeaderContext.GroupTextPath);
            AppendField(builder, definition.HeaderContext.GroupLevelPath);
            AppendField(builder, definition.HeaderContext.ParentGroupPath);
            AppendField(builder, definition.HeaderContext.RuntimeAddressProperty);
            AppendField(builder, definition.RowIdentityColumns.Count);
            foreach (var identity in definition.RowIdentityColumns)
            {
                AppendField(builder, identity);
            }

            AppendField(builder, definition.Columns.Count);
            foreach (var column in definition.Columns.OrderBy(static item => item.LogicalName, StringComparer.Ordinal))
            {
                AppendField(builder, column.LogicalName);
                AppendField(builder, column.SourceFieldName);
                AppendField(builder, column.RuntimeColumnName);
                AppendField(builder, column.DisplayValuePath);
                AppendField(builder, column.FormatString);
                AppendField(builder, column.CultureName);
                AppendField(builder, column.ValueKind);
                AppendField(builder, column.EditorKind);
                AppendField(builder, column.IsStableIdentityCandidate);
                AppendField(builder, column.RowIdentityAutomationProperty);
                AppendField(builder, column.BooleanTrueDisplayText);
                AppendField(builder, column.BooleanFalseDisplayText);
                AppendEditorParts(builder, column.EditorParts);
                if (column.IsObjectKey || column.ReadsDisplayedText)
                {
                    sourceModes.Add((definition.PagePropertyName, column));
                }
            }
        }

        // Keep the complete legacy payload unchanged so existing autosaves remain compatible without opt-in.
        if (sourceModes.Count > 0)
        {
            AppendField(builder, "GridColumnSources/v1");
            AppendField(builder, sourceModes.Count);
            foreach (var (gridName, column) in sourceModes)
            {
                AppendField(builder, gridName);
                AppendField(builder, column.LogicalName);
                AppendField(builder, column.IsObjectKey);
                AppendField(builder, column.ReadsDisplayedText);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void AppendField(StringBuilder builder, object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        builder.Append(text.Length).Append('#').Append(text);
    }

    private static void AppendEditorParts(StringBuilder builder, GridCellEditorParts? parts)
    {
        AppendField(builder, parts is not null);
        if (parts is null)
        {
            return;
        }

        AppendRelativeLocator(builder, parts.Input);
        AppendRelativeLocator(builder, parts.Results);
        AppendRelativeLocator(builder, parts.OpenButton);
        AppendRelativeLocator(builder, parts.ConfirmButton);
        AppendRelativeLocator(builder, parts.CancelButton);
        AppendRelativeLocator(builder, parts.CommitTarget);
        AppendField(builder, parts.UseKeyboardInput);
    }

    private static void AppendRelativeLocator(StringBuilder builder, GridRelativeLocator? locator)
    {
        AppendField(builder, locator is not null);
        if (locator is null)
        {
            return;
        }

        AppendField(builder, locator.Scope);
        AppendField(builder, locator.LocatorKind);
        AppendField(builder, locator.LocatorValue);
    }

    private readonly record struct CaptureLocatorKey(
        UiLocatorKind CaptureLocatorKind,
        string CaptureLocatorValue);

    private readonly record struct RuntimeLocatorKey(
        UiLocatorKind RuntimeLocatorKind,
        string RuntimeLocatorValue);
}
