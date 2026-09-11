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

    public string SourceFieldName { get; }

    public string? DisplayValuePath { get; }

    public string? FormatString { get; }

    public string? CultureName { get; }

    public GridCellValueKind ValueKind { get; }

    public GridCellEditorKind? EditorKind { get; }

    public GridCellEditorParts? EditorParts { get; }
}
