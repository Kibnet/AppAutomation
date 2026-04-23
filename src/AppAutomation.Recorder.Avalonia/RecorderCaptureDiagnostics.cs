using System.Globalization;
using System.Text;
using AppAutomation.Abstractions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace AppAutomation.Recorder.Avalonia;

internal static class RecorderCaptureDiagnostics
{
    public static string Build(
        string scenarioName,
        RecorderSessionState state,
        string captureAction,
        Control? source,
        RecordedStep? step,
        IReadOnlyList<RecorderRuntimeValidationFinding> findings,
        string? failureMessage,
        Exception? exception = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("AppAutomation recorder diagnostic");
        builder.Append("TimestampUtc: ").Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)).AppendLine();
        builder.Append("ScenarioName: ").Append(NullIfWhiteSpace(scenarioName) ?? "<empty>").AppendLine();
        builder.Append("RecorderState: ").Append(state).AppendLine();
        builder.Append("CaptureAction: ").Append(NullIfWhiteSpace(captureAction) ?? "<unknown>").AppendLine();
        builder.Append("FailureMessage: ").Append(NullIfWhiteSpace(failureMessage) ?? "<none>").AppendLine();

        if (exception is not null)
        {
            builder.Append("Exception: ").Append(exception).AppendLine();
        }

        AppendStep(builder, step);
        AppendFindings(builder, findings);
        AppendControlSnapshot(builder, source);
        AppendPaths(builder, source);
        AppendSupportSuggestion(builder, step, findings, failureMessage);
        return builder.ToString().TrimEnd();
    }

    private static void AppendStep(StringBuilder builder, RecordedStep? step)
    {
        builder.AppendLine("RecordedCommand:");
        if (step is null)
        {
            builder.AppendLine("  <none>");
            return;
        }

        builder.Append("  ActionKind: ").Append(step.ActionKind).AppendLine();
        builder.Append("  Control.PropertyName: ").Append(step.Control.ProposedPropertyName).AppendLine();
        builder.Append("  Control.UiControlType: ").Append(step.Control.ControlType).AppendLine();
        builder.Append("  Control.Locator: ").Append(step.Control.LocatorKind).Append(':').Append(step.Control.LocatorValue).AppendLine();
        builder.Append("  Control.FallbackToName: ").Append(step.Control.FallbackToName).AppendLine();
        builder.Append("  Control.AvaloniaType: ").Append(step.Control.AvaloniaTypeName).AppendLine();
        builder.Append("  Control.Warning: ").Append(NullIfWhiteSpace(step.Control.Warning) ?? "<none>").AppendLine();
        builder.Append("  Payload.StringValue: ").Append(NullIfWhiteSpace(step.StringValue) ?? "<null/empty>").AppendLine();
        builder.Append("  Payload.ItemValue: ").Append(NullIfWhiteSpace(step.ItemValue) ?? "<null/empty>").AppendLine();
        builder.Append("  Payload.BoolValue: ").Append(step.BoolValue?.ToString() ?? "<null>").AppendLine();
        builder.Append("  Payload.DoubleValue: ").Append(step.DoubleValue?.ToString(CultureInfo.InvariantCulture) ?? "<null>").AppendLine();
        builder.Append("  Payload.DateValue: ").Append(step.DateValue?.ToString("O", CultureInfo.InvariantCulture) ?? "<null>").AppendLine();
        builder.Append("  Payload.IntValue: ").Append(step.IntValue?.ToString(CultureInfo.InvariantCulture) ?? "<null>").AppendLine();
        builder.Append("  Payload.RowIndex: ").Append(step.RowIndex?.ToString(CultureInfo.InvariantCulture) ?? "<null>").AppendLine();
        builder.Append("  Payload.ColumnIndex: ").Append(step.ColumnIndex?.ToString(CultureInfo.InvariantCulture) ?? "<null>").AppendLine();
        builder.Append("  ValidationStatus: ").Append(step.ValidationStatus).AppendLine();
        builder.Append("  CanPersist: ").Append(step.CanPersist).AppendLine();
        builder.Append("  ValidationMessage: ").Append(NullIfWhiteSpace(step.ValidationMessage) ?? "<none>").AppendLine();
    }

    private static void AppendFindings(StringBuilder builder, IReadOnlyList<RecorderRuntimeValidationFinding> findings)
    {
        builder.AppendLine("ValidationFindings:");
        if (findings.Count == 0)
        {
            builder.AppendLine("  <none>");
            return;
        }

        foreach (var finding in findings)
        {
            builder
                .Append("  - Target=").Append(finding.Target)
                .Append("; Severity=").Append(finding.Severity)
                .Append("; Code=").Append(finding.Code)
                .Append("; BlocksTarget=").Append(finding.BlocksTarget)
                .Append("; Message=").Append(finding.Message)
                .AppendLine();
        }
    }

    private static void AppendControlSnapshot(StringBuilder builder, Control? source)
    {
        builder.AppendLine("ControlSnapshot:");
        if (source is null)
        {
            builder.AppendLine("  <no source control>");
            return;
        }

        builder.Append("  ClrType: ").Append(source.GetType().FullName ?? source.GetType().Name).AppendLine();
        builder.Append("  AutomationId: ").Append(NullIfWhiteSpace(SafeRead(() => AutomationProperties.GetAutomationId(source))) ?? "<none>").AppendLine();
        builder.Append("  AutomationName: ").Append(NullIfWhiteSpace(SafeRead(() => AutomationProperties.GetName(source))) ?? "<none>").AppendLine();
        builder.Append("  AvaloniaName: ").Append(NullIfWhiteSpace(SafeRead(() => source.Name)) ?? "<none>").AppendLine();
        builder.Append("  IsEnabled: ").Append(SafeRead(() => source.IsEnabled.ToString()) ?? "<unknown>").AppendLine();
        builder.Append("  IsVisible: ").Append(SafeRead(() => source.IsVisible.ToString()) ?? "<unknown>").AppendLine();
        builder.Append("  IsFocused: ").Append(IsFocused(source)?.ToString() ?? "<unknown>").AppendLine();
        builder.Append("  Bounds: ").Append(SafeRead(() => source.Bounds.ToString()) ?? "<unknown>").AppendLine();
        builder.Append("  DataContextType: ").Append(source.DataContext?.GetType().FullName ?? "<null>").AppendLine();
        builder.Append("  DataContextValue: ").Append(NullIfWhiteSpace(SafeRead(() => source.DataContext?.ToString())) ?? "<null/empty>").AppendLine();
        AppendCommonValues(builder, source);
    }

    private static void AppendCommonValues(StringBuilder builder, Control source)
    {
        builder.AppendLine("  CommonValues:");
        switch (source)
        {
            case TextBox textBox:
                builder.Append("    TextBox.Text: ").Append(NullIfWhiteSpace(SafeRead(() => textBox.Text)) ?? "<null/empty>").AppendLine();
                break;
            case ContentControl contentControl:
                builder.Append("    ContentControl.Content: ").Append(NullIfWhiteSpace(SafeRead(() => contentControl.Content?.ToString())) ?? "<null/empty>").AppendLine();
                break;
        }

        if (source is SelectingItemsControl selectingItemsControl)
        {
            builder.Append("    SelectingItemsControl.SelectedItem: ")
                .Append(NullIfWhiteSpace(SafeRead(() => selectingItemsControl.SelectedItem?.ToString())) ?? "<null/empty>")
                .AppendLine();
            builder.Append("    SelectingItemsControl.SelectedIndex: ")
                .Append(SafeRead(() => selectingItemsControl.SelectedIndex.ToString(CultureInfo.InvariantCulture)) ?? "<unknown>")
                .AppendLine();
        }

        if (source is ToggleButton toggleButton)
        {
            builder.Append("    ToggleButton.IsChecked: ").Append(SafeRead(() => toggleButton.IsChecked)?.ToString() ?? "<unknown>").AppendLine();
        }

        if (source is Slider slider)
        {
            builder.Append("    Slider.Value: ").Append(SafeRead(() => slider.Value.ToString(CultureInfo.InvariantCulture)) ?? "<unknown>").AppendLine();
        }

        if (source is DatePicker datePicker)
        {
            var selectedDate = SafeRead(() => datePicker.SelectedDate);
            builder.Append("    DatePicker.SelectedDate: ")
                .Append(selectedDate.HasValue ? selectedDate.Value.ToString("O", CultureInfo.InvariantCulture) : "<unknown>")
                .AppendLine();
        }

        if (source is global::Avalonia.Controls.Calendar calendar)
        {
            var selectedDate = SafeRead(() => calendar.SelectedDate);
            builder.Append("    Calendar.SelectedDate: ")
                .Append(selectedDate.HasValue ? selectedDate.Value.ToString("O", CultureInfo.InvariantCulture) : "<unknown>")
                .AppendLine();
        }
    }

    private static void AppendPaths(StringBuilder builder, Control? source)
    {
        builder.AppendLine("TreeContext:");
        builder.Append("  VisualPath: ").Append(source is null ? "<none>" : BuildVisualPath(source)).AppendLine();
        builder.Append("  LogicalPath: ").Append(source is null ? "<none>" : BuildLogicalPath(source)).AppendLine();
        builder.Append("  OwnerPath: ").Append(source is null ? "<none>" : BuildOwnerPath(source)).AppendLine();
    }

    private static void AppendSupportSuggestion(
        StringBuilder builder,
        RecordedStep? step,
        IReadOnlyList<RecorderRuntimeValidationFinding> findings,
        string? failureMessage)
    {
        builder.AppendLine("SupportContext:");
        builder.Append("  SuggestedArea: ").Append(ResolveSuggestedArea(step, findings, failureMessage)).AppendLine();
        builder.Append("  Notes: ").Append(ResolveSupportNotes(findings)).AppendLine();
    }

    private static string ResolveSuggestedArea(
        RecordedStep? step,
        IReadOnlyList<RecorderRuntimeValidationFinding> findings,
        string? failureMessage)
    {
        if (findings.Any(static finding => finding.Code.Contains("control-type-mismatch", StringComparison.Ordinal)))
        {
            return "action mapping or recorder control hint";
        }

        if (findings.Any(static finding => finding.Code.Contains("payload", StringComparison.Ordinal)))
        {
            return "recorder action payload extraction";
        }

        if (findings.Any(static finding => finding.Code.Contains("grid", StringComparison.Ordinal)))
        {
            return "grid hint or grid action adapter";
        }

        if (failureMessage?.Contains("locator", StringComparison.OrdinalIgnoreCase) == true
            || failureMessage?.Contains("AutomationId", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "stable locator, alias, or application AutomationId";
        }

        return step?.Control.ControlType switch
        {
            UiControlType.SearchPicker
                or UiControlType.DateRangeFilter
                or UiControlType.NumericRangeFilter
                or UiControlType.Dialog
                or UiControlType.Notification
                or UiControlType.FolderExport
                or UiControlType.ShellNavigation => "composed adapter configuration",
            _ => "provider resolver or recorder action mapping"
        };
    }

    private static string ResolveSupportNotes(IReadOnlyList<RecorderRuntimeValidationFinding> findings)
    {
        var surfaced = findings
            .Where(static finding => finding.ShouldSurface)
            .Select(static finding => $"{finding.Target}:{finding.Code}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return surfaced.Length == 0
            ? "No runtime-readiness findings."
            : string.Join(", ", surfaced);
    }

    private static string BuildVisualPath(Control source)
    {
        var controls = new List<Control>();
        for (Control? current = source; current is not null; current = current.GetVisualParent() as Control)
        {
            controls.Add(current);
        }

        controls.Reverse();
        return string.Join(" > ", controls.Select(FormatControlPathSegment));
    }

    private static string BuildLogicalPath(Control source)
    {
        var controls = new List<Control>();
        for (Control? current = source; current is not null;)
        {
            controls.Add(current);
            current = current is ILogical { LogicalParent: Control logicalParent }
                ? logicalParent
                : null;
        }

        controls.Reverse();
        return string.Join(" > ", controls.Select(FormatControlPathSegment));
    }

    private static string BuildOwnerPath(Control source)
    {
        var controls = new List<Control> { source };
        if (source is StyledElement { TemplatedParent: Control templatedParent } && !ReferenceEquals(templatedParent, source))
        {
            controls.Add(templatedParent);
        }

        if (source is ILogical { LogicalParent: Control logicalParent }
            && !ReferenceEquals(logicalParent, source)
            && !controls.Contains(logicalParent))
        {
            controls.Add(logicalParent);
        }

        var visualParent = source.GetVisualParent() as Control;
        if (visualParent is not null && !controls.Contains(visualParent))
        {
            controls.Add(visualParent);
        }

        return string.Join(" -> ", controls.Select(FormatControlPathSegment));
    }

    private static string FormatControlPathSegment(Control control)
    {
        var automationId = NullIfWhiteSpace(SafeRead(() => AutomationProperties.GetAutomationId(control)));
        var automationName = NullIfWhiteSpace(SafeRead(() => AutomationProperties.GetName(control)));
        var avaloniaName = NullIfWhiteSpace(SafeRead(() => control.Name));
        var identity = automationId is not null
            ? $"AutomationId={automationId}"
            : automationName is not null
                ? $"Name={automationName}"
                : avaloniaName is not null
                    ? $"x:Name={avaloniaName}"
                    : "no-id";
        return $"{control.GetType().Name}({identity})";
    }

    private static bool? IsFocused(Control control)
    {
        return SafeRead(() => ReferenceEquals(TopLevel.GetTopLevel(control)?.FocusManager?.GetFocusedElement(), control));
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static T? SafeRead<T>(Func<T?> accessor)
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
