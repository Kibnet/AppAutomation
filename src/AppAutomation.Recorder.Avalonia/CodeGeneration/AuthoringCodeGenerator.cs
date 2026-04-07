using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace AppAutomation.Recorder.Avalonia.CodeGeneration;

internal sealed class AuthoringCodeGenerator
{
    private readonly AuthoringProjectScanner _scanner;
    private readonly ILogger? _logger;

    public AuthoringCodeGenerator(AuthoringProjectScanner scanner, ILogger? logger)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _logger = logger;
    }

    public async Task<RecorderSaveResult> SaveAsync(
        Window window,
        AppAutomationRecorderOptions options,
        IReadOnlyList<RecordedStep> steps,
        string? outputDirectoryOverride,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.Count == 0)
        {
            return RecorderSaveResult.Failed("Recorder has no supported steps to save.");
        }

        if (string.IsNullOrWhiteSpace(options.AuthoringProjectDirectory))
        {
            return RecorderSaveResult.Failed("Authoring project directory is not configured.");
        }

        var projectDirectory = Path.GetFullPath(options.AuthoringProjectDirectory);
        if (!Directory.Exists(projectDirectory))
        {
            return RecorderSaveResult.Failed($"Authoring project directory '{projectDirectory}' does not exist.");
        }

        var target = ResolveTargetConfiguration(window, options, projectDirectory, outputDirectoryOverride);
        var snapshot = _scanner.Scan(target, cancellationToken);
        var validationError = ValidateSnapshot(target, snapshot);
        if (validationError is not null)
        {
            return RecorderSaveResult.Failed(validationError);
        }

        var diagnostics = new List<string>();
        var reservedPropertyNames = new HashSet<string>(snapshot.ExistingControlPropertyNames, StringComparer.Ordinal);
        var generatedControlsByKey = new Dictionary<string, ExistingControlInfo>(StringComparer.Ordinal);
        var generatedControls = new List<ExistingControlInfo>();
        var renderedStatements = new List<string>(steps.Count);

        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = AuthoringProjectScanner.CreateControlKey(step.Control.LocatorKind, step.Control.LocatorValue);
            if (!snapshot.ExistingControlsByKey.TryGetValue(key, out var controlInfo)
                && !generatedControlsByKey.TryGetValue(key, out controlInfo))
            {
                var propertyName = RecorderNaming.EnsureUniqueName(step.Control.ProposedPropertyName, reservedPropertyNames);
                controlInfo = new ExistingControlInfo(
                    propertyName,
                    step.Control.ControlType,
                    step.Control.LocatorValue,
                    step.Control.LocatorKind,
                    step.Control.FallbackToName);
                generatedControlsByKey.Add(key, controlInfo);
                generatedControls.Add(controlInfo);

                if (!string.Equals(propertyName, step.Control.ProposedPropertyName, StringComparison.Ordinal))
                {
                    diagnostics.Add(
                        $"Control '{step.Control.LocatorValue}' was renamed from '{step.Control.ProposedPropertyName}' to '{propertyName}' to avoid a collision.");
                }
            }

            if (!string.IsNullOrWhiteSpace(step.Warning))
            {
                diagnostics.Add(step.Warning);
            }

            renderedStatements.Add(GenerateStepStatement(step, controlInfo.PropertyName));
        }

        var reservedMethodNames = new HashSet<string>(snapshot.ExistingScenarioMethodNames, StringComparer.Ordinal);
        var timestamp = DateTimeOffset.Now;
        var methodName = RecorderNaming.EnsureUniqueName(
            RecorderNaming.CreateRecordedMethodBaseName(target.ScenarioName, timestamp),
            reservedMethodNames);
        var fileSafeScenarioName = RecorderNaming.CreateFileSafeName(target.ScenarioName, "scenario");
        var pageFilePath = generatedControls.Count == 0
            ? null
            : Path.Combine(target.OutputDirectory, $"{target.PageClassName}.{fileSafeScenarioName}.controls.g.cs");
        var scenarioFilePath = Path.Combine(
            target.OutputDirectory,
            $"{target.ScenarioClassName}.{fileSafeScenarioName}.{timestamp:yyyyMMdd_HHmmss}.g.cs");

        Directory.CreateDirectory(target.OutputDirectory);

        if (pageFilePath is not null)
        {
            var pageSource = GeneratePagePartial(target, snapshot.PageClass!, generatedControls);
            await File.WriteAllTextAsync(pageFilePath, pageSource, cancellationToken);
        }

        var scenarioSource = GenerateScenarioPartial(target, snapshot.ScenarioClass!, methodName, renderedStatements);
        await File.WriteAllTextAsync(scenarioFilePath, scenarioSource, cancellationToken);

        _logger?.LogInformation(
            "Recorder saved scenario '{ScenarioName}' to '{ScenarioFilePath}' with {GeneratedControlCount} new controls.",
            target.ScenarioName,
            scenarioFilePath,
            generatedControls.Count);

        return RecorderSaveResult.Completed(
            $"Recorded scenario '{target.ScenarioName}' was saved.",
            pageFilePath,
            scenarioFilePath,
            diagnostics);
    }

    public string GeneratePreview(IReadOnlyList<RecordedStep> steps)
    {
        if (steps.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var step in steps)
        {
            builder.AppendLine(GenerateStepStatement(step, step.Control.ProposedPropertyName));
        }

        return builder.ToString().TrimEnd();
    }

    public string GeneratePreview(RecordedStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return GenerateStepStatement(step, step.Control.ProposedPropertyName);
    }

    private static string? ValidateSnapshot(AuthoringTargetConfiguration target, AuthoringProjectSnapshot snapshot)
    {
        if (snapshot.PageClass is null)
        {
            return $"Page class '{target.PageNamespace}.{target.PageClassName}' was not found in '{target.ProjectDirectory}'.";
        }

        if (!snapshot.PageClass.IsPartial)
        {
            return $"Page class '{target.PageNamespace}.{target.PageClassName}' must be partial for recorder-generated attributes.";
        }

        if (snapshot.ScenarioClass is null)
        {
            return $"Scenario class '{target.ScenarioNamespace}.{target.ScenarioClassName}' was not found in '{target.ProjectDirectory}'.";
        }

        if (!snapshot.ScenarioClass.IsPartial)
        {
            return $"Scenario class '{target.ScenarioNamespace}.{target.ScenarioClassName}' must be partial for recorder-generated methods.";
        }

        return null;
    }

    private static AuthoringTargetConfiguration ResolveTargetConfiguration(
        Window window,
        AppAutomationRecorderOptions options,
        string projectDirectory,
        string? outputDirectoryOverride)
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var appName = entryAssembly?.GetName().Name
            ?? window.GetType().Assembly.GetName().Name
            ?? "App";

        var pageClassName = options.PageClassName ?? $"{window.GetType().Name}Page";
        var scenarioClassName = options.ScenarioClassName ?? $"{window.GetType().Name}ScenariosBase";
        var pageNamespace = options.PageNamespace ?? $"{appName}.UiTests.Authoring.Pages";
        var scenarioNamespace = options.ScenarioNamespace ?? $"{appName}.UiTests.Authoring.Tests";
        var outputDirectory = string.IsNullOrWhiteSpace(outputDirectoryOverride)
            ? Path.Combine(projectDirectory, options.OutputSubdirectory)
            : Path.GetFullPath(outputDirectoryOverride);

        return new AuthoringTargetConfiguration(
            projectDirectory,
            outputDirectory,
            pageNamespace,
            pageClassName,
            scenarioNamespace,
            scenarioClassName,
            options.ScenarioName,
            appName);
    }

    private static string GeneratePagePartial(
        AuthoringTargetConfiguration target,
        ScannedClassInfo pageClass,
        IReadOnlyList<ExistingControlInfo> controls)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using AppAutomation.Abstractions;");
        builder.AppendLine();
        builder.Append("namespace ").Append(target.PageNamespace).AppendLine(";");
        builder.AppendLine();

        foreach (var control in controls)
        {
            builder.AppendLine(GenerateUiControlAttribute(control));
        }

        builder.Append(pageClass.ModifiersText)
            .Append(" partial class ")
            .Append(target.PageClassName)
            .AppendLine();
        builder.AppendLine("{");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateScenarioPartial(
        AuthoringTargetConfiguration target,
        ScannedClassInfo scenarioClass,
        string methodName,
        IReadOnlyList<string> renderedStatements)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using AppAutomation.Abstractions;");
        builder.AppendLine("using TUnit.Core;");
        builder.AppendLine();
        builder.Append("namespace ").Append(target.ScenarioNamespace).AppendLine(";");
        builder.AppendLine();
        builder.Append(scenarioClass.ModifiersText)
            .Append(" partial class ")
            .Append(target.ScenarioClassName)
            .Append(scenarioClass.TypeParameterListText)
            .AppendLine();
        builder.AppendLine("{");
        builder.AppendLine("    [Test]");
        builder.AppendLine("    [NotInParallel(DesktopUiConstraint)]");
        builder.Append("    public void ").Append(methodName).AppendLine("()");
        builder.AppendLine("    {");
        foreach (var statement in renderedStatements)
        {
            builder.Append("        ").AppendLine(statement);
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateUiControlAttribute(ExistingControlInfo control)
    {
        var builder = new StringBuilder();
        builder.Append("[UiControl(\"")
            .Append(EscapeString(control.PropertyName))
            .Append("\", UiControlType.")
            .Append(control.ControlType)
            .Append(", \"")
            .Append(EscapeString(control.LocatorValue))
            .Append("\"");

        if (control.LocatorKind != UiLocatorKind.AutomationId)
        {
            builder.Append(", LocatorKind = UiLocatorKind.")
                .Append(control.LocatorKind);
        }

        builder.Append(", FallbackToName = ")
            .Append(control.FallbackToName ? "true" : "false")
            .Append(")]");
        return builder.ToString();
    }

    private static string GenerateStepStatement(RecordedStep step, string propertyName)
    {
        var statement = step.ActionKind switch
        {
            RecordedActionKind.EnterText => $"Page.EnterText(static page => page.{propertyName}, \"{EscapeString(step.StringValue ?? string.Empty)}\");",
            RecordedActionKind.ClickButton => $"Page.ClickButton(static page => page.{propertyName});",
            RecordedActionKind.SetChecked => $"Page.SetChecked(static page => page.{propertyName}, {FormatBoolean(step.BoolValue)});",
            RecordedActionKind.SetToggled => $"Page.SetToggled(static page => page.{propertyName}, {FormatBoolean(step.BoolValue)});",
            RecordedActionKind.SelectComboItem => $"Page.SelectComboItem(static page => page.{propertyName}, \"{EscapeString(step.StringValue ?? string.Empty)}\");",
            RecordedActionKind.SetSliderValue => $"Page.SetSliderValue(static page => page.{propertyName}, {FormatDouble(step.DoubleValue)});",
            RecordedActionKind.SetSpinnerValue => $"Page.SetSpinnerValue(static page => page.{propertyName}, {FormatDouble(step.DoubleValue)});",
            RecordedActionKind.SelectTabItem => $"Page.SelectTabItem(static page => page.{propertyName});",
            RecordedActionKind.SelectTreeItem => $"Page.SelectTreeItem(static page => page.{propertyName}, \"{EscapeString(step.StringValue ?? string.Empty)}\");",
            RecordedActionKind.SetDate => $"Page.SetDate(static page => page.{propertyName}, {FormatDate(step.DateValue)});",
            RecordedActionKind.WaitUntilTextEquals => $"Page.WaitUntilTextEquals(static page => page.{propertyName}, \"{EscapeString(step.StringValue ?? string.Empty)}\");",
            RecordedActionKind.WaitUntilTextContains => $"Page.WaitUntilTextContains(static page => page.{propertyName}, \"{EscapeString(step.StringValue ?? string.Empty)}\");",
            RecordedActionKind.WaitUntilIsChecked => $"Page.WaitUntilIsChecked(static page => page.{propertyName}, {FormatBoolean(step.BoolValue)});",
            RecordedActionKind.WaitUntilIsToggled => $"Page.WaitUntilIsToggled(static page => page.{propertyName}, {FormatBoolean(step.BoolValue)});",
            RecordedActionKind.WaitUntilIsSelected => $"Page.WaitUntilIsSelected(static page => page.{propertyName}, {FormatBoolean(step.BoolValue)});",
            RecordedActionKind.WaitUntilIsEnabled => $"Page.WaitUntilIsEnabled(static page => page.{propertyName}, {FormatBoolean(step.BoolValue)});",
            _ => $"// Unsupported recorded action '{step.ActionKind}'."
        };

        return string.IsNullOrWhiteSpace(step.Warning)
            ? statement
            : $"{statement} // {step.Warning}";
    }

    private static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string FormatBoolean(bool? value)
    {
        return value == true ? "true" : "false";
    }

    private static string FormatDouble(double? value)
    {
        return (value ?? 0).ToString("G17", CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateTime? value)
    {
        var date = (value ?? DateTime.Today).Date;
        return $"new global::System.DateTime({date.Year}, {date.Month}, {date.Day})";
    }
}
