namespace AppAutomation.Abstractions;

/// <summary>Defines the root used to resolve a repeated editor part.</summary>
public enum GridRelativeLocatorScope
{
    Cell = 0,
    EditorRoot = 1,
    DetachedPopup = 2,
    GridRoot = 3
}
