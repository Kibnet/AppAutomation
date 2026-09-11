namespace AppAutomation.Abstractions;

/// <summary>Describes a stable-address edit without persisting runtime indexes.</summary>
public sealed record GridCellValueEditRequest(
    string Value,
    GridCellEditorKind EditorKind = GridCellEditorKind.Text,
    GridCellEditCommitMode CommitMode = GridCellEditCommitMode.Commit,
    string? SearchText = null)
{
    public GridCellEditorParts? EditorParts { get; init; }
}
