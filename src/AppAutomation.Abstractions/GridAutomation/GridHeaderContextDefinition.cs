namespace AppAutomation.Abstractions;

/// <summary>Публичные структурные пути к метаданным реального заголовка, без зависимости от UI-провайдера.</summary>
public sealed record GridHeaderContextDefinition(
    string GroupFieldPath = "Column.FieldName",
    string GroupValuePath = "GroupValue",
    string GroupTextPath = "GroupValueDisplayText",
    string GroupLevelPath = "Level",
    string? ParentGroupPath = null)
{
    /// <summary>UIA-проекция полного адреса на настоящем peer заголовка. По умолчанию ItemStatus; null отключает проекцию. Name сохраняет отображаемый текст.</summary>
    public GridRowAutomationProperty? RuntimeAddressProperty { get; init; } = GridRowAutomationProperty.ItemStatus;

    internal IReadOnlyDictionary<string, string>? RuntimeColumnNames { get; init; }
}
