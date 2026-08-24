using System.Collections;
using System.Text;
using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Internal.AutomationModel;
using AppAutomation.Avalonia.Headless.Internal.AutomationModel.Conditions;
using Avalonia.Automation;
using AvaloniaControl = Avalonia.Controls.Control;
using AvaloniaTemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace AppAutomation.Avalonia.Headless.Automation;

public sealed class HeadlessControlResolver : IUiControlResolver, IUiArtifactCollector
{
    private readonly Window _window;
    private readonly ConditionFactory _conditionFactory;

    public HeadlessControlResolver(AvaloniaWindow window)
    {
        _window = new Window(window ?? throw new ArgumentNullException(nameof(window)));
        _conditionFactory = new ConditionFactory();
    }

    public UiRuntimeCapabilities Capabilities { get; } = new(
        AdapterId: "avalonia-headless",
        SupportsGridCellAccess: true,
        SupportsCalendarRangeSelection: false,
        SupportsTreeNodeExpansionState: false,
        SupportsRawNativeHandles: false,
        SupportsScreenshots: false);

    public TControl Resolve<TControl>(UiControlDefinition definition)
        where TControl : class
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (typeof(TControl) == typeof(IMultiSelectItemsControl))
        {
            return (TControl)(object)new HeadlessMultiSelectItemsControl(FindElement(definition));
        }

        if (typeof(TControl) == typeof(ISearchHistoryItemsControl))
        {
            return (TControl)(object)new HeadlessSearchHistoryItemsControl(
                () => FindSearchHistoryButtons(definition),
                definition.LocatorValue);
        }

        object resolved = definition.ControlType switch
        {
            UiControlType.TextBox => new HeadlessTextBoxControl(FindElement(definition).AsTextBox()),
            UiControlType.Button => new HeadlessButtonControl(FindElement(definition).AsButton()),
            UiControlType.Label => new HeadlessLabelControl(FindElement(definition).AsLabel()),
            UiControlType.ListBox => new HeadlessListBoxControl(FindElement(definition).AsListBox()),
            UiControlType.CheckBox => new HeadlessCheckBoxControl(FindElement(definition).AsCheckBox()),
            UiControlType.ComboBox => new HeadlessComboBoxControl(FindElement(definition).AsComboBox()),
            UiControlType.RadioButton => new HeadlessRadioButtonControl(FindElement(definition).AsRadioButton()),
            UiControlType.ToggleButton => new HeadlessToggleButtonControl(FindElement(definition).AsToggleButton()),
            UiControlType.Slider => new HeadlessSliderControl(FindElement(definition).AsSlider()),
            UiControlType.ProgressBar => new HeadlessProgressBarControl(FindElement(definition).AsProgressBar()),
            UiControlType.Calendar => new HeadlessCalendarControl(FindElement(definition).AsCalendar()),
            UiControlType.DateTimePicker => new HeadlessDateTimePickerControl(FindElement(definition).AsDateTimePicker()),
            UiControlType.TimePicker => new HeadlessTimePickerControl(FindElement(definition).AsTimePicker()),
            UiControlType.Expander => new HeadlessExpanderControl(FindElement(definition).AsExpander()),
            UiControlType.Spinner => new HeadlessSpinnerControl(FindElement(definition).AsSpinner()),
            UiControlType.Tab => new HeadlessTabControl(FindElement(definition).AsTab()),
            UiControlType.TabItem => new HeadlessTabItemControl(FindElement(definition).AsTabItem()),
            UiControlType.Tree => new HeadlessTreeControl(FindElement(definition).AsTree()),
            UiControlType.TreeItem => new HeadlessTreeItemControl(FindElement(definition).AsTreeItem()),
            UiControlType.DataGridView => new HeadlessGridControl(FindGrid(definition)),
            UiControlType.Grid => ResolveGrid(definition),
            UiControlType.DataGridViewRow or UiControlType.GridRow => new HeadlessGridRowControl(FindGridRow(definition)),
            UiControlType.DataGridViewCell or UiControlType.GridCell => new HeadlessGridCellControl(FindGridCell(definition)),
            UiControlType.ShellNavigation => new HeadlessShellNavigationControl(FindElement(definition)),
            _ => new HeadlessUiControl(FindElement(definition))
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

        var logicalTree = BuildLogicalTreeSnapshot();
        var controlState = BuildControlStateSnapshot(failureContext.LocatorValue, failureContext.LocatorKind);

        IReadOnlyList<UiFailureArtifact> artifacts =
        [
            new UiFailureArtifact(
                Kind: "logical-tree",
                LogicalName: "logical-tree",
                RelativePath: "artifacts/ui-failures/avalonia-headless/logical-tree.txt",
                ContentType: "text/plain",
                IsRequiredByContract: true,
                InlineTextPreview: logicalTree),
            new UiFailureArtifact(
                Kind: "control-state",
                LogicalName: "control-state",
                RelativePath: "artifacts/ui-failures/avalonia-headless/control-state.txt",
                ContentType: "text/plain",
                IsRequiredByContract: true,
                InlineTextPreview: controlState)
        ];

        return ValueTask.FromResult(artifacts);
    }

    private Grid FindGrid(UiControlDefinition definition)
    {
        return definition.ControlType == UiControlType.DataGridView
            ? FindElement(definition).AsDataGridView()
            : FindElement(definition).AsGrid();
    }

    private GridRow FindGridRow(UiControlDefinition definition)
    {
        return FindElement(definition).AsGridRow();
    }

    private GridCell FindGridCell(UiControlDefinition definition)
    {
        return FindElement(definition).AsGridCell();
    }

    private IGridControl ResolveGrid(UiControlDefinition definition)
    {
        var element = FindElement(definition);
        var nativeGrid = TryRead(() => element.AsGrid());
        return nativeGrid is null
            ? new HeadlessVisualGridControl(element)
            : new HeadlessGridControl(nativeGrid);
    }

    private AutomationElement FindElement(UiControlDefinition definition)
    {
        if (definition.Scope is not null)
        {
            return FindScopedElement(definition)
                ?? throw new InvalidOperationException(
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

        throw new InvalidOperationException(
            $"Element with locator [{definition.LocatorKind}:{definition.LocatorValue}] was not found.");
    }

    private AutomationElement? FindScopedElement(UiControlDefinition definition)
    {
        return AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
        {
            var scope = definition.Scope!;
            var roots = ControlTree.EnumerateDescendants(_window.Native)
                .Where(candidate => MatchesLocator(candidate, scope));
            var match = roots
                .SelectMany(ControlTree.EnumerateDescendants)
                .FirstOrDefault(candidate => MatchesLocator(
                    candidate,
                    new UiControlScope(
                        definition.LocatorValue,
                        definition.LocatorKind,
                        definition.FallbackToName)));
            return match is null ? null : AutomationElement.WrapControl(match);
        });
    }

    private Button[] FindSearchHistoryButtons(UiControlDefinition definition)
    {
        return AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
        {
            var roots = definition.Scope is null
                ? new[] { (global::Avalonia.Controls.Control)_window.Native }
                : ControlTree.EnumerateDescendants(_window.Native)
                    .Where(candidate => MatchesLocator(candidate, definition.Scope))
                    .ToArray();
            return roots
                .SelectMany(ControlTree.EnumerateDescendants)
                .OfType<global::Avalonia.Controls.Button>()
                .Where(candidate => MatchesSearchHistoryLocator(candidate, definition))
                .Select(static candidate => AutomationElement.WrapControl(candidate).AsButton())
                .ToArray();
        });
    }

    private static bool MatchesSearchHistoryLocator(
        global::Avalonia.Controls.Button candidate,
        UiControlDefinition definition)
    {
        return MatchesLocator(
            candidate,
            new UiControlScope(
                definition.LocatorValue,
                definition.LocatorKind,
                definition.FallbackToName));
    }

    private static bool MatchesLocator(
        global::Avalonia.Controls.Control candidate,
        UiControlScope scope)
    {
        var locatorValue = scope.LocatorValue.Trim();
        var primaryValue = scope.LocatorKind switch
        {
            UiLocatorKind.AutomationId => AutomationProperties.GetAutomationId(candidate),
            UiLocatorKind.Name => AutomationProperties.GetName(candidate),
            _ => null
        };
        if (string.Equals(primaryValue, locatorValue, StringComparison.Ordinal))
        {
            return true;
        }

        return scope.FallbackToName
            && scope.LocatorKind != UiLocatorKind.Name
            && string.Equals(AutomationProperties.GetName(candidate), locatorValue, StringComparison.Ordinal);
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
        var normalized = locatorValue.Trim();
        return AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
        {
            var match = FindByAutomationId(normalized);
            if (match is null && TryGetDerivedEditorRootAutomationId(normalized, out var rootAutomationId))
            {
                var editorRoot = FindByAutomationId(rootAutomationId);
                if (editorRoot is AvaloniaTemplatedControl templatedEditor)
                {
                    templatedEditor.ApplyTemplate();
                    match = FindByAutomationId(normalized);
                }
            }

            return match is null ? null : AutomationElement.WrapControl(match);
        });
    }

    private AvaloniaControl? FindByAutomationId(string automationId)
    {
        return ControlTree.EnumerateDescendants(_window.Native)
            .FirstOrDefault(candidate =>
                string.Equals(
                    AutomationProperties.GetAutomationId(candidate) ?? string.Empty,
                    automationId,
                    StringComparison.Ordinal));
    }

    private static bool TryGetDerivedEditorRootAutomationId(
        string automationId,
        out string rootAutomationId)
    {
        string[] supportedSuffixes =
        [
            "_Input",
            "_OpenButton",
            "_Results",
            "_ClearButton",
            "_ApplyButton"
        ];

        foreach (var suffix in supportedSuffixes)
        {
            if (automationId.EndsWith(suffix, StringComparison.Ordinal)
                && automationId.Length > suffix.Length)
            {
                rootAutomationId = automationId[..^suffix.Length];
                return true;
            }
        }

        rootAutomationId = string.Empty;
        return false;
    }

    private AutomationElement? SearchByName(string locatorValue)
    {
        var normalized = locatorValue.Trim();
        return AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
        {
            var match = ControlTree.EnumerateDescendants(_window.Native)
                .FirstOrDefault(candidate =>
                {
                    var name = AutomationProperties.GetName(candidate) ?? candidate.Name ?? string.Empty;
                    return string.Equals(name, normalized, StringComparison.Ordinal)
                           || string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase);
                });
            return match is null ? null : AutomationElement.WrapControl(match);
        });
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

    private string BuildControlStateSnapshot(string? locatorValue, UiLocatorKind? locatorKind)
    {
        if (string.IsNullOrWhiteSpace(locatorValue) || locatorKind is null)
        {
            return "No locator context available.";
        }

        var element = locatorKind.Value switch
        {
            UiLocatorKind.AutomationId => SearchByAutomationId(locatorValue),
            UiLocatorKind.Name => SearchByName(locatorValue),
            _ => null
        };

        if (element is null)
        {
            return $"Element [{locatorKind}:{locatorValue}] was not found during artifact collection.";
        }

        var builder = new StringBuilder();
        builder.Append("ControlType=").Append(TryRead(() => element.ControlType.ToString()) ?? "<unknown>").AppendLine();
        builder.Append("AutomationId=").Append(TryRead(() => element.AutomationId) ?? string.Empty).AppendLine();
        builder.Append("Name=").Append(TryRead(() => element.Name) ?? string.Empty).AppendLine();
        builder.Append("IsEnabled=").Append(TryRead(() => element.IsEnabled) is bool isEnabled ? isEnabled.ToString() : "<unknown>");
        return builder.ToString();
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

    private static bool IsVisualGridRow(AutomationElement candidate, string rowPrefix)
    {
        var automationId = candidate.AutomationId;
        return automationId.StartsWith(rowPrefix, StringComparison.Ordinal)
            && !automationId.Contains("_Cell", StringComparison.Ordinal)
            && ParseVisualGridIndex(automationId, "_Row") != int.MaxValue;
    }

    private static bool IsVisualGridCell(AutomationElement candidate, string cellPrefix)
    {
        var automationId = candidate.AutomationId;
        return automationId.StartsWith(cellPrefix, StringComparison.Ordinal)
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
        return int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
            ? index
            : int.MaxValue;
    }

    private static string? ReadVisualGridCellText(AutomationElement element)
    {
        return AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
        {
            var directValue = ReadControlVisibleText(element.Control);
            if (!string.IsNullOrWhiteSpace(directValue))
            {
                return directValue;
            }

            var directName = AutomationElement.ReadControlName(element.Control);
            if (!string.IsNullOrWhiteSpace(directName))
            {
                return directName;
            }

            return ControlTree.EnumerateDescendants(element.Control)
                .Select(static candidate =>
                    ReadControlVisibleText(candidate) ?? AutomationElement.ReadControlName(candidate))
                .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name));
        });
    }

    private static string? ReadControlVisibleText(global::Avalonia.Controls.Control control)
    {
        return control switch
        {
            global::Avalonia.Controls.TextBox textBox => textBox.Text,
            global::Avalonia.Controls.TextBlock textBlock => textBlock.Text,
            global::Avalonia.Controls.Label label => label.Content?.ToString(),
            global::Avalonia.Controls.CheckBox checkBox => checkBox.Content?.ToString(),
            global::Avalonia.Controls.RadioButton radioButton => radioButton.Content?.ToString(),
            global::Avalonia.Controls.Primitives.ToggleButton toggleButton => toggleButton.Content?.ToString(),
            global::Avalonia.Controls.Button button => button.Content?.ToString(),
            global::Avalonia.Controls.ComboBox comboBox => ReadComboBoxItemText(comboBox.SelectedItem),
            global::Avalonia.Controls.DatePicker datePicker => datePicker.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            global::Avalonia.Controls.TimePicker timePicker => timePicker.SelectedTime?.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
            global::Avalonia.Controls.TabItem tabItem => tabItem.Header?.ToString(),
            global::Avalonia.Controls.TreeViewItem treeViewItem => treeViewItem.Header?.ToString(),
            global::Avalonia.Controls.Slider slider => slider.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            global::Avalonia.Controls.ProgressBar progressBar => progressBar.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static string? ReadComboBoxItemText(object? item)
    {
        return item switch
        {
            null => null,
            global::Avalonia.Controls.ComboBoxItem comboBoxItem => comboBoxItem.Content?.ToString(),
            global::Avalonia.Controls.ContentControl contentControl => contentControl.Content?.ToString(),
            _ => item.ToString()
        };
    }

    private static IReadOnlyList<string> ReadDisplayValues(object? item)
    {
        if (item is null)
        {
            return Array.Empty<string>();
        }

        if (item is string text)
        {
            return [text];
        }

        var properties = item.GetType()
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(static property => property.CanRead)
            .ToArray();

        var preferredProperties = properties
            .Where(static property =>
                property.Name.StartsWith("Eremex", StringComparison.Ordinal)
                && !property.Name.Contains("Automation", StringComparison.Ordinal))
            .ToArray();

        var displayProperties = preferredProperties.Length > 0
            ? preferredProperties
            : properties
                .Where(static property =>
                    property.PropertyType == typeof(string)
                    && !property.Name.Contains("Automation", StringComparison.Ordinal)
                    && !property.Name.EndsWith("Id", StringComparison.Ordinal))
                .ToArray();

        return displayProperties
            .Select(property => property.GetValue(item)?.ToString() ?? string.Empty)
            .ToArray();
    }

    private abstract class HeadlessControlBase<TControl> : IUiControlAvailability
        where TControl : AutomationElement
    {
        protected HeadlessControlBase(TControl inner)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        protected TControl Inner { get; }

        public string AutomationId => Inner.AutomationId ?? string.Empty;

        public string Name => Inner.Name ?? string.Empty;

        public bool IsEnabled => Inner.IsEnabled;

        public bool IsAvailable => Inner.IsAvailable
            && AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() => Inner.Control.IsEffectivelyVisible);
    }

    private sealed class HeadlessUiControl : HeadlessControlBase<AutomationElement>, IReadableTextControl
    {
        public HeadlessUiControl(AutomationElement inner) : base(inner)
        {
        }

        public string Text => ReadControlVisibleText(Inner.Control) ?? string.Empty;
    }

    private sealed class HeadlessShellNavigationControl : HeadlessControlBase<AutomationElement>, IShellNavigationControl, IReadableTextControl
    {
        public HeadlessShellNavigationControl(AutomationElement inner) : base(inner)
        {
        }

        public string? ActivePaneName => AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
            ReadPaneCandidates()
                .FirstOrDefault(static candidate => candidate.IsActive)
                ?.PrimaryName);

        public IReadOnlyList<string> OpenPaneNames => AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
            ReadPaneCandidates()
                .SelectMany(static candidate => candidate.Names)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());

        public string Text => ActivePaneName ?? string.Join(" ", OpenPaneNames);

        public void OpenOrActivate(ShellPaneNavigationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.PaneName);

            AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
            {
                var candidates = ReadPaneCandidates();
                var target = candidates.FirstOrDefault(candidate => PaneNameMatches(candidate, request.PaneName))
                    ?? throw new InvalidOperationException(
                        $"Shell pane '{request.PaneName}' was not found under shell '{AutomationId}'.");

                SetActive(target.Target, true);
                foreach (var candidate in candidates.Where(candidate =>
                             !ReferenceEquals(candidate.Target, target.Target)
                             && candidate.Target is not global::Avalonia.Controls.Control))
                {
                    SetActive(candidate.Target, false);
                }

                return true;
            });
        }

        private IReadOnlyList<ShellPaneCandidate> ReadPaneCandidates()
        {
            var dataContextCandidates = ReadDataContextPaneCandidates();
            return dataContextCandidates.Count > 0
                ? dataContextCandidates
                : ReadControlPaneCandidates();
        }

        private IReadOnlyList<ShellPaneCandidate> ReadDataContextPaneCandidates()
        {
            return EnumeratePaneItems(Inner.Control)
                .Concat(EnumeratePaneItems(Inner.Control.DataContext))
                .Select(static item => CreateDataContextPaneCandidate(item))
                .Where(static candidate => candidate is not null)
                .Select(static candidate => candidate!)
                .DistinctBy(static candidate => candidate.Target)
                .Where(static candidate => candidate.Names.Count > 0)
                .ToArray();
        }

        private IReadOnlyList<ShellPaneCandidate> ReadControlPaneCandidates()
        {
            return ControlTree.EnumerateDescendants(Inner.Control)
                .Select(static control => new ShellPaneCandidate(control, ReadPaneNames(control), IsActive(control)))
                .Where(static candidate => candidate.Names.Count > 0)
                .ToArray();
        }

        private static IEnumerable<object> EnumeratePaneItems(object? source)
        {
            if (source is null)
            {
                yield break;
            }

            foreach (var item in EnumerateEnumerable(source))
            {
                yield return item;
            }

            foreach (var item in EnumerateEnumerable(ReadPropertyValue(source, "ItemsSource")))
            {
                yield return item;
            }

            foreach (var item in EnumerateEnumerable(ReadPropertyValue(source, "ViewModelsCollection")))
            {
                yield return item;
            }

            var layout = ReadPropertyValue(source, "Layout");
            foreach (var item in EnumerateEnumerable(ReadPropertyValue(layout, "ViewModelsCollection")))
            {
                yield return item;
            }
        }

        private static IEnumerable<object> EnumerateEnumerable(object? source)
        {
            if (source is not IEnumerable enumerable || source is string)
            {
                yield break;
            }

            foreach (var item in enumerable)
            {
                if (item is not null)
                {
                    yield return item;
                }
            }
        }

        private static ShellPaneCandidate CreateDataContextPaneCandidate(object item)
        {
            if (item is global::Avalonia.Controls.Control control)
            {
                var target = control.DataContext ?? control;
                var names = NormalizePaneNames(ReadPaneNames(control).Concat(ReadPaneNames(target)));
                return new ShellPaneCandidate(target, names, IsActive(control) || IsActive(target));
            }

            return new ShellPaneCandidate(item, ReadPaneNames(item), IsActive(item));
        }

        private static IReadOnlyList<string> ReadPaneNames(global::Avalonia.Controls.Control control)
        {
            var values = new[]
            {
                AutomationProperties.GetAutomationId(control),
                control.Name,
                ReadStringProperty(control, "Header"),
                ReadStringProperty(control, "Title"),
                ReadStringProperty(control.DataContext, "ViewModelId"),
                ReadStringProperty(control.DataContext, "Header"),
                ReadStringProperty(control.DataContext, "Title")
            };

            return NormalizePaneNames(values);
        }

        private static IReadOnlyList<string> ReadPaneNames(object item)
        {
            var values = new[]
            {
                ReadStringProperty(item, "ViewModelId"),
                ReadStringProperty(item, "AutomationId"),
                ReadStringProperty(item, "Name"),
                ReadStringProperty(item, "Header"),
                ReadStringProperty(item, "Title")
            };

            return NormalizePaneNames(values);
        }

        private static IReadOnlyList<string> NormalizePaneNames(IEnumerable<string?> values)
        {
            return values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool IsActive(global::Avalonia.Controls.Control control)
        {
            return ReadBooleanProperty(control, "IsActive")
                   || ReadBooleanProperty(control.DataContext, "IsActive");
        }

        private static bool IsActive(object item)
        {
            return item is global::Avalonia.Controls.Control control
                ? IsActive(control)
                : ReadBooleanProperty(item, "IsActive");
        }

        private static bool PaneNameMatches(ShellPaneCandidate candidate, string paneName)
        {
            var normalizedPaneName = NormalizeLookupText(paneName);
            return candidate.Names.Any(name =>
                string.Equals(NormalizeLookupText(name), normalizedPaneName, StringComparison.OrdinalIgnoreCase));
        }

        private static void SetActive(object? target, bool isActive)
        {
            var property = target?.GetType().GetProperty(
                "IsActive",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (property?.CanWrite == true && property.PropertyType == typeof(bool))
            {
                property.SetValue(target, isActive);
                return;
            }

            if (target is global::Avalonia.Controls.Control { DataContext: { } dataContext }
                && !ReferenceEquals(dataContext, target))
            {
                SetActive(dataContext, isActive);
            }
        }

        private static bool ReadBooleanProperty(object? target, string propertyName)
        {
            var property = target?.GetType().GetProperty(
                propertyName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            return property?.CanRead == true
                   && property.PropertyType == typeof(bool)
                   && property.GetValue(target) is true;
        }

        private static object? ReadPropertyValue(object? target, string propertyName)
        {
            var property = target?.GetType().GetProperty(
                propertyName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            return property?.CanRead == true
                ? property.GetValue(target)
                : null;
        }

        private static string? ReadStringProperty(object? target, string propertyName)
        {
            var property = target?.GetType().GetProperty(
                propertyName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            return property?.CanRead == true
                ? property.GetValue(target)?.ToString()
                : null;
        }

        private static string NormalizeLookupText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        private sealed record ShellPaneCandidate(
            object Target,
            IReadOnlyList<string> Names,
            bool IsActive)
        {
            public string? PrimaryName => Names.FirstOrDefault();
        }
    }

    private sealed class HeadlessTextBoxControl : HeadlessControlBase<TextBox>, ITextBoxControl
    {
        public HeadlessTextBoxControl(TextBox inner) : base(inner)
        {
        }

        public string Text
        {
            get => Inner.Text ?? string.Empty;
            set => Inner.Text = value;
        }

        public void Enter(string value)
        {
            Inner.Enter(value);
        }
    }

    private sealed class HeadlessButtonControl : HeadlessControlBase<Button>, IButtonControl, IReadableTextControl
    {
        public HeadlessButtonControl(Button inner) : base(inner)
        {
        }

        public string Text => Inner.Text;

        public void Invoke()
        {
            Inner.Invoke();
        }
    }

    private sealed class HeadlessLabelControl : HeadlessControlBase<Label>, ILabelControl
    {
        public HeadlessLabelControl(Label inner) : base(inner)
        {
        }

        public string Text => Inner.Text ?? Name;
    }

    private sealed class HeadlessListBoxControl : HeadlessControlBase<ListBox>, IExactSelectableListBoxControl
    {
        public HeadlessListBoxControl(ListBox inner) : base(inner)
        {
        }

        public IReadOnlyList<IListBoxItem> Items =>
            Inner.Items.Select(item => (IListBoxItem)new HeadlessListBoxItem(item)).ToArray();

        public string? SelectedItemText => Inner.SelectedItemText;

        public void SelectItem(string itemText)
        {
            Inner.SelectItem(itemText);
        }

        public void SelectItemExact(string itemText)
        {
            Inner.SelectItemExact(itemText);
        }
    }

    private sealed class HeadlessListBoxItem : IListBoxItem
    {
        private readonly ListBoxItem _inner;

        public HeadlessListBoxItem(ListBoxItem inner)
        {
            _inner = inner;
        }

        public string? Text => _inner.Text;

        public string? Name => _inner.Name;
    }

    private sealed class HeadlessCheckBoxControl : HeadlessControlBase<CheckBox>, ICheckBoxControl, IReadableTextControl
    {
        public HeadlessCheckBoxControl(CheckBox inner) : base(inner)
        {
        }

        public string Text => Inner.Text;

        public bool? IsChecked
        {
            get => Inner.IsChecked;
            set => Inner.IsChecked = value;
        }
    }

    private sealed class HeadlessMultiSelectItemsControl : HeadlessControlBase<AutomationElement>, IMultiSelectItemsControl
    {
        public HeadlessMultiSelectItemsControl(AutomationElement inner) : base(inner)
        {
        }

        public IReadOnlyList<string> Items => ReadItems()
            .ToArray();

        public IReadOnlyList<string> SelectedItems =>
            AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                ReadSelectedItemsCore());

        public void SetSelectedItems(IReadOnlyCollection<string> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            var normalizedValues = NormalizeRequestedItems(values);

            AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
            {
                var checkBoxes = ReadCheckBoxesCore();
                if (checkBoxes.Length > 0)
                {
                    ValidateAvailableItems(checkBoxes.Select(static item => item.Text), normalizedValues);
                    foreach (var item in checkBoxes)
                    {
                        item.Control.IsChecked = normalizedValues.Contains(item.Text);
                    }
                }
                else if (FindListBoxCore() is { } listBox)
                {
                    SetListBoxSelectedItems(listBox, normalizedValues);
                }
                else if (FindComboBoxCore() is { } comboBox)
                {
                    SetComboBoxSelectedItem(comboBox, normalizedValues);
                }
                else
                {
                    throw new InvalidOperationException(
                        "Multi-select items container does not expose checkbox, ListBox, or ComboBox items.");
                }

                Inner.Control.Dispatcher.RunJobs();
                return true;
            });
        }

        private string[] ReadItems()
        {
            return AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(ReadItemsCore);
        }

        private string[] ReadItemsCore()
        {
            var checkBoxes = ReadCheckBoxesCore();
            if (checkBoxes.Length > 0)
            {
                return checkBoxes.Select(static item => item.Text).ToArray();
            }

            if (FindListBoxCore() is { } listBox)
            {
                return listBox.Items.Cast<object?>().Select(ReadItemText).ToArray();
            }

            if (FindComboBoxCore() is { } comboBox)
            {
                return comboBox.Items.Cast<object?>().Select(ReadItemText).ToArray();
            }

            return [];
        }

        private string[] ReadSelectedItemsCore()
        {
            var checkBoxes = ReadCheckBoxesCore();
            if (checkBoxes.Length > 0)
            {
                return checkBoxes
                    .Where(static item => item.Control.IsChecked == true)
                    .Select(static item => item.Text)
                    .ToArray();
            }

            if (FindListBoxCore() is { } listBox)
            {
                if (listBox.SelectionMode == global::Avalonia.Controls.SelectionMode.Single)
                {
                    return listBox.SelectedItem is null ? [] : [ReadItemText(listBox.SelectedItem)];
                }

                return listBox.SelectedItems?.Cast<object?>().Select(ReadItemText).ToArray() ?? [];
            }

            if (FindComboBoxCore() is { } comboBox)
            {
                return comboBox.SelectedItem is null ? [] : [ReadItemText(comboBox.SelectedItem)];
            }

            return [];
        }

        private MultiSelectCheckBox[] ReadCheckBoxesCore()
        {
            return ControlTree.EnumerateDescendants(Inner.Control)
                .OfType<global::Avalonia.Controls.CheckBox>()
                .Select(static checkBox => new MultiSelectCheckBox(ReadItemText(checkBox), checkBox))
                .Where(static item => !string.IsNullOrWhiteSpace(item.Text))
                .ToArray();
        }

        private global::Avalonia.Controls.ListBox? FindListBoxCore()
        {
            return Inner.Control as global::Avalonia.Controls.ListBox
                ?? ControlTree.EnumerateDescendants(Inner.Control)
                    .OfType<global::Avalonia.Controls.ListBox>()
                    .FirstOrDefault();
        }

        private global::Avalonia.Controls.ComboBox? FindComboBoxCore()
        {
            return Inner.Control as global::Avalonia.Controls.ComboBox
                ?? ControlTree.EnumerateDescendants(Inner.Control)
                    .OfType<global::Avalonia.Controls.ComboBox>()
                    .FirstOrDefault();
        }

        private static void SetListBoxSelectedItems(
            global::Avalonia.Controls.ListBox listBox,
            HashSet<string> requestedValues)
        {
            var items = listBox.Items.Cast<object?>().ToArray();
            var available = items.Select(ReadItemText).ToArray();
            ValidateAvailableItems(available, requestedValues);

            if (listBox.SelectionMode == global::Avalonia.Controls.SelectionMode.Single)
            {
                if (requestedValues.Count > 1)
                {
                    throw new InvalidOperationException("The physical ListBox allows at most one selected item.");
                }

                listBox.SelectedItem = requestedValues.Count == 0
                    ? null
                    : items.Single(item => requestedValues.Contains(ReadItemText(item)));
                return;
            }

            var selectedItems = listBox.SelectedItems
                ?? throw new InvalidOperationException("The physical ListBox does not expose a selected-items collection.");
            selectedItems.Clear();
            foreach (var item in items.Where(item => requestedValues.Contains(ReadItemText(item))))
            {
                selectedItems.Add(item);
            }
        }

        private static void SetComboBoxSelectedItem(
            global::Avalonia.Controls.ComboBox comboBox,
            HashSet<string> requestedValues)
        {
            if (requestedValues.Count > 1)
            {
                throw new InvalidOperationException("The physical ComboBox allows at most one selected item.");
            }

            var items = comboBox.Items.Cast<object?>().ToArray();
            ValidateAvailableItems(items.Select(ReadItemText), requestedValues);
            comboBox.SelectedItem = requestedValues.Count == 0
                ? null
                : items.Single(item => requestedValues.Contains(ReadItemText(item)));
        }

        private static string ReadItemText(object? item)
        {
            return item?.ToString()?.Trim() ?? string.Empty;
        }

        private static string ReadItemText(global::Avalonia.Controls.CheckBox checkBox)
        {
            return (AutomationProperties.GetName(checkBox)
                    ?? checkBox.Content?.ToString()
                    ?? checkBox.Name
                    ?? AutomationProperties.GetAutomationId(checkBox)
                    ?? string.Empty)
                .Trim();
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

        private sealed record MultiSelectCheckBox(string Text, global::Avalonia.Controls.CheckBox Control);
    }

    private sealed class HeadlessSearchHistoryItemsControl : ISearchHistoryItemsControl
    {
        private readonly Func<IReadOnlyList<Button>> _resolveButtons;
        private readonly string _locator;

        public HeadlessSearchHistoryItemsControl(
            Func<IReadOnlyList<Button>> resolveButtons,
            string locator)
        {
            _resolveButtons = resolveButtons;
            _locator = locator;
        }

        public string AutomationId => _locator;

        public string Name => _locator;

        public bool IsEnabled => _resolveButtons().Any(static button => button.IsEnabled);

        public bool IsAvailable => _resolveButtons().Any(static button => button.IsAvailable);

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

            matches[0].Invoke();
        }

        private static string ReadText(Button button)
        {
            return !string.IsNullOrWhiteSpace(button.Name)
                ? button.Name
                : button.Text;
        }
    }

    private sealed class HeadlessComboBoxControl : HeadlessControlBase<ComboBox>, IComboBoxControl, IReadableTextControl
    {
        public HeadlessComboBoxControl(ComboBox inner) : base(inner)
        {
        }

        public IReadOnlyList<IComboBoxItem> Items =>
            Inner.Items.Select(item => (IComboBoxItem)new HeadlessComboBoxItem(item)).ToArray();

        public IComboBoxItem? SelectedItem => Inner.SelectedItem switch
        {
            ComboBoxItem comboBoxItem => new HeadlessComboBoxItem(comboBoxItem),
            null => null,
            _ => new HeadlessComboBoxTextItem(Inner.SelectedItem?.ToString() ?? string.Empty, Inner.SelectedItem?.ToString() ?? string.Empty)
        };

        public string Text => SelectedItem?.Text ?? string.Empty;

        public int SelectedIndex
        {
            get => Inner.SelectedIndex;
            set => Inner.SelectedIndex = value;
        }

        public void SelectByIndex(int index)
        {
            Inner.Select(index);
        }

        public void Expand()
        {
            Inner.Expand();
        }
    }

    private sealed class HeadlessComboBoxItem : IComboBoxItem
    {
        private readonly ComboBoxItem _inner;

        public HeadlessComboBoxItem(ComboBoxItem inner)
        {
            _inner = inner;
        }

        public string Text => _inner.Text ?? string.Empty;

        public string Name => _inner.Name ?? Text;
    }

    private sealed record HeadlessComboBoxTextItem(string Text, string Name) : IComboBoxItem;

    private sealed class HeadlessRadioButtonControl : HeadlessControlBase<RadioButton>, IRadioButtonControl, IReadableTextControl
    {
        public HeadlessRadioButtonControl(RadioButton inner) : base(inner)
        {
        }

        public string Text => Inner.Text;

        public bool? IsChecked
        {
            get => Inner.IsChecked;
            set => Inner.IsChecked = value;
        }
    }

    private sealed class HeadlessToggleButtonControl : HeadlessControlBase<ToggleButton>, IToggleButtonControl, IReadableTextControl
    {
        public HeadlessToggleButtonControl(ToggleButton inner) : base(inner)
        {
        }

        public string Text => Inner.Text;

        public bool IsToggled => Inner.IsToggled;

        public void Toggle()
        {
            Inner.Toggle();
        }
    }

    private sealed class HeadlessSliderControl : HeadlessControlBase<Slider>, ISliderControl
    {
        public HeadlessSliderControl(Slider inner) : base(inner)
        {
        }

        public double Value
        {
            get => Inner.Value;
            set => Inner.Value = value;
        }
    }

    private sealed class HeadlessProgressBarControl : HeadlessControlBase<ProgressBar>, IProgressBarControl
    {
        public HeadlessProgressBarControl(ProgressBar inner) : base(inner)
        {
        }

        public double Value => Inner.Value;
    }

    private sealed class HeadlessCalendarControl : HeadlessControlBase<Calendar>, ICalendarControl
    {
        public HeadlessCalendarControl(Calendar inner) : base(inner)
        {
        }

        public IReadOnlyList<DateTime> SelectedDates => Inner.SelectedDates ?? Array.Empty<DateTime>();

        public void SelectDate(DateTime selectedDate)
        {
            Inner.SelectDate(selectedDate);
        }
    }

    private sealed class HeadlessDateTimePickerControl : HeadlessControlBase<DateTimePicker>, IDateTimePickerControl
    {
        public HeadlessDateTimePickerControl(DateTimePicker inner) : base(inner)
        {
        }

        public DateTime? SelectedDate
        {
            get => Inner.SelectedDate;
            set => Inner.SelectedDate = value;
        }
    }

    private sealed class HeadlessTimePickerControl : HeadlessControlBase<TimePicker>, ITimePickerControl
    {
        public HeadlessTimePickerControl(TimePicker inner) : base(inner)
        {
        }

        public TimeSpan? SelectedTime
        {
            get => Inner.SelectedTime;
            set => Inner.SelectedTime = value;
        }
    }

    private sealed class HeadlessExpanderControl : HeadlessControlBase<Expander>, IExpanderControl
    {
        public HeadlessExpanderControl(Expander inner) : base(inner)
        {
        }

        public bool IsExpanded => Inner.IsExpanded;

        public void Expand() => Inner.Expand();

        public void Collapse() => Inner.Collapse();
    }

    private sealed class HeadlessSpinnerControl : HeadlessControlBase<Spinner>, ISpinnerControl
    {
        public HeadlessSpinnerControl(Spinner inner) : base(inner)
        {
        }

        public double Value
        {
            get => Inner.Value;
            set => Inner.Value = value;
        }
    }

    private sealed class HeadlessTabControl : HeadlessControlBase<Tab>, ITabControl
    {
        public HeadlessTabControl(Tab inner) : base(inner)
        {
        }

        public IReadOnlyList<ITabItemControl> Items =>
            Inner.Items.Select(item => (ITabItemControl)new HeadlessTabItemControl(item)).ToArray();

        public void SelectTabItem(string itemText)
        {
            Inner.SelectTabItem(itemText);
        }
    }

    private sealed class HeadlessTabItemControl : HeadlessControlBase<TabItem>, ITabItemControl, IReadableTextControl
    {
        public HeadlessTabItemControl(TabItem inner) : base(inner)
        {
        }

        public string Text => Inner.Text;

        public bool IsSelected => Inner.IsSelected;

        public void SelectTab()
        {
            Inner.Select();
        }
    }

    private sealed class HeadlessTreeControl : HeadlessControlBase<Tree>, ITreeControl
    {
        public HeadlessTreeControl(Tree inner) : base(inner)
        {
        }

        public IReadOnlyList<ITreeItemControl> Items =>
            Inner.Items.Select(item => (ITreeItemControl)new HeadlessTreeItemControl(item)).ToArray();

        public ITreeItemControl? SelectedTreeItem => Inner.SelectedTreeItem is null
            ? null
            : new HeadlessTreeItemControl(Inner.SelectedTreeItem);
    }

    private sealed class HeadlessTreeItemControl : HeadlessControlBase<TreeItem>, ITreeItemControl, IReadableTextControl
    {
        public HeadlessTreeItemControl(TreeItem inner) : base(inner)
        {
        }

        public bool IsSelected
        {
            get => Inner.IsSelected;
            set => Inner.IsSelected = value;
        }

        public string Text => Inner.Text ?? Name;

        public IReadOnlyList<ITreeItemControl> Items =>
            Inner.Items.Select(item => (ITreeItemControl)new HeadlessTreeItemControl(item)).ToArray();

        public void Expand()
        {
            Inner.Expand();
        }

        public void SelectNode()
        {
            Inner.Select();
        }
    }

    private sealed class HeadlessGridControl : HeadlessControlBase<Grid>, IGridControl
    {
        public HeadlessGridControl(Grid inner) : base(inner)
        {
        }

        public IReadOnlyList<IGridRowControl> Rows =>
            Inner.Rows.Select(row => (IGridRowControl)new HeadlessGridRowControl(row)).ToArray();

        public IGridRowControl? GetRowByIndex(int index)
        {
            var row = Inner.GetRowByIndex(index);
            return row is null ? null : new HeadlessGridRowControl(row);
        }
    }

    private sealed class HeadlessVisualGridControl : HeadlessControlBase<AutomationElement>, IEditableGridControl
    {
        public HeadlessVisualGridControl(AutomationElement inner) : base(inner)
        {
        }

        public IReadOnlyList<IGridRowControl> Rows => ReadRows();

        public IGridRowControl? GetRowByIndex(int index)
        {
            var rows = Rows;
            return index >= 0 && index < rows.Count
                ? rows[index]
                : null;
        }

        public void EditCell(GridCellEditRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentOutOfRangeException.ThrowIfNegative(request.RowIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(request.ColumnIndex);
            ArgumentNullException.ThrowIfNull(request.Value);

            var cell = FindVisualCell(request.RowIndex, request.ColumnIndex)
                ?? throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] was not found in grid '{AutomationId}'.");

            if (request.CommitMode == GridCellEditCommitMode.Cancel)
            {
                return;
            }

            if (!TryWriteCellValue(cell, request))
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose a writable '{request.EditorKind}' editor.");
            }
        }

        private AutomationElement? FindVisualCell(int rowIndex, int columnIndex)
        {
            if (string.IsNullOrWhiteSpace(AutomationId))
            {
                return null;
            }

            var expectedAutomationId = $"{AutomationId}_Row{rowIndex}_Cell{columnIndex}";
            return Inner.FindAllDescendants()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.AutomationId,
                        expectedAutomationId,
                        StringComparison.Ordinal));
        }

        private bool TryWriteCellValue(AutomationElement cell, GridCellEditRequest request)
        {
            return AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
            {
                var controls = ReadCellControls(cell.Control);

                if (request.EditorKind == GridCellEditorKind.SearchPicker)
                {
                    controls = MaterializeSearchPickerControls(cell.Control, controls);
                    return TryWriteSearchPicker(controls, request);
                }

                foreach (var candidate in controls)
                {
                    if (TryWriteTypedEditor(candidate, request))
                    {
                        return true;
                    }
                }

                foreach (var candidate in controls)
                {
                    if (TryWriteTextLikeControl(candidate, request.Value))
                    {
                        return true;
                    }
                }

                return false;
            });
        }

        private static List<global::Avalonia.Controls.Control> ReadCellControls(
            global::Avalonia.Controls.Control cell)
        {
            return new[] { cell }
                .Concat(ControlTree.EnumerateDescendants(cell))
                .ToList();
        }

        private static List<global::Avalonia.Controls.Control> MaterializeSearchPickerControls(
            global::Avalonia.Controls.Control cell,
            List<global::Avalonia.Controls.Control> controls)
        {
            if (controls.OfType<global::Avalonia.Controls.TextBox>().Any()
                && controls.OfType<global::Avalonia.Controls.ListBox>().Any())
            {
                return controls;
            }

            foreach (var editor in controls
                         .OfType<global::Avalonia.Controls.Primitives.TemplatedControl>()
                         .ToArray())
            {
                editor.ApplyTemplate();
                controls = ReadCellControls(cell);
                if (controls.OfType<global::Avalonia.Controls.TextBox>().Any()
                    && controls.OfType<global::Avalonia.Controls.ListBox>().Any())
                {
                    break;
                }
            }

            return controls;
        }

        private static bool TryWriteSearchPicker(
            IReadOnlyList<global::Avalonia.Controls.Control> controls,
            GridCellEditRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SearchText))
            {
                return false;
            }

            var searchInput = controls.OfType<global::Avalonia.Controls.TextBox>().FirstOrDefault();
            var results = controls.OfType<global::Avalonia.Controls.ListBox>().FirstOrDefault();
            if (searchInput is null || results is null)
            {
                return false;
            }

            searchInput.Text = request.SearchText;
            return TrySelectListBoxItem(results, request.Value);
        }

        private static bool TryWriteTypedEditor(global::Avalonia.Controls.Control control, GridCellEditRequest request)
        {
            switch (control)
            {
                case global::Avalonia.Controls.DatePicker datePicker
                    when request.EditorKind == GridCellEditorKind.Date:
                    datePicker.SelectedDate = ParseDate(request.Value);
                    return true;
                case global::Avalonia.Controls.TimePicker timePicker
                    when request.EditorKind == GridCellEditorKind.Time:
                    timePicker.SelectedTime = ParseTime(request.Value);
                    return true;
                case global::Avalonia.Controls.ComboBox comboBox
                    when request.EditorKind == GridCellEditorKind.ComboBox:
                    return TrySelectComboBoxItem(comboBox, request.Value);
                case global::Avalonia.Controls.TextBox textBox
                    when request.EditorKind is GridCellEditorKind.Text
                        or GridCellEditorKind.Number
                        or GridCellEditorKind.Date
                        or GridCellEditorKind.Time
                        or GridCellEditorKind.ComboBox:
                    textBox.Text = request.Value;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryWriteTextLikeControl(global::Avalonia.Controls.Control control, string value)
        {
            switch (control)
            {
                case global::Avalonia.Controls.TextBlock textBlock:
                    textBlock.Text = value;
                    return true;
                case global::Avalonia.Controls.Label label:
                    label.Content = value;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TrySelectComboBoxItem(global::Avalonia.Controls.ComboBox comboBox, string itemText)
        {
            var items = comboBox.Items?.Cast<object?>().ToArray() ?? Array.Empty<object?>();
            var normalizedTarget = NormalizeLookupText(itemText);
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index];
                if (!string.Equals(NormalizeLookupText(ReadComboBoxItemText(item)), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                comboBox.SelectedIndex = index;
                comboBox.SelectedItem = item;
                return true;
            }

            return false;
        }

        private static bool TrySelectListBoxItem(global::Avalonia.Controls.ListBox listBox, string itemText)
        {
            var items = listBox.Items?.Cast<object?>().ToArray() ?? Array.Empty<object?>();
            var normalizedTarget = NormalizeLookupText(itemText);
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index];
                if (!string.Equals(NormalizeLookupText(ReadComboBoxItemText(item)), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                listBox.SelectedIndex = index;
                listBox.SelectedItem = item;
                return true;
            }

            return false;
        }

        private static string? ReadComboBoxItemText(object? item)
        {
            return item switch
            {
                null => null,
                global::Avalonia.Controls.ComboBoxItem comboBoxItem => comboBoxItem.Content?.ToString(),
                global::Avalonia.Controls.ContentControl contentControl => contentControl.Content?.ToString(),
                _ => item.ToString()
            };
        }

        private static DateTimeOffset? ParseDate(string value)
        {
            if (DateTimeOffset.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var exactDate))
            {
                return exactDate.Date;
            }

            if (DateTimeOffset.TryParse(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var invariantDate))
            {
                return invariantDate.Date;
            }

            if (DateTimeOffset.TryParse(
                    value,
                    System.Globalization.CultureInfo.CurrentCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var currentCultureDate))
            {
                return currentCultureDate.Date;
            }

            throw new InvalidOperationException($"Grid cell date value '{value}' could not be parsed.");
        }

        private static TimeSpan ParseTime(string value)
        {
            if (TimeSpan.TryParseExact(value, "c", System.Globalization.CultureInfo.InvariantCulture, out var time)
                && time >= TimeSpan.Zero
                && time < TimeSpan.FromDays(1))
            {
                return time;
            }

            throw new InvalidOperationException($"Grid time value '{value}' is not a valid invariant time of day.");
        }

        private static string NormalizeLookupText(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }

        private IReadOnlyList<IGridRowControl> ReadRows()
        {
            if (string.IsNullOrWhiteSpace(AutomationId))
            {
                return Array.Empty<IGridRowControl>();
            }

            var visualRows = ReadVisualRows()
                .Select(row => (IGridRowControl)new HeadlessVisualGridRowControl(row))
                .ToArray();
            if (visualRows.Length > 0)
            {
                return visualRows;
            }

            return ReadDataRows()
                .Select(values => (IGridRowControl)new HeadlessVisualGridDataRowControl(values))
                .ToArray();
        }

        private AutomationElement[] ReadVisualRows()
        {
            var rowPrefix = $"{AutomationId}_Row";
            return Inner.FindAllDescendants()
                .Where(candidate => IsVisualGridRow(candidate, rowPrefix))
                .OrderBy(candidate => ParseVisualGridIndex(candidate.AutomationId, "_Row"))
                .ToArray();
        }

        private IReadOnlyList<IReadOnlyList<string>> ReadDataRows()
        {
            return AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
            {
                if (Inner.Control is not global::Avalonia.Controls.ItemsControl itemsControl
                    || itemsControl.ItemsSource is not IEnumerable source)
                {
                    return Array.Empty<IReadOnlyList<string>>();
                }

                return source
                    .Cast<object?>()
                    .Select(ReadDisplayValues)
                    .Where(static values => values.Count > 0)
                    .ToArray();
            });
        }
    }

    private sealed class HeadlessVisualGridRowControl : IGridRowControl
    {
        private readonly AutomationElement _inner;

        public HeadlessVisualGridRowControl(AutomationElement inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<IGridCellControl> Cells =>
            ReadCells().Select(cell => (IGridCellControl)new HeadlessVisualGridCellControl(cell)).ToArray();

        private AutomationElement[] ReadCells()
        {
            if (string.IsNullOrWhiteSpace(_inner.AutomationId))
            {
                return Array.Empty<AutomationElement>();
            }

            var cellPrefix = $"{_inner.AutomationId}_Cell";
            return _inner.FindAllDescendants()
                .Where(candidate => IsVisualGridCell(candidate, cellPrefix))
                .OrderBy(candidate => ParseVisualGridIndex(candidate.AutomationId, "_Cell"))
                .ToArray();
        }
    }

    private sealed class HeadlessVisualGridCellControl : IGridCellControl
    {
        private readonly AutomationElement _inner;

        public HeadlessVisualGridCellControl(AutomationElement inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string Value => ReadVisualGridCellText(_inner) ?? string.Empty;
    }

    private sealed class HeadlessVisualGridDataRowControl : IGridRowControl
    {
        private readonly IReadOnlyList<string> _values;

        public HeadlessVisualGridDataRowControl(IReadOnlyList<string> values)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public IReadOnlyList<IGridCellControl> Cells =>
            _values.Select(value => (IGridCellControl)new HeadlessVisualGridDataCellControl(value)).ToArray();
    }

    private sealed record HeadlessVisualGridDataCellControl(string Value) : IGridCellControl;

    private sealed class HeadlessGridRowControl : IGridRowControl
    {
        private readonly GridRow _inner;

        public HeadlessGridRowControl(GridRow inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<IGridCellControl> Cells =>
            _inner.Cells.Select(cell => (IGridCellControl)new HeadlessGridCellControl(cell)).ToArray();
    }

    private sealed class HeadlessGridCellControl : IGridCellControl
    {
        private readonly GridCell _inner;

        public HeadlessGridCellControl(GridCell inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string Value => _inner.Value ?? string.Empty;
    }
}
