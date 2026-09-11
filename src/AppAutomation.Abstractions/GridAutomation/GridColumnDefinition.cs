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

    public static GridColumnDefinition Auto(string fieldName) => new(fieldName);

    public static GridColumnDefinition Map(string logicalName) => new(logicalName);

    public GridColumnDefinition FromField(string sourceFieldName)
    {
        return this with
        {
            SourceFieldName = GridAutomationDefinition.NormalizeRequired(sourceFieldName, nameof(sourceFieldName))
        };
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
}
