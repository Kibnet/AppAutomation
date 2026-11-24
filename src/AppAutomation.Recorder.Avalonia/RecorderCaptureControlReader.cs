using AppAutomation.Abstractions;
using Avalonia.Automation;
using Avalonia.Controls;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderCaptureControlReader(RecorderSelectorResolver selectorResolver)
{
    internal static string? ExtractTextValue(Control control)
    {
        return control switch
        {
            TextBox textBox => textBox.Text,
            TextBlock textBlock => textBlock.Text,
            Label label => label.Content?.ToString(),
            Button button => MenuPathValue.TryGetVisibleText(
                button.Content,
                AutomationProperties.GetName(button)),
            ComboBox comboBox => ExtractSelectionText(comboBox.SelectedItem),
            ListBox listBox => ExtractSelectionText(listBox.SelectedItem),
            _ => AutomationProperties.GetName(control)
        };
    }

    internal static bool TryReadPropertyValue(object item, string propertyName, out string value)
    {
        value = string.Empty;
        if (!GridPropertyValueReader.TryReadProperty(item, propertyName, out var propertyValue))
        {
            return false;
        }

        value = propertyValue?.ToString() ?? string.Empty;
        return true;
    }

    internal static IEnumerable<Control> EnumerateDescendantControls(Control root) =>
        RecorderControlTree.EnumerateReachableControls(root);

    internal bool TryFindControl(string locatorValue, UiLocatorKind locatorKind, out Control control)
    {
        control = null!;
        var descriptor = new RecordedControlDescriptor(
            "TemporaryLookup",
            UiControlType.AutomationElement,
            locatorValue.Trim(),
            locatorKind,
            FallbackToName: locatorKind == UiLocatorKind.Name,
            AvaloniaTypeName: typeof(Control).FullName ?? nameof(Control),
            Warning: null);
        var resolved = selectorResolver.ResolveExisting(descriptor);
        if (resolved.MatchedControl is null)
        {
            return false;
        }

        control = resolved.MatchedControl;
        return true;
    }

    internal static bool TryGetLocator(Control control, UiLocatorKind locatorKind, out string locator)
    {
        locator = locatorKind switch
        {
            UiLocatorKind.AutomationId => AutomationProperties.GetAutomationId(control) ?? string.Empty,
            UiLocatorKind.Name => AutomationProperties.GetName(control) ?? control.Name ?? string.Empty,
            _ => string.Empty
        };

        locator = locator.Trim();
        return !string.IsNullOrWhiteSpace(locator);
    }

    internal static string? FirstNonWhiteSpace(params string?[] values)
    {
        return values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
    }

    internal static IEnumerable<Control> EnumerateRelatedControls(Control? control)
        => RecorderControlTree.EnumerateRelatedControls(control, includePopupPlacementTarget: true);

    internal static string? ExtractSelectionText(object? selectedItem)
    {
        return selectedItem switch
        {
            null => null,
            string value => value,
            TabItem tabItem => FirstNonWhiteSpace(
                tabItem.Header?.ToString(),
                ExtractTextValue(tabItem),
                AutomationProperties.GetAutomationId(tabItem),
                tabItem.Name),
            Control control => FirstNonWhiteSpace(
                ExtractTextValue(control),
                AutomationProperties.GetAutomationId(control),
                control.Name),
            _ when TryReadPropertyValue(selectedItem, "Header", out var header) && !string.IsNullOrWhiteSpace(header) => header,
            _ when TryReadPropertyValue(selectedItem, "Title", out var title) && !string.IsNullOrWhiteSpace(title) => title,
            _ when TryReadPropertyValue(selectedItem, "Text", out var text) && !string.IsNullOrWhiteSpace(text) => text,
            _ when TryReadPropertyValue(selectedItem, "Name", out var name) && !string.IsNullOrWhiteSpace(name) => name,
            _ => selectedItem?.ToString()
        };
    }

    internal static RecordedControlDescriptor CreateCompositeDescriptor(
        string locatorValue,
        UiControlType controlType,
        UiLocatorKind locatorKind,
        bool fallbackToName,
        Control source,
        string? warning)
    {
        return new RecordedControlDescriptor(
            RecorderNaming.CreateControlPropertyName(locatorValue, controlType),
            controlType,
            locatorValue.Trim(),
            locatorKind,
            fallbackToName,
            source.GetType().FullName ?? source.GetType().Name,
            warning);
    }

    internal static bool HasExactLocator(Control source, UiLocatorKind locatorKind, string locatorValue)
    {
        return !string.IsNullOrWhiteSpace(locatorValue)
            && TryGetLocator(source, locatorKind, out var currentLocator)
            && string.Equals(currentLocator, locatorValue.Trim(), StringComparison.Ordinal);
    }
    internal static bool MatchesLocator(Control source, UiLocatorKind locatorKind, string locatorValue)
    {
        if (string.IsNullOrWhiteSpace(locatorValue))
        {
            return false;
        }

        foreach (var current in EnumerateRelatedControls(source))
        {
            if (TryGetLocator(current, locatorKind, out var currentLocator)
                && string.Equals(currentLocator, locatorValue.Trim(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

}
