using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Automation.GridAutomation;
using AppAutomation.Avalonia.Headless.Internal.AutomationModel;
using Avalonia.Automation;
using Avalonia.VisualTree;

namespace AppAutomation.Avalonia.Headless.Automation;

public sealed partial class HeadlessControlResolver
{
    private sealed class HeadlessVisualGridControl :
        HeadlessControlBase<AutomationElement>,
        IEditableGridControl,
        IIndexedAddressableGridControl,
        IAddressableGridControl,
        IGridColumnMetadataControl
    {
        private readonly AutomationElement _searchRoot;

        public HeadlessVisualGridControl(AutomationElement searchRoot, AutomationElement inner) : base(inner)
        {
            _searchRoot = searchRoot ?? throw new ArgumentNullException(nameof(searchRoot));
        }

        public IReadOnlyList<IGridRowControl> Rows => ReadRows();

        public IReadOnlyList<string> ColumnNames =>
            AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                HeadlessGridRuntimeAccess.ReadColumnNames(Inner.Control));

        public IGridRowControl? GetRowByIndex(int index)
        {
            var rows = Rows;
            return index >= 0 && index < rows.Count
                ? rows[index]
                : null;
        }

        public bool TryGetColumnIndex(string columnName, out int columnIndex)
        {
            if (string.IsNullOrWhiteSpace(columnName))
            {
                columnIndex = -1;
                return false;
            }

            var matches = ColumnNames
                .Select(static (name, index) => (Name: name, Index: index))
                .Where(candidate => string.Equals(
                    candidate.Name,
                    columnName.Trim(),
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            columnIndex = matches.Length == 1 ? matches[0].Index : -1;
            return matches.Length == 1;
        }

        public GridRowResolution ResolveRow(GridRowSelector row, int timeoutMs) =>
            ResolveRow(MapRow(row), timeoutMs);

        public GridCellValueSnapshot ReadCell(GridCellAddress address, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(address);
            return ReadCell(MapRow(address.Row), ResolveRuntimeColumn(address.ColumnName), timeoutMs);
        }

        public string CopyCell(GridCellAddress address, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(address);
            return CopyCell(MapRow(address.Row), ResolveRuntimeColumn(address.ColumnName), timeoutMs);
        }

        public void EditCell(
            GridCellAddress address,
            GridCellValueEditRequest request,
            int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(address);
            EditCell(MapRow(address.Row), ResolveRuntimeColumn(address.ColumnName), request, timeoutMs);
        }

        public void OpenRow(GridRowSelector row, int timeoutMs) =>
            OpenRow(MapRow(row), timeoutMs);

        public GridRowResolution ResolveRow(GridIndexedRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);

            var matches = FindMatchingRows(row, out var rowCount);
            var description = DescribeResolution(row, matches.Count, rowCount);
            return matches.Count switch
            {
                0 => GridRowResolution.NotFound(description),
                1 => GridRowResolution.Unique(description),
                _ => GridRowResolution.Ambiguous(matches.Count, description)
            };
        }

        public GridCellValueSnapshot ReadCell(
            GridIndexedRowSelector row,
            GridRuntimeColumn column,
            int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(column);
            var match = ResolveUniqueRow(row, timeoutMs);
            return ReadCellSnapshot(match, column);
        }

        public string CopyCell(
            GridIndexedRowSelector row,
            GridRuntimeColumn column,
            int timeoutMs)
        {
            return ReadCell(row, column, timeoutMs).DisplayText ?? string.Empty;
        }

        public void EditCell(
            GridIndexedRowSelector row,
            GridRuntimeColumn column,
            GridCellValueEditRequest request,
            int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(column);
            ArgumentNullException.ThrowIfNull(request);
            var operation = System.Diagnostics.Stopwatch.StartNew();
            var match = ResolveUniqueRow(row, timeoutMs);
            if (match.Item is not null)
            {
                _ = AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                    HeadlessGridRuntimeAccess.ScrollIntoView(
                        Inner.Control,
                        match.Item,
                        match.RowIndex,
                        column));
            }

            var activation = match.Item is null
                ? new HeadlessGridEditorActivation(false, null, "The resolved row has no source item.")
                : AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                    HeadlessGridRuntimeAccess.ActivateEditor(
                        Inner.Control,
                        match.Item,
                        match.RowIndex,
                        column));
            var editRequest = new GridCellEditRequest(
                match.RowIndex,
                column.RuntimeColumnIndex ?? column.ColumnIndex,
                request.Value,
                request.EditorKind,
                request.CommitMode,
                request.SearchText)
            {
                TimeoutMs = timeoutMs,
                EditorParts = request.EditorParts ?? column.EditorParts
            };
            var transactionCompleted = false;
            try
            {
                var activeEditor = activation.ActiveEditor;
                AutomationElement? cell = null;
                if (activeEditor is null)
                {
                    activeEditor = WaitForActivatedEditor(activation);
                }

                if (activeEditor is null)
                {
                    var remaining = timeoutMs - (int)operation.ElapsedMilliseconds;
                    cell = remaining > 0
                        ? FindCatalogVisualCell(match, column, remaining)
                        : null;
                }

                var editorRoot = activeEditor ?? cell?.Control;
                if (editorRoot is null)
                {
                    throw new InvalidOperationException(
                        $"Visual grid '{AutomationId}' did not expose a writable active editor or materialize a visible cell "
                        + $"for source field '{column.SourceFieldName}' and row "
                        + $"'{match.Item?.GetType().FullName ?? match.RowIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}'. "
                        + $"Editor activation details: {activation.FailureReason ?? "<none>"}.");
                }

                if (!TryWriteCellValue(editorRoot, editRequest, activeEditor))
                {
                    var observedControls = AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                        ReadCellControls(editorRoot)
                            .Select(static control => control.GetType().FullName ?? control.GetType().Name)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray());
                    throw new InvalidOperationException(
                        $"Visual grid cell for source field '{column.SourceFieldName}' in grid '{AutomationId}' "
                        + $"does not expose a writable '{request.EditorKind}' editor. "
                        + $"Editor activation started: {activation.Started}; "
                        + $"activation details: {activation.FailureReason ?? "<none>"}; "
                        + $"active editor: {activeEditor?.GetType().FullName ?? "<none>"}; "
                        + $"cell controls: {string.Join(", ", observedControls)}.");
                }

                var invoked = InvokeEditorButton(
                    editorRoot,
                    request.CommitMode == GridCellEditCommitMode.Cancel
                        ? editRequest.EditorParts?.CancelButton
                        : editRequest.EditorParts?.ConfirmButton);
                if (!invoked)
                {
                    var completed = AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                        HeadlessGridRuntimeAccess.FinishEditing(Inner.Control, request.CommitMode));
                    if (!completed
                        && (activeEditor is not null
                            || activation.Started
                            || request.CommitMode == GridCellEditCommitMode.Cancel))
                    {
                        throw new NotSupportedException(
                            $"Visual grid '{AutomationId}' does not expose a provider-neutral "
                            + $"'{request.CommitMode}' editor completion action.");
                    }

                    transactionCompleted = completed;
                }
                else
                {
                    transactionCompleted = true;
                }
            }
            finally
            {
                if (activation.Started && !transactionCompleted)
                {
                    _ = AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                        HeadlessGridRuntimeAccess.FinishEditing(
                            Inner.Control,
                            GridCellEditCommitMode.Cancel));
                }
            }

            global::Avalonia.Controls.Control? WaitForActivatedEditor(
                HeadlessGridEditorActivation candidate)
            {
                var editor = candidate.ActiveEditor;
                if (!candidate.InvocationAttempted || editor is not null)
                {
                    return editor;
                }

                var waitDeadline = Math.Min(
                    timeoutMs,
                    operation.ElapsedMilliseconds + 500);
                while (editor is null && operation.ElapsedMilliseconds < waitDeadline)
                {
                    var remaining = timeoutMs - (int)operation.ElapsedMilliseconds;
                    if (remaining <= 0)
                    {
                        break;
                    }

                    Thread.Sleep(Math.Min(20, remaining));
                    editor = AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                        HeadlessGridRuntimeAccess.ReadActiveEditor(Inner.Control));
                }

                return editor;
            }
        }

        public void OpenRow(GridIndexedRowSelector row, int timeoutMs)
        {
            var match = ResolveUniqueRow(row, timeoutMs);
            EnsureVisualRow(match);
            if (match.Item is not null
                && AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                    HeadlessGridRuntimeAccess.SelectRow(Inner.Control, match.Item)))
            {
                return;
            }

            throw new NotSupportedException(
                $"Visual grid '{AutomationId}' does not expose a provider-neutral row activation action in Headless runtime.");
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

            // Preserve the legacy index-based contract: before declarative editor parts existed,
            // Cancel was a no-op and therefore could not mutate the underlying value. Catalog-backed
            // editors that declare a real cancel part still exercise the full edit/cancel lifecycle.
            if (request.CommitMode == GridCellEditCommitMode.Cancel
                && request.EditorParts?.CancelButton is null)
            {
                return;
            }

            if (!TryWriteCellValue(cell.Control, request))
            {
                throw new InvalidOperationException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' does not expose a writable '{request.EditorKind}' editor.");
            }

            var invoked = InvokeEditorButton(
                cell.Control,
                request.CommitMode == GridCellEditCommitMode.Cancel
                    ? request.EditorParts?.CancelButton
                    : request.EditorParts?.ConfirmButton);
            if (request.CommitMode == GridCellEditCommitMode.Cancel && !invoked)
            {
                throw new NotSupportedException(
                    $"Visual grid cell [{request.RowIndex},{request.ColumnIndex}] in grid '{AutomationId}' requires a configured cancel button for Headless replay.");
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

        private bool TryWriteCellValue(
            global::Avalonia.Controls.Control cell,
            GridCellEditRequest request,
            global::Avalonia.Controls.Control? activeEditor = null)
        {
            if (request.EditorKind == GridCellEditorKind.SearchPicker)
            {
                return TryWriteSearchPicker(cell, request, activeEditor);
            }

            return AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
            {
                var controls = ReadCellControls(cell);
                if (activeEditor is not null && !controls.Contains(activeEditor))
                {
                    controls.Insert(0, activeEditor);
                    foreach (var descendant in ControlTree.EnumerateDescendants(activeEditor).Reverse())
                    {
                        if (!controls.Contains(descendant))
                        {
                            controls.Insert(1, descendant);
                        }
                    }
                }

                var configuredInput = ResolveEditorPart(
                    cell,
                    request.EditorParts?.Input,
                    activeEditor);
                var preserveTypedEditorLifecycle = request.EditorKind == GridCellEditorKind.Number
                    && request.EditorParts?.UseKeyboardInput == true
                    && activeEditor is not null;
                if (configuredInput is not null && !preserveTypedEditorLifecycle)
                {
                    controls.Remove(configuredInput);
                    controls.Insert(0, configuredInput);
                }

                foreach (var candidate in controls)
                {
                    if (TryWriteTypedEditor(candidate, request))
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

        private bool TryWriteSearchPicker(
            global::Avalonia.Controls.Control cell,
            GridCellEditRequest request,
            global::Avalonia.Controls.Control? activeEditor)
        {
            if (string.IsNullOrWhiteSpace(request.SearchText))
            {
                return false;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var searchInputControl = WaitForSearchPickerInput(
                cell,
                request,
                activeEditor,
                stopwatch);
            if (searchInputControl is null)
            {
                throw CreateSearchPickerPartException(cell, request, "input", "was not found");
            }
            if (searchInputControl is not global::Avalonia.Controls.TextBox searchInput)
            {
                throw CreateSearchPickerPartException(
                    cell,
                    request,
                    "input",
                    $"expected TextBox but found '{searchInputControl.GetType().FullName}'");
            }

            AutomationElement.WrapControl(searchInput).AsTextBox().Enter(request.SearchText);

            var openButton = request.EditorParts?.OpenButton;
            var remaining = RemainingMilliseconds(stopwatch, request.TimeoutMs);
            var initialWait = openButton is null
                ? remaining
                : Math.Max(1, remaining / 2);
            var selection = WaitForSearchPickerSelection(
                cell,
                request,
                activeEditor,
                stopwatch,
                initialWait);
            if (selection.Selected)
            {
                return true;
            }

            if (!selection.ResultsObserved && openButton is not null)
            {
                _ = RemainingMilliseconds(stopwatch, request.TimeoutMs);
                if (!InvokeEditorButton(cell, openButton, request))
                {
                    throw CreateSearchPickerPartException(cell, request, "open button", "was not found");
                }
            }

            remaining = request.TimeoutMs - (int)stopwatch.ElapsedMilliseconds;
            if (remaining > 0 && openButton is not null)
            {
                var finalSelection = WaitForSearchPickerSelection(
                    cell,
                    request,
                    activeEditor,
                    stopwatch,
                    remaining);
                if (finalSelection.Selected)
                {
                    return true;
                }

                selection = (
                    false,
                    selection.ResultsObserved || finalSelection.ResultsObserved,
                    finalSelection.AvailableItems.Length > 0
                        ? finalSelection.AvailableItems
                        : selection.AvailableItems);
            }

            if (!selection.ResultsObserved)
            {
                throw CreateSearchPickerPartException(
                    cell,
                    request,
                    "results",
                    $"did not appear within {request.TimeoutMs}ms after entering search text");
            }

            var availableItems = selection.AvailableItems.Length == 0
                ? "<empty>"
                : string.Join(", ", selection.AvailableItems.Select(static item => $"'{item}'"));
            throw CreateSearchPickerPartException(
                cell,
                request,
                "results",
                $"did not expose exact item '{request.Value}' within {request.TimeoutMs}ms; available items: {availableItems}");
        }

        private global::Avalonia.Controls.Control? WaitForSearchPickerInput(
            global::Avalonia.Controls.Control cell,
            GridCellEditRequest request,
            global::Avalonia.Controls.Control? activeEditor,
            System.Diagnostics.Stopwatch stopwatch)
        {
            do
            {
                var input = AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                {
                    var controls = ReadSearchPickerControls(cell, activeEditor);
                    var configured = ResolveEditorPart(
                        cell,
                        request.EditorParts?.Input,
                        activeEditor);
                    return configured is null
                        ? RequireUniqueSearchPickerControl<global::Avalonia.Controls.TextBox>(
                            controls,
                            "input",
                            request.EditorParts?.Input)
                        : configured;
                });
                if (input is not null)
                {
                    return input;
                }

                var remaining = request.TimeoutMs - (int)stopwatch.ElapsedMilliseconds;
                if (remaining <= 0)
                {
                    return null;
                }

                Thread.Sleep(Math.Min(20, remaining));
            }
            while (true);
        }

        private List<global::Avalonia.Controls.Control> ReadSearchPickerControls(
            global::Avalonia.Controls.Control cell,
            global::Avalonia.Controls.Control? activeEditor)
        {
            var controls = ReadCellControls(cell);
            var currentEditor = activeEditor ?? HeadlessGridRuntimeAccess.ReadActiveEditor(Inner.Control);
            if (currentEditor is not null && !controls.Contains(currentEditor))
            {
                controls.Insert(0, currentEditor);
                foreach (var descendant in ControlTree.EnumerateDescendants(currentEditor).Reverse())
                {
                    if (!controls.Contains(descendant))
                    {
                        controls.Insert(1, descendant);
                    }
                }
            }

            return MaterializeSearchPickerControls(cell, controls);
        }

        private (bool Selected, bool ResultsObserved, string[] AvailableItems) WaitForSearchPickerSelection(
            global::Avalonia.Controls.Control cell,
            GridCellEditRequest request,
            global::Avalonia.Controls.Control? activeEditor,
            System.Diagnostics.Stopwatch stopwatch,
            int maximumWaitMilliseconds)
        {
            var deadline = Math.Min(
                request.TimeoutMs,
                stopwatch.ElapsedMilliseconds + maximumWaitMilliseconds);
            var resultsObserved = false;
            var availableItems = Array.Empty<string>();
            do
            {
                var resultsControl = AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                {
                    var controls = ReadSearchPickerControls(cell, activeEditor);
                    var configured = ResolveEditorPart(
                        cell,
                        request.EditorParts?.Results,
                        activeEditor);
                    return configured is null
                        ? RequireUniqueSearchPickerControl<global::Avalonia.Controls.ListBox>(
                            controls,
                            "results",
                            request.EditorParts?.Results)
                        : configured;
                });
                if (resultsControl is global::Avalonia.Controls.ListBox results)
                {
                    resultsObserved = true;
                    var list = AutomationElement.WrapControl(results).AsListBox();
                    availableItems = list.Items
                        .Select(static item => item.Text ?? string.Empty)
                        .ToArray();
                    var matches = availableItems.Count(item =>
                        string.Equals(item, request.Value, StringComparison.Ordinal));
                    if (matches > 1)
                    {
                        throw CreateSearchPickerPartException(
                            cell,
                            request,
                            "results",
                            $"contains {matches} items with exact caption '{request.Value}'");
                    }
                    if (matches == 1)
                    {
                        list.SelectItemExact(request.Value);
                        return (true, true, availableItems);
                    }
                }
                else if (resultsControl is not null)
                {
                    throw CreateSearchPickerPartException(
                        cell,
                        request,
                        "results",
                        $"expected ListBox but found '{resultsControl.GetType().FullName}'");
                }

                if (stopwatch.ElapsedMilliseconds >= deadline)
                {
                    return (false, resultsObserved, availableItems);
                }

                Thread.Sleep(Math.Min(20, Math.Max(1, (int)(deadline - stopwatch.ElapsedMilliseconds))));
            }
            while (true);
        }

        private static TControl? RequireUniqueSearchPickerControl<TControl>(
            IReadOnlyList<global::Avalonia.Controls.Control> controls,
            string partName,
            GridRelativeLocator? locator)
            where TControl : global::Avalonia.Controls.Control
        {
            if (locator is not null)
            {
                return null;
            }

            var matches = controls.OfType<TControl>().Take(2).ToArray();
            return matches.Length switch
            {
                0 => null,
                1 => matches[0],
                _ => throw new InvalidOperationException(
                    $"Grid SearchPicker {partName} is ambiguous: found multiple '{typeof(TControl).FullName}' controls.")
            };
        }

        private static int RemainingMilliseconds(System.Diagnostics.Stopwatch stopwatch, int timeoutMs)
        {
            var remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                throw new TimeoutException("The Headless grid SearchPicker operation exceeded its timeout.");
            }

            return remaining;
        }

        private static bool TryWriteTypedEditor(global::Avalonia.Controls.Control control, GridCellEditRequest request)
        {
            if (request.EditorKind == GridCellEditorKind.Number
                && HeadlessGridRuntimeAccess.TrySetNumericEditorValue(control, request.Value))
            {
                return true;
            }

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
                case global::Avalonia.Controls.CheckBox checkBox
                    when request.EditorKind == GridCellEditorKind.CheckBox
                         && bool.TryParse(request.Value, out var isChecked):
                    checkBox.IsChecked = isChecked;
                    return true;
                case global::Avalonia.Controls.TextBox textBox
                    when request.EditorKind is GridCellEditorKind.Text
                        or GridCellEditorKind.Number
                        or GridCellEditorKind.Date
                        or GridCellEditorKind.Time
                        or GridCellEditorKind.Color
                        or GridCellEditorKind.ComboBox:
                    textBox.Text = request.EditorKind == GridCellEditorKind.Color
                        ? ColorValue.Normalize(request.Value)
                        : request.Value;
                    return true;
                default:
                    return false;
            }
        }

        private bool InvokeEditorButton(
            global::Avalonia.Controls.Control cell,
            GridRelativeLocator? locator,
            GridCellEditRequest? request = null)
        {
            if (locator is null)
            {
                return false;
            }

            var control = AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                ResolveEditorPart(cell, locator));
            if (control is null)
            {
                return false;
            }

            try
            {
                AutomationElement.WrapControl(control).Click();
                return true;
            }
            catch (InvalidOperationException exception) when (request is not null)
            {
                throw CreateSearchPickerPartException(
                    cell,
                    request,
                    "open button",
                    $"control '{control.GetType().FullName}' is not semantically invokable: {exception.Message}");
            }
        }

        private InvalidOperationException CreateSearchPickerPartException(
            global::Avalonia.Controls.Control cell,
            GridCellEditRequest request,
            string partName,
            string reason)
        {
            var parts = AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                DescribeSearchPickerParts(cell, request.EditorParts));
            return new InvalidOperationException(
                $"Grid '{AutomationId}' SearchPicker {partName} {reason}. Configured parts: {parts}");
        }

        private string DescribeSearchPickerParts(
            global::Avalonia.Controls.Control cell,
            GridCellEditorParts? parts)
        {
            var configuredParts = string.Join(
                "; ",
                new[]
                {
                    DescribeSearchPickerPart(cell, "Input", parts?.Input),
                    DescribeSearchPickerPart(cell, "Results", parts?.Results),
                    DescribeSearchPickerPart(cell, "OpenButton", parts?.OpenButton),
                    DescribeSearchPickerPart(cell, "ConfirmButton", parts?.ConfirmButton)
                });
            var relevantCandidates = ReadSearchPickerControls(cell, null)
                .Where(static candidate =>
                    !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(candidate))
                    || candidate is global::Avalonia.Controls.TextBox
                    or global::Avalonia.Controls.ListBox
                    or global::Avalonia.Controls.Button
                    or global::Avalonia.Controls.Primitives.ToggleButton
                    or global::Avalonia.Controls.MenuItem)
                .ToArray();
            var candidates = relevantCandidates
                .Take(20)
                .Select(static candidate =>
                    $"{candidate.GetType().FullName}"
                    + $"[AutomationId='{AutomationProperties.GetAutomationId(candidate)}', "
                    + $"attached={candidate.IsAttachedToVisualTree()}, "
                    + $"visible={candidate.IsEffectivelyVisible}, "
                    + $"focusable={candidate.Focusable}]")
                .ToArray();
            var omitted = relevantCandidates.Length - candidates.Length;

            return configuredParts
                + "; discovered cell/editor controls: "
                + (candidates.Length == 0 ? "<none>" : string.Join(", ", candidates))
                + (omitted > 0 ? $", ... ({omitted} more)" : string.Empty);
        }

        private string DescribeSearchPickerPart(
            global::Avalonia.Controls.Control cell,
            string name,
            GridRelativeLocator? locator)
        {
            if (locator is null)
            {
                return $"{name}=not configured";
            }

            try
            {
                var match = ResolveEditorPart(cell, locator);
                return match is null
                    ? $"{name}={locator.Scope}/{locator.LocatorKind}:{locator.LocatorValue} -> missing"
                    : $"{name}={locator.Scope}/{locator.LocatorKind}:{locator.LocatorValue} -> {match.GetType().FullName}";
            }
            catch (InvalidOperationException exception)
            {
                return $"{name}={locator.Scope}/{locator.LocatorKind}:{locator.LocatorValue} -> {exception.Message}";
            }
        }

        private global::Avalonia.Controls.Control? ResolveEditorPart(
            global::Avalonia.Controls.Control cell,
            GridRelativeLocator? locator,
            global::Avalonia.Controls.Control? activeEditor = null)
        {
            if (locator is null)
            {
                return null;
            }

            if (locator.Scope == GridRelativeLocatorScope.EditorRoot)
            {
                var currentEditor = activeEditor ?? HeadlessGridRuntimeAccess.ReadActiveEditor(Inner.Control);
                if (currentEditor is not null
                    && currentEditor.IsEffectivelyVisible)
                {
                    var activeMatches = FindEditorPartMatches(
                        currentEditor,
                        locator,
                        requireVisualAttachment: false);
                    if (activeMatches.Length > 0)
                    {
                        return RequireUniqueEditorPart(activeMatches, locator);
                    }
                }

                var matchingRoots = cell.GetVisualChildren()
                    .OfType<global::Avalonia.Controls.Control>()
                    .Where(static root => root.IsEffectivelyVisible)
                    .Select(root => new
                    {
                        Root = root,
                        Matches = FindEditorPartMatches(
                            root,
                            locator,
                            requireVisualAttachment: false)
                    })
                    .Where(static candidate => candidate.Matches.Length > 0)
                    .Take(2)
                    .ToArray();
                if (matchingRoots.Length > 1)
                {
                    throw CreateAmbiguousEditorPartException(locator);
                }

                if (matchingRoots.Length == 1)
                {
                    return RequireUniqueEditorPart(matchingRoots[0].Matches, locator);
                }

                return null;
            }

            var scopeRoot = locator.Scope switch
            {
                GridRelativeLocatorScope.Cell => cell,
                GridRelativeLocatorScope.Row => cell
                    .GetVisualAncestors()
                    .OfType<global::Avalonia.Controls.Control>()
                    .TakeWhile(candidate => !ReferenceEquals(candidate, Inner.Control))
                    .Where(candidate => ReferenceEquals(candidate.DataContext, cell.DataContext))
                    .LastOrDefault() ?? cell,
                GridRelativeLocatorScope.GridRoot => Inner.Control,
                GridRelativeLocatorScope.DetachedPopup => _searchRoot.Control,
                _ => cell
            };
            var scopedMatches = FindEditorPartMatches(scopeRoot, locator);
            if (scopedMatches.Length > 0 || locator.Scope != GridRelativeLocatorScope.DetachedPopup)
            {
                return RequireUniqueEditorPart(scopedMatches, locator);
            }

            if (!HasOpenPopupOwner(cell))
            {
                return null;
            }

            return RequireUniqueEditorPart(
                FindEditorPartMatches(cell, locator, requireVisualAttachment: false),
                locator);
        }

        private static bool HasOpenPopupOwner(global::Avalonia.Controls.Control cell)
        {
            return new[] { cell }
                .Concat(ControlTree.EnumerateDescendants(cell))
                .Any(static candidate =>
                    GridPropertyValueReader.TryReadProperty(candidate, "IsPopupOpen", out var value)
                    && value is true);
        }

        private static global::Avalonia.Controls.Control[] FindEditorPartMatches(
            global::Avalonia.Controls.Control scopeRoot,
            GridRelativeLocator locator,
            bool requireVisualAttachment = true)
        {
            return new[] { scopeRoot }
                .Concat(ControlTree.EnumerateDescendants(scopeRoot))
                .Where(candidate =>
                    (!requireVisualAttachment || candidate.IsAttachedToVisualTree())
                    && candidate.IsEffectivelyVisible)
                .Where(candidate => locator.LocatorKind switch
                {
                    UiLocatorKind.AutomationId => string.Equals(
                        AutomationProperties.GetAutomationId(candidate),
                        locator.LocatorValue,
                        StringComparison.Ordinal),
                    UiLocatorKind.Name => string.Equals(
                        AutomationElement.ReadControlName(candidate),
                        locator.LocatorValue,
                        StringComparison.Ordinal),
                    _ => false
                })
                .Take(2)
                .ToArray();
        }

        private global::Avalonia.Controls.Control? RequireUniqueEditorPart(
            global::Avalonia.Controls.Control[] matches,
            GridRelativeLocator locator)
        {
            if (matches.Length > 1)
            {
                throw CreateAmbiguousEditorPartException(locator);
            }

            return matches.Length == 1 ? matches[0] : null;
        }

        private InvalidOperationException CreateAmbiguousEditorPartException(GridRelativeLocator locator)
        {
            return new InvalidOperationException(
                $"Grid editor part '{locator.LocatorKind}:{locator.LocatorValue}' is ambiguous "
                + $"within scope '{locator.Scope}' of the active cell in grid '{AutomationId}'.");
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

        private IGridRowControl[] ReadRows()
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

        private GridIndexedRowSelector MapRow(GridRowSelector row)
        {
            ArgumentNullException.ThrowIfNull(row);
            return new GridIndexedRowSelector(row.Conditions.Select(condition =>
                new GridIndexedCellCondition(
                    ResolveRuntimeColumn(condition.ColumnName),
                    condition.Value)));
        }

        private GridRuntimeColumn ResolveRuntimeColumn(string columnName)
        {
            var columnNames = ColumnNames;
            var matches = columnNames
                .Select(static (name, index) => (Name: name, Index: index))
                .Where(candidate => string.Equals(
                    candidate.Name,
                    columnName?.Trim(),
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Grid column '{columnName}' was not found in grid '{AutomationId}'. "
                    + $"Available columns: {string.Join(", ", columnNames)}.");
            }

            var columnIndex = matches[0].Index;

            return new GridRuntimeColumn(
                columnIndex,
                columnNames[columnIndex],
                displayValuePath: null,
                formatString: null,
                cultureName: null,
                GridCellValueKind.Text,
                editorKind: null,
                editorParts: null)
            {
                RuntimeColumnIndex = columnIndex
            };
        }

        private IndexedHeadlessRow ResolveUniqueRow(GridIndexedRowSelector row, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            var matches = FindMatchingRows(row, out var rowCount);
            if (matches.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Grid row selector matched {matches.Count} rows in grid '{AutomationId}'; expected exactly one. "
                    + DescribeResolution(row, matches.Count, rowCount));
            }

            return matches[0];
        }

        private List<IndexedHeadlessRow> FindMatchingRows(
            GridIndexedRowSelector selector,
            out int rowCount)
        {
            var rows = ReadIndexedRows();
            rowCount = rows.Length;
            return rows
                .Where(row => selector.Conditions.All(condition =>
                    string.Equals(
                        ReadCellSnapshot(row, condition.Column).DisplayText,
                        condition.ExpectedText,
                        StringComparison.Ordinal)))
                .ToList();
        }

        private IndexedHeadlessRow[] ReadIndexedRows()
        {
            var sourceItems = ReadSourceItems();
            if (sourceItems.Length > 0)
            {
                return sourceItems
                    .Select(static (item, index) => new IndexedHeadlessRow(index, item, null))
                    .ToArray();
            }

            return ReadVisualRows()
                .Select(row => new IndexedHeadlessRow(
                    ParseVisualGridIndex(row.AutomationId, "_Row"),
                    null,
                    row))
                .Where(static row => row.RowIndex != int.MaxValue)
                .ToArray();
        }

        private object?[] ReadSourceItems()
        {
            return AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                HeadlessGridRuntimeAccess.ReadSourceItems(Inner.Control));
        }

        private AutomationElement? FindCatalogVisualCell(
            IndexedHeadlessRow row,
            GridRuntimeColumn column,
            int timeoutMs)
        {
            if (row.Item is null)
            {
                return FindVisualCell(row.RowIndex, column.RuntimeColumnIndex ?? column.ColumnIndex);
            }

            var gridDescription = AutomationId;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var structuralCell = AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
            {
                var candidate = HeadlessGridRuntimeAccess.FindCell(
                    Inner.Control,
                    row.Item,
                    column,
                    gridDescription);
                if (candidate is not null)
                {
                    return candidate;
                }

                _ = HeadlessGridRuntimeAccess.ScrollIntoView(
                    Inner.Control,
                    row.Item,
                    row.RowIndex,
                    column);
                RefreshGridLayout();

                return HeadlessGridRuntimeAccess.FindCell(
                    Inner.Control,
                    row.Item,
                    column,
                    gridDescription);
            });
            if (structuralCell is not null)
            {
                return new AutomationElement(structuralCell);
            }

            var indexedCell = FindVisualCell(row.RowIndex, column.RuntimeColumnIndex ?? column.ColumnIndex);
            while (indexedCell is null && stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                var remaining = timeoutMs - (int)stopwatch.ElapsedMilliseconds;
                Thread.Sleep(Math.Min(20, Math.Max(1, remaining)));
                structuralCell = AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
                {
                    RefreshGridLayout();
                    return HeadlessGridRuntimeAccess.FindCell(
                        Inner.Control,
                        row.Item,
                        column,
                        gridDescription);
                });
                if (structuralCell is not null)
                {
                    return new AutomationElement(structuralCell);
                }

                indexedCell = FindVisualCell(row.RowIndex, column.RuntimeColumnIndex ?? column.ColumnIndex);
            }

            return indexedCell;

            void RefreshGridLayout()
            {
                Inner.Control.Dispatcher.RunJobs();
                if (Inner.Control is global::Avalonia.Controls.Primitives.TemplatedControl templatedGrid)
                {
                    templatedGrid.ApplyTemplate();
                }

                Inner.Control.UpdateLayout();
            }
        }

        private GridCellValueSnapshot ReadCellSnapshot(
            IndexedHeadlessRow row,
            GridRuntimeColumn column)
        {
            if (row.Item is not null)
            {
                var rawValue = ReadPropertyPath(row.Item, column.SourceFieldName);
                return GridCellValueNormalizer.Normalize(AutomationId, new GridCellValueSnapshot(
                    GridCellDisplayFormatter.Format(rawValue, cultureName: column.CultureName),
                    rawValue,
                    column.ValueKind)
                {
                    ValueSource = row.Item
                }, column);
            }

            if (row.VisualRow is null)
            {
                return new GridCellValueSnapshot(null, null, column.ValueKind);
            }

            var cells = new HeadlessVisualGridRowControl(row.VisualRow).Cells;
            var runtimeColumnIndex = column.RuntimeColumnIndex ?? column.ColumnIndex;
            var displayText = runtimeColumnIndex < cells.Count
                ? cells[runtimeColumnIndex].Value
                : null;
            return GridCellValueNormalizer.Normalize(AutomationId,
                new GridCellValueSnapshot(displayText, displayText, column.ValueKind) { IsDisplayOnly = true },
                column);
        }

        private void EnsureVisualRow(IndexedHeadlessRow row)
        {
            if (FindVisualCell(row.RowIndex, 0) is not null || row.Item is null)
            {
                return;
            }

            AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
            {
                var methods = Inner.Control.GetType()
                    .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .Where(static method => string.Equals(method.Name, "ScrollIntoView", StringComparison.Ordinal))
                    .OrderBy(static method => method.GetParameters().Length)
                    .ToArray();
                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 0 || !parameters[0].ParameterType.IsInstanceOfType(row.Item))
                    {
                        continue;
                    }

                    var arguments = new object?[parameters.Length];
                    arguments[0] = row.Item;
                    for (var index = 1; index < arguments.Length; index++)
                    {
                        arguments[index] = parameters[index].HasDefaultValue
                            ? parameters[index].DefaultValue
                            : null;
                    }

                    method.Invoke(Inner.Control, arguments);
                    break;
                }

                return true;
            });
        }

        private string DescribeResolution(
            GridIndexedRowSelector selector,
            int matchCount,
            int rowCount)
        {
            var conditions = string.Join(", ", selector.Conditions.Select(static condition =>
                $"column[{condition.ColumnIndex}]='{condition.ExpectedText}'"));
            return $"grid='{AutomationId}'; selector={conditions}; matches={matchCount}; rows={rowCount}";
        }

        private static object? ReadPropertyPath(object source, string? path)
        {
            return GridPropertyValueReader.TryReadPath(source, path, out var value)
                ? value
                : null;
        }


        private sealed record IndexedHeadlessRow(int RowIndex, object? Item, AutomationElement? VisualRow);

        private AutomationElement[] ReadVisualRows()
        {
            var rowPrefix = $"{AutomationId}_Row";
            return Inner.FindAllDescendants()
                .Where(candidate => IsVisualGridRow(candidate, rowPrefix))
                .OrderBy(candidate => ParseVisualGridIndex(candidate.AutomationId, "_Row"))
                .ToArray();
        }

        private IReadOnlyList<string>[] ReadDataRows()
        {
            var automationId = AutomationId;
            return AppAutomation.Avalonia.Headless.Session.HeadlessRuntime.Dispatch(() =>
            {
                return HeadlessGridRuntimeAccess.ReadSourceItems(Inner.Control)
                    .Select(item => ReadDisplayValues(item, automationId))
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

    private sealed class HeadlessGridCellControl : IGridCellValueControl
    {
        private readonly GridCell _inner;

        public HeadlessGridCellControl(GridCell inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public string Value => _inner.Value ?? string.Empty;

        public GridCellValueSnapshot ValueSnapshot => _inner.ValueSnapshot;
    }
}
