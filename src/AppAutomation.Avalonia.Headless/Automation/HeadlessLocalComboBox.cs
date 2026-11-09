using AppAutomation.Abstractions;
using AppAutomation.Avalonia.Headless.Internal.AutomationModel;
using AppAutomation.Avalonia.Headless.Session;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using ListBox = Avalonia.Controls.ListBox;

namespace AppAutomation.Avalonia.Headless.Automation;

internal static class HeadlessLocalComboBox
{
    public static IReadOnlyList<string> Select(
        string editorName,
        Control? editor,
        string itemText,
        bool commit,
        UiOperationTimeoutBudget budget,
        Func<Control?> resolveInput,
        Func<LocalComboBoxSurface<Control>?> resolveResults,
        Action open,
        Func<bool>? isPopupOpen = null,
        Func<string?>? readCommittedText = null)
    {
        var standalone = readCommittedText is not null;
        Func<bool>? selectionApplied = null;
        bool? ReadOpen() => (editor is null ? null : HeadlessRuntime.Dispatch<bool?>(() =>
            GridPropertyValueReader.TryReadProperty(editor, "IsPopupOpen", out var state)
                && state is bool openState ? openState : null)) ?? isPopupOpen?.Invoke();

        return LocalComboBoxSelection.Select(editorName, itemText, budget, ReadOpen, open, ResolveVisibleList, Activate,
            standalone ? IsCommitted : null);

        LocalComboBoxSurface<Control>? ResolveVisibleList()
        {
            var surface = resolveResults();
            return surface?.Native switch
            {
                null => surface,
                ListBox list => HeadlessRuntime.Dispatch(() => IsVisible(list)) ? surface : null,
                var other => throw new InvalidOperationException(
                    $"ComboBox '{editorName}' results expected ListBox but found '{other.GetType().FullName}'.")
            };
        }

        IReadOnlyList<string> Activate(Control results)
        {
            var list = (ListBox)results;
            var match = UiWait.Until(() => HeadlessRuntime.Dispatch(() => FindItem(list, itemText, budget, standalone)),
                static candidate => candidate.Index >= 0,
                new UiWaitOptions { Timeout = budget.Remaining, PollInterval = TimeSpan.FromMilliseconds(20) },
                $"ComboBox '{editorName}' results did not expose exact item '{itemText}'.");
            _ = budget.Remaining;
            var input = resolveInput();
            HeadlessRuntime.Dispatch(() =>
            {
                ValidateTarget();
                selectionApplied = ObserveSelection(editor, match.Item);
                if (!TrySelectWithKeyboard(input, list, itemText, (match.Index, match.Item), commit, standalone, budget, selectionApplied)
                    && IsVisible(list))
                {
                    ValidateTarget();
                    HeadlessPointerInput.ClickItem(list, match.Index, ValidateTarget);
                }
            });
            return match.Captions ?? [];

            void ValidateTarget()
            {
                _ = budget.Remaining;
                if (!IsVisible(list) || match.Index >= list.ItemCount || !Equals(list.Items[match.Index], match.Item))
                    throw new InvalidOperationException($"ComboBox item '{itemText}' is no longer available for selection.");
            }
        }

        bool IsCommitted(Control results)
        {
            var list = (ListBox)results;
            if (ReadOpen() ?? HeadlessRuntime.Dispatch(() => IsVisible(list)))
                return false;
            if (selectionApplied is not null)
                return HeadlessRuntime.Dispatch(selectionApplied);
            try
            {
                return GridSelectionText.Equals(readCommittedText!(), itemText);
            }
            catch (UiControlResolutionException exception) when (exception.IsTransient)
            {
                return false;
            }
        }
    }

    private static bool IsVisible(ListBox list) =>
        list.IsEffectivelyVisible && list.IsEffectivelyEnabled && TopLevel.GetTopLevel(list) is not null;

    private static (int Index, object? Item, string[]? Captions) FindItem(
        ListBox list, string text, UiOperationTimeoutBudget budget, bool captureCaptions) =>
        ListItemCaptionReader.ReadSnapshot(list, read =>
        {
            var match = -1;
            var captions = captureCaptions ? new string[list.ItemCount] : null;
            for (var index = 0; index < list.ItemCount; index++)
            {
                _ = budget.Remaining;
                var caption = read(index);
                if (captions is not null) captions[index] = caption ?? string.Empty;
                if (!GridSelectionText.Equals(caption, text)) continue;
                if (match >= 0)
                    throw new InvalidOperationException($"ComboBox results contain multiple exact items matching '{text}'.");
                match = index;
            }
            return (match, match >= 0 ? list.Items[match] : null, captions);
        }, budget.RemainingMilliseconds);

    private static Func<bool>? ObserveSelection(Control? editor, object? requestedItem)
    {
        if (editor is null || !GridPropertyValueReader.TryReadProperty(editor, "SelectedItem", out var originalSelection))
            return null;
        var hasEditorValue = GridPropertyValueReader.TryReadProperty(editor, "EditorValue", out var originalValue);
        return () =>
        {
            if (!GridPropertyValueReader.TryReadProperty(editor, "SelectedItem", out var appliedSelection)
                || !Equals(appliedSelection, requestedItem)) return false;
            if (!hasEditorValue) return true;
            GridPropertyValueReader.TryReadProperty(editor, "EditorValue", out var appliedValue);
            if (GridPropertyValueReader.TryReadProperty(editor, "ValueMember", out var valueMember)
                && valueMember is string { Length: > 0 } member)
                return requestedItem is not null && GridPropertyValueReader.TryReadPath(requestedItem, member, out var expectedValue)
                    && Equals(appliedValue, expectedValue);
            return Equals(appliedValue, requestedItem) || !Equals(appliedValue, originalValue)
                || Equals(originalSelection, requestedItem) && Equals(appliedValue, originalValue);
        };
    }

    private static bool TrySelectWithKeyboard(
        Control? input,
        ListBox results,
        string itemText,
        (int Index, object? Item) match,
        bool commit,
        bool requireClosedPopup,
        UiOperationTimeoutBudget budget,
        Func<bool>? selectionApplied)
    {
        if (input is null || !input.IsEnabled || !input.IsEffectivelyVisible
            || TopLevel.GetTopLevel(input) is not { } owner || !input.IsKeyboardFocusWithin)
            return false;

        // Keep focus in the editor; focusing the detached list may commit the old grid value.
        var targetIndex = match.Index;
        if (commit && selectionApplied is not null
            && ControlAutomationPeer.CreatePeerForElement(input)
                ?.GetProvider<IValueProvider>() is { IsReadOnly: false } valueInput)
        {
            _ = budget.Remaining;
            var previousText = valueInput.Value;
            valueInput.SetValue(itemText);
            input.Dispatcher.RunJobs();
            _ = budget.Remaining;
            if (selectionApplied())
            {
                if (!requireClosedPopup || !IsVisible(results)) return true;
            }
            else
            {
                if (!input.IsAttachedToVisualTree() || !input.IsKeyboardFocusWithin) return false;
                valueInput.SetValue(previousText);
                input.Dispatcher.RunJobs();
            }
            targetIndex = Enumerable.Range(0, results.ItemCount)
                .FirstOrDefault(index => Equals(results.Items[index], match.Item), -1);
            if (targetIndex < 0) return false;
        }

        var currentIndex = results.SelectedIndex;
        for (var step = 0; currentIndex != targetIndex && step < results.ItemCount; step++)
        {
            var nextIndex = currentIndex < targetIndex ? currentIndex + 1 : currentIndex - 1;
            Press(currentIndex < targetIndex ? Key.Down : Key.Up);
            if (results.SelectedIndex != nextIndex || !input.IsKeyboardFocusWithin) return false;
            currentIndex = nextIndex;
        }
        if (currentIndex != targetIndex) return false;
        _ = budget.Remaining;
        if (!commit) return true;
        if (!input.IsAttachedToVisualTree() || !input.IsKeyboardFocusWithin || !IsVisible(results)
            || targetIndex >= results.ItemCount || !Equals(results.Items[targetIndex], match.Item)) return false;
        Press(Key.Enter);
        // Do not deliver a second activation after Enter in a standalone editor.
        return requireClosedPopup || selectionApplied?.Invoke() == true;

        void Press(Key key)
        {
            _ = budget.Remaining;
            try
            {
                owner.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, keySymbol: null);
            }
            finally
            {
                owner.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, keySymbol: null);
            }
            input.Dispatcher.RunJobs();
        }
    }
}
