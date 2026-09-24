using System.Globalization;

namespace AppAutomation.Abstractions;

/// <summary>Describes one logical grid column and its optional source/value mapping.</summary>
public sealed record GridColumnDefinition
{
    private GridColumnDefinition(string logicalName)
    {
        LogicalName = GridAutomationDefinition.NormalizeRequired(logicalName, nameof(logicalName));
        SourceFieldName = LogicalName;
    }

    public string LogicalName { get; private init; }

    public string SourceFieldName { get; private init; }

    /// <summary>
    /// Gets the provider-facing runtime column name, such as a visible UIA header.
    /// When omitted, the logical and source names remain valid runtime matches.
    /// </summary>
    public string? RuntimeColumnName { get; private init; }

    public string? DisplayValuePath { get; private init; }

    public string? FormatString { get; private init; }

    public string? CultureName { get; private init; }

    public GridCellValueKind? ValueKind { get; private init; }

    public GridCellEditorKind? EditorKind { get; private init; }

    public GridCellEditorParts? EditorParts { get; private init; }

    /// <summary>
    /// Gets whether provider or model metadata proved this column can participate in an automatic stable row identity.
    /// Explicit client configuration should normally use <see cref="GridAutomationDefinition.IdentifyRowsBy"/>.
    /// </summary>
    public bool IsStableIdentityCandidate { get; private init; }

    /// <summary>
    /// Gets the UI Automation row property that exposes this hidden identity during cross-process playback.
    /// </summary>
    public GridRowAutomationProperty? RowIdentityAutomationProperty { get; private init; }

    public string? BooleanTrueDisplayText { get; private init; }

    public string? BooleanFalseDisplayText { get; private init; }

    public static GridColumnDefinition Auto(string fieldName) => new(fieldName);

    public static GridColumnDefinition Map(string logicalName) => new(logicalName);

    public GridColumnDefinition FromField(string sourceFieldName)
    {
        return this with
        {
            SourceFieldName = GridAutomationDefinition.NormalizeRequired(sourceFieldName, nameof(sourceFieldName))
        };
    }

    /// <summary>Maps this column to an independently named runtime column.</summary>
    public GridColumnDefinition AtRuntime(string runtimeColumnName)
    {
        return this with
        {
            RuntimeColumnName = GridAutomationDefinition.NormalizeRequired(
                runtimeColumnName,
                nameof(runtimeColumnName))
        };
    }

    internal GridColumnDefinition BindRuntimeColumn(string runtimeColumnName)
    {
        return AtRuntime(runtimeColumnName);
    }

    public GridColumnDefinition DisplayValueFrom(string propertyPath)
    {
        return this with
        {
            DisplayValuePath = GridPropertyPath.Normalize(propertyPath, nameof(propertyPath))
        };
    }

    public GridColumnDefinition FormatWith(string formatString, string? cultureName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatString);
        if (!string.IsNullOrWhiteSpace(cultureName))
        {
            _ = CultureInfo.GetCultureInfo(cultureName.Trim());
        }

        return this with
        {
            FormatString = formatString,
            CultureName = string.IsNullOrWhiteSpace(cultureName) ? null : cultureName.Trim()
        };
    }

    public GridColumnDefinition AsValue(GridCellValueKind valueKind) =>
        this with { ValueKind = valueKind };

    public GridColumnDefinition EditWith(
        GridCellEditorKind editorKind,
        GridCellEditorParts? parts = null)
    {
        return this with { EditorKind = editorKind, EditorParts = parts };
    }

    public GridColumnDefinition AsStableIdentityCandidate() =>
        this with { IsStableIdentityCandidate = true };

    /// <summary>
    /// Reads this identity value from metadata on the real row when no visible cell exposes it.
    /// </summary>
    public GridColumnDefinition ReadIdentityFromRow(GridRowAutomationProperty automationProperty) =>
        this with { RowIdentityAutomationProperty = automationProperty };

    /// <summary>Maps localized boolean captions while preserving the semantic Boolean value.</summary>
    public GridColumnDefinition WithBooleanDisplayText(string trueText, string falseText)
    {
        var normalizedTrue = GridAutomationDefinition.NormalizeRequired(trueText, nameof(trueText));
        var normalizedFalse = GridAutomationDefinition.NormalizeRequired(falseText, nameof(falseText));
        if (string.Equals(normalizedTrue, normalizedFalse, StringComparison.Ordinal))
        {
            throw new ArgumentException("Boolean display texts must be distinct.", nameof(falseText));
        }

        return this with
        {
            BooleanTrueDisplayText = normalizedTrue,
            BooleanFalseDisplayText = normalizedFalse
        };
    }
}
