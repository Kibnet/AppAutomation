using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AppAutomation.Abstractions;
using AppAutomation.FlaUI.Extensions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;
using FlaUI.Core.WindowsAPI;
using CultureInfo = System.Globalization.CultureInfo;
using DateTimeStyles = System.Globalization.DateTimeStyles;
using NumberStyles = System.Globalization.NumberStyles;

namespace AppAutomation.FlaUI.Automation;

public sealed class FlaUiControlResolver : IUiControlResolver, IUiArtifactCollector
{
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint WindowMessageKeyUp = 0x0101;
    private const uint WindowMessageLeftButtonDown = 0x0201;
    private const uint WindowMessageLeftButtonUp = 0x0202;
    private const uint WindowMessageMouseWheel = 0x020A;
    private const int MouseKeyLeftButton = 0x0001;
    private const int VirtualKeySpace = 0x20;
    private const int SpaceKeyDownData = 0x00390001;
    private const int SpaceKeyUpData = unchecked((int)0xC0390001);

    private readonly Window _window;
    private readonly ConditionFactory _conditionFactory;

    public FlaUiControlResolver(Window window, ConditionFactory conditionFactory)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _conditionFactory = conditionFactory ?? throw new ArgumentNullException(nameof(conditionFactory));
    }

    public UiRuntimeCapabilities Capabilities { get; } = new(
        AdapterId: "flaui",
        SupportsGridCellAccess: true,
        SupportsCalendarRangeSelection: true,
        SupportsTreeNodeExpansionState: true,
        SupportsRawNativeHandles: true,
        SupportsScreenshots: true);

    public TControl Resolve<TControl>(UiControlDefinition definition)
        where TControl : class
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (typeof(TControl) == typeof(IMultiSelectItemsControl))
        {
            return (TControl)(object)new FlaUiMultiSelectItemsControl(FindElement(definition));
        }

        if (typeof(TControl) == typeof(ISearchHistoryItemsControl))
        {
            return (TControl)(object)new FlaUiSearchHistoryItemsControl(
                () => FindSearchHistoryButtons(definition),
                definition.LocatorValue);
        }

        object resolved = definition.ControlType switch
        {
            UiControlType.TextBox => new FlaUiTextBoxControl(FindElement(definition).AsTextBox()),
            UiControlType.Button => new FlaUiButtonControl(FindElement(definition).AsButton()),
            UiControlType.Label => new FlaUiLabelControl(FindElement(definition).AsLabel()),
            UiControlType.ListBox => new FlaUiListBoxControl(FindElement(definition).AsListBox()),
            UiControlType.CheckBox => new FlaUiCheckBoxControl(FindElement(definition).AsCheckBox()),
            UiControlType.ComboBox => new FlaUiComboBoxControl(FindElement(definition).AsComboBox()),
            UiControlType.RadioButton => new FlaUiRadioButtonControl(FindElement(definition).AsRadioButton()),
            UiControlType.ToggleButton => new FlaUiToggleButtonControl(FindElement(definition).AsToggleButton()),
            UiControlType.Slider => new FlaUiSliderControl(FindElement(definition).AsSlider()),
            UiControlType.ProgressBar => new FlaUiProgressBarControl(FindElement(definition).AsProgressBar()),
            UiControlType.Calendar => new FlaUiCalendarControl(FindElement(definition).AsCalendar()),
            UiControlType.DateTimePicker => new FlaUiDateTimePickerControl(FindElement(definition).AsDateTimePicker()),
            UiControlType.TimePicker => new FlaUiTimePickerControl(FindElement(definition)),
            UiControlType.Expander => new FlaUiExpanderControl(FindElement(definition)),
            UiControlType.Menu => new FlaUiMenuControl(FindElement(definition).AsMenu()),
            UiControlType.MenuItem => new FlaUiMenuItemControl(GetProcessSearchRoots, definition),
            UiControlType.Spinner => new FlaUiSpinnerControl(FindElement(definition).AsSpinner()),
            UiControlType.Tab => new FlaUiTabControl(FindElement(definition).AsTab()),
            UiControlType.TabItem => new FlaUiTabItemControl(FindElement(definition).AsTabItem()),
            UiControlType.Tree => new FlaUiTreeControl(FindElement(definition).AsTree()),
            UiControlType.TreeItem => new FlaUiTreeItemControl(FindElement(definition).AsTreeItem()),
            UiControlType.DataGridView => new FlaUiDataGridViewControl(FindElement(definition).AsDataGridView()),
            UiControlType.Grid => ResolveGrid(definition),
            UiControlType.DataGridViewRow or UiControlType.GridRow => new FlaUiGridRowControl(FindGridRow(definition)),
            UiControlType.DataGridViewCell or UiControlType.GridCell => new FlaUiGridCellControl(FindGridCell(definition)),
            _ => new FlaUiControl(FindElement(definition))
        };

        return resolved as TControl
            ?? throw new InvalidOperationException(
                $"Resolved control '{definition.PropertyName}' cannot be cast to '{typeof(TControl).FullName}'.");
    }

    public ValueTask<IReadOnlyList<UiFailureArtifact>> CollectAsync(
        UiFailureContext failureContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var screenshotArtifact = CreateScreenshotArtifact();
        var windowHandleArtifact = CreateWindowHandleArtifact();
        var processInfoArtifact = CreateProcessInfoArtifact();

        IReadOnlyList<UiFailureArtifact> artifacts =
        [
            new UiFailureArtifact(
                Kind: "logical-tree",
                LogicalName: "logical-tree",
                RelativePath: "artifacts/ui-failures/flaui/logical-tree.txt",
                ContentType: "text/plain",
                IsRequiredByContract: true,
                InlineTextPreview: BuildLogicalTreeSnapshot()),
            screenshotArtifact,
            processInfoArtifact,
            windowHandleArtifact
        ];

        return ValueTask.FromResult(artifacts);
    }

    private GridRow FindGridRow(UiControlDefinition definition)
    {
        return definition.ControlType == UiControlType.DataGridViewRow
            ? FindElement(definition).AsGridRow()
            : FindElement(definition).AsGridRow();
    }

    private GridCell FindGridCell(UiControlDefinition definition)
    {
        return definition.ControlType == UiControlType.DataGridViewCell
            ? FindElement(definition).AsGridCell()
            : FindElement(definition).AsGridCell();
    }

    private IGridControl ResolveGrid(UiControlDefinition definition)
    {
        var element = FindElement(definition);
        if (TryRead(() => element.Patterns.Grid.IsSupported) != true)
        {
            var fallbackGrid = TryRead(() => element.AsGrid());
            return new FlaUiVisualGridControl(
                _window,
                definition.LocatorValue,
                fallbackGrid is null ? null : new FlaUiGridControl(_window, fallbackGrid));
        }

        return new FlaUiGridControl(_window, element.AsGrid());
    }

    private AutomationElement FindElement(UiControlDefinition definition)
    {
        if (definition.Scope is not null)
        {
            return FindScopedElement(definition)
                ?? throw new ElementNotAvailableException(
                    $"Element with locator [{definition.LocatorKind}:{definition.LocatorValue}] was not found "
                    + $"inside scope [{definition.Scope.LocatorKind}:{definition.Scope.LocatorValue}].");
        }

        var element = _window.FindFirstDescendant(CreateCondition(definition.LocatorValue, definition.LocatorKind));
        if (element is not null)
        {
            return element;
        }

        if (definition.FallbackToName && definition.LocatorKind != UiLocatorKind.Name)
        {
            element = _window.FindFirstDescendant(CreateCondition(definition.LocatorValue, UiLocatorKind.Name));
            if (element is not null)
            {
                return element;
            }
        }

        var rootSearch = definition.LocatorKind switch
        {
            UiLocatorKind.AutomationId => SearchByAutomationId(definition.LocatorValue),
            UiLocatorKind.Name => SearchByName(definition.LocatorValue),
            _ => SearchByAutomationId(definition.LocatorValue)
        };

        if (rootSearch is not null)
        {
            return rootSearch;
        }

        throw new ElementNotAvailableException(
            $"Element with locator [{definition.LocatorKind}:{definition.LocatorValue}] was not found.");
    }

    private AutomationElement? FindScopedElement(UiControlDefinition definition)
    {
        return FindScopedCandidates(definition).FirstOrDefault(IsAttachedAndAvailable);
    }

    private AutomationElement[] FindSearchHistoryButtons(UiControlDefinition definition)
    {
        var candidates = definition.Scope is null
            ? GetProcessSearchRoots().SelectMany(root => FindCandidates(root, definition))
            : FindScopedCandidates(definition);

        return candidates
            .Where(IsAttachedAndAvailable)
            .Where(candidate => TryRead(() => candidate.ControlType) == ControlType.Button)
            .DistinctBy(GetAutomationElementIdentity)
            .ToArray();
    }

    private AutomationElement[] FindScopedCandidates(UiControlDefinition definition)
    {
        var scope = definition.Scope!;
        var scopeRoots = FindScopeRoots(scope);
        var directCandidates = scopeRoots
            .SelectMany(root => FindCandidates(root, definition))
            .Where(IsAttachedAndAvailable)
            .ToArray();
        if (directCandidates.Length > 0)
        {
            return directCandidates;
        }

        var anchors = scopeRoots.Length > 0
            ? scopeRoots
            : FindScopeAnchors(scope);
        if (anchors.Length == 0)
        {
            return Array.Empty<AutomationElement>();
        }

        return GetProcessSearchRoots()
            .Select(root => FindCandidates(root, definition)
                .Where(IsAttachedAndAvailable)
                .ToArray())
            .Where(static candidates => candidates.Length > 0)
            .OrderBy(candidates => candidates.Min(candidate =>
                anchors.Min(anchor => GetBoundsDistanceSquared(anchor, candidate))))
            .FirstOrDefault() ?? Array.Empty<AutomationElement>();
    }

    private AutomationElement[] FindCandidates(AutomationElement root, UiControlDefinition definition)
    {
        var candidates = TryRead(() => root.FindAllDescendants(CreateCondition(
            definition.LocatorValue,
            definition.LocatorKind))) ?? Array.Empty<AutomationElement>();
        if (!definition.FallbackToName || definition.LocatorKind == UiLocatorKind.Name)
        {
            return candidates;
        }

        var fallback = TryRead(() => root.FindAllDescendants(
            _conditionFactory.ByName(definition.LocatorValue))) ?? Array.Empty<AutomationElement>();
        return candidates.Concat(fallback).ToArray();
    }

    private AutomationElement[] GetProcessSearchRoots()
    {
        return new[] { (AutomationElement)_window }
            .Concat(EnumerateDetachedProcessRoots())
            .ToArray();
    }

    private AutomationElement[] FindScopeRoots(UiControlScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.LocatorValue);
        return GetProcessSearchRoots()
            .SelectMany(root => new[] { root }.Concat(FindAutomationDescendants(root)))
            .Where(candidate => MatchesLocator(candidate, scope))
            .Where(candidate => TryRead(() => candidate.IsAvailable))
            .DistinctBy(GetAutomationElementIdentity)
            .ToArray();
    }

    private static string GetAutomationElementIdentity(AutomationElement candidate)
    {
        var runtimeId = TryRead(() => candidate.FrameworkAutomationElement.RuntimeId.ValueOrDefault);
        if (runtimeId is { Length: > 0 })
        {
            return $"runtime:{string.Join(',', runtimeId)}";
        }

        return string.Join(
            '|',
            TryRead(() => candidate.FrameworkAutomationElement.ProcessId.ValueOrDefault),
            TryRead(() => candidate.FrameworkAutomationElement.NativeWindowHandle.ValueOrDefault),
            TryRead(() => candidate.ControlType),
            TryRead(() => candidate.AutomationId),
            TryRead(() => candidate.Name),
            TryRead(() => candidate.BoundingRectangle));
    }

    private AutomationElement[] FindScopeAnchors(UiControlScope scope)
    {
        if (string.IsNullOrWhiteSpace(scope.AnchorLocatorValue))
        {
            return Array.Empty<AutomationElement>();
        }

        var anchorScope = scope with { LocatorValue = scope.AnchorLocatorValue };
        return FindScopeRoots(anchorScope);
    }

    private static long GetBoundsDistanceSquared(AutomationElement first, AutomationElement second)
    {
        var firstBounds = TryRead(() => first.BoundingRectangle);
        var secondBounds = TryRead(() => second.BoundingRectangle);
        if (firstBounds.Width <= 0
            || firstBounds.Height <= 0
            || secondBounds.Width <= 0
            || secondBounds.Height <= 0)
        {
            return long.MaxValue;
        }

        var horizontalDistance = firstBounds.Right < secondBounds.Left
            ? secondBounds.Left - firstBounds.Right
            : secondBounds.Right < firstBounds.Left
                ? firstBounds.Left - secondBounds.Right
                : 0;
        var verticalDistance = firstBounds.Bottom < secondBounds.Top
            ? secondBounds.Top - firstBounds.Bottom
            : secondBounds.Bottom < firstBounds.Top
                ? firstBounds.Top - secondBounds.Bottom
                : 0;
        return (long)horizontalDistance * horizontalDistance
            + (long)verticalDistance * verticalDistance;
    }

    private static bool MatchesLocator(AutomationElement candidate, UiControlScope scope)
    {
        var primaryValue = scope.LocatorKind switch
        {
            UiLocatorKind.AutomationId => TryRead(() => candidate.AutomationId),
            UiLocatorKind.Name => TryRead(() => candidate.Name),
            _ => null
        };
        if (string.Equals(primaryValue, scope.LocatorValue, StringComparison.Ordinal))
        {
            return true;
        }

        return scope.FallbackToName
            && scope.LocatorKind != UiLocatorKind.Name
            && string.Equals(TryRead(() => candidate.Name), scope.LocatorValue, StringComparison.Ordinal);
    }

    private PropertyCondition CreateCondition(string locatorValue, UiLocatorKind locatorKind)
    {
        return locatorKind switch
        {
            UiLocatorKind.AutomationId => _conditionFactory.ByAutomationId(locatorValue),
            UiLocatorKind.Name => _conditionFactory.ByName(locatorValue),
            _ => throw new ArgumentOutOfRangeException(nameof(locatorKind), locatorKind, "Unsupported locator kind.")
        };
    }

    private AutomationElement? SearchByAutomationId(string locatorValue)
    {
        var direct = _window.FindAllDescendants(factory => factory.ByAutomationId(locatorValue));
        if (direct.Length > 0)
        {
            return direct.FirstOrDefault(candidate => candidate?.IsAvailable == true);
        }

        var detached = SearchDetachedProcessRootsByAutomationId(locatorValue);
        if (detached is not null)
        {
            return detached;
        }

        var normalized = locatorValue.Trim().ToLowerInvariant();
        var descendant = _window.FindAllDescendants()
            .FirstOrDefault(candidate =>
            {
                if (!candidate.IsAvailable)
                {
                    return false;
                }

                var automationId = TryRead(() => candidate.AutomationId)?.ToLowerInvariant();
                return automationId is not null && (automationId == normalized || automationId.StartsWith(normalized, StringComparison.Ordinal));
            });

        return descendant ?? SearchDetachedProcessRootsByAutomationIdPrefix(normalized);
    }

    private AutomationElement? SearchByName(string locatorValue)
    {
        var direct = _window.FindAllDescendants(factory => factory.ByName(locatorValue));
        if (direct.Length > 0)
        {
            return direct.FirstOrDefault(candidate => candidate?.IsAvailable == true);
        }

        var detached = SearchDetachedProcessRootsByName(locatorValue);
        if (detached is not null)
        {
            return detached;
        }

        var normalized = locatorValue.Trim().ToLowerInvariant();
        var descendant = _window.FindAllDescendants()
            .FirstOrDefault(candidate =>
            {
                if (!candidate.IsAvailable)
                {
                    return false;
                }

                var name = TryRead(() => candidate.Name)?.ToLowerInvariant();
                return name is not null && (name == normalized || name.Contains(normalized, StringComparison.Ordinal));
            });

        return descendant ?? SearchDetachedProcessRootsByNameContains(normalized);
    }

    private AutomationElement? SearchDetachedProcessRootsByAutomationId(string locatorValue)
    {
        foreach (var root in EnumerateDetachedProcessRoots())
        {
            var direct = TryRead(() => root.FindAllDescendants(factory => factory.ByAutomationId(locatorValue)))
                ?? Array.Empty<AutomationElement>();
            var match = direct.FirstOrDefault(IsAttachedAndAvailable);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private AutomationElement? SearchDetachedProcessRootsByName(string locatorValue)
    {
        foreach (var root in EnumerateDetachedProcessRoots())
        {
            var direct = TryRead(() => root.FindAllDescendants(factory => factory.ByName(locatorValue)))
                ?? Array.Empty<AutomationElement>();
            var match = direct.FirstOrDefault(IsAttachedAndAvailable);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private AutomationElement? SearchDetachedProcessRootsByAutomationIdPrefix(string normalizedLocatorValue)
    {
        foreach (var root in EnumerateDetachedProcessRoots())
        {
            var match = FindAutomationDescendants(root)
                .FirstOrDefault(candidate =>
                {
                    if (!IsAttachedAndAvailable(candidate))
                    {
                        return false;
                    }

                    var automationId = TryRead(() => candidate.AutomationId)?.ToLowerInvariant();
                    return automationId is not null
                        && (automationId == normalizedLocatorValue || automationId.StartsWith(normalizedLocatorValue, StringComparison.Ordinal));
                });
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private AutomationElement? SearchDetachedProcessRootsByNameContains(string normalizedLocatorValue)
    {
        foreach (var root in EnumerateDetachedProcessRoots())
        {
            var match = FindAutomationDescendants(root)
                .FirstOrDefault(candidate =>
                {
                    if (!IsAttachedAndAvailable(candidate))
                    {
                        return false;
                    }

                    var name = TryRead(() => candidate.Name)?.ToLowerInvariant();
                    return name is not null
                        && (name == normalizedLocatorValue || name.Contains(normalizedLocatorValue, StringComparison.Ordinal));
                });
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private AutomationElement[] EnumerateDetachedProcessRoots()
    {
        var processId = TryRead(() => _window.FrameworkAutomationElement.ProcessId.ValueOrDefault);
        if (processId <= 0)
        {
            return Array.Empty<AutomationElement>();
        }

        var desktop = TryRead(() => _window.Automation.GetDesktop());
        if (desktop is null)
        {
            return Array.Empty<AutomationElement>();
        }

        var windowHandle = TryRead(() => _window.FrameworkAutomationElement.NativeWindowHandle.ValueOrDefault);
        var roots = TryRead(() => desktop.FindAllChildren(factory => factory.ByProcessId(processId)))
            ?? Array.Empty<AutomationElement>();

        return roots
            .Where(candidate => candidate?.IsAvailable == true && !IsSameNativeWindow(candidate, windowHandle))
            .ToArray();
    }

    private static bool IsSameNativeWindow(AutomationElement candidate, IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var candidateHandle = TryRead(() => candidate.FrameworkAutomationElement.NativeWindowHandle.ValueOrDefault);
        return candidateHandle != IntPtr.Zero && candidateHandle == windowHandle;
    }

    private static bool IsAttachedAndAvailable(AutomationElement? candidate)
    {
        if (candidate is null)
        {
            return false;
        }

        try
        {
            return candidate.IsAvailable
                && candidate.Parent is not null;
        }
        catch
        {
            return false;
        }
    }

    private static T? TryRead<T>(Func<T> accessor)
    {
        try
        {
            return accessor();
        }
        catch
        {
            return default;
        }
    }

    private string BuildLogicalTreeSnapshot()
    {
        var builder = new StringBuilder();
        AppendElement(builder, _window, depth: 0);

        foreach (var candidate in _window.FindAllDescendants())
        {
            AppendElement(builder, candidate, depth: 1);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendElement(StringBuilder builder, AutomationElement element, int depth)
    {
        builder.Append(' ', depth * 2)
            .Append(TryRead(() => element.ControlType.ToString()) ?? "<unknown>")
            .Append(" | Id=")
            .Append(TryRead(() => element.AutomationId) ?? string.Empty)
            .Append(" | Name=")
            .Append(TryRead(() => element.Name) ?? string.Empty)
            .AppendLine();
    }

    private UiFailureArtifact CreateScreenshotArtifact()
    {
        try
        {
            using var screenshot = _window.Capture();
            return new UiFailureArtifact(
                Kind: "screenshot",
                LogicalName: "window-screenshot",
                RelativePath: "artifacts/ui-failures/flaui/window.png",
                ContentType: "image/png",
                IsRequiredByContract: true,
                InlineTextPreview: $"{screenshot.Width}x{screenshot.Height}");
        }
        catch (Exception ex)
        {
            return new UiFailureArtifact(
                Kind: "screenshot-unavailable",
                LogicalName: "window-screenshot",
                RelativePath: "artifacts/ui-failures/flaui/window.png",
                ContentType: "text/plain",
                IsRequiredByContract: false,
                InlineTextPreview: ex.Message);
        }
    }

    private UiFailureArtifact CreateWindowHandleArtifact()
    {
        var handle = TryRead(() => _window.FrameworkAutomationElement.NativeWindowHandle.ValueOrDefault);
        var isAvailable = handle != IntPtr.Zero;

        return new UiFailureArtifact(
            Kind: "window-handle",
            LogicalName: "window-handle",
            RelativePath: "artifacts/ui-failures/flaui/window-handle.txt",
            ContentType: "text/plain",
            IsRequiredByContract: isAvailable,
            InlineTextPreview: isAvailable
                ? $"0x{handle.ToInt64():X}"
                : "Window handle unavailable.");
    }

    private UiFailureArtifact CreateProcessInfoArtifact()
    {
        var processId = TryRead(() => _window.FrameworkAutomationElement.ProcessId.ValueOrDefault);
        if (processId <= 0)
        {
            return new UiFailureArtifact(
                Kind: "process-info",
                LogicalName: "process-info",
                RelativePath: "artifacts/ui-failures/flaui/process-info.txt",
                ContentType: "text/plain",
                IsRequiredByContract: false,
                InlineTextPreview: "Process id unavailable.");
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            var startedAt = TryRead(() => process.StartTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            return new UiFailureArtifact(
                Kind: "process-info",
                LogicalName: "process-info",
                RelativePath: "artifacts/ui-failures/flaui/process-info.txt",
                ContentType: "text/plain",
                IsRequiredByContract: true,
                InlineTextPreview: $"Pid={processId}; Name={process.ProcessName}; StartedAtUtc={startedAt ?? "<unknown>"}");
        }
        catch (Exception ex)
        {
            return new UiFailureArtifact(
                Kind: "process-info",
                LogicalName: "process-info",
                RelativePath: "artifacts/ui-failures/flaui/process-info.txt",
                ContentType: "text/plain",
                IsRequiredByContract: false,
                InlineTextPreview: $"Pid={processId}; Error={ex.Message}");
        }
    }

    private abstract class FlaUiControlBase<TControl> : IUiControlAvailability, IContextMenuOwnerControl
        where TControl : AutomationElement
    {
        protected FlaUiControlBase(TControl inner)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        protected TControl Inner { get; }

        public string AutomationId => TryRead(() => Inner.AutomationId) ?? string.Empty;

        public string Name => TryRead(() => Inner.Name) ?? string.Empty;

        public bool IsEnabled => TryRead(() => Inner.IsEnabled);

        public bool IsAvailable => TryRead(() => Inner.IsAvailable && !Inner.IsOffscreen);

        public void InvokeContextMenuItem(IReadOnlyList<string> path, int timeoutMs)
        {
            FlaUiContextMenuRuntime.Invoke(Inner, path, timeoutMs);
        }

        protected static TResult? TryRead<TResult>(Func<TResult> accessor)
        {
            try
            {
                return accessor();
            }
            catch
            {
                return default;
            }
        }
    }

    private sealed class FlaUiControl : FlaUiControlBase<AutomationElement>, IReadableTextControl
    {
        public FlaUiControl(AutomationElement inner) : base(inner)
        {
        }

        public string Text => ReadAutomationElementVisibleText(Inner) ?? string.Empty;
    }

    private sealed class FlaUiTextBoxControl : FlaUiControlBase<TextBox>, ITextBoxControl
    {
        public FlaUiTextBoxControl(TextBox inner) : base(inner)
        {
        }

        public string Text
        {
            get => TryRead(() => Inner.Text) ?? string.Empty;
            set => Inner.Text = value;
        }

        public void Enter(string value)
        {
            Inner.EnterText(value);
        }
    }

    private sealed class FlaUiButtonControl : FlaUiControlBase<Button>, IButtonControl, IReadableTextControl
    {
        public FlaUiButtonControl(Button inner) : base(inner)
        {
        }

        public string Text => ReadAutomationElementVisibleText(Inner) ?? string.Empty;

        public void Invoke()
        {
            if (Inner.Patterns.Toggle.IsSupported && IsOpenToggleButton(Inner))
            {
                var togglePattern = Inner.Patterns.Toggle.Pattern;
                if (togglePattern.ToggleState.Value != ToggleState.On)
                {
                    togglePattern.Toggle();
                }

                return;
            }

            if (Inner.Patterns.Invoke.IsSupported)
            {
                Inner.Invoke();
                return;
            }

            if (Inner.Patterns.Toggle.IsSupported)
            {
                Inner.Patterns.Toggle.Pattern.Toggle();
                return;
            }

            Inner.Click();
        }

        private static bool IsOpenToggleButton(AutomationElement element)
        {
            return IsOpenToggleToken(TryRead(() => element.AutomationId))
                || IsOpenToggleToken(TryRead(() => element.Name));
        }

        private static bool IsOpenToggleToken(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && (value.EndsWith("OpenButton", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("PopupOpenButton", StringComparison.OrdinalIgnoreCase));
        }

    }

    private sealed class FlaUiLabelControl : FlaUiControlBase<Label>, ILabelControl
    {
        public FlaUiLabelControl(Label inner) : base(inner)
        {
        }

        public string Text => TryRead(() => Inner.Text) ?? ReadAutomationElementVisibleText(Inner) ?? string.Empty;
    }

    private sealed class FlaUiListBoxControl : FlaUiControlBase<ListBox>, IExactSelectableListBoxControl, IReadableTextControl
    {
        public FlaUiListBoxControl(ListBox inner) : base(inner)
        {
        }

        public IReadOnlyList<IListBoxItem> Items =>
            ReadItems();

        public string Text
        {
            get
            {
                var selectedText = SelectedItemText;
                return !string.IsNullOrWhiteSpace(selectedText)
                    ? selectedText
                    : string.Join(" ", Items.Select(static item => item.Text).Where(static text => !string.IsNullOrWhiteSpace(text)));
            }
        }

        public string? SelectedItemText => ReadSelectedText();

        private IListBoxItem[] ReadItems()
        {
            return GetSelectableItems()
                .Select(candidate =>
                {
                    var text = ReadAutomationElementText(candidate);
                    return (IListBoxItem)new FlaUiListBoxItem(text, text);
                })
                .Where(static item => !string.IsNullOrWhiteSpace(item.Text) || !string.IsNullOrWhiteSpace(item.Name))
                .ToArray();
        }

        public void SelectItem(string itemText)
        {
            SelectItem(itemText, TimeSpan.FromSeconds(1), exact: false);
        }

        public void SelectItemExact(string itemText)
        {
            SelectItem(itemText, TimeSpan.FromSeconds(1), exact: true);
        }

        internal void SelectItem(string itemText, TimeSpan timeout)
        {
            SelectItem(itemText, timeout, exact: false);
        }

        private void SelectItem(string itemText, TimeSpan timeout, bool exact)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemText);

            var normalizedTarget = NormalizeLookupText(itemText);
            var stopwatch = Stopwatch.StartNew();
            AutomationElement? candidate;
            do
            {
                candidate = ReadSelectableItems().FirstOrDefault(element =>
                {
                    var text = ReadAutomationElementText(element);
                    return exact
                        ? string.Equals(text, itemText, StringComparison.Ordinal)
                        : string.Equals(
                            NormalizeLookupText(text),
                            normalizedTarget,
                            StringComparison.OrdinalIgnoreCase);
                });
                if (candidate is not null)
                {
                    break;
                }

                Thread.Sleep(50);
            }
            while (stopwatch.Elapsed < timeout);

            if (candidate is null)
            {
                throw new InvalidOperationException($"ListBox item '{itemText}' was not found.");
            }

            bool SelectionMatchesTarget()
            {
                return exact
                    ? string.Equals(ReadSelectedText(), itemText, StringComparison.Ordinal)
                    : SelectionMatches(normalizedTarget);
            }

            if (TrySelect(candidate) && (SelectionMatchesTarget() || SelectionStateUnavailable()))
            {
                return;
            }

            if (TryClick(candidate) && (SelectionMatchesTarget() || SelectionStateUnavailable()))
            {
                return;
            }

            throw new InvalidOperationException($"ListBox item '{itemText}' could not be selected.");
        }

        private List<AutomationElement> GetSelectableItems()
        {
            var timeout = Stopwatch.StartNew();
            List<AutomationElement> items;
            do
            {
                items = ReadSelectableItems();
                if (items.Count > 0)
                {
                    return items;
                }

                Thread.Sleep(50);
            }
            while (timeout.Elapsed < TimeSpan.FromSeconds(1));

            return items;
        }

        private List<AutomationElement> ReadSelectableItems()
        {
            var items = new List<AutomationElement>();

            try
            {
                foreach (var item in Inner.Items)
                {
                    if (item is not null && !items.Contains(item))
                    {
                        items.Add(item);
                    }
                }
            }
            catch
            {
                // Some providers do not expose direct list items.
            }

            foreach (var candidate in FindAutomationDescendants(Inner))
            {
                if (candidate is null || candidate == Inner || items.Contains(candidate) || !IsListItemCandidate(candidate))
                {
                    continue;
                }

                var text = ReadAutomationElementText(candidate);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    items.Add(candidate);
                }
            }

            return items;
        }

        private string? ReadSelectedText()
        {
            var selectedCandidate = GetSelectableItems().FirstOrDefault(candidate =>
            {
                try
                {
                    return candidate.Patterns.SelectionItem.IsSupported
                        && candidate.Patterns.SelectionItem.Pattern.IsSelected.Value;
                }
                catch
                {
                    return false;
                }
            });

            return selectedCandidate is null
                ? null
                : ReadAutomationElementText(selectedCandidate);
        }

        private bool SelectionMatches(string expectedText)
        {
            if (string.IsNullOrWhiteSpace(expectedText))
            {
                return true;
            }

            try
            {
                return string.Equals(
                    NormalizeLookupText(ReadSelectedText()),
                    expectedText,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool SelectionStateUnavailable()
        {
            try
            {
                _ = Inner.IsAvailable;
                _ = Inner.FindAllDescendants();
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsListItemCandidate(AutomationElement candidate)
        {
            return candidate.ControlType == ControlType.ListItem
                || candidate.ControlType == ControlType.Text
                || candidate.ControlType == ControlType.DataItem;
        }

        private static bool TrySelect(AutomationElement candidate)
        {
            try
            {
                if (candidate.Patterns.SelectionItem.IsSupported)
                {
                    candidate.Patterns.SelectionItem.Pattern.Select();
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryClick(AutomationElement candidate)
        {
            try
            {
                candidate.Click();
                return true;
            }
            catch
            {
            }

            try
            {
                if (candidate.Patterns.Invoke.IsSupported)
                {
                    candidate.Patterns.Invoke.Pattern.Invoke();
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }

    private sealed class FlaUiListBoxItem : IListBoxItem
    {
        public FlaUiListBoxItem(string? text, string? name)
        {
            Text = text;
            Name = name;
        }

        public string? Text { get; }

        public string? Name { get; }
    }

    private sealed class FlaUiCheckBoxControl : FlaUiControlBase<CheckBox>, ICheckBoxControl, IReadableTextControl
    {
        public FlaUiCheckBoxControl(CheckBox inner) : base(inner)
        {
        }

        public string Text => ReadAutomationElementVisibleText(Inner) ?? string.Empty;

        public bool? IsChecked
        {
            get => TryRead(() => Inner.IsChecked);
            set => Inner.IsChecked = value == true;
        }

        public void SetChecked(bool value, string itemText)
        {
            if (IsChecked == value)
            {
                return;
            }

            var toggle = Inner.Patterns.Toggle.PatternOrDefault
                ?? throw new InvalidOperationException(
                    $"Multi-select checkbox '{itemText}' does not expose a Toggle pattern.");
            toggle.Toggle();

            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(1))
            {
                if (IsChecked == value)
                {
                    return;
                }

                Thread.Sleep(25);
            }

            throw new InvalidOperationException(
                $"Multi-select checkbox '{itemText}' did not change to '{value}' after Toggle.");
        }
    }

    private sealed class FlaUiMultiSelectItemsControl : FlaUiControlBase<AutomationElement>, IMultiSelectItemsControl
    {
        private const int BoundaryRealizationAttempts = 4;
        private const int BoundaryRealizationDelayMs = 50;
        private const int MaxScrollSteps = 256;
        private MultiSelectItemSnapshot[]? _lastItems;

        public FlaUiMultiSelectItemsControl(AutomationElement inner) : base(inner)
        {
        }

        public IReadOnlyList<string> Items
        {
            get
            {
                var checkBoxItems = GetItems();
                return checkBoxItems.Length > 0
                    ? checkBoxItems.Select(static item => item.Text).ToArray()
                    : ReadSelectableItems().Select(static item => item.Text).ToArray();
            }
        }

        public IReadOnlyList<string> SelectedItems
        {
            get
            {
                var checkBoxItems = GetItems();
                return checkBoxItems.Length > 0
                    ? checkBoxItems.Where(static item => item.IsChecked).Select(static item => item.Text).ToArray()
                    : ReadSelectableItems().Where(static item => item.IsSelected).Select(static item => item.Text).ToArray();
            }
        }

        public void SetSelectedItems(IReadOnlyCollection<string> values)
        {
            _ = SetSelectedItemsAndGetAvailableItems(values);
        }

        public IReadOnlyList<string> SetSelectedItemsAndGetAvailableItems(IReadOnlyCollection<string> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            var normalizedValues = NormalizeRequestedItems(values);
            var items = new Dictionary<string, MultiSelectItemSnapshot>(StringComparer.OrdinalIgnoreCase);
            DiscoverRequestedItems(items, normalizedValues);
            if (items.Count > 0)
            {
                ValidateAvailableItems(items.Keys, normalizedValues);
                ApplyExactSelection(items, normalizedValues);
                _lastItems = items.Values.ToArray();
                return _lastItems.Select(static item => item.Text).ToArray();
            }

            return SetSelectableItems(normalizedValues);
        }

        private MultiSelectItemSnapshot[] GetItems()
        {
            return _lastItems ??= ReadAllItems();
        }

        private SelectableItemSnapshot[] ReadSelectableItems()
        {
            return FindAutomationDescendants(Inner)
                .Where(static candidate => candidate.ControlType is ControlType.ListItem or ControlType.DataItem)
                .Select(candidate => new SelectableItemSnapshot(
                    ReadAutomationElementText(candidate)?.Trim() ?? string.Empty,
                    candidate,
                    IsSelected(candidate)))
                .Where(static item => !string.IsNullOrWhiteSpace(item.Text))
                .GroupBy(static item => item.Text, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.Single())
                .ToArray();
        }

        private string[] SetSelectableItems(HashSet<string> requestedValues)
        {
            var items = ReadSelectableItems();
            ValidateAvailableItems(items.Select(static item => item.Text), requestedValues);

            foreach (var item in items.Where(item => item.IsSelected && !requestedValues.Contains(item.Text)))
            {
                RemoveFromSelection(item);
            }

            foreach (var item in items.Where(item => !item.IsSelected && requestedValues.Contains(item.Text)))
            {
                AddToSelection(item);
            }

            return items.Select(static item => item.Text).ToArray();
        }

        private static bool IsSelected(AutomationElement item)
        {
            try
            {
                return item.Patterns.SelectionItem.IsSupported
                    && item.Patterns.SelectionItem.Pattern.IsSelected.Value;
            }
            catch
            {
                return false;
            }
        }

        private static void AddToSelection(SelectableItemSnapshot item)
        {
            var pattern = item.Control.Patterns.SelectionItem.PatternOrDefault
                ?? throw new InvalidOperationException(
                    $"Selectable filter item '{item.Text}' does not expose a SelectionItem pattern.");
            try
            {
                pattern.AddToSelection();
            }
            catch
            {
                pattern.Select();
            }
        }

        private static void RemoveFromSelection(SelectableItemSnapshot item)
        {
            var pattern = item.Control.Patterns.SelectionItem.PatternOrDefault
                ?? throw new InvalidOperationException(
                    $"Selectable filter item '{item.Text}' does not expose a SelectionItem pattern.");
            pattern.RemoveFromSelection();
        }

        private MultiSelectItemSnapshot[] ReadAllItems()
        {
            var items = new Dictionary<string, MultiSelectItemSnapshot>(StringComparer.OrdinalIgnoreCase);
            var scroll = FindVerticalScrollPattern(Inner);
            var scrollBarRange = scroll is null ? FindVerticalScrollBarRange(Inner) : null;
            AddCurrentItems(items);

            if (scroll is not null)
            {
                var startingPosition = scroll.VerticalScrollPercent.ValueOrDefault;
                TraverseItems(items, () => ScrollForward(scroll));
                if (startingPosition > 0)
                {
                    TraverseItems(items, () => ScrollBackward(scroll));
                }
            }
            else if (scrollBarRange is not null)
            {
                var startingPosition = scrollBarRange.Value.ValueOrDefault;
                var minimum = scrollBarRange.Minimum.ValueOrDefault;
                TraverseItems(items, () => ScrollForward(scrollBarRange));
                if (startingPosition > minimum)
                {
                    TraverseItems(items, () => ScrollBackward(scrollBarRange));
                }
            }

            return items.Values.ToArray();
        }

        private void TraverseItems(
            IDictionary<string, MultiSelectItemSnapshot> items,
            Func<bool> scroll)
        {
            for (var step = 0; step < MaxScrollSteps; step++)
            {
                if (!scroll())
                {
                    WaitForBoundaryItems(items);
                    return;
                }

                AddCurrentItems(items);
            }

            throw new InvalidOperationException(
                $"Multi-select items traversal exceeded {MaxScrollSteps} vertical scroll steps.");
        }

        private void AddCurrentItems(
            IDictionary<string, MultiSelectItemSnapshot> items)
        {
            foreach (var item in ReadCurrentItems())
            {
                if (items.ContainsKey(item.Text))
                {
                    continue;
                }

                items.Add(item.Text, new MultiSelectItemSnapshot(item.Text, item.Control.IsChecked == true));
            }
        }

        private void DiscoverRequestedItems(
            IDictionary<string, MultiSelectItemSnapshot> items,
            IReadOnlySet<string> requestedValues)
        {
            var pendingValues = new HashSet<string>(requestedValues, StringComparer.OrdinalIgnoreCase);
            AddCurrentItems(items, pendingValues);
            if (pendingValues.Count == 0)
            {
                return;
            }

            var scroll = FindVerticalScrollPattern(Inner);
            var scrollBarRange = scroll is null ? FindVerticalScrollBarRange(Inner) : null;
            if (scroll is not null)
            {
                var startingPosition = scroll.VerticalScrollPercent.ValueOrDefault;
                if (startingPosition < 100)
                {
                    TraverseUntilRequestedItemsFound(items, pendingValues, () => ScrollForward(scroll));
                }

                if (pendingValues.Count > 0 && startingPosition > 0)
                {
                    TraverseUntilRequestedItemsFound(items, pendingValues, () => ScrollBackward(scroll));
                }

                return;
            }

            if (scrollBarRange is not null)
            {
                var startingPosition = scrollBarRange.Value.ValueOrDefault;
                var minimum = scrollBarRange.Minimum.ValueOrDefault;
                var maximum = scrollBarRange.Maximum.ValueOrDefault;
                if (startingPosition < maximum)
                {
                    TraverseUntilRequestedItemsFound(items, pendingValues, () => ScrollForward(scrollBarRange));
                }

                if (pendingValues.Count > 0 && startingPosition > minimum)
                {
                    TraverseUntilRequestedItemsFound(items, pendingValues, () => ScrollBackward(scrollBarRange));
                }
            }
        }

        private void TraverseUntilRequestedItemsFound(
            IDictionary<string, MultiSelectItemSnapshot> items,
            HashSet<string> pendingValues,
            Func<bool> scroll)
        {
            for (var step = 0; step < MaxScrollSteps && pendingValues.Count > 0; step++)
            {
                if (!scroll())
                {
                    WaitForBoundaryItems(items);
                    RemoveDiscoveredItems(pendingValues, items.Keys);
                    return;
                }

                AddCurrentItems(items, pendingValues);
            }

            if (pendingValues.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Multi-select item lookup exceeded {MaxScrollSteps} vertical scroll steps.");
            }
        }

        private void AddCurrentItems(
            IDictionary<string, MultiSelectItemSnapshot> items,
            HashSet<string> pendingValues)
        {
            AddCurrentItems(items);
            RemoveDiscoveredItems(pendingValues, items.Keys);
        }

        private static void RemoveDiscoveredItems(HashSet<string> pendingValues, IEnumerable<string> discoveredValues)
        {
            foreach (var value in discoveredValues)
            {
                pendingValues.Remove(value);
            }
        }

        private void ApplyExactSelection(
            IDictionary<string, MultiSelectItemSnapshot> items,
            IReadOnlySet<string> requestedValues)
        {
            ApplyCurrentExactSelection(items, requestedValues);

            var scroll = FindVerticalScrollPattern(Inner);
            var scrollBarRange = scroll is null ? FindVerticalScrollBarRange(Inner) : null;
            if (scroll is not null)
            {
                var startingPosition = scroll.VerticalScrollPercent.ValueOrDefault;
                var needsBackwardTraversal = startingPosition > 0;
                if (startingPosition < 100)
                {
                    TraverseExactSelection(items, requestedValues, () => ScrollForward(scroll));
                }

                if (needsBackwardTraversal)
                {
                    TraverseExactSelection(items, requestedValues, () => ScrollBackward(scroll));
                }

                return;
            }

            if (scrollBarRange is not null)
            {
                var startingPosition = scrollBarRange.Value.ValueOrDefault;
                var minimum = scrollBarRange.Minimum.ValueOrDefault;
                var maximum = scrollBarRange.Maximum.ValueOrDefault;
                var needsBackwardTraversal = startingPosition > minimum;
                if (startingPosition < maximum)
                {
                    TraverseExactSelection(items, requestedValues, () => ScrollForward(scrollBarRange));
                }

                if (needsBackwardTraversal)
                {
                    TraverseExactSelection(items, requestedValues, () => ScrollBackward(scrollBarRange));
                }
            }
        }

        private void TraverseExactSelection(
            IDictionary<string, MultiSelectItemSnapshot> items,
            IReadOnlySet<string> requestedValues,
            Func<bool> scroll)
        {
            for (var step = 0; step < MaxScrollSteps; step++)
            {
                if (!scroll())
                {
                    WaitForBoundaryExactSelection(items, requestedValues);
                    return;
                }

                ApplyCurrentExactSelection(items, requestedValues);
            }

            throw new InvalidOperationException(
                $"Multi-select selection traversal exceeded {MaxScrollSteps} vertical scroll steps.");
        }

        private void ApplyCurrentExactSelection(
            IDictionary<string, MultiSelectItemSnapshot> items,
            IReadOnlySet<string> requestedValues)
        {
            foreach (var item in ReadCurrentItems())
            {
                var shouldBeChecked = requestedValues.Contains(item.Text);
                item.Control.SetChecked(shouldBeChecked, item.Text);
                items[item.Text] = new MultiSelectItemSnapshot(item.Text, shouldBeChecked);
            }
        }

        private void WaitForBoundaryItems(IDictionary<string, MultiSelectItemSnapshot> items)
        {
            for (var attempt = 0; attempt < BoundaryRealizationAttempts; attempt++)
            {
                Thread.Sleep(BoundaryRealizationDelayMs);
                AddCurrentItems(items);
            }
        }

        private void WaitForBoundaryExactSelection(
            IDictionary<string, MultiSelectItemSnapshot> items,
            IReadOnlySet<string> requestedValues)
        {
            for (var attempt = 0; attempt < BoundaryRealizationAttempts; attempt++)
            {
                Thread.Sleep(BoundaryRealizationDelayMs);
                ApplyCurrentExactSelection(items, requestedValues);
            }
        }

        private MultiSelectCheckBox[] ReadCurrentItems()
        {
            var currentItems = FindAutomationDescendants(Inner)
                .Where(candidate => candidate.ControlType == ControlType.CheckBox && IsVisibleItem(candidate))
                .Select(candidate =>
                {
                    var text = ReadAutomationElementText(candidate)?.Trim() ?? string.Empty;
                    return new MultiSelectCheckBox(text, new FlaUiCheckBoxControl(candidate.AsCheckBox()));
                })
                .Where(static item => !string.IsNullOrWhiteSpace(item.Text))
                .ToArray();

            if (currentItems
                    .Select(static item => item.Text)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != currentItems.Length)
            {
                throw new InvalidOperationException("Multi-select items container exposes duplicate item text.");
            }

            return currentItems;
        }

        private bool IsVisibleItem(AutomationElement candidate)
        {
            if (TryRead(() => candidate.IsAvailable && !candidate.IsOffscreen) != true)
            {
                return false;
            }

            var viewport = TryRead(() => Inner.BoundingRectangle);
            var bounds = TryRead(() => candidate.BoundingRectangle);
            if (viewport.Width <= 0 || viewport.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return false;
            }

            return System.Drawing.Rectangle.Intersect(viewport, bounds) is { Width: > 0, Height: > 0 };
        }

        private static IScrollPattern? FindVerticalScrollPattern(AutomationElement root)
        {
            var rootScroll = root.Patterns.Scroll.PatternOrDefault;
            if (rootScroll?.VerticallyScrollable.ValueOrDefault == true)
            {
                return rootScroll;
            }

            var descendantScroll = FindAutomationDescendants(root)
                .Select(static candidate => candidate.Patterns.Scroll.PatternOrDefault)
                .FirstOrDefault(static scroll => scroll?.VerticallyScrollable.ValueOrDefault == true);
            if (descendantScroll is not null)
            {
                return descendantScroll;
            }

            foreach (var ancestor in EnumerateLocalScrollAncestors(root))
            {
                var ancestorScroll = ancestor.Patterns.Scroll.PatternOrDefault;
                if (ancestorScroll?.VerticallyScrollable.ValueOrDefault == true)
                {
                    return ancestorScroll;
                }
            }

            return null;
        }

        private static IRangeValuePattern? FindVerticalScrollBarRange(AutomationElement root)
        {
            if (root.ControlType == ControlType.ScrollBar && IsVerticalScrollBar(root))
            {
                var rootRange = root.Patterns.RangeValue.PatternOrDefault;
                if (rootRange is not null
                    && rootRange.IsReadOnly.ValueOrDefault == false
                    && rootRange.Maximum.ValueOrDefault > rootRange.Minimum.ValueOrDefault)
                {
                    return rootRange;
                }
            }

            var descendantRange = FindAutomationDescendants(root)
                .Where(static candidate => candidate.ControlType == ControlType.ScrollBar)
                .Where(IsVerticalScrollBar)
                .Select(static candidate => candidate.Patterns.RangeValue.PatternOrDefault)
                .FirstOrDefault(static candidate =>
                    candidate is not null
                    && candidate.IsReadOnly.ValueOrDefault == false
                    && candidate.Maximum.ValueOrDefault > candidate.Minimum.ValueOrDefault);
            if (descendantRange is not null)
            {
                return descendantRange;
            }

            foreach (var ancestor in EnumerateLocalScrollAncestors(root))
            {
                var ancestorRange = FindAutomationDescendants(ancestor)
                    .Where(static candidate => candidate.ControlType == ControlType.ScrollBar)
                    .Where(IsVerticalScrollBar)
                    .Select(static candidate => candidate.Patterns.RangeValue.PatternOrDefault)
                    .FirstOrDefault(static candidate =>
                        candidate is not null
                        && candidate.IsReadOnly.ValueOrDefault == false
                        && candidate.Maximum.ValueOrDefault > candidate.Minimum.ValueOrDefault);
                if (ancestorRange is not null)
                {
                    return ancestorRange;
                }
            }

            return null;
        }

        private static IEnumerable<AutomationElement> EnumerateLocalScrollAncestors(AutomationElement root)
        {
            // A locator may target an inner items panel, but the surrounding page scroll viewer is not part of the popup.
            for (var current = TryRead(() => root.Parent);
                 current is not null && current.ControlType != ControlType.Window;
                 current = TryRead(() => current.Parent))
            {
                if (!HasComparableScrollBounds(root, current))
                {
                    yield break;
                }

                yield return current;
            }
        }

        private static bool HasComparableScrollBounds(AutomationElement root, AutomationElement candidate)
        {
            var rootBounds = TryRead(() => root.BoundingRectangle);
            var candidateBounds = TryRead(() => candidate.BoundingRectangle);
            if (rootBounds.Width <= 0
                || rootBounds.Height <= 0
                || candidateBounds.Width <= 0
                || candidateBounds.Height <= 0)
            {
                return false;
            }

            if (System.Drawing.Rectangle.Intersect(rootBounds, candidateBounds) is not { Width: > 0, Height: > 0 })
            {
                return false;
            }

            var maximumWidth = Math.Max(rootBounds.Width * 2, rootBounds.Width + 128);
            var maximumHeight = Math.Max(rootBounds.Height * 2, rootBounds.Height + 128);
            return candidateBounds.Width <= maximumWidth
                && candidateBounds.Height <= maximumHeight;
        }

        private static bool IsVerticalScrollBar(AutomationElement element)
        {
            var orientation = element.FrameworkAutomationElement.Orientation.ValueOrDefault;
            if (orientation == OrientationType.Vertical)
            {
                return true;
            }

            var bounds = element.BoundingRectangle;
            return bounds.Height > bounds.Width;
        }

        private static bool ScrollBackward(IScrollPattern scroll)
        {
            var previousPosition = scroll.VerticalScrollPercent.ValueOrDefault;
            if (previousPosition <= 0)
            {
                return false;
            }

            scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeDecrement);
            Thread.Sleep(50);
            return scroll.VerticalScrollPercent.ValueOrDefault < previousPosition;
        }

        private static bool ScrollForward(IScrollPattern scroll)
        {
            var previousPosition = scroll.VerticalScrollPercent.ValueOrDefault;
            if (previousPosition < 0 || previousPosition >= 100)
            {
                return false;
            }

            scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
            Thread.Sleep(50);
            return scroll.VerticalScrollPercent.ValueOrDefault > previousPosition;
        }

        private static bool ScrollForward(IRangeValuePattern scrollBarRange)
        {
            var previousPosition = scrollBarRange.Value.ValueOrDefault;
            var maximum = scrollBarRange.Maximum.ValueOrDefault;
            var minimum = scrollBarRange.Minimum.ValueOrDefault;
            var range = maximum - minimum;
            var positionTolerance = Math.Max(0.001, range / 10_000);
            if (previousPosition >= maximum - positionTolerance)
            {
                return false;
            }

            var largeChange = scrollBarRange.LargeChange.ValueOrDefault;
            largeChange = Math.Max(largeChange, range / 10);

            scrollBarRange.SetValue(Math.Min(maximum, previousPosition + largeChange));
            Thread.Sleep(50);
            return scrollBarRange.Value.ValueOrDefault > previousPosition + positionTolerance;
        }

        private static bool ScrollBackward(IRangeValuePattern scrollBarRange)
        {
            var previousPosition = scrollBarRange.Value.ValueOrDefault;
            var maximum = scrollBarRange.Maximum.ValueOrDefault;
            var minimum = scrollBarRange.Minimum.ValueOrDefault;
            var range = maximum - minimum;
            var positionTolerance = Math.Max(0.001, range / 10_000);
            if (previousPosition <= minimum + positionTolerance)
            {
                return false;
            }

            var largeChange = scrollBarRange.LargeChange.ValueOrDefault;
            largeChange = Math.Max(largeChange, range / 10);

            scrollBarRange.SetValue(Math.Max(minimum, previousPosition - largeChange));
            Thread.Sleep(50);
            return scrollBarRange.Value.ValueOrDefault < previousPosition - positionTolerance;
        }

        private static HashSet<string> NormalizeRequestedItems(IEnumerable<string> values)
        {
            var normalized = values.Select(static value => value?.Trim() ?? string.Empty).ToArray();
            if (normalized.Any(string.IsNullOrWhiteSpace)
                || normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
            {
                throw new ArgumentException("Multi-select item values must be non-empty and distinct.", nameof(values));
            }

            return normalized.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static void ValidateAvailableItems(
            IEnumerable<string> availableValues,
            IReadOnlySet<string> requestedValues)
        {
            var available = availableValues.ToArray();
            if (available.Distinct(StringComparer.OrdinalIgnoreCase).Count() != available.Length)
            {
                throw new InvalidOperationException("Multi-select items container exposes duplicate item text.");
            }

            var missing = requestedValues.Where(value => !available.Contains(value, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Multi-select items were not found: [{string.Join(", ", missing)}].");
            }
        }

        private sealed record MultiSelectCheckBox(string Text, FlaUiCheckBoxControl Control);

        private sealed record MultiSelectItemSnapshot(string Text, bool IsChecked);

        private sealed record SelectableItemSnapshot(
            string Text,
            AutomationElement Control,
            bool IsSelected);
    }

    private sealed class FlaUiSearchHistoryItemsControl : ISearchHistoryItemsControl
    {
        private readonly Func<AutomationElement[]> _resolveButtons;
        private readonly string _locator;

        public FlaUiSearchHistoryItemsControl(
            Func<AutomationElement[]> resolveButtons,
            string locator)
        {
            _resolveButtons = resolveButtons;
            _locator = locator;
        }

        public string AutomationId => _locator;

        public string Name => _locator;

        public bool IsEnabled => _resolveButtons().Any(candidate => TryRead(() => candidate.IsEnabled));

        public bool IsAvailable => _resolveButtons().Any(candidate =>
            TryRead(() => candidate.IsAvailable && !candidate.IsOffscreen));

        public IReadOnlyList<string> Items => _resolveButtons()
            .Select(ReadText)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        public void Apply(string itemText)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemText);
            var matches = _resolveButtons()
                .Where(candidate => string.Equals(ReadText(candidate), itemText, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 0)
            {
                throw new InvalidOperationException($"Search history item '{itemText}' was not found.");
            }

            if (matches.Length > 1)
            {
                throw new InvalidOperationException($"Search history item '{itemText}' is ambiguous.");
            }

            new FlaUiButtonControl(matches[0].AsButton()).Invoke();
        }

        private static string ReadText(AutomationElement button)
        {
            return ReadAutomationElementVisibleText(button)
                ?? TryRead(() => button.Name)
                ?? string.Empty;
        }
    }

    private sealed class FlaUiComboBoxControl : FlaUiControlBase<ComboBox>, IComboBoxControl, IReadableTextControl
    {
        public FlaUiComboBoxControl(ComboBox inner) : base(inner)
        {
        }

        public IReadOnlyList<IComboBoxItem> Items =>
            GetSelectableItems()
                .Select(ToComboBoxItem)
                .ToArray();

        public IComboBoxItem? SelectedItem
        {
            get
            {
                var selectedText = ReadSelectedText();
                if (string.IsNullOrWhiteSpace(selectedText))
                {
                    return null;
                }

                return new FlaUiComboBoxTextItem(selectedText, selectedText);
            }
        }

        public string Text => SelectedItem?.Text ?? string.Empty;

        public int SelectedIndex
        {
            get
            {
                var selected = SelectedItem;
                if (selected is null)
                {
                    return -1;
                }

                var selectedText = selected.Text;
                for (var index = 0; index < Items.Count; index++)
                {
                    if (string.Equals(NormalizeLookupText(Items[index].Text), NormalizeLookupText(selectedText), StringComparison.OrdinalIgnoreCase))
                    {
                        return index;
                    }
                }

                return -1;
            }
            set => SelectByIndex(value);
        }

        public void SelectByIndex(int index) => Select(index);

        public void Select(int index)
        {
            var items = GetSelectableItems();
            if (index < 0 || index >= items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            Expand();

            var candidate = items[index];
            var expectedText = NormalizeLookupText(ReadAutomationElementText(candidate));

            try
            {
                Inner.Select(index);
                if (SelectionMatches(expectedText))
                {
                    return;
                }
            }
            catch
            {
                // Fall back to direct item interaction below.
            }

            TryClick(candidate);
        }

        public void Expand()
        {
            try
            {
                Inner.Expand();
            }
            catch
            {
                // Some providers do not expose expand directly.
            }
        }

        internal void SelectItem(string itemText, TimeSpan timeout)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemText);
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Combo-box selection timeout must be positive.");
            }

            var normalizedTarget = NormalizeLookupText(itemText);
            var stopwatch = Stopwatch.StartNew();
            string[] observedItems = [];

            Expand();
            do
            {
                var items = GetSelectableItems();
                observedItems = items
                    .Select(ReadAutomationElementText)
                    .Where(static text => !string.IsNullOrWhiteSpace(text))
                    .Select(static text => text!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var index = items.FindIndex(candidate =>
                    string.Equals(
                        NormalizeLookupText(ReadAutomationElementText(candidate)),
                        normalizedTarget,
                        StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    Select(index);
                    if (SelectionMatches(normalizedTarget))
                    {
                        return;
                    }
                }

                Thread.Sleep(50);
            }
            while (stopwatch.Elapsed < timeout);

            var observed = observedItems.Length == 0
                ? "<none>"
                : string.Join(", ", observedItems.Select(static value => $"'{value}'"));
            throw new InvalidOperationException(
                $"Combo-box item '{itemText}' was not found or selected within {timeout.TotalMilliseconds:0} ms. Observed items: {observed}.");
        }

        private List<AutomationElement> GetSelectableItems()
        {
            var items = new List<AutomationElement>();

            try
            {
                foreach (var item in Inner.Items)
                {
                    if (item is not null && !items.Contains(item))
                    {
                        items.Add(item);
                    }
                }
            }
            catch
            {
                // Some providers do not expose direct combo box items.
            }

            foreach (var candidate in FindAutomationDescendants(Inner))
            {
                if (candidate is null || items.Contains(candidate) || !IsComboItemCandidate(candidate))
                {
                    continue;
                }

                var text = ReadAutomationElementText(candidate);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    items.Add(candidate);
                }
            }

            return items;
        }

        private IComboBoxItem ToComboBoxItem(AutomationElement item)
        {
            if (item is ComboBoxItem comboBoxItem)
            {
                return new FlaUiComboBoxItem(comboBoxItem);
            }

            var text = ReadAutomationElementText(item) ?? string.Empty;
            return new FlaUiComboBoxTextItem(text, text);
        }

        private string? ReadSelectedText()
        {
            var selected = TryRead(() => Inner.SelectedItem);
            if (selected is ComboBoxItem comboBoxItem)
            {
                return ReadAutomationElementText(comboBoxItem);
            }

            if (selected is AutomationElement selectedElement)
            {
                return ReadAutomationElementText(selectedElement);
            }

            var selectedText = selected?.ToString();
            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                return selectedText;
            }

            var selectedCandidate = GetSelectableItems().FirstOrDefault(candidate =>
            {
                try
                {
                    return candidate.Patterns.SelectionItem.IsSupported
                        && candidate.Patterns.SelectionItem.Pattern.IsSelected.Value;
                }
                catch
                {
                    return false;
                }
            });

            if (selectedCandidate is not null)
            {
                return ReadAutomationElementText(selectedCandidate);
            }

            var valuePatternText = TryRead(() =>
            {
                if (Inner.Patterns.Value.IsSupported)
                {
                    return Inner.Patterns.Value.Pattern.Value;
                }

                return null;
            });
            if (!string.IsNullOrWhiteSpace(valuePatternText))
            {
                return valuePatternText;
            }

            return null;
        }

        private bool SelectionMatches(string expectedText)
        {
            if (string.IsNullOrWhiteSpace(expectedText))
            {
                return true;
            }

            return string.Equals(
                NormalizeLookupText(ReadSelectedText()),
                expectedText,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsComboItemCandidate(AutomationElement candidate)
        {
            return candidate.ControlType == ControlType.ListItem
                || candidate.ControlType == ControlType.Text
                || candidate.ControlType == ControlType.Button
                || candidate.ControlType == ControlType.DataItem;
        }

        private static bool TryClick(AutomationElement candidate)
        {
            try
            {
                candidate.Click();
                return true;
            }
            catch
            {
            }

            try
            {
                if (candidate.Patterns.Invoke.IsSupported)
                {
                    candidate.Patterns.Invoke.Pattern.Invoke();
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }

    private sealed class FlaUiComboBoxItem : IComboBoxItem
    {
        private readonly ComboBoxItem _inner;

        public FlaUiComboBoxItem(ComboBoxItem inner)
        {
            _inner = inner;
        }

        public string Text => _inner.Text ?? string.Empty;

        public string Name => _inner.Name ?? Text;
    }

    private sealed record FlaUiComboBoxTextItem(string Text, string Name) : IComboBoxItem;

    private sealed class FlaUiRadioButtonControl : FlaUiControlBase<RadioButton>, IRadioButtonControl, IReadableTextControl
    {
        public FlaUiRadioButtonControl(RadioButton inner) : base(inner)
        {
        }

        public string Text => ReadAutomationElementVisibleText(Inner) ?? string.Empty;

        public bool? IsChecked
        {
            get => TryRead(() => Inner.IsChecked);
            set => Inner.IsChecked = value == true;
        }
    }

    private sealed class FlaUiToggleButtonControl : FlaUiControlBase<ToggleButton>, IToggleButtonControl, IReadableTextControl
    {
        public FlaUiToggleButtonControl(ToggleButton inner) : base(inner)
        {
        }

        public string Text => ReadAutomationElementVisibleText(Inner) ?? string.Empty;

        public bool IsToggled => TryRead(() => Inner.IsToggled) == true;

        public void Toggle()
        {
            Inner.Toggle();
        }
    }

    private sealed class FlaUiSliderControl : FlaUiControlBase<Slider>, ISliderControl
    {
        public FlaUiSliderControl(Slider inner) : base(inner)
        {
        }

        public double Value
        {
            get => TryRead(() => Inner.Value);
            set => Inner.Value = value;
        }
    }

    private sealed class FlaUiProgressBarControl : FlaUiControlBase<ProgressBar>, IProgressBarControl
    {
        public FlaUiProgressBarControl(ProgressBar inner) : base(inner)
        {
        }

        public double Value => TryRead(() => Inner.Value);
    }

    private sealed class FlaUiCalendarControl : FlaUiControlBase<Calendar>, ICalendarControl
    {
        public FlaUiCalendarControl(Calendar inner) : base(inner)
        {
        }

        public IReadOnlyList<DateTime> SelectedDates =>
            TryRead(() => Inner.SelectedDates) ?? Array.Empty<DateTime>();

        public void SelectDate(DateTime selectedDate)
        {
            FlaUiCalendarSelection.SelectDate(Inner, selectedDate);
        }
    }

    private sealed class FlaUiDateTimePickerControl : FlaUiControlBase<DateTimePicker>, IDateTimePickerControl
    {
        public FlaUiDateTimePickerControl(DateTimePicker inner) : base(inner)
        {
        }

        public DateTime? SelectedDate
        {
            get => ReadSelectedDate();
            set
            {
                if (!TrySetSelectedDate(value))
                {
                    throw new InvalidOperationException("Unable to set the selected date for this DateTimePicker");
                }
            }
        }

        private DateTime? ReadSelectedDate()
        {
            var selectedDate = TryRead(() => Inner.SelectedDate);
            if (selectedDate.HasValue)
            {
                return selectedDate.Value;
            }

            var textInput = FindTextInput();
            if (textInput is null)
            {
                return null;
            }

            var text = TryRead(() => textInput.Text);
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var currentCultureDate))
            {
                return currentCultureDate;
            }

            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var invariantDate)
                ? invariantDate
                : null;
        }

        private bool TrySetSelectedDate(DateTime? value)
        {
            if (!value.HasValue)
            {
                try
                {
                    Inner.SelectedDate = null;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            try
            {
                Inner.SelectedDate = value;
                if (ReadSelectedDate()?.Date == value.Value.Date)
                {
                    return true;
                }
            }
            catch
            {
                // Fall back to text input below.
            }

            var textInput = FindTextInput();
            if (textInput is null)
            {
                return false;
            }

            var candidates = new[]
            {
                value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                value.Value.ToString("M/d/yyyy", CultureInfo.InvariantCulture),
                value.Value.ToShortDateString(),
                value.Value.ToString("d", CultureInfo.CurrentCulture)
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    textInput.Text = candidate;
                }
                catch
                {
                    continue;
                }

                if (ReadSelectedDate()?.Date == value.Value.Date)
                {
                    return true;
                }
            }

            return false;
        }

        private TextBox? FindTextInput()
        {
            var rootTextBox = TryRead(() => Inner.AsTextBox());
            if (rootTextBox is not null && TryRead(() => rootTextBox.IsAvailable))
            {
                return rootTextBox;
            }

            var descendant = Inner.FindAllDescendants()
                .FirstOrDefault(candidate => candidate.ControlType == ControlType.Edit);

            return descendant?.AsTextBox();
        }
    }

    private sealed class FlaUiTimePickerControl : FlaUiControlBase<AutomationElement>, ITimePickerControl
    {
        public FlaUiTimePickerControl(AutomationElement inner) : base(inner)
        {
        }

        public TimeSpan? SelectedTime
        {
            get => ReadTime();
            set
            {
                if (!value.HasValue || !TrySetTime(value.Value))
                {
                    throw new InvalidOperationException("Unable to set the selected time for this TimePicker.");
                }
            }
        }

        private TimeSpan? ReadTime()
        {
            var text = ReadValueText();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var invariantTime)
                || TimeSpan.TryParse(text, CultureInfo.CurrentCulture, out invariantTime))
            {
                return invariantTime;
            }

            return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dateTime)
                || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime)
                    ? dateTime.TimeOfDay
                    : null;
        }

        private string? ReadValueText()
        {
            var value = TryRead(() => Inner.Patterns.Value.IsSupported
                ? Inner.Patterns.Value.Pattern.Value
                : null);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return TryRead(() => FindTextInput()?.Text);
        }

        private bool TrySetTime(TimeSpan value)
        {
            foreach (var candidate in new[]
                     {
                         value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
                         value.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
                     })
            {
                try
                {
                    if (Inner.Patterns.Value.IsSupported)
                    {
                        Inner.Patterns.Value.Pattern.SetValue(candidate);
                    }
                    else if (FindTextInput() is { } textBox)
                    {
                        textBox.Text = candidate;
                    }
                    else
                    {
                        return false;
                    }
                }
                catch
                {
                    continue;
                }

                if (ReadTime() == value)
                {
                    return true;
                }
            }

            return false;
        }

        private TextBox? FindTextInput()
        {
            try
            {
                return Inner.FindAllDescendants()
                    .FirstOrDefault(static candidate => candidate.ControlType == ControlType.Edit)
                    ?.AsTextBox();
            }
            catch
            {
                return null;
            }
        }
    }

    private sealed class FlaUiExpanderControl : FlaUiControlBase<AutomationElement>, IExpanderControl
    {
        public FlaUiExpanderControl(AutomationElement inner) : base(inner)
        {
        }

        public bool IsExpanded
        {
            get
            {
                var pattern = Inner.Patterns.ExpandCollapse.PatternOrDefault;
                if (pattern is not null)
                {
                    return pattern.ExpandCollapseState.Value is
                        ExpandCollapseState.Expanded or ExpandCollapseState.PartiallyExpanded;
                }

                var header = ResolveHeaderToggle();
                return header.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On;
            }
        }

        public void Expand() => SetExpanded(true);

        public void Collapse() => SetExpanded(false);

        private void SetExpanded(bool expanded)
        {
            if (IsExpanded == expanded)
            {
                return;
            }

            var pattern = Inner.Patterns.ExpandCollapse.PatternOrDefault;
            if (pattern is not null)
            {
                if (expanded)
                {
                    pattern.Expand();
                }
                else
                {
                    pattern.Collapse();
                }

                return;
            }

            ResolveHeaderToggle().Patterns.Toggle.Pattern.Toggle();
        }

        private AutomationElement ResolveHeaderToggle()
        {
            var level = Inner.FindAllChildren();
            while (level.Length > 0)
            {
                var candidates = level
                    .Where(static candidate => candidate.Patterns.Toggle.IsSupported)
                    .ToArray();
                if (candidates.Length == 1)
                {
                    return candidates[0];
                }

                if (candidates.Length > 1)
                {
                    throw new InvalidOperationException(
                        $"Expander '{AutomationId}' has multiple accessible header toggle candidates at the same depth.");
                }

                level = level.SelectMany(static candidate => candidate.FindAllChildren()).ToArray();
            }

            throw new InvalidOperationException(
                $"Expander '{AutomationId}' exposes neither ExpandCollapse pattern nor an accessible header toggle.");
        }
    }

    private static class FlaUiContextMenuRuntime
    {
        public static void Invoke(AutomationElement owner, IReadOnlyList<string> path, int timeoutMs)
        {
            var exactPath = MenuPathValue.Normalize(path);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var budget = UiOperationTimeoutBudget.Start(timeoutMs, "context-menu");
            UiWait.Until(
                () => FlaUiMenuControl.IsInteractable(owner),
                static ready => ready,
                new UiWaitOptions
                {
                    Timeout = budget.Remaining,
                    PollInterval = TimeSpan.FromMilliseconds(50)
                },
                $"Context-menu owner '{TryRead(() => owner.AutomationId)}' did not become visible and enabled.");

            var visibleBefore = FindVisibleMenuItems(owner)
                .Select(GetAutomationElementIdentity)
                .ToHashSet(StringComparer.Ordinal);
            owner.Focus();
            owner.RightClick();

            var popupItems = UiWait.Until(
                () => FindVisibleMenuItems(owner)
                    .Where(candidate => !visibleBefore.Contains(GetAutomationElementIdentity(candidate)))
                    .ToArray(),
                static candidates => candidates.Length > 0,
                new UiWaitOptions
                {
                    Timeout = budget.Remaining,
                    PollInterval = TimeSpan.FromMilliseconds(50)
                },
                $"Control '{TryRead(() => owner.AutomationId)}' did not open an addressable context menu.");

            var popupItemIdentities = popupItems
                .Select(GetAutomationElementIdentity)
                .ToHashSet(StringComparer.Ordinal);
            var rootItems = popupItems
                .Where(static candidate => !string.IsNullOrWhiteSpace(TryRead(() => candidate.Name)))
                .Where(candidate => !HasCaptionedPopupAncestor(candidate, popupItemIdentities))
                .Select(static candidate => candidate.AsMenuItem())
                .ToArray();
            var popupRoots = rootItems
                .Select(static item => TryRead(() => item.Parent))
                .Where(static parent => parent is not null)
                .Select(static parent => GetAutomationElementIdentity(parent!))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (rootItems.Length == 0 || popupRoots.Length != 1)
            {
                Keyboard.Press(VirtualKeyShort.ESCAPE);
                throw new InvalidOperationException(
                    $"Context-menu owner '{TryRead(() => owner.AutomationId)}' opened an ambiguous popup.");
            }

            try
            {
                FlaUiMenuControl.InvokePath(
                    () => rootItems,
                    exactPath,
                    budget,
                    rootIsPopup: true);
            }
            catch
            {
                Keyboard.Press(VirtualKeyShort.ESCAPE);
                throw;
            }
        }

        private static AutomationElement[] FindVisibleMenuItems(AutomationElement owner)
        {
            var processId = TryRead(() => owner.FrameworkAutomationElement.ProcessId.ValueOrDefault);
            var desktop = TryRead(() => owner.Automation.GetDesktop());
            if (processId <= 0 || desktop is null)
            {
                return [];
            }

            var roots = TryRead(() => desktop.FindAllChildren(factory => factory.ByProcessId(processId))) ?? [];
            return roots
                .SelectMany(static root =>
                    (TryRead(() => root.FindAllDescendants()) ?? []).Prepend(root))
                .Where(static candidate => candidate.ControlType == ControlType.MenuItem)
                .Where(static candidate =>
                    TryRead(() => candidate.IsAvailable)
                    && TryRead(() => candidate.BoundingRectangle) is { Width: > 0, Height: > 0 })
                .DistinctBy(GetAutomationElementIdentity)
                .ToArray();
        }

        private static bool HasCaptionedPopupAncestor(
            AutomationElement candidate,
            HashSet<string> popupItemIdentities)
        {
            for (var parent = TryRead(() => candidate.Parent);
                 parent is not null
                 && parent.ControlType == ControlType.MenuItem
                 && popupItemIdentities.Contains(GetAutomationElementIdentity(parent));
                 parent = TryRead(() => parent.Parent))
            {
                if (!string.IsNullOrWhiteSpace(TryRead(() => parent.Name)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class FlaUiMenuControl : FlaUiControlBase<Menu>, IMenuControl
    {
        public FlaUiMenuControl(Menu inner) : base(inner)
        {
        }

        public void InvokeItem(IReadOnlyList<string> path, int timeoutMs)
        {
            var exactPath = MenuPathValue.Normalize(path);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var budget = UiOperationTimeoutBudget.Start(timeoutMs, "menu");
            UiWait.Until(
                () => TryRead(() => Inner.IsEnabled),
                static enabled => enabled,
                new UiWaitOptions
                {
                    Timeout = budget.Remaining,
                    PollInterval = TimeSpan.FromMilliseconds(50)
                },
                $"Menu '{AutomationId}' did not become enabled.");
            InvokePath(
                () => Inner.Items.ToArray(),
                exactPath,
                budget,
                rootIsPopup: false);
        }

        internal static void InvokePath(
            Func<MenuItem[]> rootItems,
            IReadOnlyList<string> exactPath,
            UiOperationTimeoutBudget budget,
            bool rootIsPopup)
        {
            Func<MenuItem[]> currentItems = rootItems;
            var openedItems = new List<MenuItem>();
            try
            {
                for (var index = 0; index < exactPath.Count; index++)
                {
                    var item = FindUniqueItem(currentItems, exactPath[index], budget.Remaining);
                    if (index == exactPath.Count - 1)
                    {
                        InvokeLeaf(item, budget.Remaining, rootIsPopup || index > 0);
                        return;
                    }

                    if (!item.IsEnabled)
                    {
                        throw new InvalidOperationException($"Menu item '{exactPath[index]}' is disabled.");
                    }

                    var childItems = ExpandAndReadChildItems(item, budget.Remaining);
                    openedItems.Add(item);
                    currentItems = () => childItems;
                }
            }
            catch
            {
                CloseOpenedItems(openedItems);
                throw;
            }
        }

        private static void CloseOpenedItems(List<MenuItem> openedItems)
        {
            for (var index = openedItems.Count - 1; index >= 0; index--)
            {
                var pattern = TryRead(() => openedItems[index].Patterns.ExpandCollapse.PatternOrDefault);
                if (pattern is not null)
                {
                    if (TryRead(() => pattern.ExpandCollapseState.Value) != ExpandCollapseState.Collapsed)
                    {
                        pattern.Collapse();
                    }

                    continue;
                }

                Keyboard.Press(VirtualKeyShort.ESCAPE);
            }
        }

        private static MenuItem FindUniqueItem(
            Func<MenuItem[]> itemSource,
            string caption,
            TimeSpan timeout)
        {
            var matches = UiWait.Until(
                () => itemSource()
                    .Where(item => string.Equals(
                        ReadVisibleCaption(item),
                        caption,
                        StringComparison.Ordinal))
                    .ToArray(),
                static candidates => candidates.Length > 0,
                new UiWaitOptions
                {
                    Timeout = timeout,
                    PollInterval = TimeSpan.FromMilliseconds(50)
                },
                $"Menu item '{caption}' did not become available.");
            return matches.Length == 1
                ? matches[0]
                : throw new InvalidOperationException(
                    $"Menu item caption '{caption}' is ambiguous among siblings ({matches.Length} matches).");
        }

        private static void InvokeLeaf(MenuItem item, TimeSpan timeout, bool waitForPopupClose)
        {
            if (!item.IsEnabled)
            {
                throw new InvalidOperationException($"Menu item '{item.Text}' is disabled.");
            }

            if (HasSubmenu(item))
            {
                throw new InvalidOperationException($"Menu item '{item.Text}' is not a leaf item.");
            }

            InvokeNativeItem(item);
            if (waitForPopupClose)
            {
                WaitForPopupClose(item, timeout);
            }
        }

        internal static void InvokeNativeItem(MenuItem item)
        {
            if (item.Patterns.Invoke.IsSupported)
            {
                item.Invoke();
            }
            else
            {
                item.Click();
            }
        }

        private static string ReadVisibleCaption(MenuItem item)
        {
            var descendantCaption = (TryRead(() => item.FindAllDescendants()) ?? [])
                .Where(static candidate => candidate.ControlType == ControlType.Text)
                .Select(static candidate => TryRead(() => candidate.Name))
                .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text));
            return !string.IsNullOrWhiteSpace(descendantCaption)
                ? descendantCaption.Trim()
                : (TryRead(() => item.Text) ?? string.Empty).Trim();
        }

        internal static bool HasSubmenu(MenuItem item)
        {
            var pattern = item.Patterns.ExpandCollapse.PatternOrDefault;
            return pattern is not null
                && pattern.ExpandCollapseState.Value != ExpandCollapseState.LeafNode;
        }

        internal static MenuItem[] ExpandAndReadChildItems(MenuItem item, TimeSpan timeout)
        {
            var pattern = item.Patterns.ExpandCollapse.PatternOrDefault;
            if (pattern is not null && pattern.ExpandCollapseState.Value == ExpandCollapseState.Collapsed)
            {
                pattern.Expand();
            }
            else if (pattern is null)
            {
                item.Focus();
                Keyboard.Press(VirtualKeyShort.RETURN);
            }

            return UiWait.Until(
                () => item.FindAllChildren(static condition => condition.ByControlType(ControlType.MenuItem))
                    .Select(static child => child.AsMenuItem())
                    .ToArray(),
                static children => children.Length > 0,
                new UiWaitOptions
                {
                    Timeout = timeout,
                    PollInterval = TimeSpan.FromMilliseconds(50)
                },
                $"Menu item '{item.Text}' did not expose its submenu items.");
        }

        internal static void WaitUntilVisible(MenuItem item, TimeSpan timeout)
        {
            UiWait.Until(
                () => IsInteractable(item),
                static ready => ready,
                new UiWaitOptions
                {
                    Timeout = timeout,
                    PollInterval = TimeSpan.FromMilliseconds(50)
                },
                $"Menu item '{item.Text}' did not become visible and enabled.");
        }

        internal static void WaitForPopupClose(MenuItem item, TimeSpan timeout)
        {
            UiWait.Until(
                () => !IsVisible(item),
                static closed => closed,
                new UiWaitOptions
                {
                    Timeout = timeout,
                    PollInterval = TimeSpan.FromMilliseconds(50)
                },
                $"Menu item '{item.Text}' was invoked, but its menu did not close.");
        }

        internal static bool IsInteractable(MenuItem item)
        {
            return IsVisible(item) && TryRead(() => item.IsEnabled);
        }

        internal static bool IsInteractable(AutomationElement element)
        {
            return TryRead(() => element.IsAvailable)
                && TryRead(() => element.IsEnabled)
                && !TryRead(() => element.IsOffscreen)
                && TryRead(() => element.BoundingRectangle) is { Width: > 0, Height: > 0 };
        }

        internal static bool IsVisible(MenuItem item)
        {
            return TryRead(() => item.IsAvailable)
                && TryRead(() => item.BoundingRectangle) is { Width: > 0, Height: > 0 };
        }

    }

    private sealed class FlaUiMenuItemControl : IMenuItemControl
    {
        private readonly Func<AutomationElement[]> _searchRoots;
        private readonly UiControlDefinition _definition;

        public FlaUiMenuItemControl(
            Func<AutomationElement[]> searchRoots,
            UiControlDefinition definition)
        {
            _searchRoots = searchRoots;
            _definition = definition;
        }

        public string AutomationId => _definition.LocatorKind == UiLocatorKind.AutomationId
            ? _definition.LocatorValue
            : _definition.PropertyName;

        public string Name => _definition.LocatorKind == UiLocatorKind.Name
            ? _definition.LocatorValue
            : _definition.PropertyName;

        public bool IsEnabled => FindVisibleMatches().FirstOrDefault()?.IsEnabled ?? false;

        public void Invoke(int timeoutMs)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var budget = UiOperationTimeoutBudget.Start(timeoutMs, "menu-item");
            var item = UiWait.Until(
                FindVisibleMatches,
                static matches => matches.Length > 0,
                new UiWaitOptions
                {
                    Timeout = budget.Remaining,
                    PollInterval = TimeSpan.FromMilliseconds(50)
                },
                $"Direct menu item with locator [{_definition.LocatorKind}:{_definition.LocatorValue}] " +
                "did not become addressable. Use the owning menu and an exact path for nested items.");
            if (item.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Menu item locator '{_definition.LocatorValue}' is ambiguous ({item.Length} visible matches).");
            }

            var target = item[0];
            if (!target.IsEnabled)
            {
                throw new InvalidOperationException($"Menu item '{target.Text}' is disabled.");
            }

            if (FlaUiMenuControl.HasSubmenu(target))
            {
                throw new InvalidOperationException($"Menu item '{target.Text}' is not a leaf item.");
            }

            FlaUiMenuControl.WaitUntilVisible(target, budget.Remaining);
            var waitForPopupClose = HasMenuItemAncestor(target);
            if (waitForPopupClose)
            {
                target.Click();
            }
            else
            {
                FocusContainingWindow(target);
                TryFocus(target);
                MoveMouseImmediatelyTo(target);
                Mouse.LeftClick();
            }

            if (waitForPopupClose)
            {
                FlaUiMenuControl.WaitForPopupClose(target, budget.Remaining);
            }
        }

        private MenuItem[] FindVisibleMatches()
        {
            return _searchRoots()
                .SelectMany(static root =>
                    (TryRead(() => root.FindAllDescendants()) ?? []).Prepend(root))
                .Where(static candidate => candidate.ControlType == ControlType.MenuItem)
                .Where(static candidate => TryRead(() => candidate.IsAvailable))
                .Select(static candidate => candidate.AsMenuItem())
                .Where(FlaUiMenuControl.IsVisible)
                .Where(MatchesDefinition)
                .DistinctBy(GetVisibleIdentity)
                .ToArray();
        }

        private static string GetVisibleIdentity(MenuItem item)
        {
            return string.Join(
                '|',
                TryRead(() => item.FrameworkAutomationElement.ProcessId.ValueOrDefault),
                TryRead(() => item.AutomationId),
                TryRead(() => item.Name),
                TryRead(() => item.BoundingRectangle));
        }

        private bool MatchesDefinition(MenuItem item)
        {
            if (_definition.LocatorKind == UiLocatorKind.AutomationId
                && string.Equals(TryRead(() => item.AutomationId), _definition.LocatorValue, StringComparison.Ordinal))
            {
                return true;
            }

            return (_definition.LocatorKind == UiLocatorKind.Name || _definition.FallbackToName)
                && string.Equals(TryRead(() => item.Name), _definition.LocatorValue, StringComparison.Ordinal);
        }

        private static bool HasMenuItemAncestor(MenuItem item)
        {
            for (var current = TryRead(() => item.Parent); current is not null; current = TryRead(() => current.Parent))
            {
                if (current.ControlType == ControlType.MenuItem)
                {
                    return true;
                }
            }

            return false;
        }

        private static void FocusContainingWindow(AutomationElement item)
        {
            for (var current = TryRead(() => item.Parent); current is not null; current = TryRead(() => current.Parent))
            {
                if (current.ControlType != ControlType.Window)
                {
                    continue;
                }

                TryFocus(current);
                Thread.Sleep(50);
                return;
            }
        }

    }

    private sealed class FlaUiSpinnerControl : FlaUiControlBase<Spinner>, ISpinnerControl
    {
        public FlaUiSpinnerControl(Spinner inner) : base(inner)
        {
        }

        public double Value
        {
            get => TryRead(() => Inner.Value);
            set
            {
                try
                {
                    Inner.Value = value;
                }
                catch (ArgumentException) when (TryEnterValue(value))
                {
                    // Avalonia NumericUpDown can expose a RangeValue pattern that rejects
                    // values accepted by its visible editor. Use that real editor as the
                    // provider fallback and let the Page postcondition verify the result.
                }
            }
        }

        private bool TryEnterValue(double value)
        {
            var input = TryRead(() => Inner.FindAllDescendants()
                .FirstOrDefault(static candidate => candidate.ControlType == ControlType.Edit)
                ?.AsTextBox());
            var text = value.ToString("R", CultureInfo.InvariantCulture);
            if (input is not null && TryRead(() => input.IsEnabled))
            {
                input.EnterText(text);
                Keyboard.Press(VirtualKeyShort.RETURN);
                return true;
            }

            Inner.Focus();
            Inner.Click();
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            Keyboard.Type(text);
            Keyboard.Press(VirtualKeyShort.RETURN);
            return true;
        }
    }

    private sealed class FlaUiTabControl : FlaUiControlBase<Tab>, ITabControl
    {
        public FlaUiTabControl(Tab inner) : base(inner)
        {
        }

        public IReadOnlyList<ITabItemControl> Items =>
            Inner.TabItems.Select(item => (ITabItemControl)new FlaUiTabItemControl(item)).ToArray();

        public void SelectTabItem(string itemText)
        {
            Inner.SelectTabItem(itemText);
        }
    }

    private sealed class FlaUiTabItemControl : FlaUiControlBase<TabItem>, ITabItemControl, IReadableTextControl
    {
        public FlaUiTabItemControl(TabItem inner) : base(inner)
        {
        }

        public string Text => ReadAutomationElementVisibleText(Inner) ?? string.Empty;

        public bool IsSelected => TryRead(() => Inner.IsSelected) == true;

        public void SelectTab()
        {
            try
            {
                Inner.Click();
            }
            catch
            {
                Inner.Select();
            }

            if (TryRead(() => Inner.IsSelected) != true)
            {
                Inner.Select();
            }
        }
    }

    private sealed class FlaUiTreeControl : FlaUiControlBase<Tree>, ITreeControl
    {
        public FlaUiTreeControl(Tree inner) : base(inner)
        {
        }

        public IReadOnlyList<ITreeItemControl> Items =>
            Inner.Items.Select(item => (ITreeItemControl)new FlaUiTreeItemControl(item)).ToArray();

        public ITreeItemControl? SelectedTreeItem
        {
            get
            {
                var selected = TryRead(() => Inner.SelectedTreeItem);
                return selected is null ? null : new FlaUiTreeItemControl(selected);
            }
        }
    }

    private sealed class FlaUiTreeItemControl : FlaUiControlBase<TreeItem>, ITreeItemControl, IReadableTextControl
    {
        private bool _selectedByInteraction;

        public FlaUiTreeItemControl(TreeItem inner) : base(inner)
        {
        }

        public bool IsSelected
        {
            get
            {
                if (_selectedByInteraction)
                {
                    return true;
                }

                if (TryRead(() => Inner.IsSelected) == true)
                {
                    return true;
                }

                try
                {
                    return Inner.Patterns.SelectionItem.IsSupported
                        && Inner.Patterns.SelectionItem.Pattern.IsSelected.Value;
                }
                catch
                {
                    return false;
                }
            }
            set
            {
                if (value)
                {
                    SelectNode();
                    return;
                }

                _selectedByInteraction = false;

                try
                {
                    Inner.IsSelected = false;
                }
                catch
                {
                    // Tree nodes without selection support cannot be force-unselected.
                }
            }
        }

        public string Text => TryRead(() => Inner.Text) ?? ReadAutomationElementVisibleText(Inner) ?? string.Empty;

        public IReadOnlyList<ITreeItemControl> Items =>
            Inner.Items.Select(item => (ITreeItemControl)new FlaUiTreeItemControl(item)).ToArray();

        public void Expand()
        {
            try
            {
                Inner.Expand();
            }
            catch
            {
                // Ignore expansion failures for leaf nodes.
            }
        }

        public void SelectNode()
        {
            if (TrySelectTreeItem(Inner))
            {
                _selectedByInteraction = true;
                return;
            }

            var normalizedTargetText = NormalizeLookupText(ReadAutomationElementText(Inner));
            foreach (var candidate in GetFallbackSelectionCandidates(normalizedTargetText))
            {
                if (TryActivateTreeSelectionCandidate(candidate))
                {
                    _selectedByInteraction = true;
                    return;
                }
            }
        }

        private IEnumerable<AutomationElement> GetFallbackSelectionCandidates(string normalizedTargetText)
        {
            return Inner.FindAllDescendants()
                .Where(candidate => candidate is not null && IsTreeSelectionCandidate(candidate))
                .OrderBy(candidate => GetTreeSelectionCandidatePriority(candidate, normalizedTargetText));
        }
    }

    private sealed class FlaUiGridControl : FlaUiControlBase<Grid>, IGridUserActionControl, IAddressableGridControl, IGridColumnMetadataControl
    {
        private readonly AutomationElement _searchRoot;

        public FlaUiGridControl(AutomationElement searchRoot, Grid inner) : base(inner)
        {
            _searchRoot = searchRoot ?? throw new ArgumentNullException(nameof(searchRoot));
        }

        public IReadOnlyList<IGridRowControl> Rows =>
            Inner.Rows.Select(row => (IGridRowControl)new FlaUiGridRowControl(row)).ToArray();

        public IReadOnlyList<string> ColumnNames =>
            (TryRead(() => Inner.ColumnHeaders) ?? Array.Empty<GridHeader>())
            .Select(header => ReadAutomationElementText(header) ?? header.Name ?? string.Empty)
            .ToArray();

        public IGridRowControl? GetRowByIndex(int index)
        {
            try
            {
                var row = Inner.GetRowByIndex(index);
                return row is null ? null : new FlaUiGridRowControl(row);
            }
            catch
            {
                var rows = Inner.Rows;
                return index >= 0 && index < rows.Length
                    ? new FlaUiGridRowControl(rows[index])
                    : null;
            }
        }

        public bool TryGetColumnIndex(string columnName, out int columnIndex)
        {
            var matches = ColumnNames
                .Select(static (name, index) => (name, index))
                .Where(candidate => string.Equals(candidate.name, columnName?.Trim(), StringComparison.Ordinal))
                .Select(static candidate => candidate.index)
                .Take(2)
                .ToArray();
            columnIndex = matches.Length == 1 ? matches[0] : -1;
            return matches.Length == 1;
        }

        public GridRowResolution ResolveRow(GridRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var matches = FindMatchingRows(row, timeoutMs);
            var description = $"grid='{AutomationId}'; selector={GridRuntimeResolver.DescribeRowSelector(row)}; matches={matches.Length}; rows={Rows.Count}";
            return matches.Length switch
            {
                0 => GridRowResolution.NotFound(description),
                1 => GridRowResolution.Unique(description),
                _ => GridRowResolution.Ambiguous(matches.Length, description)
            };
        }

        public GridCellValueSnapshot ReadCell(GridCellAddress address, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(address);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var budget = Stopwatch.StartNew();
            var row = ResolveUniqueRow(address.Row, timeoutMs);
            EnsureRemainingBudget(budget, timeoutMs, "read a grid cell");
            var columnIndex = ResolveColumnIndex(address.ColumnName);
            var cell = TryRead(() => row.Cells.ElementAtOrDefault(columnIndex))
                ?? throw new InvalidOperationException(
                    $"Grid column '{address.ColumnName}' was not found in the selected row of grid '{AutomationId}'.");
            var value = new FlaUiGridCellControl(cell).Value;
            return new GridCellValueSnapshot(value, value, GridCellValueKind.Text);
        }

        public string CopyCell(GridCellAddress address, int timeoutMs) =>
            ReadCell(address, timeoutMs).DisplayText ?? string.Empty;

        public void EditCell(GridCellAddress address, GridCellValueEditRequest request, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(address);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var budget = Stopwatch.StartNew();
            var row = ResolveUniqueRow(address.Row, timeoutMs);
            var columnIndex = ResolveColumnIndex(address.ColumnName);
            var cell = TryRead(() => row.Cells.ElementAtOrDefault(columnIndex))
                ?? throw new InvalidOperationException(
                    $"Grid column '{address.ColumnName}' was not found in the selected row of grid '{AutomationId}'.");
            TryRead(() =>
            {
                row.ScrollIntoView();
                return true;
            });

            TryDoubleClick(cell, out _);
            var candidates = new[] { (AutomationElement)cell }
                .Concat(FindAutomationDescendants(cell))
                .ToArray();
            switch (request.EditorKind)
            {
                case GridCellEditorKind.CheckBox:
                    SetNativeCheckBox(cell, candidates, request);
                    break;
                case GridCellEditorKind.ComboBox:
                    SelectNativeComboBox(cell, candidates, request, RemainingMilliseconds(budget, timeoutMs));
                    break;
                case GridCellEditorKind.SearchPicker:
                    SelectNativeSearchPicker(cell, candidates, request, RemainingMilliseconds(budget, timeoutMs));
                    break;
                case GridCellEditorKind.Number:
                    if (!TrySetNativeSpinner(cell, candidates, request))
                    {
                        EnterNativeGridText(cell, candidates, request);
                    }
                    break;
                case GridCellEditorKind.Text:
                    EnterNativeGridText(cell, candidates, request);
                    break;
                case GridCellEditorKind.Date:
                    SetNativeDate(cell, candidates, request, RemainingMilliseconds(budget, timeoutMs));
                    break;
                case GridCellEditorKind.Time:
                    SetNativeTime(cell, candidates, request);
                    break;
                case GridCellEditorKind.Color:
                    EnterNativeGridText(
                        cell,
                        candidates,
                        request with { Value = ColorValue.Normalize(request.Value) });
                    break;
                default:
                    throw new System.NotSupportedException(
                        $"Native grid '{AutomationId}' does not expose a standard '{request.EditorKind}' cell editor. "
                        + "Register a declarative grid definition with editor parts for this template column.");
            }

            if (request.CommitMode == GridCellEditCommitMode.Cancel)
            {
                var cancel = ResolveNativeEditorPart(cell, request.EditorParts?.CancelButton);
                if (cancel is not null)
                {
                    cancel.Click();
                }
                else
                {
                    TryFocus(cell);
                    Keyboard.Press(VirtualKeyShort.ESCAPE);
                }

                return;
            }

            var confirm = ResolveNativeEditorPart(cell, request.EditorParts?.ConfirmButton);
            if (confirm is not null)
            {
                confirm.Click();
            }
            else
            {
                Keyboard.Press(VirtualKeyShort.RETURN);
            }
        }

        public void OpenRow(GridRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var resolved = ResolveUniqueRow(row, timeoutMs);
            TryRead(() =>
            {
                resolved.ScrollIntoView();
                return true;
            });
            if (!TryDoubleClick(resolved, out var exception))
            {
                throw new InvalidOperationException(
                    $"Grid row '{GridRuntimeResolver.DescribeRowSelector(row)}' could not be opened in grid '{AutomationId}'.",
                    exception);
            }
        }

        private int[] FindMatchingRows(GridRowSelector selector, int timeoutMs)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var columns = selector.Conditions
                .Select(condition => (Index: ResolveColumnIndex(condition.ColumnName), condition.Value))
                .ToArray();
            var stopwatch = Stopwatch.StartNew();
            var candidates = TryRead(() => Inner.GetRowsByValue(columns[0].Index, columns[0].Value, 0))
                ?? Array.Empty<GridRow>();
            var matches = new HashSet<int>();
            foreach (var row in candidates)
            {
                EnsureRemainingBudget(stopwatch, timeoutMs, "resolve a stable grid row");
                var cells = TryRead(() => row.Cells) ?? Array.Empty<GridCell>();
                if (!columns.All(condition =>
                        condition.Index < cells.Length
                        && string.Equals(
                            new FlaUiGridCellControl(cells[condition.Index]).Value,
                            condition.Value,
                            StringComparison.Ordinal)))
                {
                    continue;
                }

                var rowIndex = ReadGridRowIndex(row);
                if (rowIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Grid '{AutomationId}' returned a matching row without a GridItem row index; "
                        + "the virtualized row cannot be re-resolved safely.");
                }

                matches.Add(rowIndex);
            }

            return matches.ToArray();
        }

        private GridRow ResolveUniqueRow(GridRowSelector selector, int timeoutMs)
        {
            var stopwatch = Stopwatch.StartNew();
            var matches = FindMatchingRows(selector, timeoutMs);
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Grid row selector '{GridRuntimeResolver.DescribeRowSelector(selector)}' matched {matches.Length} rows in grid '{AutomationId}'; expected exactly one.");
            }

            return ResolveCurrentGridRow(matches[0], selector, stopwatch, timeoutMs);
        }

        private GridRow ResolveCurrentGridRow(
            int rowIndex,
            GridRowSelector selector,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var columns = selector.Conditions
                .Select(condition => (Index: ResolveColumnIndex(condition.ColumnName), condition.Value))
                .ToArray();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                var row = TryRead(() => Inner.GetRowByIndex(rowIndex));
                if (row is not null)
                {
                    TryRead(() =>
                    {
                        row.ScrollIntoView();
                        return true;
                    });
                    var cells = TryRead(() => row.Cells) ?? Array.Empty<GridCell>();
                    if (columns.All(condition =>
                            condition.Index < cells.Length
                            && string.Equals(
                                new FlaUiGridCellControl(cells[condition.Index]).Value,
                                condition.Value,
                                StringComparison.Ordinal)))
                    {
                        return row;
                    }
                }

                Thread.Sleep(25);
            }

            throw new TimeoutException(
                $"Grid row selector '{GridRuntimeResolver.DescribeRowSelector(selector)}' was unique at row {rowIndex} "
                + $"but could not be re-resolved in grid '{AutomationId}' within the operation timeout.");
        }

        private static int ReadGridRowIndex(GridRow row)
        {
            var cells = TryRead(() => row.Cells) ?? Array.Empty<GridCell>();
            return cells.Length == 0
                ? -1
                : TryRead(() => cells[0].Patterns.GridItem.PatternOrDefault?.Row.ValueOrDefault ?? -1);
        }

        private static int RemainingMilliseconds(Stopwatch stopwatch, int timeoutMs)
        {
            var remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                throw new TimeoutException("The grid operation exceeded its timeout while resolving the stable row.");
            }

            return remaining;
        }

        private static void EnsureRemainingBudget(Stopwatch stopwatch, int timeoutMs, string operation)
        {
            if (stopwatch.ElapsedMilliseconds >= timeoutMs)
            {
                throw new TimeoutException($"The grid operation exceeded its timeout before it could {operation}.");
            }
        }

        private int ResolveColumnIndex(string columnName)
        {
            var matches = ColumnNames.Count(candidate => string.Equals(
                candidate,
                columnName?.Trim(),
                StringComparison.Ordinal));
            if (matches > 1)
            {
                throw new InvalidOperationException(
                    $"Grid column '{columnName}' is ambiguous in grid '{AutomationId}' ({matches} matches).");
            }

            if (!TryGetColumnIndex(columnName, out var columnIndex))
            {
                throw new InvalidOperationException(
                    $"Grid column '{columnName}' was not found. Available columns: {string.Join(", ", ColumnNames)}.");
            }

            return columnIndex;
        }

        private void EnterNativeGridText(
            AutomationElement cell,
            IEnumerable<AutomationElement> candidates,
            GridCellValueEditRequest request)
        {
            var input = ResolveNativeEditorPart(cell, request.EditorParts?.Input)
                ?? candidates.FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Edit)
                ?? throw new InvalidOperationException("The active grid cell does not expose a writable text editor.");
            new FlaUiTextBoxControl(input.AsTextBox()).Enter(request.Value);
        }

        private bool TrySetNativeSpinner(
            AutomationElement cell,
            IEnumerable<AutomationElement> candidates,
            GridCellValueEditRequest request)
        {
            var configured = ResolveNativeEditorPart(cell, request.EditorParts?.Input);
            var spinner = configured is not null && TryRead(() => configured.ControlType) == ControlType.Spinner
                ? configured
                : candidates.FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Spinner);
            if (spinner is null || !double.TryParse(request.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            new FlaUiSpinnerControl(spinner.AsSpinner()).Value = number;
            return true;
        }

        private void SelectNativeComboBox(
            AutomationElement cell,
            IEnumerable<AutomationElement> candidates,
            GridCellValueEditRequest request,
            int timeoutMs)
        {
            var configured = ResolveNativeEditorPart(cell, request.EditorParts?.Input);
            var combo = configured is not null && TryRead(() => configured.ControlType) == ControlType.ComboBox
                ? configured
                : candidates.FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.ComboBox)
                ?? throw new InvalidOperationException("The active grid cell does not expose a ComboBox editor.");
            new FlaUiComboBoxControl(combo.AsComboBox()).SelectItem(request.Value, TimeSpan.FromMilliseconds(timeoutMs));
        }

        private void SetNativeCheckBox(
            AutomationElement cell,
            IEnumerable<AutomationElement> candidates,
            GridCellValueEditRequest request)
        {
            if (!bool.TryParse(request.Value, out var expected))
            {
                throw new InvalidOperationException($"Grid check-box value '{request.Value}' is not Boolean.");
            }

            var configured = ResolveNativeEditorPart(cell, request.EditorParts?.Input);
            var element = configured is not null && TryRead(() => configured.ControlType) == ControlType.CheckBox
                ? configured
                : candidates.FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.CheckBox)
                ?? throw new InvalidOperationException("The active grid cell does not expose a CheckBox editor.");
            var checkBox = new FlaUiCheckBoxControl(element.AsCheckBox());
            if (checkBox.IsChecked != expected)
            {
                checkBox.IsChecked = expected;
            }
        }

        private void SelectNativeSearchPicker(
            AutomationElement cell,
            IReadOnlyList<AutomationElement> candidates,
            GridCellValueEditRequest request,
            int timeoutMs)
        {
            if (string.IsNullOrWhiteSpace(request.SearchText))
            {
                throw new ArgumentException("Search text cannot be empty for a search-picker grid edit.", nameof(request));
            }

            var stopwatch = Stopwatch.StartNew();
            TimeSpan Remaining() => TimeSpan.FromMilliseconds(Math.Max(0, timeoutMs - stopwatch.ElapsedMilliseconds));
            var input = ResolveNativeEditorPart(cell, request.EditorParts?.Input)
                ?? candidates.FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Edit)
                ?? throw new InvalidOperationException("The active grid cell does not expose a search input.");
            new FlaUiTextBoxControl(input.AsTextBox()).Enter(request.SearchText);

            var results = request.EditorParts?.Results is { } resultsLocator
                ? WaitForNativeEditorPart(cell, resultsLocator, Remaining())
                : null;
            if (results is null)
            {
                ResolveNativeEditorPart(cell, request.EditorParts?.OpenButton)?.Click();
                results = request.EditorParts?.Results is { } openedResults
                    ? WaitForNativeEditorPart(cell, openedResults, Remaining())
                    : null;
            }

            if (results is null)
            {
                throw new InvalidOperationException("The active grid cell does not expose configured search results.");
            }

            if (TryRead(() => results.ControlType) == ControlType.List)
            {
                new FlaUiListBoxControl(results.AsListBox()).SelectItem(request.Value, Remaining());
                return;
            }

            if (TryRead(() => results.ControlType) == ControlType.ComboBox)
            {
                new FlaUiComboBoxControl(results.AsComboBox()).SelectItem(request.Value, Remaining());
                return;
            }

            throw new InvalidOperationException("Configured search results are neither a ListBox nor a ComboBox.");
        }

        private void SetNativeDate(
            AutomationElement cell,
            IReadOnlyList<AutomationElement> candidates,
            GridCellValueEditRequest request,
            int timeoutMs)
        {
            if (!DateTime.TryParseExact(request.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                throw new InvalidOperationException($"Grid date value '{request.Value}' is not a valid invariant date.");
            }

            var calendar = ResolveNativeEditorPart(cell, request.EditorParts?.Results)
                ?? candidates.FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Calendar);
            if (calendar is null)
            {
                ResolveNativeEditorPart(cell, request.EditorParts?.OpenButton)?.Click();
                calendar = request.EditorParts?.Results is { } locator
                    ? WaitForNativeEditorPart(cell, locator, TimeSpan.FromMilliseconds(timeoutMs))
                    : null;
            }

            if (calendar is null || TryRead(() => calendar.ControlType) != ControlType.Calendar)
            {
                throw new InvalidOperationException("The active grid cell does not expose a calendar editor.");
            }

            new FlaUiCalendarControl(calendar.AsCalendar()).SelectDate(date.Date);
        }

        private void SetNativeTime(
            AutomationElement cell,
            IReadOnlyList<AutomationElement> candidates,
            GridCellValueEditRequest request)
        {
            if (!TimeSpan.TryParseExact(request.Value, "c", CultureInfo.InvariantCulture, out var time)
                || time < TimeSpan.Zero
                || time >= TimeSpan.FromDays(1))
            {
                throw new InvalidOperationException($"Grid time value '{request.Value}' is not a valid invariant time of day.");
            }

            var input = ResolveNativeEditorPart(cell, request.EditorParts?.Input)
                ?? candidates.FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Edit);
            if (input is null || TryRead(() => input.ControlType) != ControlType.Edit)
            {
                throw new InvalidOperationException("The active grid cell does not expose a time input.");
            }

            new FlaUiTextBoxControl(input.AsTextBox()).Enter(time.ToString("c", CultureInfo.InvariantCulture));
        }

        private AutomationElement? ResolveNativeEditorPart(
            AutomationElement cell,
            GridRelativeLocator? locator)
        {
            return ResolveGridEditorPart(
                _searchRoot,
                cell,
                Inner,
                locator,
                CreateNativeAmbiguousEditorPartException);
        }

        private InvalidOperationException CreateNativeAmbiguousEditorPartException(
            GridRelativeLocator locator)
        {
            return new InvalidOperationException(
                $"Grid editor part '{locator.LocatorKind}:{locator.LocatorValue}' is ambiguous "
                + $"within scope '{locator.Scope}' of the active cell in grid '{AutomationId}'.");
        }

        private AutomationElement? WaitForNativeEditorPart(
            AutomationElement cell,
            GridRelativeLocator locator,
            TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            do
            {
                var resolved = ResolveNativeEditorPart(cell, locator);
                if (resolved is not null && TryRead(() => resolved.IsAvailable))
                {
                    return resolved;
                }

                Thread.Sleep(50);
            }
            while (stopwatch.Elapsed < timeout);

            return null;
        }

        public void OpenRow(int rowIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);

            Exception? rowException = null;
            var row = TryRead(() => Inner.GetRowByIndex(rowIndex));
            if (row is not null && TryDoubleClick(row, out rowException))
            {
                return;
            }

            var rows = TryRead(() => Inner.Rows) ?? Array.Empty<GridRow>();
            if (rowIndex >= rows.Length)
            {
                throw new InvalidOperationException($"Grid row {rowIndex} was not found in grid '{AutomationId}'.");
            }

            if (TryDoubleClick(rows[rowIndex], out var indexedRowException))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Grid row {rowIndex} in grid '{AutomationId}' could not be opened by double-click.",
                indexedRowException ?? rowException);
        }

        public void SortByColumn(string columnName)
        {
            ThrowUnsupportedUserAction(nameof(SortByColumn));
        }

        public void ScrollToEnd()
        {
            ThrowUnsupportedUserAction(nameof(ScrollToEnd));
        }

        public string CopyCell(int rowIndex, int columnIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);

            return GetRowByIndex(rowIndex)?.Cells.ElementAtOrDefault(columnIndex)?.Value
                   ?? string.Empty;
        }

        public void Export()
        {
            ThrowUnsupportedUserAction(nameof(Export));
        }

        private void ThrowUnsupportedUserAction(string actionName)
        {
            throw new System.NotSupportedException(
                $"Grid '{AutomationId}' does not support user action '{actionName}' in the FlaUI adapter.");
        }
    }

    private sealed class FlaUiVisualGridControl :
        IGridUserActionControl,
        IEditableGridControl,
        IIndexedAddressableGridControl,
        IAddressableGridControl,
        IGridColumnMetadataControl
    {
        private readonly Window _searchRoot;
        private readonly IGridControl? _fallback;
        private AutomationElement? _gridRoot;
        private NativeGridColumnHeader[]? _nativeColumnHeaders;
        private NativeFlaUiRow[]? _prefetchedNativeRows;

        public FlaUiVisualGridControl(
            Window searchRoot,
            string automationId,
            IGridControl? fallback = null)
        {
            _searchRoot = searchRoot ?? throw new ArgumentNullException(nameof(searchRoot));
            _fallback = fallback;
            AutomationId = string.IsNullOrWhiteSpace(automationId)
                ? throw new ArgumentException("Automation id cannot be empty.", nameof(automationId))
                : automationId;
        }

        public string AutomationId { get; }

        public string Name => AutomationId;

        public bool IsEnabled => TryRead(() => FindGridRoot()?.IsEnabled)
            ?? _fallback?.IsEnabled
            ?? false;

        public IReadOnlyList<IGridRowControl> Rows =>
            ReadVisualRows();

        public IReadOnlyList<string> ColumnNames => ReadColumnNames();

        public IGridRowControl? GetRowByIndex(int index)
        {
            var row = ReadRows()
                .FirstOrDefault(candidate =>
                    ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row") == index);
            if (row is not null)
            {
                return new FlaUiVisualGridRowControl(row);
            }

            var cellRow = ReadCellRows()
                .FirstOrDefault(candidate => candidate.RowIndex == index);
            if (cellRow is not null && cellRow.Cells.Count > 0)
            {
                return new FlaUiVisualGridCellBackedRowControl(cellRow.Cells);
            }

            return _fallback?.GetRowByIndex(index);
        }

        public bool TryGetColumnIndex(string columnName, out int columnIndex)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
            var matches = ColumnNames
                .Select((name, index) => (name, index))
                .Where(candidate => string.Equals(
                    candidate.name,
                    columnName.Trim(),
                    StringComparison.Ordinal))
                .Select(static candidate => candidate.index)
                .Take(2)
                .ToArray();
            columnIndex = matches.Length == 1 ? matches[0] : -1;
            return matches.Length == 1;
        }

        public GridRowResolution ResolveRow(GridRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            if (!HasNativeDataRows())
            {
                return ResolveRow(MapRow(row), timeoutMs);
            }

            var scan = ScanNativeRows(row, Stopwatch.StartNew(), timeoutMs);
            var description = DescribeNativeResolution(row, scan.MatchingRows.Count, scan.Rows);
            return scan.MatchingRows.Count switch
            {
                0 => GridRowResolution.NotFound(description),
                1 => GridRowResolution.Unique(description),
                _ => GridRowResolution.Ambiguous(scan.MatchingRows.Count, description)
            };
        }

        public GridCellValueSnapshot ReadCell(GridCellAddress address, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(address);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            if (HasNativeDataRows())
            {
                var stopwatch = Stopwatch.StartNew();
                var row = ResolveUniqueNativeRow(address.Row, stopwatch, timeoutMs);
                var columnIndex = ResolveNamedColumnIndex(address.ColumnName);
                var displayText = ReadVisualGridCellText(row.Cells[columnIndex]);
                return new GridCellValueSnapshot(
                    displayText,
                    displayText,
                    GridCellValueKind.Text);
            }

            return ReadCell(
                MapRow(address.Row),
                CreateRuntimeColumn(address.ColumnName),
                timeoutMs);
        }

        public string CopyCell(GridCellAddress address, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(address);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            if (HasNativeDataRows())
            {
                var stopwatch = Stopwatch.StartNew();
                var row = ResolveUniqueNativeRow(address.Row, stopwatch, timeoutMs);
                var columnIndex = ResolveNamedColumnIndex(address.ColumnName);
                var cell = row.Cells[columnIndex];
                if (TryRead(() => cell.Patterns.Value.IsSupported))
                {
                    var value = TryRead(() => cell.Patterns.Value.Pattern.Value);
                    if (value is not null)
                    {
                        return value;
                    }
                }

                return ReadVisualGridCellText(cell) ?? string.Empty;
            }

            return CopyCell(
                MapRow(address.Row),
                CreateRuntimeColumn(address.ColumnName),
                timeoutMs);
        }

        public void EditCell(
            GridCellAddress address,
            GridCellValueEditRequest request,
            int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(address);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            if (HasNativeDataRows())
            {
                var stopwatch = Stopwatch.StartNew();
                var row = ResolveUniqueNativeRow(address.Row, stopwatch, timeoutMs);
                var columnIndex = ResolveNamedColumnIndex(address.ColumnName);
                EditResolvedCell(
                    row.Cells[columnIndex],
                    new GridCellEditRequest(
                        0,
                        columnIndex,
                        request.Value,
                        request.EditorKind,
                        request.CommitMode,
                        request.SearchText)
                    {
                        TimeoutMs = RemainingGridMilliseconds(stopwatch, timeoutMs),
                        EditorParts = request.EditorParts
                    });
                return;
            }

            var column = CreateRuntimeColumn(
                address.ColumnName,
                request.EditorKind,
                request.EditorParts);
            EditCell(MapRow(address.Row), column, request, timeoutMs);
        }

        public void OpenRow(GridRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            if (HasNativeDataRows())
            {
                var stopwatch = Stopwatch.StartNew();
                var target = ResolveUniqueNativeRow(row, stopwatch, timeoutMs).Element;
                if (TryDoubleClick(target, out var exception))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"Grid '{AutomationId}' stable row could not be opened by double-click.",
                    exception);
            }

            OpenRow(MapRow(row), timeoutMs);
        }

        private bool HasNativeDataRows()
        {
            return ReadNativeDataRows().Length > 0;
        }

        private NativeFlaUiRow ResolveUniqueNativeRow(
            GridRowSelector selector,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var scan = ScanNativeRows(selector, stopwatch, timeoutMs);
            if (scan.MatchingRows.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Grid row selector matched {scan.MatchingRows.Count} rows in grid '{AutomationId}'; expected exactly one. "
                    + DescribeNativeResolution(selector, scan.MatchingRows.Count, scan.Rows));
            }

            if (scan.LiveMatchingRow is { } liveMatch
                && TryRead(() => liveMatch.Element.IsAvailable)
                && NativeRowMatches(liveMatch, selector))
            {
                return liveMatch;
            }

            var scroll = FindGridScrollState();
            if (TryRestoreGridScrollPosition(
                    scroll,
                    scan.MatchingRows[0].ScrollPosition,
                    stopwatch,
                    timeoutMs))
            {
                var restoredMatch = TakePrefetchedNativeRows()
                    .FirstOrDefault(row => row.IsVisible && NativeRowMatches(row, selector));
                if (restoredMatch is not null)
                {
                    return restoredMatch;
                }
            }

            MoveGridScrollToStart(scroll, stopwatch, timeoutMs);
            do
            {
                var visibleRows = TakePrefetchedNativeRows();
                var match = visibleRows.FirstOrDefault(row => NativeRowMatches(row, selector));
                if (match is not null)
                {
                    return match;
                }

                var previousSignature = CreateNativeRowSignature(visibleRows);
                if (!MoveGridScrollForward(
                        scroll,
                        stopwatch,
                        timeoutMs,
                        previousSignature,
                        EstimateNativeScrollIncrement(visibleRows)))
                {
                    var finalRows = ReadNativeDataRows();
                    if (!string.Equals(
                            previousSignature,
                            CreateNativeRowSignature(finalRows),
                            StringComparison.Ordinal))
                    {
                        match = finalRows.FirstOrDefault(row => NativeRowMatches(row, selector));
                        if (match is not null)
                        {
                            return match;
                        }
                    }

                    break;
                }
            }
            while (true);

            throw new InvalidOperationException(
                $"Grid '{AutomationId}' stable row disappeared during bounded traversal.");
        }

        private NativeGridScan ScanNativeRows(
            GridRowSelector selector,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var rows = new List<NativeGridRowSnapshot>();
            NativeFlaUiRow[] visibleRows;
            var selectorColumnIndexes = selector.Conditions
                .Select(condition => ResolveNamedColumnIndex(condition.ColumnName))
                .Distinct()
                .ToArray();
            var scroll = FindGridScrollState();
            MoveGridScrollToStart(scroll, stopwatch, timeoutMs);
            scroll = FindGridScrollState();

            do
            {
                visibleRows = TakePrefetchedNativeRows();
                AppendNativeRows(
                    rows,
                    visibleRows,
                    ReadGridScrollPosition(scroll),
                    selectorColumnIndexes);

                var previousSignature = CreateNativeRowSignature(visibleRows);
                var moved = MoveGridScrollForward(
                    scroll,
                    stopwatch,
                    timeoutMs,
                    previousSignature,
                    EstimateNativeScrollIncrement(visibleRows));
                if (!moved)
                {
                    var finalRows = ReadNativeDataRows();
                    if (!string.Equals(
                            previousSignature,
                            CreateNativeRowSignature(finalRows),
                            StringComparison.Ordinal))
                    {
                        visibleRows = finalRows;
                        AppendNativeRows(
                            rows,
                            visibleRows,
                            ReadGridScrollPosition(scroll),
                            selectorColumnIndexes);
                    }

                    break;
                }
            }
            while (true);

            var matchingRows = rows.Where(row => NativeRowMatches(row, selector)).ToArray();
            return new NativeGridScan(
                rows,
                matchingRows,
                matchingRows.Length == 1
                    ? visibleRows.FirstOrDefault(row => row.IsVisible && NativeRowMatches(row, selector))
                    : null);
        }

        private static void AppendNativeRows(
            List<NativeGridRowSnapshot> accumulated,
            IReadOnlyList<NativeFlaUiRow> visibleRows,
            GridScrollPosition scrollPosition,
            IReadOnlyList<int> valueColumnIndexes)
        {
            if (visibleRows.Count == 0)
            {
                return;
            }

            var rawSnapshots = visibleRows.Select(row =>
            {
                var cellTexts = new string[row.Cells.Count];
                foreach (var columnIndex in valueColumnIndexes)
                {
                    cellTexts[columnIndex] = row.GetCellText(columnIndex);
                }

                return new NativeGridRowSnapshot(
                    cellTexts,
                    ReadNativeGridRowIndex(row),
                    TryRead(() => row.Element.BoundingRectangle),
                    scrollPosition);
            }).ToArray();
            var snapshots = rawSnapshots
                .Where((snapshot, index) => rawSnapshots
                    .Take(index)
                    .All(existing => !snapshot.HasSameVisualRow(existing)))
                .ToArray();
            foreach (var snapshot in snapshots)
            {
                if (accumulated.All(existing => !snapshot.HasSameStableRow(existing)))
                {
                    accumulated.Add(snapshot);
                }
            }
        }

        private static int? ReadNativeGridRowIndex(NativeFlaUiRow row)
        {
            if (row.Cells.Count > 0)
            {
                var gridItem = TryRead(() => row.Cells[0].Patterns.GridItem.PatternOrDefault);
                if (gridItem is not null)
                {
                    return TryRead(() => gridItem.Row.ValueOrDefault);
                }
            }

            return null;
        }

        private bool NativeRowMatches(NativeFlaUiRow row, GridRowSelector selector)
        {
            return selector.Conditions.All(condition =>
            {
                if (!TryGetColumnIndex(condition.ColumnName, out var columnIndex)
                    || columnIndex >= row.Cells.Count)
                {
                    return false;
                }

                return string.Equals(
                    row.GetCellText(columnIndex),
                    condition.Value,
                    StringComparison.Ordinal);
            });
        }

        private bool NativeRowMatches(NativeGridRowSnapshot row, GridRowSelector selector)
        {
            return selector.Conditions.All(condition =>
            {
                if (!TryGetColumnIndex(condition.ColumnName, out var columnIndex)
                    || columnIndex >= row.CellTexts.Count)
                {
                    return false;
                }

                return string.Equals(
                    row.CellTexts[columnIndex],
                    condition.Value,
                    StringComparison.Ordinal);
            });
        }

        private string DescribeNativeResolution(
            GridRowSelector selector,
            int matchCount,
            IReadOnlyList<NativeGridRowSnapshot> discoveredRows)
        {
            var root = FindGridRoot();
            var scroll = FindGridScrollState();
            var scrollDescription = scroll.ScrollPattern is not null
                ? $"ScrollPattern(percent={TryRead(() => scroll.ScrollPattern.VerticalScrollPercent.ValueOrDefault)})"
                : scroll.RangeValuePattern is not null
                    ? $"RangeValuePattern(min={TryRead(() => scroll.RangeValuePattern.Minimum.ValueOrDefault)},"
                      + $"value={TryRead(() => scroll.RangeValuePattern.Value.ValueOrDefault)},"
                      + $"max={TryRead(() => scroll.RangeValuePattern.Maximum.ValueOrDefault)},"
                      + $"small={TryRead(() => scroll.RangeValuePattern.SmallChange.ValueOrDefault)},"
                      + $"large={TryRead(() => scroll.RangeValuePattern.LargeChange.ValueOrDefault)})"
                    : $"buttons(back={DescribeScrollButton(scroll.BackwardButton)},forward={DescribeScrollButton(scroll.ForwardButton)})";
            var matchingRows = string.Join(
                " | ",
                discoveredRows.Where(row => NativeRowMatches(row, selector)).Select(DescribeNativeRow));
            return $"grid='{AutomationId}'; selector={GridRuntimeResolver.DescribeRowSelector(selector)}; "
                   + $"matches={matchCount}; scannedRows={discoveredRows.Count}; "
                   + $"firstRow={DescribeNativeRow(discoveredRows.Count == 0 ? null : discoveredRows[0])}; "
                   + $"lastRow={DescribeNativeRow(discoveredRows.Count == 0 ? null : discoveredRows[^1])}; "
                   + $"matchingRows={matchingRows}; "
                   + $"columns={string.Join(", ", ColumnNames)}; "
                   + $"patterns=(grid={TryRead(() => root?.Patterns.Grid.IsSupported) == true},"
                   + $"itemContainer={TryRead(() => root?.Patterns.ItemContainer.IsSupported) == true},"
                   + $"scroll={TryRead(() => root?.Patterns.Scroll.IsSupported) == true}); "
                   + $"scroll={scrollDescription}";
        }

        private static string DescribeNativeRow(NativeGridRowSnapshot? row)
        {
            return row is null
                ? "<none>"
                : $"[{string.Join(", ", row.CellTexts)}]"
                  + $"@index={row.RowIndex?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}"
                  + $"/bounds={row.Bounds}"
                  + $"/scroll={row.ScrollPosition}";
        }

        private static string DescribeScrollButton(AutomationElement? button)
        {
            return button is null
                ? "missing"
                : $"{TryRead(() => button.AutomationId)}/enabled={TryRead(() => button.IsEnabled)}/invoke={TryRead(() => button.Patterns.Invoke.IsSupported)}";
        }

        private GridIndexedRowSelector MapRow(GridRowSelector row)
        {
            ArgumentNullException.ThrowIfNull(row);
            return new GridIndexedRowSelector(
                row.Conditions.Select(condition => new GridIndexedCellCondition(
                    CreateRuntimeColumn(condition.ColumnName),
                    condition.Value)));
        }

        private GridRuntimeColumn CreateRuntimeColumn(
            string columnName,
            GridCellEditorKind? editorKind = null,
            GridCellEditorParts? editorParts = null)
        {
            return new GridRuntimeColumn(
                ResolveNamedColumnIndex(columnName),
                columnName,
                displayValuePath: null,
                formatString: null,
                cultureName: null,
                GridCellValueKind.Text,
                editorKind,
                editorParts);
        }

        private int ResolveNamedColumnIndex(string columnName)
        {
            var matches = ColumnNames.Count(candidate => string.Equals(
                candidate,
                columnName?.Trim(),
                StringComparison.Ordinal));
            if (matches > 1)
            {
                throw new InvalidOperationException(
                    $"Grid column '{columnName}' is ambiguous in visual grid '{AutomationId}' ({matches} matches).");
            }

            if (TryGetColumnIndex(columnName, out var columnIndex))
            {
                return columnIndex;
            }

            throw new InvalidOperationException(
                $"Grid column '{columnName}' was not found in visual grid '{AutomationId}'. "
                + $"Available columns: {string.Join(", ", ColumnNames)}.");
        }

        public GridRowResolution ResolveRow(GridIndexedRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var matches = FindMatchingRowIndexes(row, timeoutMs);
            var description = DescribeIndexedResolution(row, matches.Length);
            return matches.Length switch
            {
                0 => GridRowResolution.NotFound(description),
                1 => GridRowResolution.Unique(description),
                _ => GridRowResolution.Ambiguous(matches.Length, description)
            };
        }

        public GridCellValueSnapshot ReadCell(
            GridIndexedRowSelector row,
            GridRuntimeColumn column,
            int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(column);
            var stopwatch = Stopwatch.StartNew();
            var rowIndex = ResolveUniqueRowIndex(row, timeoutMs);
            var cell = FindVisualCellWithTraversal(
                    rowIndex,
                    column.ColumnIndex,
                    RemainingGridMilliseconds(stopwatch, timeoutMs))
                ?? throw new InvalidOperationException(
                    $"Grid '{AutomationId}' row {rowIndex} no longer exposes column {column.ColumnIndex}.");
            var displayText = ReadVisualGridCellText(cell);
            return new GridCellValueSnapshot(
                displayText,
                ParseGridRuntimeValue(displayText, column),
                column.ValueKind);
        }

        public string CopyCell(
            GridIndexedRowSelector row,
            GridRuntimeColumn column,
            int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(column);
            var rowIndex = ResolveUniqueRowIndex(row, timeoutMs);
            var cell = FindVisualCellWithTraversal(rowIndex, column.ColumnIndex, timeoutMs)
                ?? throw new InvalidOperationException(
                    $"Grid '{AutomationId}' row {rowIndex} no longer exposes column {column.ColumnIndex}.");
            if (TryRead(() => cell.Patterns.Value.IsSupported))
            {
                var value = TryRead(() => cell.Patterns.Value.Pattern.Value);
                if (value is not null)
                {
                    return value;
                }
            }

            return ReadVisualGridCellText(cell) ?? string.Empty;
        }

        public void EditCell(
            GridIndexedRowSelector row,
            GridRuntimeColumn column,
            GridCellValueEditRequest request,
            int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(column);
            ArgumentNullException.ThrowIfNull(request);
            var stopwatch = Stopwatch.StartNew();
            var rowIndex = ResolveUniqueRowIndex(row, timeoutMs);
            var cell = FindVisualCellWithTraversal(
                    rowIndex,
                    column.ColumnIndex,
                    RemainingGridMilliseconds(stopwatch, timeoutMs))
                ?? throw new InvalidOperationException(
                    $"Grid '{AutomationId}' row {rowIndex} no longer exposes column {column.ColumnIndex}.");
            var remaining = RemainingGridMilliseconds(stopwatch, timeoutMs);
            EditResolvedCell(
                cell,
                new GridCellEditRequest(
                    rowIndex,
                    column.ColumnIndex,
                    request.Value,
                    request.EditorKind,
                    request.CommitMode,
                    request.SearchText)
                {
                    TimeoutMs = remaining,
                    EditorParts = request.EditorParts ?? column.EditorParts
                });
        }

        public void OpenRow(GridIndexedRowSelector row, int timeoutMs)
        {
            var stopwatch = Stopwatch.StartNew();
            var rowIndex = ResolveUniqueRowIndex(row, timeoutMs);
            var target = FindVisualCellWithTraversal(
                    rowIndex,
                    0,
                    RemainingGridMilliseconds(stopwatch, timeoutMs))
                ?? ReadRows().FirstOrDefault(candidate =>
                    ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row") == rowIndex);
            if (target is null)
            {
                throw new InvalidOperationException(
                    $"Grid '{AutomationId}' stable row was resolved but is not visible after bounded traversal.");
            }

            if (TryDoubleClick(target, out var exception))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Grid '{AutomationId}' stable row could not be opened by double-click.",
                exception);
        }

        public void EditCell(GridCellEditRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentOutOfRangeException.ThrowIfNegative(request.RowIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(request.ColumnIndex);
            ArgumentNullException.ThrowIfNull(request.Value);
            if (request.TimeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Grid edit timeout must be positive.");
            }

            var cell = FindVisualCellWithTraversal(request.RowIndex, request.ColumnIndex, request.TimeoutMs)
                ?? throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] was not found in grid '{AutomationId}'.");

            EditResolvedCell(cell, request);
        }

        private void EditResolvedCell(AutomationElement cell, GridCellEditRequest request)
        {
            if (request.EditorKind == GridCellEditorKind.SearchPicker)
            {
                EditSearchPickerCell(cell, request);
            }
            else if (request.EditorKind == GridCellEditorKind.Time)
            {
                EditTimeCell(cell, request);
            }
            else if (request.EditorKind == GridCellEditorKind.Date)
            {
                EditDateCell(cell, request);
            }
            else if (request.EditorKind == GridCellEditorKind.Color)
            {
                EditColorCell(cell, request);
            }
            else if (request.EditorKind == GridCellEditorKind.ComboBox)
            {
                EditComboBoxCell(cell, request);
            }
            else if (request.EditorKind == GridCellEditorKind.CheckBox)
            {
                EditCheckBoxCell(cell, request);
            }
            else if (request.EditorKind is GridCellEditorKind.Text or GridCellEditorKind.Number)
            {
                EditTextOrNumberCell(cell, request);
            }
            else if (_fallback is IEditableGridControl editableFallback)
            {
                editableFallback.EditCell(request);
                return;
            }
            else
            {
                throw new System.NotSupportedException(
                    $"Visual grid '{AutomationId}' does not support '{request.EditorKind}' cell editing in the FlaUI adapter.");
            }

            if (request.CommitMode == GridCellEditCommitMode.Cancel)
            {
                CancelCellEdit(cell, request);
                return;
            }

            ConfirmCellEdit(cell, request);
        }

        public void OpenRow(int rowIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);

            var candidates = FindOpenRowTargets(rowIndex);
            Exception? lastException = null;
            foreach (var candidate in candidates)
            {
                if (TryDoubleClick(candidate, out lastException))
                {
                    return;
                }
            }

            if (_fallback is IGridUserActionControl actionFallback)
            {
                actionFallback.OpenRow(rowIndex);
                return;
            }

            var detail = lastException is null
                ? $"Visual grid row {rowIndex} was not found in grid '{AutomationId}'."
                : $"Visual grid row {rowIndex} in grid '{AutomationId}' could not be opened by double-click.";
            throw new InvalidOperationException(detail, lastException);
        }

        private AutomationElement? FindVisualCell(int rowIndex, int columnIndex)
        {
            var expectedAutomationId = $"{AutomationId}_Row{rowIndex}_Cell{columnIndex}";
            return FindAutomationDescendants(_searchRoot)
                .FirstOrDefault(candidate =>
                    string.Equals(
                        TryRead(() => candidate.AutomationId),
                        expectedAutomationId,
                        StringComparison.Ordinal));
        }

        private AutomationElement? FindVisualCellWithTraversal(int rowIndex, int columnIndex, int timeoutMs)
        {
            var current = FindVisualCell(rowIndex, columnIndex);
            if (current is not null)
            {
                return current;
            }

            var stopwatch = Stopwatch.StartNew();
            var scroll = FindGridScrollPattern();
            if (scroll is null)
            {
                return null;
            }

            MoveGridScrollToStart(scroll, stopwatch, timeoutMs);
            do
            {
                current = FindVisualCell(rowIndex, columnIndex);
                if (current is not null)
                {
                    return current;
                }
            }
            while (stopwatch.ElapsedMilliseconds < timeoutMs && ScrollGridForward(scroll));

            return FindVisualCell(rowIndex, columnIndex);
        }

        private void EditSearchPickerCell(AutomationElement cell, GridCellEditRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SearchText))
            {
                throw new ArgumentException(
                    "Search text cannot be empty for a search-picker grid edit.",
                    nameof(request));
            }

            var editorElements = new[] { cell }
                .Concat(FindAutomationDescendants(cell))
                .ToArray();
            var searchInput = ResolveEditorPart(cell, request.EditorParts?.Input)
                ?? editorElements
                .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Edit);
            if (searchInput is null)
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose a ServerSearchComboBox input.");
            }

            var timeout = TimeSpan.FromMilliseconds(request.TimeoutMs);
            var stopwatch = Stopwatch.StartNew();
            TimeSpan RemainingTimeout()
            {
                var remaining = timeout - stopwatch.Elapsed;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }

            new FlaUiTextBoxControl(searchInput.AsTextBox()).Enter(request.SearchText);

            var configuredResults = request.EditorParts?.Results;
            var inputAutomationId = TryRead(() => searchInput.AutomationId);
            var editorAutomationId = TryGetEditorAutomationId(inputAutomationId);
            var resultsAutomationId = configuredResults?.LocatorKind == UiLocatorKind.AutomationId
                ? configuredResults.LocatorValue
                : editorAutomationId is null
                    ? null
                    : $"{editorAutomationId}_Results";
            var initialWait = RemainingTimeout() < TimeSpan.FromMilliseconds(500)
                ? RemainingTimeout()
                : TimeSpan.FromMilliseconds(500);
            var results = configuredResults is null
                ? resultsAutomationId is null ? null : WaitForProcessElementByAutomationId(resultsAutomationId, initialWait)
                : WaitForEditorPart(cell, configuredResults, initialWait);
            if (results is null)
            {
                var openButton = ResolveEditorPart(cell, request.EditorParts?.OpenButton)
                    ?? (editorAutomationId is null
                        ? null
                        : FindProcessElementByAutomationId($"{editorAutomationId}_OpenButton"));
                if (openButton is not null && RemainingTimeout() > TimeSpan.Zero)
                {
                    openButton.Click();
                    results = configuredResults is null
                        ? resultsAutomationId is null ? null : WaitForProcessElementByAutomationId(resultsAutomationId, RemainingTimeout())
                        : WaitForEditorPart(cell, configuredResults, RemainingTimeout());
                }
            }

            if (results is null)
            {
                throw new InvalidOperationException(
                    $"Search-picker results were not exposed for visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}'.");
            }

            if (TryRead(() => results.ControlType) == ControlType.List)
            {
                new FlaUiListBoxControl(results.AsListBox()).SelectItem(request.Value, RemainingTimeout());
                return;
            }

            if (TryRead(() => results.ControlType) == ControlType.ComboBox)
            {
                new FlaUiComboBoxControl(results.AsComboBox()).SelectItem(request.Value, RemainingTimeout());
                return;
            }

            throw new InvalidOperationException(
                $"Search-picker results for visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' are neither a ListBox nor a ComboBox.");
        }

        private void EditTextOrNumberCell(AutomationElement cell, GridCellEditRequest request)
        {
            var explicitInput = ResolveEditorPart(cell, request.EditorParts?.Input);
            if (request.EditorKind == GridCellEditorKind.Number)
            {
                var spinner = explicitInput is not null && TryRead(() => explicitInput.ControlType) == ControlType.Spinner
                    ? explicitInput
                    : new[] { cell }.Concat(FindAutomationDescendants(cell))
                        .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Spinner);
                if (spinner is not null
                    && double.TryParse(request.Value, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    new FlaUiSpinnerControl(spinner.AsSpinner()).Value = number;
                    return;
                }
            }

            var input = explicitInput
                ?? new[] { cell }.Concat(FindAutomationDescendants(cell))
                    .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Edit);
            if (input is null || TryRead(() => input.ControlType) != ControlType.Edit)
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose a writable text editor.");
            }

            new FlaUiTextBoxControl(input.AsTextBox()).Enter(request.Value);
        }

        private void EditDateCell(AutomationElement cell, GridCellEditRequest request)
        {
            var date = ParseGridDate(request.Value);
            var calendar = ResolveEditorPart(cell, request.EditorParts?.Results)
                ?? new[] { cell }.Concat(FindAutomationDescendants(cell))
                    .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Calendar);
            if (calendar is null)
            {
                ResolveEditorPart(cell, request.EditorParts?.OpenButton)?.Click();
                calendar = request.EditorParts?.Results is { } results
                    ? WaitForEditorPart(cell, results, TimeSpan.FromMilliseconds(request.TimeoutMs))
                    : null;
            }

            if (calendar is null || TryRead(() => calendar.ControlType) != ControlType.Calendar)
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose a calendar editor.");
            }

            new FlaUiCalendarControl(calendar.AsCalendar()).SelectDate(date);
        }

        private void EditTimeCell(AutomationElement cell, GridCellEditRequest request)
        {
            var time = ParseGridTime(request.Value);
            var input = ResolveEditorPart(cell, request.EditorParts?.Input);
            if (input is not null && TryRead(() => input.ControlType) == ControlType.Edit)
            {
                new FlaUiTextBoxControl(input.AsTextBox()).Enter(time.ToString("c", CultureInfo.InvariantCulture));
                Keyboard.Press(VirtualKeyShort.RETURN);
                return;
            }

            new FlaUiTimePickerControl(cell).SelectedTime = time;
        }

        private void EditColorCell(AutomationElement cell, GridCellEditRequest request)
        {
            var expected = ColorValue.Normalize(request.Value);
            var editor = new[] { cell }
                .Concat(FindAutomationDescendants(cell))
                .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.Edit);
            if (editor is null)
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose an editable color-value surface.");
            }

            new FlaUiTextBoxControl(editor.AsTextBox()).Enter(expected);
        }

        private void EditComboBoxCell(AutomationElement cell, GridCellEditRequest request)
        {
            var editor = ResolveEditorPart(cell, request.EditorParts?.Input)
                ?? new[] { cell }
                .Concat(FindAutomationDescendants(cell))
                .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.ComboBox);
            if (editor is null)
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose a ComboBox editor.");
            }

            TryScrollIntoView(editor);
            TryFocus(editor);
            new FlaUiComboBoxControl(editor.AsComboBox()).SelectItem(
                request.Value,
                TimeSpan.FromMilliseconds(request.TimeoutMs));
        }

        private void ConfirmCellEdit(AutomationElement cell, GridCellEditRequest request)
        {
            var confirm = ResolveEditorPart(cell, request.EditorParts?.ConfirmButton);
            if (confirm is not null)
            {
                confirm.Click();
                return;
            }

            if (request.EditorKind is GridCellEditorKind.Text
                or GridCellEditorKind.Number
                or GridCellEditorKind.Color)
            {
                TryFocus(ResolveEditorPart(cell, request.EditorParts?.Input) ?? cell);
                Keyboard.Press(VirtualKeyShort.RETURN);
            }
        }

        private void CancelCellEdit(AutomationElement cell, GridCellEditRequest request)
        {
            var cancel = ResolveEditorPart(cell, request.EditorParts?.CancelButton);
            if (cancel is not null)
            {
                cancel.Click();
                return;
            }

            TryFocus(cell);
            Keyboard.Press(VirtualKeyShort.ESCAPE);
        }

        private void EditCheckBoxCell(AutomationElement cell, GridCellEditRequest request)
        {
            if (!bool.TryParse(request.Value, out var expected))
            {
                throw new InvalidOperationException(
                    $"Grid check-box value '{request.Value}' is not a Boolean value.");
            }

            var editor = ResolveEditorPart(cell, request.EditorParts?.Input)
                ?? new[] { cell }
                    .Concat(FindAutomationDescendants(cell))
                    .FirstOrDefault(candidate => TryRead(() => candidate.ControlType) == ControlType.CheckBox);
            if (editor is null || TryRead(() => editor.ControlType) != ControlType.CheckBox)
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose a CheckBox editor.");
            }

            var checkBox = new FlaUiCheckBoxControl(editor.AsCheckBox());
            if (checkBox.IsChecked != expected)
            {
                checkBox.IsChecked = expected;
            }
        }

        private AutomationElement? ResolveEditorPart(AutomationElement cell, GridRelativeLocator? locator)
        {
            return ResolveGridEditorPart(
                _searchRoot,
                cell,
                FindGridRoot(),
                locator,
                CreateAmbiguousEditorPartException);
        }

        private InvalidOperationException CreateAmbiguousEditorPartException(
            GridRelativeLocator locator)
        {
            return new InvalidOperationException(
                $"Grid editor part '{locator.LocatorKind}:{locator.LocatorValue}' is ambiguous "
                + $"within scope '{locator.Scope}' of the active cell in grid '{AutomationId}'.");
        }

        private AutomationElement? WaitForEditorPart(
            AutomationElement cell,
            GridRelativeLocator locator,
            TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            do
            {
                var result = ResolveEditorPart(cell, locator);
                if (result is not null && TryRead(() => result.IsAvailable))
                {
                    return result;
                }

                Thread.Sleep(50);
            }
            while (stopwatch.Elapsed < timeout);

            return null;
        }

        private static DateTime ParseGridDate(string value)
        {
            if (DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var exact))
            {
                return exact.Date;
            }

            throw new InvalidOperationException(
                $"Grid date value '{value}' is not a valid invariant date.");
        }

        private static TimeSpan ParseGridTime(string value)
        {
            if (TimeSpan.TryParseExact(value, "c", CultureInfo.InvariantCulture, out var time)
                && time >= TimeSpan.Zero
                && time < TimeSpan.FromDays(1))
            {
                return time;
            }

            throw new InvalidOperationException($"Grid time value '{value}' is not a valid invariant time of day.");
        }

        private AutomationElement? WaitForProcessElementByAutomationId(string automationId, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            do
            {
                var element = FindProcessElementByAutomationId(automationId);
                if (element is not null && TryRead(() => element.IsAvailable))
                {
                    return element;
                }

                Thread.Sleep(50);
            }
            while (stopwatch.Elapsed < timeout);

            return null;
        }

        private AutomationElement? FindProcessElementByAutomationId(string automationId)
        {
            var condition = _searchRoot.Automation.ConditionFactory.ByAutomationId(automationId);
            var local = TryRead(() => _searchRoot.FindFirstDescendant(condition));
            if (local is not null && TryRead(() => local.IsAvailable))
            {
                return local;
            }

            var processId = TryRead(() => _searchRoot.FrameworkAutomationElement.ProcessId.ValueOrDefault);
            var desktop = TryRead(() => _searchRoot.Automation.GetDesktop());
            if (processId <= 0 || desktop is null)
            {
                return null;
            }

            var processRoots = TryRead(() => desktop.FindAllChildren(factory => factory.ByProcessId(processId)))
                ?? Array.Empty<AutomationElement>();
            foreach (var root in processRoots)
            {
                if (root is null || !TryRead(() => root.IsAvailable))
                {
                    continue;
                }

                var match = TryRead(() => root.FindFirstDescendant(condition));
                if (match is not null && TryRead(() => match.IsAvailable))
                {
                    return match;
                }
            }

            return null;
        }

        private static string? TryGetEditorAutomationId(string? inputAutomationId)
        {
            const string inputSuffix = "_Input";
            return !string.IsNullOrWhiteSpace(inputAutomationId)
                   && inputAutomationId.EndsWith(inputSuffix, StringComparison.Ordinal)
                ? inputAutomationId[..^inputSuffix.Length]
                : null;
        }

        public void SortByColumn(string columnName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

            if (_fallback is IGridUserActionControl actionFallback)
            {
                actionFallback.SortByColumn(columnName);
                return;
            }

            ThrowUnsupportedUserAction(nameof(SortByColumn));
        }

        public void ScrollToEnd()
        {
            if (_fallback is IGridUserActionControl actionFallback)
            {
                actionFallback.ScrollToEnd();
                return;
            }

            ThrowUnsupportedUserAction(nameof(ScrollToEnd));
        }

        public string CopyCell(int rowIndex, int columnIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);

            if (_fallback is IGridUserActionControl actionFallback)
            {
                return actionFallback.CopyCell(rowIndex, columnIndex);
            }

            ThrowUnsupportedUserAction(nameof(CopyCell));
            return string.Empty;
        }

        public void Export()
        {
            if (_fallback is IGridUserActionControl actionFallback)
            {
                actionFallback.Export();
                return;
            }

            ThrowUnsupportedUserAction(nameof(Export));
        }

        private AutomationElement[] FindOpenRowTargets(int rowIndex)
        {
            var targets = new List<AutomationElement>();
            var row = ReadRows()
                .FirstOrDefault(candidate =>
                    ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row") == rowIndex);
            if (row is not null)
            {
                targets.Add(row);
            }

            var cellRow = ReadCellRows()
                .FirstOrDefault(candidate => candidate.RowIndex == rowIndex);
            if (cellRow is not null)
            {
                targets.AddRange(cellRow.Cells);
            }

            return targets.ToArray();
        }

        private void ThrowUnsupportedUserAction(string actionName)
        {
            throw new System.NotSupportedException(
                $"Visual grid '{AutomationId}' does not support user action '{actionName}' in the FlaUI adapter.");
        }

        private int ResolveUniqueRowIndex(GridIndexedRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var matches = FindMatchingRowIndexes(row, timeoutMs);
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Grid row selector matched {matches.Length} rows in grid '{AutomationId}'; expected exactly one. "
                    + DescribeIndexedResolution(row, matches.Length));
            }

            return matches[0];
        }

        private int[] FindMatchingRowIndexes(GridIndexedRowSelector selector, int timeoutMs)
        {
            var matches = new HashSet<int>();
            var stopwatch = Stopwatch.StartNew();
            var scroll = FindGridScrollPattern();
            if (scroll is not null)
            {
                MoveGridScrollToStart(scroll, stopwatch, timeoutMs);
            }

            do
            {
                foreach (var row in ReadCurrentIndexedRows())
                {
                    if (selector.Conditions.All(condition =>
                            condition.ColumnIndex < row.Cells.Count
                            && string.Equals(
                                ReadVisualGridCellText(row.Cells[condition.ColumnIndex]),
                                condition.ExpectedText,
                                StringComparison.Ordinal)))
                    {
                        matches.Add(row.RowIndex);
                    }
                }
            }
            while (scroll is not null
                   && stopwatch.ElapsedMilliseconds < timeoutMs
                   && ScrollGridForward(scroll));

            return matches.OrderBy(static index => index).ToArray();
        }

        private IndexedFlaUiRow[] ReadCurrentIndexedRows()
        {
            var gridItemRows = ReadGridItemRows();
            if (gridItemRows.Length > 0)
            {
                return gridItemRows;
            }

            var cellRows = ReadCellRows();
            if (cellRows.Length > 0)
            {
                return cellRows
                    .Select(static row => new IndexedFlaUiRow(row.RowIndex, row.Cells))
                    .ToArray();
            }

            return ReadRows()
                .Select(row => new IndexedFlaUiRow(
                    ParseVisualGridIndex(TryRead(() => row.AutomationId), "_Row"),
                    new FlaUiVisualGridRowControl(row)
                        .ReadAutomationCells()))
                .Where(static row => row.RowIndex != int.MaxValue && row.Cells.Count > 0)
                .ToArray();
        }

        private IReadOnlyList<string> ReadColumnNames()
        {
            if (_fallback is IGridColumnMetadataControl metadata
                && metadata.ColumnNames.Count > 0)
            {
                return metadata.ColumnNames;
            }

            return ReadNativeColumnHeaders()
                .Select(static candidate => candidate.Name!)
                .ToArray();
        }

        private NativeGridColumnHeader[] ReadNativeColumnHeaders()
        {
            if (_nativeColumnHeaders is { Length: > 0 })
            {
                return _nativeColumnHeaders;
            }

            var root = FindGridRoot();
            if (root is null)
            {
                return Array.Empty<NativeGridColumnHeader>();
            }

            var headerCondition = root.Automation.ConditionFactory.ByControlType(ControlType.HeaderItem);
            _nativeColumnHeaders = (TryRead(() => root.FindAllDescendants(headerCondition))
                    ?? Array.Empty<AutomationElement>())
                .Select(candidate => new NativeGridColumnHeader(
                    ReadVisualGridCellText(candidate),
                    TryRead(() => candidate.BoundingRectangle)))
                .Where(static candidate =>
                    !string.IsNullOrWhiteSpace(candidate.Name)
                    && candidate.Bounds.Width > 0)
                .OrderBy(static candidate => candidate.Bounds.Left)
                .Select(static candidate => candidate with { Name = candidate.Name!.Trim() })
                .ToArray();
            return _nativeColumnHeaders;
        }

        private NativeFlaUiRow[] ReadNativeDataRows()
        {
            var root = FindGridRoot();
            var headers = ReadNativeColumnHeaders();
            if (root is null || headers.Length == 0)
            {
                return Array.Empty<NativeFlaUiRow>();
            }

            var dataItemCondition = root.Automation.ConditionFactory.ByControlType(ControlType.DataItem);
            var rootBounds = TryRead(() => root.BoundingRectangle);
            var rowElements = TryRead(() => root.FindAllDescendants(dataItemCondition))
                ?? Array.Empty<AutomationElement>();
            return rowElements
                .Select(candidate => new
                {
                    Element = candidate,
                    Bounds = TryRead(() => candidate.BoundingRectangle),
                    IsOffscreen = TryRead(() => candidate.IsOffscreen)
                })
                .Where(static candidate =>
                    candidate.Bounds.Width > 0
                    && candidate.Bounds.Height > 0
                    && !candidate.IsOffscreen)
                .Where(candidate =>
                {
                    var intersection = System.Drawing.Rectangle.Intersect(candidate.Bounds, rootBounds);
                    return intersection.Width > 0 && intersection.Height > 0;
                })
                .OrderBy(static candidate => candidate.Bounds.Top)
                .Select(row => new NativeFlaUiRow(
                    row.Element,
                    ResolveNativeRowCells(row.Element, headers)))
                .Where(row => row.Cells.Count == headers.Length)
                .ToArray();
        }

        private static IReadOnlyList<AutomationElement> ResolveNativeRowCells(
            AutomationElement row,
            IReadOnlyList<NativeGridColumnHeader> headers)
        {
            var children = TryRead(() => row.FindAllChildren()) ?? Array.Empty<AutomationElement>();
            var rowBounds = TryRead(() => row.BoundingRectangle);
            var candidates = children
                .Select(candidate => new
                {
                    Element = candidate,
                    Bounds = TryRead(() => candidate.BoundingRectangle),
                    IsOffscreen = TryRead(() => candidate.IsOffscreen)
                })
                .Where(static candidate =>
                    candidate.Bounds.Width > 0
                    && candidate.Bounds.Height > 0
                    && !candidate.IsOffscreen)
                .ToArray();
            var cells = new List<AutomationElement>(headers.Count);
            foreach (var header in headers)
            {
                var cell = candidates
                    .Where(candidate =>
                        OverlapWidth(candidate.Bounds, header.Bounds) > 0
                        && System.Drawing.Rectangle.Intersect(candidate.Bounds, rowBounds).Height > 0)
                    .OrderByDescending(candidate => OverlapWidth(candidate.Bounds, header.Bounds))
                    .ThenBy(static candidate => candidate.Bounds.Width)
                    .Select(static candidate => candidate.Element)
                    .FirstOrDefault();
                if (cell is null)
                {
                    return Array.Empty<AutomationElement>();
                }

                cells.Add(cell);
            }

            return cells;
        }

        private static int OverlapWidth(
            System.Drawing.Rectangle left,
            System.Drawing.Rectangle right)
        {
            return Math.Max(0, Math.Min(left.Right, right.Right) - Math.Max(left.Left, right.Left));
        }

        private static bool HasVisibleBounds(AutomationElement element)
        {
            var bounds = TryRead(() => element.BoundingRectangle);
            return bounds.Width > 0
                && bounds.Height > 0
                && !TryRead(() => element.IsOffscreen);
        }

        private IndexedFlaUiRow[] ReadGridItemRows()
        {
            var root = FindGridRoot();
            if (root is null)
            {
                return Array.Empty<IndexedFlaUiRow>();
            }

            return FindAutomationDescendants(root)
                .Select(candidate => new
                {
                    Element = candidate,
                    Pattern = TryRead(() => candidate.Patterns.GridItem.PatternOrDefault)
                })
                .Where(static candidate =>
                    candidate.Pattern is not null
                    && candidate.Pattern.Row.ValueOrDefault >= 0
                    && candidate.Pattern.Column.ValueOrDefault >= 0)
                .GroupBy(static candidate => candidate.Pattern!.Row.ValueOrDefault)
                .OrderBy(static group => group.Key)
                .Select(static group => new IndexedFlaUiRow(
                    group.Key,
                    group
                        .OrderBy(static candidate => candidate.Pattern!.Column.ValueOrDefault)
                        .Select(static candidate => candidate.Element)
                        .ToArray()))
                .Where(static row => row.Cells.Count > 0)
                .ToArray();
        }

        private AutomationElement? FindGridRoot()
        {
            if (_gridRoot is not null && TryRead(() => _gridRoot.IsAvailable))
            {
                return _gridRoot;
            }

            _gridRoot = new[] { _searchRoot }
                .Concat(FindAutomationDescendants(_searchRoot))
                .FirstOrDefault(candidate => string.Equals(
                    TryRead(() => candidate.AutomationId),
                    AutomationId,
                    StringComparison.Ordinal));
            return _gridRoot;
        }

        private IScrollPattern? FindGridScrollPattern()
        {
            var root = FindGridRoot();
            if (root is null)
            {
                return null;
            }

            var direct = new[] { root }
                .Concat(FindAutomationDescendants(root))
                .Select(static candidate => TryRead(() => candidate.Patterns.Scroll.PatternOrDefault))
                .FirstOrDefault(static pattern =>
                    pattern?.VerticallyScrollable.ValueOrDefault == true);
            if (direct is not null)
            {
                return direct;
            }

            var rootBounds = TryRead(() => root.BoundingRectangle);
            for (var ancestor = TryRead(() => root.Parent);
                 ancestor is not null && TryRead(() => ancestor.ControlType) != ControlType.Window;
                 ancestor = TryRead(() => ancestor.Parent))
            {
                var ancestorBounds = TryRead(() => ancestor.BoundingRectangle);
                if (rootBounds.Width <= 0
                    || rootBounds.Height <= 0
                    || ancestorBounds.Width <= 0
                    || ancestorBounds.Height <= 0
                    || System.Drawing.Rectangle.Intersect(rootBounds, ancestorBounds) is not { Width: > 0, Height: > 0 })
                {
                    break;
                }

                var pattern = TryRead(() => ancestor.Patterns.Scroll.PatternOrDefault);
                if (pattern?.VerticallyScrollable.ValueOrDefault == true)
                {
                    return pattern;
                }
            }

            return null;
        }

        private IRangeValuePattern? FindGridScrollBarRange()
        {
            var root = FindGridRoot();
            if (root is null)
            {
                return null;
            }

            return FindAutomationDescendants(root)
                .Where(static candidate =>
                    TryRead(() => candidate.ControlType) == ControlType.ScrollBar)
                .Where(static candidate =>
                {
                    var bounds = TryRead(() => candidate.BoundingRectangle);
                    return bounds.Height > bounds.Width;
                })
                .Select(static candidate =>
                    TryRead(() => candidate.Patterns.RangeValue.PatternOrDefault))
                .FirstOrDefault(static range =>
                    range is not null
                    && range.IsReadOnly.ValueOrDefault == false
                    && range.Maximum.ValueOrDefault > range.Minimum.ValueOrDefault);
        }

        private GridScrollState FindGridScrollState()
        {
            var scrollBar = FindGridScrollBar();
            return new GridScrollState(
                FindGridScrollPattern(),
                FindGridScrollBarRange(),
                FindGridScrollButton("PART_PageUpButton")
                    ?? FindGridScrollButton("PART_LineUpButton"),
                FindGridScrollButton("PART_PageDownButton")
                    ?? FindGridScrollButton("PART_LineDownButton"),
                FindGridRoot(),
                scrollBar,
                scrollBar is null
                    ? null
                    : FindAutomationDescendants(scrollBar)
                        .FirstOrDefault(static candidate =>
                            TryRead(() => candidate.ControlType) == ControlType.Thumb
                            && HasVisibleBounds(candidate)));
        }

        private AutomationElement? FindGridScrollBar()
        {
            var root = FindGridRoot();
            if (root is null)
            {
                return null;
            }

            return FindAutomationDescendants(root)
                .FirstOrDefault(static candidate =>
                {
                    if (TryRead(() => candidate.ControlType) != ControlType.ScrollBar)
                    {
                        return false;
                    }

                    var bounds = TryRead(() => candidate.BoundingRectangle);
                    return bounds.Width > 0 && bounds.Height > bounds.Width;
                });
        }

        private AutomationElement? FindGridScrollButton(string automationId)
        {
            var root = FindGridRoot();
            if (root is null)
            {
                return null;
            }

            return FindAutomationDescendants(root)
                .FirstOrDefault(candidate =>
                    string.Equals(
                        TryRead(() => candidate.AutomationId),
                        automationId,
                        StringComparison.Ordinal)
                    && TryRead(() => candidate.ControlType) == ControlType.Button
                    && HasVisibleBounds(candidate));
        }

        private void MoveGridScrollToStart(
            GridScrollState scroll,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            _prefetchedNativeRows = null;
            if (scroll.ScrollPattern is not null)
            {
                MoveGridScrollToStart(scroll.ScrollPattern, stopwatch, timeoutMs);
                return;
            }

            if (scroll.RangeValuePattern is not null)
            {
                var previous = ReadVisibleNativeRowSignature();
                var minimum = TryRead(() => scroll.RangeValuePattern.Minimum.ValueOrDefault);
                var moved = TryRead(() =>
                {
                    scroll.RangeValuePattern.SetValue(minimum);
                    return true;
                });
                if (moved
                    && WaitForVisibleNativeRowsToChange(
                        previous,
                        stopwatch,
                        timeoutMs,
                        maximumWaitMilliseconds: 1000))
                {
                    return;
                }

                var refreshedRange = FindGridScrollBarRange();
                if (refreshedRange is not null
                    && TryRead(() =>
                    {
                        refreshedRange.SetValue(
                            TryRead(() => refreshedRange.Minimum.ValueOrDefault));
                        return true;
                    })
                    && WaitForVisibleNativeRowsToChange(
                        previous,
                        stopwatch,
                        timeoutMs,
                        maximumWaitMilliseconds: 1000))
                {
                    return;
                }
            }

            var beforeKeyboardReset = ReadVisibleNativeRowSignature();
            if (TryMoveNativeGridWithKeyboard(forward: false)
                && WaitForVisibleNativeRowsToChange(
                    beforeKeyboardReset,
                    stopwatch,
                    timeoutMs))
            {
                return;
            }

            var beforeThumbReset = ReadVisibleNativeRowSignature();
            if (TryDragGridThumbToStart(scroll)
                && WaitForVisibleNativeRowsToChange(
                    beforeThumbReset,
                    stopwatch,
                    timeoutMs))
            {
                return;
            }

            while (scroll.BackwardButton is not null || scroll.Root is not null)
            {
                _ = RemainingGridMilliseconds(stopwatch, timeoutMs);
                var previous = ReadVisibleNativeRowSignature();
                var changed = scroll.BackwardButton is not null
                    && TryClickGridScrollButton(scroll.BackwardButton)
                    && WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs);
                if (!changed
                    && scroll.Root is not null
                    && TryScrollGridWithWheel(scroll.Root, 3))
                {
                    changed = WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs);
                }
                if (!changed
                    && scroll.Root is not null
                    && TrySendGridMouseWheel(scroll.Root, 3))
                {
                    changed = WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs);
                }

                if (!changed
                    && TryPageGridScrollBar(scroll, forward: false))
                {
                    changed = WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs);
                }

                if (!changed && scroll.RangeValuePattern is not null)
                {
                    var minimum = TryRead(() => scroll.RangeValuePattern.Minimum.ValueOrDefault);
                    var current = TryRead(() => scroll.RangeValuePattern.Value.ValueOrDefault);
                    changed = current > minimum
                        && TryRead(() =>
                        {
                            scroll.RangeValuePattern.SetValue(minimum);
                            return true;
                        })
                        && WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs);
                }

                if (!changed)
                {
                    return;
                }
            }
        }

        private bool MoveGridScrollForward(
            GridScrollState scroll,
            Stopwatch stopwatch,
            int timeoutMs,
            string? previousSignature = null,
            double? rangeIncrement = null)
        {
            if (stopwatch.ElapsedMilliseconds >= timeoutMs)
            {
                return false;
            }

            if (scroll.ScrollPattern is not null)
            {
                return ScrollGridForward(scroll.ScrollPattern);
            }

            var previous = previousSignature ?? ReadVisibleNativeRowSignature();
            if (scroll.RangeValuePattern is not null
                && TryMoveGridRangeForward(
                    scroll.RangeValuePattern,
                    rangeIncrement,
                    previous,
                    stopwatch,
                    timeoutMs))
            {
                return true;
            }

            if (scroll.ForwardButton is not null
                && TryClickGridScrollButton(scroll.ForwardButton)
                && WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs))
            {
                return true;
            }

            if (TryMoveNativeGridWithKeyboard(forward: true)
                && WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs))
            {
                return true;
            }

            if (TryPageGridScrollBar(scroll, forward: true)
                && WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs))
            {
                return true;
            }

            if (TryDragGridThumbForward(scroll)
                && WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs))
            {
                return true;
            }

            if (scroll.Root is not null
                && TryScrollGridWithWheel(scroll.Root, -3)
                && WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs))
            {
                return true;
            }

            if (scroll.Root is not null
                && TrySendGridMouseWheel(scroll.Root, -3)
                && WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs))
            {
                return true;
            }

            return false;
        }

        private bool TryMoveGridRangeForward(
            IRangeValuePattern rangeValue,
            double? requestedIncrement,
            string previousSignature,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            var current = TryRead(() => rangeValue.Value.ValueOrDefault);
            var minimum = TryRead(() => rangeValue.Minimum.ValueOrDefault);
            var maximum = TryRead(() => rangeValue.Maximum.ValueOrDefault);
            var range = maximum - minimum;
            var tolerance = Math.Max(0.001, range / 10_000);
            if (current >= maximum - tolerance)
            {
                return false;
            }

            var largeChange = TryRead(() => rangeValue.LargeChange.ValueOrDefault);
            var smallChange = TryRead(() => rangeValue.SmallChange.ValueOrDefault);
            var increment = Math.Max(
                1,
                requestedIncrement ?? Math.Max(largeChange, Math.Max(smallChange, range / 10)));
            var next = Math.Min(maximum, current + increment);
            var moved = TryRead(() =>
            {
                rangeValue.SetValue(next);
                return true;
            });
            if (!moved)
            {
                return false;
            }

            return WaitForVisibleNativeRowsToChange(
                previousSignature,
                stopwatch,
                timeoutMs,
                maximumWaitMilliseconds: 500);
        }

        private static double? EstimateNativeScrollIncrement(IReadOnlyList<NativeFlaUiRow> rows)
        {
            var bounds = rows
                .Where(static row => row.IsVisible)
                .Select(static row => TryRead(() => row.Element.BoundingRectangle))
                .Where(static bounds => bounds.Height > 0)
                .OrderBy(static bounds => bounds.Top)
                .ToArray();
            if (bounds.Length == 0)
            {
                return null;
            }

            var visibleExtent = bounds[^1].Bottom - bounds[0].Top;
            var overlap = bounds.Min(static bounds => bounds.Height);
            return Math.Max(1, visibleExtent - overlap);
        }

        private static GridScrollPosition ReadGridScrollPosition(GridScrollState scroll)
        {
            return new GridScrollPosition(
                scroll.ScrollPattern is null
                    ? null
                    : TryRead(() => scroll.ScrollPattern.VerticalScrollPercent.ValueOrDefault),
                scroll.RangeValuePattern is null
                    ? null
                    : TryRead(() => scroll.RangeValuePattern.Value.ValueOrDefault));
        }

        private bool TryRestoreGridScrollPosition(
            GridScrollState scroll,
            GridScrollPosition position,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            if (stopwatch.ElapsedMilliseconds >= timeoutMs)
            {
                return false;
            }

            var previous = ReadVisibleNativeRowSignature();
            if (position.RangeValue is { } targetRange
                && scroll.RangeValuePattern is not null)
            {
                var minimum = TryRead(() => scroll.RangeValuePattern.Minimum.ValueOrDefault);
                if (Math.Abs(targetRange - minimum) <= 0.001
                    && TryMoveNativeGridWithKeyboard(forward: false)
                    && WaitForVisibleNativeRowsToChange(
                        previous,
                        stopwatch,
                        timeoutMs,
                        maximumWaitMilliseconds: 1000))
                {
                    return true;
                }
            }

            var restored = false;
            if (scroll.ScrollPattern is not null && position.ScrollPercent is { } scrollPercent)
            {
                restored = TryRead(() =>
                {
                    scroll.ScrollPattern.SetScrollPercent(-1, scrollPercent);
                    return true;
                });
            }
            else if (scroll.RangeValuePattern is not null && position.RangeValue is { } rangeValue)
            {
                restored = TryRead(() =>
                {
                    scroll.RangeValuePattern.SetValue(rangeValue);
                    return true;
                });
            }

            if (!restored)
            {
                return false;
            }

            return WaitForVisibleNativeRowsToChange(previous, stopwatch, timeoutMs);
        }

        private bool TryDragGridThumbToStart(GridScrollState scroll)
        {
            if (scroll.ScrollBar is null || scroll.Thumb is null)
            {
                return false;
            }

            var scrollBounds = TryRead(() => scroll.ScrollBar.BoundingRectangle);
            var thumbBounds = TryRead(() => scroll.Thumb.BoundingRectangle);
            var targetCenterY = scrollBounds.Top + scrollBounds.Width + thumbBounds.Height / 2;
            if (thumbBounds.Top <= scrollBounds.Top + scrollBounds.Width + 1)
            {
                return false;
            }

            return TryDragGridThumb(scroll.Thumb, targetCenterY);
        }

        private bool TryDragGridThumbForward(GridScrollState scroll)
        {
            if (scroll.ScrollBar is null || scroll.Thumb is null)
            {
                return false;
            }

            var scrollBounds = TryRead(() => scroll.ScrollBar.BoundingRectangle);
            var thumbBounds = TryRead(() => scroll.Thumb.BoundingRectangle);
            var maximumTop = scrollBounds.Bottom - scrollBounds.Width - thumbBounds.Height;
            if (thumbBounds.Top >= maximumTop - 1)
            {
                return false;
            }

            var delta = Math.Max(1, Math.Min(3, thumbBounds.Height / 4));
            return TryDragGridThumb(
                scroll.Thumb,
                Math.Min(maximumTop + thumbBounds.Height / 2, thumbBounds.Top + thumbBounds.Height / 2 + delta));
        }

        private bool TryDragGridThumb(AutomationElement thumb, int targetCenterY)
        {
            if (!TryRead(() => thumb.IsAvailable) || !HasVisibleBounds(thumb))
            {
                return false;
            }

            PrepareForPhysicalGridInput(thumb);
            return TryRead(() =>
            {
                var bounds = TryRead(() => thumb.BoundingRectangle);
                var start = new System.Drawing.Point(
                    bounds.Left + bounds.Width / 2,
                    bounds.Top + bounds.Height / 2);
                Mouse.Position = start;
                Mouse.Down(MouseButton.Left);
                Mouse.Position = new System.Drawing.Point(start.X, targetCenterY);
                Mouse.Up(MouseButton.Left);
                return true;
            });
        }

        private bool TryPageGridScrollBar(GridScrollState scroll, bool forward)
        {
            if (scroll.ScrollBar is null || scroll.Thumb is null)
            {
                return false;
            }

            var scrollBounds = TryRead(() => scroll.ScrollBar.BoundingRectangle);
            var thumbBounds = TryRead(() => scroll.Thumb.BoundingRectangle);
            var trackStart = scrollBounds.Top + scrollBounds.Width;
            var trackEnd = scrollBounds.Bottom - scrollBounds.Width;
            var availableStart = forward ? thumbBounds.Bottom + 2 : trackStart + 1;
            var availableEnd = forward ? trackEnd - 1 : thumbBounds.Top - 2;
            if (availableStart > availableEnd)
            {
                return false;
            }

            PrepareForPhysicalGridInput(scroll.ScrollBar);
            return TryRead(() =>
            {
                Mouse.Position = new System.Drawing.Point(
                    scrollBounds.Left + scrollBounds.Width / 2,
                    availableStart + (availableEnd - availableStart) / 2);
                Mouse.LeftClick();
                return true;
            });
        }

        private string ReadVisibleNativeRowSignature()
        {
            return CreateNativeRowSignature(ReadNativeDataRows());
        }

        private static string CreateNativeRowSignature(IEnumerable<NativeFlaUiRow> rows)
        {
            return string.Join(
                "\u001e",
                rows.Where(static row => row.IsVisible).Select(static row =>
                    row.Cells.Count == 0 ? string.Empty : row.GetCellText(0)));
        }

        private NativeFlaUiRow[] TakePrefetchedNativeRows()
        {
            var rows = _prefetchedNativeRows;
            _prefetchedNativeRows = null;
            return rows ?? ReadNativeDataRows();
        }

        private bool WaitForVisibleNativeRowsToChange(
            string previous,
            Stopwatch stopwatch,
            int timeoutMs,
            int maximumWaitMilliseconds = 300)
        {
            var waitDeadline = Math.Min(
                timeoutMs,
                stopwatch.ElapsedMilliseconds + maximumWaitMilliseconds);
            while (stopwatch.ElapsedMilliseconds < waitDeadline)
            {
                var remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
                if (remaining <= 0)
                {
                    return false;
                }

                Thread.Sleep(Math.Min(25, remaining));
                var rows = ReadNativeDataRows();
                if (!string.Equals(previous, CreateNativeRowSignature(rows), StringComparison.Ordinal))
                {
                    _prefetchedNativeRows = rows;
                    return true;
                }
            }

            return false;
        }

        private static bool TryClickGridScrollButton(AutomationElement button)
        {
            if (!TryRead(() => button.IsAvailable)
                || !TryRead(() => button.IsEnabled))
            {
                return false;
            }

            return TryRead(() =>
            {
                var typedButton = button.AsButton();
                if (typedButton.Patterns.Invoke.IsSupported)
                {
                    typedButton.Invoke();
                }
                else
                {
                    typedButton.Click();
                }

                return true;
            });
        }

        private bool TryScrollGridWithWheel(AutomationElement root, double lines)
        {
            if (!TryRead(() => root.IsAvailable) || !HasVisibleBounds(root))
            {
                return false;
            }

            PrepareForPhysicalGridInput(root);
            return TryRead(() =>
            {
                var bounds = TryRead(() => root.BoundingRectangle);
                Mouse.Position = new System.Drawing.Point(
                    bounds.Left + bounds.Width / 2,
                    bounds.Top + bounds.Height * 3 / 4);
                Mouse.Scroll(lines);
                return true;
            });
        }

        private static bool TrySendGridMouseWheel(AutomationElement root, int wheelNotches)
        {
            if (!TryRead(() => root.IsAvailable) || !HasVisibleBounds(root) || wheelNotches == 0)
            {
                return false;
            }

            var windowHandle = FindNativeWindowHandle(root);
            if (windowHandle == IntPtr.Zero)
            {
                return false;
            }

            return TryRead(() =>
            {
                var bounds = TryRead(() => root.BoundingRectangle);
                var screenPoint = new System.Drawing.Point(
                    bounds.Left + bounds.Width / 2,
                    bounds.Top + bounds.Height * 3 / 4);
                var packedPoint = new IntPtr(
                    (screenPoint.X & 0xFFFF) | ((screenPoint.Y & 0xFFFF) << 16));
                var wheelDelta = wheelNotches * 120;
                SendMessage(
                    windowHandle,
                    WindowMessageMouseWheel,
                    new IntPtr(wheelDelta << 16),
                    packedPoint);
                return true;
            });
        }

        private bool TryMoveNativeGridWithKeyboard(bool forward)
        {
            var rows = ReadNativeDataRows();
            var target = forward ? rows.LastOrDefault() : rows.FirstOrDefault();
            if (target is null)
            {
                return false;
            }

            PrepareForPhysicalGridInput(target.Element);
            return TryRead(() =>
            {
                if (target.Element.Patterns.SelectionItem.IsSupported)
                {
                    target.Element.Patterns.SelectionItem.Pattern.Select();
                }

                TryFocus(target.Element);
                MoveMouseImmediatelyTo(target.Element);
                Mouse.LeftClick();
                if (forward)
                {
                    Keyboard.Press(VirtualKeyShort.NEXT);
                }
                else
                {
                    Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.HOME);
                }

                return true;
            });
        }

        private void PrepareForPhysicalGridInput(AutomationElement target)
        {
            _ = TryRead(() =>
            {
                _searchRoot.SetForeground();
                return true;
            });
            TryFocus(target);
        }

        private static int RemainingGridMilliseconds(Stopwatch stopwatch, int timeoutMs)
        {
            var remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                throw new TimeoutException(
                    "The grid operation exceeded its timeout while resolving a stable row.");
            }

            return remaining;
        }

        private static void MoveGridScrollToStart(
            IScrollPattern scroll,
            Stopwatch stopwatch,
            int timeoutMs)
        {
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                var previous = TryRead(() => scroll.VerticalScrollPercent.ValueOrDefault);
                if (previous <= 0)
                {
                    return;
                }

                TryRead(() =>
                {
                    scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeDecrement);
                    return true;
                });
                Thread.Sleep(25);
                var current = TryRead(() => scroll.VerticalScrollPercent.ValueOrDefault);
                if (current >= previous)
                {
                    return;
                }
            }
        }

        private static bool ScrollGridForward(IScrollPattern scroll)
        {
            var previous = TryRead(() => scroll.VerticalScrollPercent.ValueOrDefault);
            if (previous < 0 || previous >= 100)
            {
                return false;
            }

            var scrolled = TryRead(() =>
            {
                scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
                return true;
            });
            if (!scrolled)
            {
                return false;
            }

            Thread.Sleep(25);
            return TryRead(() => scroll.VerticalScrollPercent.ValueOrDefault) > previous;
        }

        private string DescribeIndexedResolution(GridIndexedRowSelector selector, int matchCount)
        {
            var conditions = string.Join(", ", selector.Conditions.Select(static condition =>
                $"column[{condition.ColumnIndex}]='{condition.ExpectedText}'"));
            return $"grid='{AutomationId}'; selector={conditions}; matches={matchCount}";
        }

        private static object? ParseGridRuntimeValue(string? value, GridRuntimeColumn column)
        {
            if (value is null)
            {
                return null;
            }

            var culture = string.IsNullOrWhiteSpace(column.CultureName)
                ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo(column.CultureName);
            return column.ValueKind switch
            {
                GridCellValueKind.Number when decimal.TryParse(value, System.Globalization.NumberStyles.Number, culture, out var number) => number,
                GridCellValueKind.Date when DateTime.TryParse(value, culture, DateTimeStyles.AllowWhiteSpaces, out var date) => date,
                GridCellValueKind.Time when TimeSpan.TryParse(value, culture, out var time) => time,
                GridCellValueKind.Boolean when bool.TryParse(value, out var boolean) => boolean,
                _ => value
            };
        }

        private IReadOnlyList<IGridRowControl> ReadVisualRows()
        {
            var rows = ReadRows()
                .Select(row => (IGridRowControl)new FlaUiVisualGridRowControl(row))
                .ToArray();
            if (rows.Length > 0)
            {
                return rows;
            }

            var cellRows = ReadCellRows()
                .Select(row => (IGridRowControl)new FlaUiVisualGridCellBackedRowControl(row.Cells))
                .ToArray();
            if (cellRows.Length > 0)
            {
                return cellRows;
            }

            return _fallback?.Rows ?? Array.Empty<IGridRowControl>();
        }

        private AutomationElement[] ReadRows()
        {
            var rowPrefix = $"{AutomationId}_Row";
            return FindAutomationDescendants(_searchRoot)
                .Where(candidate => IsVisualGridRow(candidate, rowPrefix))
                .OrderBy(candidate => ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row"))
                .ToArray();
        }

        private VisualGridCellRow[] ReadCellRows()
        {
            var cellPrefix = $"{AutomationId}_Row";
            return FindAutomationDescendants(_searchRoot)
                .Select(static candidate => new VisualGridCellCandidate(
                    candidate,
                    TryRead(() => candidate.AutomationId),
                    RowIndex: ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Row"),
                    ColumnIndex: ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Cell")))
                .Where(candidate =>
                    candidate.AutomationId?.StartsWith(cellPrefix, StringComparison.Ordinal) == true
                    && candidate.RowIndex != int.MaxValue
                    && candidate.ColumnIndex != int.MaxValue
                    && HasExactVisualGridIndexSuffix(candidate.AutomationId, "_Cell"))
                .GroupBy(static candidate => candidate.RowIndex)
                .OrderBy(static group => group.Key)
                .Select(static group => new VisualGridCellRow(
                    group.Key,
                    group
                        .OrderBy(static candidate => candidate.ColumnIndex)
                        .Select(static candidate => candidate.Element)
                        .ToArray()))
                .ToArray();
        }

        private sealed record NativeGridColumnHeader(
            string? Name,
            System.Drawing.Rectangle Bounds);

        private sealed class NativeFlaUiRow
        {
            private readonly string?[] _cellTexts;
            private readonly bool[] _readCells;

            public NativeFlaUiRow(
                AutomationElement element,
                IReadOnlyList<AutomationElement> cells)
            {
                Element = element;
                Cells = cells;
                _cellTexts = new string?[cells.Count];
                _readCells = new bool[cells.Count];
            }

            public AutomationElement Element { get; }

            public IReadOnlyList<AutomationElement> Cells { get; }

            public bool IsVisible => HasVisibleBounds(Element);

            public string GetCellText(int columnIndex)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
                if (columnIndex >= Cells.Count)
                {
                    return string.Empty;
                }

                if (!_readCells[columnIndex])
                {
                    _cellTexts[columnIndex] = ReadNativeGridCellText(Cells[columnIndex]) ?? string.Empty;
                    _readCells[columnIndex] = true;
                }

                return _cellTexts[columnIndex] ?? string.Empty;
            }
        }

        private sealed record NativeGridRowSnapshot(
            IReadOnlyList<string> CellTexts,
            int? RowIndex,
            System.Drawing.Rectangle Bounds,
            GridScrollPosition ScrollPosition)
        {
            public bool HasSameVisualRow(NativeGridRowSnapshot other)
            {
                return HasSameStableRow(other);
            }

            public bool HasSameStableRow(NativeGridRowSnapshot other)
            {
                if (RowIndex is { } rowIndex && other.RowIndex is { } otherRowIndex)
                {
                    return rowIndex == otherRowIndex;
                }

                if (ScrollPosition.RangeValue is { } rangeValue
                    && other.ScrollPosition.RangeValue is { } otherRangeValue)
                {
                    var contentOffset = rangeValue + Bounds.Top;
                    var otherContentOffset = otherRangeValue + other.Bounds.Top;
                    return Math.Abs(contentOffset - otherContentOffset) <= 2;
                }

                return ScrollPosition.ScrollPercent is { } scrollPercent
                       && other.ScrollPosition.ScrollPercent is { } otherScrollPercent
                       && Math.Abs(scrollPercent - otherScrollPercent) <= 0.001
                       && Bounds == other.Bounds;
            }

        }

        private sealed record NativeGridScan(
            IReadOnlyList<NativeGridRowSnapshot> Rows,
            IReadOnlyList<NativeGridRowSnapshot> MatchingRows,
            NativeFlaUiRow? LiveMatchingRow);

        private sealed record GridScrollState(
            IScrollPattern? ScrollPattern,
            IRangeValuePattern? RangeValuePattern,
            AutomationElement? BackwardButton,
            AutomationElement? ForwardButton,
            AutomationElement? Root,
            AutomationElement? ScrollBar,
            AutomationElement? Thumb);

        private sealed record GridScrollPosition(double? ScrollPercent, double? RangeValue);

        private sealed record IndexedFlaUiRow(int RowIndex, IReadOnlyList<AutomationElement> Cells);
    }

    private static string? ReadNativeGridCellText(AutomationElement element)
    {
        var name = TryRead(() => element.Name);
        if (IsUsefulAutomationText(name))
        {
            return name;
        }

        var textCondition = element.Automation.ConditionFactory.ByControlType(ControlType.Text);
        var textChild = TryRead(() => element.FindFirstDescendant(textCondition));
        return textChild is null
            ? null
            : TryRead(() => textChild.Name);
    }

    private sealed record VisualGridCellCandidate(
        AutomationElement Element,
        string? AutomationId,
        int RowIndex,
        int ColumnIndex);

    private sealed record VisualGridCellRow(
        int RowIndex,
        IReadOnlyList<AutomationElement> Cells);

    private static bool TryDoubleClick(AutomationElement element, out Exception? exception)
    {
        try
        {
            TryScrollIntoView(element);
            TryFocus(element);
            MoveMouseImmediatelyTo(element);
            Mouse.LeftDoubleClick();
            exception = null;
            return true;
        }
        catch (Exception mouseException)
        {
            try
            {
                if (TrySendDoubleClickToContainingWindow(element))
                {
                    exception = null;
                    return true;
                }
            }
            catch (Exception nativeException)
            {
                exception = new AggregateException(
                    "The standard and native double-click paths both failed.",
                    mouseException,
                    nativeException);
                return false;
            }

            exception = mouseException;
            return false;
        }
    }

    private static void MoveMouseImmediatelyTo(AutomationElement element)
    {
        if (element.TryGetClickablePoint(out var point))
        {
            Mouse.Position = point;
            return;
        }

        var bounds = element.BoundingRectangle;
        Mouse.Position = new System.Drawing.Point(
            bounds.Left + bounds.Width / 2,
            bounds.Top + bounds.Height / 2);
    }

    private static void TryScrollIntoView(AutomationElement element)
    {
        try
        {
            if (element.Patterns.ScrollItem.IsSupported)
            {
                element.Patterns.ScrollItem.Pattern.ScrollIntoView();
            }
        }
        catch
        {
            // Some visual bridge elements expose no scroll pattern; double-click can still work.
        }
    }

    private static void TryFocus(AutomationElement element)
    {
        try
        {
            element.Focus();
        }
        catch
        {
            // Focus is best-effort before the mouse gesture.
        }
    }

    private sealed class FlaUiVisualGridRowControl : IGridRowControl
    {
        private readonly AutomationElement _inner;

        public FlaUiVisualGridRowControl(AutomationElement inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<IGridCellControl> Cells =>
            ReadCells().Select(cell => (IGridCellControl)new FlaUiVisualGridCellControl(cell)).ToArray();

        internal AutomationElement[] ReadAutomationCells() => ReadCells();

        private AutomationElement[] ReadCells()
        {
            var rowAutomationId = TryRead(() => _inner.AutomationId);
            if (string.IsNullOrWhiteSpace(rowAutomationId))
            {
                return Array.Empty<AutomationElement>();
            }

            var cellPrefix = $"{rowAutomationId}_Cell";
            return FindAutomationDescendants(_inner)
                .Where(candidate => IsVisualGridCell(candidate, cellPrefix))
                .OrderBy(candidate => ParseVisualGridIndex(TryRead(() => candidate.AutomationId), "_Cell"))
                .ToArray();
        }
    }

    private sealed class FlaUiVisualGridCellBackedRowControl : IGridRowControl
    {
        private readonly IReadOnlyList<AutomationElement> _cells;

        public FlaUiVisualGridCellBackedRowControl(IReadOnlyList<AutomationElement> cells)
        {
            _cells = cells ?? throw new ArgumentNullException(nameof(cells));
        }

        public IReadOnlyList<IGridCellControl> Cells =>
            _cells.Select(cell => (IGridCellControl)new FlaUiVisualGridCellControl(cell)).ToArray();
    }

    private sealed class FlaUiVisualGridCellControl : IGridCellControl
    {
        private readonly AutomationElement _inner;

        public FlaUiVisualGridCellControl(AutomationElement inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string Value => ReadVisualGridCellText(_inner) ?? string.Empty;
    }

    private sealed class FlaUiDataGridViewControl : FlaUiControlBase<DataGridView>, IGridControl
    {
        public FlaUiDataGridViewControl(DataGridView inner) : base(inner)
        {
        }

        public IReadOnlyList<IGridRowControl> Rows =>
            Inner.Rows.Select(row => (IGridRowControl)new FlaUiObjectGridRowControl(row)).ToArray();

        public IGridRowControl? GetRowByIndex(int index)
        {
            var rows = Inner.Rows;
            if (index < 0 || index >= rows.Length)
            {
                return null;
            }

            return new FlaUiObjectGridRowControl(rows[index]);
        }
    }

    private sealed class FlaUiGridRowControl : IGridRowControl
    {
        private readonly GridRow _inner;

        public FlaUiGridRowControl(GridRow inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<IGridCellControl> Cells =>
            _inner.Cells.Select(cell => (IGridCellControl)new FlaUiGridCellControl(cell)).ToArray();
    }

    private sealed class FlaUiGridCellControl : IGridCellControl
    {
        private readonly GridCell _inner;

        public FlaUiGridCellControl(GridCell inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string Value =>
            ReadAutomationElementText(_inner)
            ?? ReadObjectText(TryRead(() => _inner.Value))
            ?? string.Empty;
    }

    private sealed class FlaUiObjectGridRowControl : IGridRowControl
    {
        private readonly object _inner;

        public FlaUiObjectGridRowControl(object inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<IGridCellControl> Cells
        {
            get
            {
                var cellsProperty = _inner.GetType().GetProperty("Cells");
                if (cellsProperty?.GetValue(_inner) is not System.Collections.IEnumerable cells)
                {
                    return Array.Empty<IGridCellControl>();
                }

                var result = new List<IGridCellControl>();
                foreach (var cell in cells)
                {
                    if (cell is not null)
                    {
                        result.Add(new FlaUiObjectGridCellControl(cell));
                    }
                }

                return result;
            }
        }
    }

    private sealed class FlaUiObjectGridCellControl : IGridCellControl
    {
        private readonly object _inner;

        public FlaUiObjectGridCellControl(object inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string Value
        {
            get
            {
                var valueProperty = _inner.GetType().GetProperty("Value");
                return ReadObjectText(valueProperty?.GetValue(_inner))
                    ?? _inner.ToString()
                    ?? string.Empty;
            }
        }
    }

    private static string? ReadObjectText(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.GetType().GetProperty("Text")?.GetValue(value)?.ToString();
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var content = value.GetType().GetProperty("Content")?.GetValue(value)?.ToString();
        if (!string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        var display = value.ToString();
        return IsUsefulAutomationText(display) ? display : null;
    }

    private static AutomationElement[] FindAutomationDescendants(AutomationElement element)
    {
        return TryRead(() => element.FindAllDescendants()) ?? Array.Empty<AutomationElement>();
    }

    private static AutomationElement? ResolveGridEditorPart(
        AutomationElement searchRoot,
        AutomationElement cell,
        AutomationElement? gridRoot,
        GridRelativeLocator? locator,
        Func<GridRelativeLocator, InvalidOperationException> ambiguityFactory)
    {
        if (locator is null)
        {
            return null;
        }

        bool Matches(AutomationElement candidate) =>
            TryRead(() => candidate.IsAvailable && !candidate.IsOffscreen)
            && (locator.LocatorKind switch
            {
                UiLocatorKind.AutomationId => string.Equals(
                    TryRead(() => candidate.AutomationId),
                    locator.LocatorValue,
                    StringComparison.Ordinal),
                UiLocatorKind.Name => string.Equals(
                    TryRead(() => candidate.Name),
                    locator.LocatorValue,
                    StringComparison.Ordinal),
                _ => false
            });

        if (locator.Scope == GridRelativeLocatorScope.EditorRoot)
        {
            var roots = TryRead(() => cell.FindAllChildren()) ?? Array.Empty<AutomationElement>();
            var matchingRoots = roots
                .Select(root => new
                {
                    Matches = new[] { root }
                        .Concat(FindAutomationDescendants(root))
                        .Where(Matches)
                        .Take(2)
                        .ToArray()
                })
                .Where(static candidate => candidate.Matches.Length > 0)
                .Take(2)
                .ToArray();
            if (matchingRoots.Length > 1 || matchingRoots.FirstOrDefault()?.Matches.Length > 1)
            {
                throw ambiguityFactory(locator);
            }

            return matchingRoots.SingleOrDefault()?.Matches.Single();
        }

        IEnumerable<AutomationElement> candidates = locator.Scope switch
        {
            GridRelativeLocatorScope.Cell =>
                new[] { cell }.Concat(FindAutomationDescendants(cell)),
            GridRelativeLocatorScope.GridRoot when gridRoot is not null =>
                new[] { gridRoot }.Concat(FindAutomationDescendants(gridRoot)),
            GridRelativeLocatorScope.DetachedPopup => EnumerateProcessElements(searchRoot),
            _ => Array.Empty<AutomationElement>()
        };
        var matches = candidates
            .Where(Matches)
            .Take(2)
            .ToArray();
        if (matches.Length > 1)
        {
            throw ambiguityFactory(locator);
        }

        return matches.SingleOrDefault();
    }

    private static IEnumerable<AutomationElement> EnumerateProcessElements(AutomationElement searchRoot)
    {
        var processId = TryRead(() => searchRoot.FrameworkAutomationElement.ProcessId.ValueOrDefault);
        var desktop = TryRead(() => searchRoot.Automation.GetDesktop());
        if (processId <= 0 || desktop is null)
        {
            return Array.Empty<AutomationElement>();
        }

        var roots = TryRead(() => desktop.FindAllChildren(factory => factory.ByProcessId(processId)))
            ?? Array.Empty<AutomationElement>();
        return roots.SelectMany(root => new[] { root }.Concat(FindAutomationDescendants(root)));
    }

    private static bool IsVisualGridRow(AutomationElement candidate, string rowPrefix)
    {
        var automationId = TryRead(() => candidate.AutomationId);
        return automationId?.StartsWith(rowPrefix, StringComparison.Ordinal) == true
            && !automationId.Contains("_Cell", StringComparison.Ordinal)
            && ParseVisualGridIndex(automationId, "_Row") != int.MaxValue;
    }

    private static bool IsVisualGridCell(AutomationElement candidate, string cellPrefix)
    {
        var automationId = TryRead(() => candidate.AutomationId);
        return automationId?.StartsWith(cellPrefix, StringComparison.Ordinal) == true
            && HasExactVisualGridIndexSuffix(automationId, "_Cell");
    }

    private static bool HasExactVisualGridIndexSuffix(string? automationId, string marker)
    {
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return false;
        }

        var markerIndex = automationId.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var digitStart = markerIndex + marker.Length;
        var digitEnd = digitStart;
        while (digitEnd < automationId.Length && char.IsDigit(automationId[digitEnd]))
        {
            digitEnd++;
        }

        return digitEnd > digitStart && digitEnd == automationId.Length;
    }

    private static int ParseVisualGridIndex(string? automationId, string marker)
    {
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return int.MaxValue;
        }

        var markerIndex = automationId.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return int.MaxValue;
        }

        var digitStart = markerIndex + marker.Length;
        var digitEnd = digitStart;
        while (digitEnd < automationId.Length && char.IsDigit(automationId[digitEnd]))
        {
            digitEnd++;
        }

        if (digitEnd == digitStart)
        {
            return int.MaxValue;
        }

        var digits = automationId[digitStart..digitEnd];
        return int.TryParse(digits, System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : int.MaxValue;
    }

    private static string? ReadVisualGridCellText(AutomationElement element)
    {
        var name = TryRead(() => element.Name);
        if (IsUsefulAutomationText(name))
        {
            return name;
        }

        return FindAutomationDescendants(element)
            .Select(static candidate => TryRead(() => candidate.Name))
            .FirstOrDefault(IsUsefulAutomationText);
    }

    private static string? ReadAutomationElementText(AutomationElement element)
    {
        if (element is null)
        {
            return null;
        }

        var name = TryRead(() => element.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        try
        {
            if (element.Patterns.Value.IsSupported)
            {
                var value = element.Patterns.Value.Pattern.Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // Ignore pattern access errors and continue with fallbacks.
        }

        var descendants = FindAutomationDescendants(element);
        var textChild = descendants
            .FirstOrDefault(candidate => candidate.ControlType == ControlType.Text);
        if (textChild is not null)
        {
            var textChildName = TryRead(() => textChild.Name);
            if (IsUsefulAutomationText(textChildName))
            {
                return textChildName;
            }
        }

        var namedDescendant = descendants
            .Select(static candidate => TryRead(() => candidate.Name))
            .FirstOrDefault(IsUsefulAutomationText);
        if (!string.IsNullOrWhiteSpace(namedDescendant))
        {
            return namedDescendant;
        }

        var automationId = TryRead(() => element.AutomationId);
        return string.IsNullOrWhiteSpace(automationId) ? name : automationId;
    }

    private static string? ReadAutomationElementVisibleText(AutomationElement element)
    {
        if (element is null)
        {
            return null;
        }

        var name = TryRead(() => element.Name);
        if (IsUsefulAutomationText(name))
        {
            return name;
        }

        try
        {
            if (element.Patterns.Value.IsSupported)
            {
                var value = element.Patterns.Value.Pattern.Value;
                if (IsUsefulAutomationText(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // Ignore pattern access errors and continue with text descendants.
        }

        var textChild = FindAutomationDescendants(element)
            .FirstOrDefault(candidate => candidate.ControlType == ControlType.Text);
        if (textChild is not null)
        {
            var textChildName = TryRead(() => textChild.Name);
            if (IsUsefulAutomationText(textChildName))
            {
                return textChildName;
            }
        }

        return FindAutomationDescendants(element)
            .Select(static candidate => TryRead(() => candidate.Name))
            .FirstOrDefault(IsUsefulAutomationText);
    }

    private static bool IsUsefulAutomationText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !value.StartsWith("Avalonia.Controls.", StringComparison.Ordinal)
            && !string.Equals(value, "TextBlock", StringComparison.Ordinal);
    }

    private static bool TrySelectTreeItem(TreeItem treeItem)
    {
        try
        {
            if (treeItem.Patterns.SelectionItem.IsSupported)
            {
                treeItem.Patterns.SelectionItem.Pattern.Select();
                return true;
            }
        }
        catch
        {
        }

        if (TrySendSpaceToFocusedTreeItem(treeItem))
        {
            return true;
        }

        try
        {
            treeItem.Select();
            return true;
        }
        catch
        {
        }

        return false;
    }

    private static bool TrySendSpaceToFocusedTreeItem(TreeItem treeItem)
    {
        try
        {
            treeItem.Focus();
            if (TryRead(() => treeItem.Properties.HasKeyboardFocus.Value) != true)
            {
                return false;
            }

            var windowHandle = FindNativeWindowHandle(treeItem);
            if (windowHandle == IntPtr.Zero)
            {
                return false;
            }

            SendMessage(
                windowHandle,
                WindowMessageKeyDown,
                new IntPtr(VirtualKeySpace),
                new IntPtr(SpaceKeyDownData));
            SendMessage(
                windowHandle,
                WindowMessageKeyUp,
                new IntPtr(VirtualKeySpace),
                new IntPtr(SpaceKeyUpData));
            return true;
        }
        catch
        {
        }

        return false;
    }

    private static bool TrySendDoubleClickToContainingWindow(AutomationElement element)
    {
        if (!element.TryGetClickablePoint(out var screenPoint))
        {
            var bounds = element.BoundingRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return false;
            }

            screenPoint = new System.Drawing.Point(
                bounds.Left + bounds.Width / 2,
                bounds.Top + bounds.Height / 2);
        }

        var windowHandle = FindNativeWindowHandle(element);
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var clientPoint = new NativePoint(screenPoint.X, screenPoint.Y);
        if (!ScreenToClient(windowHandle, ref clientPoint))
        {
            return false;
        }

        var packedPoint = new IntPtr(
            (clientPoint.X & 0xFFFF) | ((clientPoint.Y & 0xFFFF) << 16));
        SendMessage(
            windowHandle,
            WindowMessageLeftButtonDown,
            new IntPtr(MouseKeyLeftButton),
            packedPoint);
        SendMessage(windowHandle, WindowMessageLeftButtonUp, IntPtr.Zero, packedPoint);
        Thread.Sleep(50);
        SendMessage(
            windowHandle,
            WindowMessageLeftButtonDown,
            new IntPtr(MouseKeyLeftButton),
            packedPoint);
        SendMessage(windowHandle, WindowMessageLeftButtonUp, IntPtr.Zero, packedPoint);
        return true;
    }

    private static IntPtr FindNativeWindowHandle(AutomationElement element)
    {
        AutomationElement? current = element;
        while (current is not null)
        {
            var windowHandle = TryRead(
                () => current.FrameworkAutomationElement.NativeWindowHandle.ValueOrDefault);
            if (windowHandle != IntPtr.Zero)
            {
                return windowHandle;
            }

            current = TryRead(() => current.Parent);
        }

        return IntPtr.Zero;
    }

    private static bool TryActivateTreeSelectionCandidate(AutomationElement candidate)
    {
        return TryClickTreeSelectionCandidate(candidate)
            || TryInvokeTreeSelectionCandidate(candidate)
            || TrySelectTreeSelectionCandidate(candidate);
    }

    private static bool TryClickTreeSelectionCandidate(AutomationElement candidate)
    {
        try
        {
            candidate.Click();
            return true;
        }
        catch
        {
        }

        return false;
    }

    private static bool TryInvokeTreeSelectionCandidate(AutomationElement candidate)
    {
        try
        {
            if (candidate.Patterns.Invoke.IsSupported)
            {
                candidate.Patterns.Invoke.Pattern.Invoke();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TrySelectTreeSelectionCandidate(AutomationElement candidate)
    {
        try
        {
            if (candidate.Patterns.SelectionItem.IsSupported)
            {
                candidate.Patterns.SelectionItem.Pattern.Select();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsTreeSelectionCandidate(AutomationElement candidate)
    {
        return candidate.ControlType == ControlType.Text
            || candidate.ControlType == ControlType.Button
            || candidate.ControlType == ControlType.Custom
            || candidate.ControlType == ControlType.Pane
            || candidate.ControlType == ControlType.Group
            || candidate.ControlType == ControlType.TreeItem
            || candidate.ControlType == ControlType.ListItem;
    }

    private static int GetTreeSelectionCandidatePriority(AutomationElement candidate, string normalizedTargetText)
    {
        var candidateText = NormalizeLookupText(ReadAutomationElementText(candidate));
        if (!string.IsNullOrWhiteSpace(normalizedTargetText)
            && string.Equals(candidateText, normalizedTargetText, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return candidate.ControlType switch
        {
            ControlType.Text => 1,
            ControlType.Button => 2,
            ControlType.Custom => 3,
            ControlType.Pane => 4,
            ControlType.Group => 5,
            ControlType.TreeItem => 6,
            ControlType.ListItem => 7,
            _ => int.MaxValue
        };
    }

    private static string NormalizeLookupText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr windowHandle, ref NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }
}
