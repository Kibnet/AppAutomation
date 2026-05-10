namespace AppAutomation.Session.Contracts;

/// <summary>
/// Describes the requested monitor, size and position for a desktop test window.
/// </summary>
/// <remarks>
/// Desktop runtime adapters interpret all coordinates and sizes as Windows desktop pixels, not application
/// layout units such as Avalonia DIPs. Placement is opt-in; omit this option to keep the existing launch behavior.
/// </remarks>
public sealed class DesktopWindowPlacement
{
    /// <summary>
    /// Gets the target monitor selector. Defaults to the primary monitor.
    /// </summary>
    public DesktopMonitorSelector Monitor { get; init; } = DesktopMonitorSelector.Primary;

    /// <summary>
    /// Gets the requested outer window size. If omitted, the adapter keeps the current outer window size.
    /// </summary>
    public DesktopWindowSize? Size { get; init; }

    /// <summary>
    /// Gets the anchor used to position the window within the selected monitor area.
    /// </summary>
    public DesktopWindowAnchor Anchor { get; init; } = DesktopWindowAnchor.Center;

    /// <summary>
    /// Gets the anchor-relative offset in Windows desktop pixels.
    /// </summary>
    /// <remarks>
    /// For <see cref="DesktopWindowAnchor.Center"/>, positive X moves right and positive Y moves down.
    /// For right and bottom anchors, positive values move the window inward from the corresponding edge.
    /// </remarks>
    public DesktopWindowOffset Offset { get; init; } = DesktopWindowOffset.Zero;

    /// <summary>
    /// Gets whether placement should use the monitor work area, excluding taskbars and docked shell UI.
    /// </summary>
    public bool UseWorkingArea { get; init; } = true;

    /// <summary>
    /// Gets the behavior used when the requested monitor cannot be resolved.
    /// </summary>
    public DesktopWindowPlacementUnavailableBehavior UnavailableBehavior { get; init; } =
        DesktopWindowPlacementUnavailableBehavior.Fail;

    /// <summary>
    /// Creates a centered placement using the primary monitor.
    /// </summary>
    public static DesktopWindowPlacement Centered(int width, int height)
    {
        return Centered(DesktopMonitorSelector.Primary, width, height);
    }

    /// <summary>
    /// Creates a centered placement using the specified monitor.
    /// </summary>
    public static DesktopWindowPlacement Centered(DesktopMonitorSelector monitor, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        return new DesktopWindowPlacement
        {
            Monitor = monitor,
            Size = new DesktopWindowSize(width, height),
            Anchor = DesktopWindowAnchor.Center
        };
    }
}

/// <summary>
/// Selects the monitor used for desktop window placement.
/// </summary>
public sealed class DesktopMonitorSelector : IEquatable<DesktopMonitorSelector>
{
    private DesktopMonitorSelector(bool isPrimary, bool isLastAvailable, int? index, string? deviceName)
    {
        IsPrimary = isPrimary;
        IsLastAvailable = isLastAvailable;
        Index = index;
        DeviceName = deviceName;
    }

    /// <summary>
    /// Gets a selector for the primary monitor.
    /// </summary>
    public static DesktopMonitorSelector Primary { get; } = new(
        isPrimary: true,
        isLastAvailable: false,
        index: null,
        deviceName: null);

    /// <summary>
    /// Gets a selector for the last monitor in the stable monitor ordering.
    /// </summary>
    /// <remarks>
    /// The FlaUI adapter orders monitors with the primary monitor first, then by virtual desktop coordinates.
    /// On a single-monitor desktop this selector resolves to the primary monitor.
    /// </remarks>
    public static DesktopMonitorSelector LastAvailable { get; } = new(
        isPrimary: false,
        isLastAvailable: true,
        index: null,
        deviceName: null);

    /// <summary>
    /// Gets whether this selector targets the primary monitor.
    /// </summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// Gets whether this selector targets the last monitor in the stable monitor ordering.
    /// </summary>
    public bool IsLastAvailable { get; }

    /// <summary>
    /// Gets the zero-based monitor index, sorted primary first and then by virtual desktop coordinates.
    /// </summary>
    public int? Index { get; }

    /// <summary>
    /// Gets the Win32 monitor device name, for example <c>\\.\DISPLAY2</c>.
    /// </summary>
    public string? DeviceName { get; }

    /// <summary>
    /// Creates a selector for a stable zero-based monitor index.
    /// </summary>
    public static DesktopMonitorSelector FromIndex(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Monitor index cannot be negative.");
        }

        return new DesktopMonitorSelector(isPrimary: false, isLastAvailable: false, index, deviceName: null);
    }

    /// <summary>
    /// Creates a selector for a Win32 monitor device name, for example <c>\\.\DISPLAY2</c>.
    /// </summary>
    public static DesktopMonitorSelector FromDeviceName(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            throw new ArgumentException("Monitor device name is required.", nameof(deviceName));
        }

        return new DesktopMonitorSelector(
            isPrimary: false,
            isLastAvailable: false,
            index: null,
            deviceName.Trim());
    }

    /// <inheritdoc />
    public bool Equals(DesktopMonitorSelector? other)
    {
        return other is not null
            && IsPrimary == other.IsPrimary
            && IsLastAvailable == other.IsLastAvailable
            && Index == other.Index
            && string.Equals(DeviceName, other.DeviceName, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is DesktopMonitorSelector other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(
            IsPrimary,
            IsLastAvailable,
            Index,
            StringComparer.OrdinalIgnoreCase.GetHashCode(DeviceName ?? string.Empty));
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (IsPrimary)
        {
            return "Primary";
        }

        if (IsLastAvailable)
        {
            return "LastAvailable";
        }

        if (Index is not null)
        {
            return $"Index={Index.Value}";
        }

        return $"DeviceName={DeviceName}";
    }
}

/// <summary>
/// Defines the requested outer size of a desktop test window in Windows desktop pixels.
/// </summary>
public sealed record DesktopWindowSize
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopWindowSize"/> class.
    /// </summary>
    public DesktopWindowSize(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Window width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Window height must be greater than zero.");
        }

        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets the requested width in Windows desktop pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the requested height in Windows desktop pixels.
    /// </summary>
    public int Height { get; }
}

/// <summary>
/// Defines an anchor-relative offset in Windows desktop pixels.
/// </summary>
public sealed record DesktopWindowOffset
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopWindowOffset"/> class.
    /// </summary>
    public DesktopWindowOffset(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Gets the zero offset.
    /// </summary>
    public static DesktopWindowOffset Zero { get; } = new(0, 0);

    /// <summary>
    /// Gets the horizontal offset.
    /// </summary>
    public int X { get; }

    /// <summary>
    /// Gets the vertical offset.
    /// </summary>
    public int Y { get; }
}

/// <summary>
/// Defines how a desktop test window is anchored inside the selected monitor area.
/// </summary>
public enum DesktopWindowAnchor
{
    /// <summary>
    /// Centers the window in the selected area.
    /// </summary>
    Center = 0,

    /// <summary>
    /// Places the window relative to the top-left corner.
    /// </summary>
    TopLeft = 1,

    /// <summary>
    /// Places the window relative to the top-right corner.
    /// </summary>
    TopRight = 2,

    /// <summary>
    /// Places the window relative to the bottom-left corner.
    /// </summary>
    BottomLeft = 3,

    /// <summary>
    /// Places the window relative to the bottom-right corner.
    /// </summary>
    BottomRight = 4
}

/// <summary>
/// Defines how placement behaves when the requested monitor is unavailable.
/// </summary>
public enum DesktopWindowPlacementUnavailableBehavior
{
    /// <summary>
    /// Fails launch placement with an actionable error.
    /// </summary>
    Fail = 0,

    /// <summary>
    /// Falls back to the primary monitor when the requested monitor cannot be resolved.
    /// </summary>
    UsePrimaryMonitor = 1
}
