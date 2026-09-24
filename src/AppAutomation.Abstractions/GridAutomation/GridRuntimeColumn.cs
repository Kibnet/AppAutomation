namespace AppAutomation.Abstractions;

/// <summary>Carries a provider-facing column position and its declarative value mapping.</summary>
public sealed record GridRuntimeColumn
{
    public GridRuntimeColumn(
        int columnIndex,
        string sourceFieldName,
        string? displayValuePath,
        string? formatString,
        string? cultureName,
        GridCellValueKind valueKind,
        GridCellEditorKind? editorKind,
        GridCellEditorParts? editorParts)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
        ColumnIndex = columnIndex;
        SourceFieldName = GridAutomationDefinition.NormalizeRequired(sourceFieldName, nameof(sourceFieldName));
        DisplayValuePath = displayValuePath;
        FormatString = formatString;
        CultureName = cultureName;
        ValueKind = valueKind;
        EditorKind = editorKind;
        EditorParts = editorParts;
    }

    public int ColumnIndex { get; }

    /// <summary>
    /// Gets the current visible provider index when the column is present in runtime metadata.
    /// A null value means the configured column is currently hidden or otherwise unavailable visually.
    /// </summary>
    public int? RuntimeColumnIndex { get; init; }

    /// <summary>Gets the real row metadata property used when an identity column is hidden.</summary>
    public GridRowAutomationProperty? RowIdentityAutomationProperty { get; init; }

    public string SourceFieldName { get; }

    public string? DisplayValuePath { get; }

    public string? FormatString { get; }

    public string? CultureName { get; }

    public GridCellValueKind ValueKind { get; }

    public GridCellEditorKind? EditorKind { get; }

    public GridCellEditorParts? EditorParts { get; }

    /// <summary>Gets the configured display caption for a semantic true value.</summary>
    public string? BooleanTrueDisplayText { get; init; }

    /// <summary>Gets the configured display caption for a semantic false value.</summary>
    public string? BooleanFalseDisplayText { get; init; }

    /// <summary>Gets the structural DataContext paths used to locate the visible cell.</summary>
    public GridCellContextDefinition CellContext { get; init; } = GridCellContextDefinition.Default;
}
