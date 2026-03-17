namespace AppAutomation.Abstractions;

/// <summary>
/// Describes the capabilities and features supported by a UI automation runtime.
/// </summary>
/// <remarks>
/// Different automation adapters support different features. Use this record to query
/// available capabilities at runtime and adapt test behavior accordingly.
/// </remarks>
/// <param name="AdapterId">A unique identifier for the adapter (e.g., "flaui", "avalonia-headless").</param>
/// <param name="SupportsGridCellAccess">Indicates if the adapter supports accessing individual grid cells.</param>
/// <param name="SupportsCalendarRangeSelection">Indicates if the adapter supports selecting date ranges in calendars.</param>
/// <param name="SupportsTreeNodeExpansionState">Indicates if the adapter can read/set tree node expansion state.</param>
/// <param name="SupportsRawNativeHandles">Indicates if the adapter exposes raw platform window handles.</param>
/// <param name="SupportsScreenshots">Indicates if the adapter can capture screenshots for diagnostics.</param>
public sealed record UiRuntimeCapabilities(
    string AdapterId,
    bool SupportsGridCellAccess = false,
    bool SupportsCalendarRangeSelection = false,
    bool SupportsTreeNodeExpansionState = false,
    bool SupportsRawNativeHandles = false,
    bool SupportsScreenshots = false);
