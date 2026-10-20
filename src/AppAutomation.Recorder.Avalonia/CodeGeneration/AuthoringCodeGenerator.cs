using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AppAutomation.Abstractions;
using AppAutomation.Recorder.Avalonia.SourceScanning;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace AppAutomation.Recorder.Avalonia.CodeGeneration;

internal sealed class AuthoringCodeGenerator
{
    private readonly AuthoringProjectScanner _scanner;
    private readonly ILogger? _logger;
    private readonly Dictionary<string, AutosaveTargetRegistration> _autosaveTargets = new(StringComparer.Ordinal);

    public AuthoringCodeGenerator(AuthoringProjectScanner scanner, ILogger? logger)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _logger = logger;
    }

    public Task<RecorderSaveResult> SaveAsync(
        Window window,
        AppAutomationRecorderOptions options,
        IReadOnlyList<RecordedStep> steps,
        string? outputDirectoryOverride,
        CancellationToken cancellationToken = default)
    {
        return SaveCoreAsync(window, options, steps, outputDirectoryOverride, AuthoringSaveKind.Snapshot, saveContext: null, cancellationToken);
    }

    internal Task<RecorderSaveResult> SaveAsync(
        Window window,
        AppAutomationRecorderOptions options,
        IReadOnlyList<RecordedStep> steps,
        string? outputDirectoryOverride,
        RecorderScenarioSaveContext saveContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saveContext);
        return SaveCoreAsync(window, options, steps, outputDirectoryOverride, AuthoringSaveKind.Snapshot, saveContext, cancellationToken);
    }

    public Task<RecorderSaveResult> AutosaveAsync(
        Window window,
        AppAutomationRecorderOptions options,
        IReadOnlyList<RecordedStep> steps,
        string? outputDirectoryOverride,
        CancellationToken cancellationToken = default)
    {
        return SaveCoreAsync(window, options, steps, outputDirectoryOverride, AuthoringSaveKind.Autosave, saveContext: null, cancellationToken);
    }

    internal Task<RecorderSaveResult> AutosaveAsync(
        Window window,
        AppAutomationRecorderOptions options,
        IReadOnlyList<RecordedStep> steps,
        string? outputDirectoryOverride,
        RecorderScenarioSaveContext saveContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saveContext);
        return SaveCoreAsync(window, options, steps, outputDirectoryOverride, AuthoringSaveKind.Autosave, saveContext, cancellationToken);
    }

    internal Task<RecorderAutosaveRestoreResult> RestoreAutosaveAsync(
        Window window,
        AppAutomationRecorderOptions options,
        string? outputDirectoryOverride,
        RecorderScenarioSaveContext saveContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(saveContext);

        if (string.IsNullOrWhiteSpace(options.AuthoringProjectDirectory))
        {
            return Task.FromResult(RecorderAutosaveRestoreResult.Failed(
                "Authoring project directory is not configured."));
        }

        var projectDirectory = Path.GetFullPath(options.AuthoringProjectDirectory);
        if (!Directory.Exists(projectDirectory))
        {
            return Task.FromResult(RecorderAutosaveRestoreResult.Failed(
                $"Authoring project directory '{projectDirectory}' does not exist."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var target = ResolveTargetConfiguration(
            window,
            options,
            projectDirectory,
            outputDirectoryOverride,
            saveContext);
        var diagnostics = new List<string>();
        var candidates = new List<(string FilePath, RecorderAutosaveState State)>();
        string? recoveryError = null;
        foreach (var filePath in FindAutosaveFilesForDestination(target, diagnostics)
                     .Where(static path => path.EndsWith(".g.cs.autosave", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!RecorderAutosaveStateSerializer.TryRead(filePath, out var state, out var error))
            {
                recoveryError ??= error;
                continue;
            }

            if (string.Equals(state!.ScenarioName, target.ScenarioName, StringComparison.Ordinal))
            {
                candidates.Add((filePath, state));
            }
        }

        if (candidates.Count == 0)
        {
            return Task.FromResult(recoveryError is null
                ? RecorderAutosaveRestoreResult.NotFound(
                    $"No autosave recovery was found for scenario '{target.ScenarioName}'.")
                : RecorderAutosaveRestoreResult.Failed(recoveryError));
        }

        var recovery = candidates
            .OrderByDescending(static candidate => candidate.State.SavedAtUtc)
            .ThenByDescending(static candidate => File.GetLastWriteTimeUtc(candidate.FilePath))
            .ThenBy(static candidate => candidate.FilePath, StringComparer.OrdinalIgnoreCase)
            .First();
        var graphValidation = RecorderScenarioGraphValidator.Validate(recovery.State.Steps);
        if (!graphValidation.Success)
        {
            return Task.FromResult(RecorderAutosaveRestoreResult.Failed(
                $"Recorder autosave recovery graph is invalid: {graphValidation.Error}"));
        }

        var scenarioPrefix = $"{target.ScenarioClassName}.";
        var pageFilePath = GetPairedPageAutosavePath(
                target.PageClassName,
                recovery.FilePath,
                scenarioPrefix)
            ?? Path.Combine(target.OutputDirectory, $"{target.PageClassName}.controls.g.cs.autosave");
        var restoredTarget = new AuthoringSaveTarget(
            pageFilePath,
            recovery.FilePath,
            recovery.State.MethodName);
        _autosaveTargets[CreateAutosaveTargetKey(target, recovery.State.DraftIdentity)] =
            new AutosaveTargetRegistration(CreateAutosaveDestinationKey(target), restoredTarget);

        return Task.FromResult(RecorderAutosaveRestoreResult.Restored(
            $"Restored {recovery.State.Steps.Count} recorded step(s) for scenario '{target.ScenarioName}'.",
            recovery.State.DraftIdentity,
            recovery.State.Steps));
    }

    private async Task<RecorderSaveResult> SaveCoreAsync(
        Window window,
        AppAutomationRecorderOptions options,
        IReadOnlyList<RecordedStep> steps,
        string? outputDirectoryOverride,
        AuthoringSaveKind saveKind,
        RecorderScenarioSaveContext? saveContext,
        CancellationToken cancellationToken)
    {
        var previousAutosaveKeys = _autosaveTargets.Keys.ToHashSet(StringComparer.Ordinal);
        var diagnostics = new List<string>();
        var completed = false;
        try
        {
            var result = await GenerateAndSaveAsync(
                window, options, steps, outputDirectoryOverride, saveKind, saveContext, diagnostics, cancellationToken);
            completed = result.Success;
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(exception, "Recorder could not save the generated files.");
            return RecorderSaveResult.Failed($"Recorder output could not be saved: {exception.Message}", diagnostics.ToArray());
        }
        finally
        {
            if (!completed && saveKind == AuthoringSaveKind.Autosave)
            {
                foreach (var key in _autosaveTargets.Keys.Except(previousAutosaveKeys).ToArray())
                {
                    var reservedTarget = _autosaveTargets[key].Target;
                    TryDeleteReservedFile(reservedTarget.PageFilePath);
                    TryDeleteReservedFile(reservedTarget.ScenarioFilePath);
                    _autosaveTargets.Remove(key);
                }
            }
        }
    }

    private async Task<RecorderSaveResult> GenerateAndSaveAsync(
        Window window,
        AppAutomationRecorderOptions options,
        IReadOnlyList<RecordedStep> steps,
        string? outputDirectoryOverride,
        AuthoringSaveKind saveKind,
        RecorderScenarioSaveContext? saveContext,
        List<string> diagnostics,
        CancellationToken cancellationToken)
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

        var target = ResolveTargetConfiguration(window, options, projectDirectory, outputDirectoryOverride, saveContext);
        var snapshot = _scanner.Scan(target, cancellationToken);
        var validationError = ValidateSnapshot(target, snapshot);
        if (validationError is not null)
        {
            return RecorderSaveResult.Failed(validationError);
        }

        var activeSteps = steps.Where(static step => !step.IsIgnored).ToArray();
        var activeGraphValidation = RecorderScenarioGraphValidator.Validate(activeSteps);
        if (!activeGraphValidation.Success)
        {
            return RecorderSaveResult.Failed(
                $"Recorder scenario graph is invalid: {activeGraphValidation.Error}");
        }

        var persistableSteps = activeSteps.Where(static step => step.CanPersist).ToArray();
        var skippedSteps = activeSteps.Where(static step => !step.CanPersist).ToArray();
        foreach (var skippedStep in skippedSteps)
        {
            diagnostics.Add(
                string.IsNullOrWhiteSpace(skippedStep.ValidationMessage)
                    ? $"Skipped invalid step '{skippedStep.ActionKind}'."
                    : skippedStep.ValidationMessage!);
        }

        if (persistableSteps.Length == 0)
        {
            return RecorderSaveResult.Failed(
                "Recorder has no valid steps to save.",
                diagnostics.ToArray());
        }

        var invalidSemanticStep = skippedSteps.FirstOrDefault(static step =>
            step.ActionKind is RecordedActionKind.CaptureCheckpoint
                or RecordedActionKind.CaptureCopiedValue
                or RecordedActionKind.AssertValue
            || step.GeneratedValueId is not null
            || step.ExpectedGeneratedValueId is not null
            || step.InputCopiedValueId is not null);
        if (invalidSemanticStep is not null)
        {
            return RecorderSaveResult.Failed(
                invalidSemanticStep.ValidationMessage
                ?? $"Recorder semantic step '{invalidSemanticStep.ActionKind}' is invalid and must be fixed or removed before save.",
                diagnostics.ToArray());
        }

        var graphValidation = RecorderScenarioGraphValidator.Validate(persistableSteps);
        if (!graphValidation.Success)
        {
            return RecorderSaveResult.Failed(
                $"Recorder scenario graph is invalid: {graphValidation.Error}",
                diagnostics.ToArray());
        }

        var controlUsageGroups = persistableSteps
            .SelectMany(GetControlUsages)
            .GroupBy(
                usage => AuthoringProjectScanner.CreateControlKey(
                    usage.Control.LocatorKind,
                    usage.Control.LocatorValue),
                StringComparer.Ordinal)
            .ToArray();
        var reservedPropertyNames = new HashSet<string>(snapshot.ExistingControlPropertyNames, StringComparer.Ordinal);
        var generatedControlsByKey = new Dictionary<string, ExistingControlInfo>(StringComparer.Ordinal);
        var generatedControls = new List<ExistingControlInfo>();
        var resolvedControlsByKey = new Dictionary<string, ExistingControlInfo>(StringComparer.Ordinal);
        var renderedStatements = new List<string>(persistableSteps.Length);

        foreach (var usageGroup in controlUsageGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var typedUsages = usageGroup.Where(static usage => !usage.IsTypeAgnostic).ToArray();
            var requiredTypes = typedUsages
                .Select(static usage => usage.Control.ControlType)
                .Distinct()
                .ToArray();
            if (requiredTypes.Length > 1)
            {
                var conflictingControl = usageGroup.First().Control;
                return RecorderSaveResult.Failed(
                    $"Recorded actions for locator '{conflictingControl.LocatorKind}:{conflictingControl.LocatorValue}' require incompatible control types: {string.Join(", ", requiredTypes.Select(static type => $"UiControlType.{type}"))}. Recorder will not generate duplicate control properties.",
                    diagnostics.ToArray());
            }

            var usage = typedUsages.Length > 0 ? typedUsages[0] : usageGroup.First();
            var control = usage.Control;
            var key = usageGroup.Key;
            var typedKey = AuthoringProjectScanner.CreateTypedControlKey(
                control.LocatorKind,
                control.LocatorValue,
                control.ControlType);
            var hasExistingControl = snapshot.ExistingControlsByKey.TryGetValue(key, out var existingControl);
            ExistingControlInfo? controlInfo = null;
            if (typedUsages.Length == 0 && hasExistingControl)
            {
                controlInfo = existingControl;
            }
            else if (snapshot.ExistingControlsByTypedKey.TryGetValue(typedKey, out var existingTypedControl))
            {
                controlInfo = existingTypedControl;
            }
            else if (hasExistingControl)
            {
                return RecorderSaveResult.Failed(
                    CreateIncompatibleControlMessage(control, usage.Requirement, existingControl!),
                    diagnostics.ToArray());
            }
            else if (generatedControlsByKey.TryGetValue(key, out var generatedControl))
            {
                if (typedUsages.Length > 0 && generatedControl.ControlType != control.ControlType)
                {
                    return RecorderSaveResult.Failed(
                        CreateIncompatibleControlMessage(control, usage.Requirement, generatedControl),
                        diagnostics.ToArray());
                }

                controlInfo = generatedControl;
            }
            else
            {
                var propertyName = RecorderNaming.EnsureUniqueName(control.ProposedPropertyName, reservedPropertyNames);
                controlInfo = new ExistingControlInfo(
                    propertyName,
                    control.ControlType,
                    control.LocatorValue,
                    control.LocatorKind,
                    control.FallbackToName);
                generatedControlsByKey.Add(key, controlInfo);
                generatedControls.Add(controlInfo);

                if (!string.Equals(propertyName, control.ProposedPropertyName, StringComparison.Ordinal))
                {
                    diagnostics.Add(
                        $"Control '{control.LocatorValue}' was renamed from '{control.ProposedPropertyName}' to '{propertyName}' to avoid a collision.");
                }
            }

            resolvedControlsByKey.Add(key, controlInfo!);
            foreach (var warning in usageGroup
                         .Select(static candidate => candidate.Control.Warning)
                         .Where(static warning => !string.IsNullOrWhiteSpace(warning))
                         .Distinct(StringComparer.Ordinal))
            {
                diagnostics.Add(warning!);
            }
        }

        var propertyNamesByControlKey = resolvedControlsByKey.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.PropertyName,
            StringComparer.Ordinal);
        var gridRowAnchorNames = RecorderStatementEmitter.CreateGridRowAnchorNames(persistableSteps, graphValidation);
        var spinnerKeyVariables = RecorderStatementEmitter.CreateSpinnerKeyVariables(graphValidation, gridRowAnchorNames);
        var numericTypes = RecorderStatementEmitter.CreateNumericTypes(persistableSteps);
        foreach (var step in persistableSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = AuthoringProjectScanner.CreateControlKey(
                step.Control.LocatorKind,
                step.Control.LocatorValue);
            var controlInfo = resolvedControlsByKey[key];

            renderedStatements.Add(RecorderStatementEmitter.GenerateStepStatement(
                step,
                controlInfo!.PropertyName,
                graphValidation.CheckpointVariables,
                graphValidation.GeneratedValueVariables,
                graphValidation.CopiedValueVariables,
                graphValidation.GeneratedValueSeriesVariable,
                gridRowAnchorNames,
                propertyNamesByControlKey,
                spinnerKeyVariables,
                numericTypes));
        }

        if (graphValidation.GeneratedValueSeriesVariable is { } generatedValueSeriesVariable)
        {
            renderedStatements.Insert(
                0,
                $"var {generatedValueSeriesVariable} = RecordedValueGenerator.Start();");
        }

        var containsAssertions = persistableSteps.Any(
            static step => step.ActionKind == RecordedActionKind.AssertValue);
        var requiresAsync = containsAssertions || persistableSteps.Any(
            static step => step.ActionKind == RecordedActionKind.CaptureCopiedValue);
        var containsRelativeDates = persistableSteps.Any(RecorderStatementEmitter.HasRelativeDateExpression);

        Directory.CreateDirectory(target.OutputDirectory);
        var fileSafeScenarioName = RecorderNaming.CreateFileSafeName(target.ScenarioName, "scenario");
        var saveTarget = saveKind == AuthoringSaveKind.Autosave
            ? GetOrCreateAutosaveTarget(
                target,
                snapshot,
                fileSafeScenarioName,
                saveContext?.DraftIdentity,
                reservePageFile: saveContext is not null && generatedControls.Count > 0)
            : CreateSnapshotTarget(target, snapshot);
        IReadOnlyList<ExistingControlInfo> controlsToWrite;
        if (generatedControls.Count == 0)
        {
            controlsToWrite = Array.Empty<ExistingControlInfo>();
        }
        else if (!TryMergeControlsFile(
                     saveTarget.PageFilePath,
                     generatedControls,
                     cancellationToken,
                     out controlsToWrite,
                     out var controlsMergeError))
        {
            return RecorderSaveResult.Failed(controlsMergeError!, diagnostics.ToArray());
        }
        var pageFilePath = generatedControls.Count == 0
            ? null
            : saveTarget.PageFilePath;
        var scenarioFilePath = saveTarget.ScenarioFilePath;
        var autosaveDestinationId = saveKind == AuthoringSaveKind.Autosave
            ? CreateAutosaveDestinationId(target)
            : null;
        var autosaveStateMarker = saveKind == AuthoringSaveKind.Autosave
            ? RecorderAutosaveStateSerializer.CreateMarker(
                new RecorderAutosaveState(
                    target.ScenarioName,
                    saveContext?.DraftIdentity ?? string.Empty,
                    saveTarget.MethodName,
                    DateTimeOffset.UtcNow,
                    persistableSteps))
            : null;

        var pageSource = pageFilePath is null
            ? null
            : RecorderStatementEmitter.GeneratePagePartial(target, snapshot.PageClass!, controlsToWrite, autosaveDestinationId);
        string scenarioSource;
        if (saveKind == AuthoringSaveKind.Autosave || !File.Exists(scenarioFilePath))
        {
            scenarioSource = RecorderStatementEmitter.GenerateScenarioPartial(
                target,
                snapshot.ScenarioClass!,
                saveTarget.MethodName,
                renderedStatements,
                containsAssertions,
                requiresAsync,
                containsRelativeDates,
                isAutosave: saveKind == AuthoringSaveKind.Autosave,
                autosaveDestinationId: autosaveDestinationId,
                autosaveStateMarker: autosaveStateMarker);
        }
        else if (!TryMergeScenarioPartial(
                     scenarioFilePath,
                     target,
                     snapshot.ScenarioClass!,
                     saveTarget.MethodName,
                     renderedStatements,
                     containsAssertions,
                     requiresAsync,
                     containsRelativeDates,
                     cancellationToken,
                     out var mergedScenarioSource,
                     out var scenarioMergeError))
        {
            return RecorderSaveResult.Failed(scenarioMergeError!, diagnostics.ToArray());
        }
        else
        {
            scenarioSource = mergedScenarioSource!;
        }

        IReadOnlyList<(string Path, string Source)> sources = pageFilePath is not null
            ? [(pageFilePath, pageSource!), (scenarioFilePath, scenarioSource)]
            : [(scenarioFilePath, scenarioSource)];
        await RecorderGeneratedFileTransaction.WriteAsync(sources, diagnostics, cancellationToken);

        if (saveKind == AuthoringSaveKind.Snapshot)
        {
            CleanupAutosaveTargets(target, diagnostics);
        }

        _logger?.LogInformation(
            "Recorder saved scenario '{ScenarioName}' to '{ScenarioFilePath}' with {GeneratedControlCount} new controls.",
            target.ScenarioName,
            scenarioFilePath,
            generatedControls.Count);

        var saveVerb = saveKind == AuthoringSaveKind.Autosave ? "autosaved" : "saved";
        var message = skippedSteps.Length == 0
            ? $"Recorded scenario '{target.ScenarioName}' was {saveVerb}."
            : $"Recorded scenario '{target.ScenarioName}' was {saveVerb}. {skippedSteps.Length} invalid step(s) were skipped.";

        return RecorderSaveResult.Completed(
            message,
            pageFilePath,
            scenarioFilePath,
            persistedStepCount: persistableSteps.Length,
            skippedStepCount: skippedSteps.Length,
            diagnostics);
    }

    private static bool IsTypeAgnosticControlAction(RecordedActionKind actionKind)
    {
        return actionKind is RecordedActionKind.WaitUntilExists
            or RecordedActionKind.InvokeContextMenuItem;
    }

    private static IEnumerable<RecorderControlUsage> GetControlUsages(RecordedStep step)
    {
        yield return new RecorderControlUsage(
            step.Control,
            IsTypeAgnosticControlAction(step.ActionKind),
            step.ActionKind.ToString());

        if (step.NumericExpectedExpression is not { } expression)
        {
            yield break;
        }

        foreach (var operand in expression.Operands())
        {
            if (operand.Kind == RecorderNumericOperandKind.Control
                && operand.Control is { } control)
            {
                yield return new RecorderControlUsage(
                    control,
                    IsTypeAgnostic: false,
                    "calculated numeric assertion operand");
            }
        }
    }

    private static string CreateIncompatibleControlMessage(
        RecordedControlDescriptor control,
        string requirement,
        ExistingControlInfo existingControl)
    {
        return $"Control '{existingControl.PropertyName}' for locator '{control.LocatorKind}:{control.LocatorValue}' is declared as UiControlType.{existingControl.ControlType}, which is incompatible with {requirement} requiring UiControlType.{control.ControlType}. Recorder will not generate a duplicate control property.";
    }

    private readonly record struct RecorderControlUsage(
        RecordedControlDescriptor Control,
        bool IsTypeAgnostic,
        string Requirement);

    internal RecorderOutputDescription DescribeOutput(
        Window window,
        AppAutomationRecorderOptions options,
        string? outputDirectoryOverride,
        RecorderScenarioSaveContext? saveContext = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.AuthoringProjectDirectory))
        {
            return new RecorderOutputDescription(
                IsConfigured: false,
                ScenarioFilePathDisplay: "Authoring project directory is not configured.",
                OutputDirectory: null,
                PageFilePathDisplay: null);
        }

        var projectDirectory = Path.GetFullPath(options.AuthoringProjectDirectory);
        var target = ResolveTargetConfiguration(window, options, projectDirectory, outputDirectoryOverride, saveContext);
        var scenarioFilePathDisplay = Path.Combine(
            target.OutputDirectory,
            $"{target.ScenarioClassName}.RecorderScenarios.g.cs");
        var pageFilePathDisplay = $"{target.PageClassName}.RecorderControls.g.cs (beside Page source)";

        return new RecorderOutputDescription(
            IsConfigured: true,
            ScenarioFilePathDisplay: scenarioFilePathDisplay,
            OutputDirectory: target.OutputDirectory,
            PageFilePathDisplay: pageFilePathDisplay);
    }

    public string GeneratePreview(IReadOnlyList<RecordedStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            return string.Empty;
        }

        var graphValidation = RecorderScenarioGraphValidator.Validate(steps);
        if (!graphValidation.Success)
        {
            return $"// AppAutomation recorder graph error: {RecorderStatementEmitter.SanitizeCommentText(graphValidation.Error ?? "Unknown scenario graph error.")}";
        }

        var builder = new StringBuilder();
        var gridRowAnchorNames = RecorderStatementEmitter.CreateGridRowAnchorNames(steps, graphValidation);
        var spinnerKeyVariables = RecorderStatementEmitter.CreateSpinnerKeyVariables(graphValidation, gridRowAnchorNames);
        var numericTypes = RecorderStatementEmitter.CreateNumericTypes(steps);
        if (graphValidation.GeneratedValueSeriesVariable is { } generatedValueSeriesVariable)
        {
            builder.Append("var ")
                .Append(generatedValueSeriesVariable)
                .AppendLine(" = RecordedValueGenerator.Start();");
        }

        foreach (var step in steps)
        {
            builder.AppendLine(RecorderStatementEmitter.GenerateStepStatement(
                step,
                step.Control.ProposedPropertyName,
                graphValidation.CheckpointVariables,
                graphValidation.GeneratedValueVariables,
                graphValidation.CopiedValueVariables,
                graphValidation.GeneratedValueSeriesVariable,
                gridRowAnchorNames,
                spinnerKeyVariables: spinnerKeyVariables,
                numericTypes: numericTypes));
        }

        return builder.ToString().TrimEnd();
    }

    public string GeneratePreview(RecordedStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return GeneratePreview([step]);
    }

    internal string GeneratePreviewForStep(
        RecordedStep step,
        IReadOnlyList<RecordedStep> scenarioSteps)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(scenarioSteps);
        var graphValidation = RecorderScenarioGraphValidator.Validate(scenarioSteps);
        if (graphValidation.StepErrors.TryGetValue(step.StepId, out var error))
        {
            return $"// AppAutomation recorder graph error: {RecorderStatementEmitter.SanitizeCommentText(error)}";
        }

        var checkpointVariables = graphValidation.CheckpointVariables;
        if (step.ActionKind == RecordedActionKind.CaptureCheckpoint
            && step.CheckpointId is { } checkpointId
            && !checkpointVariables.ContainsKey(checkpointId))
        {
            var variables = new Dictionary<Guid, string>(checkpointVariables);
            var reservedNames = variables.Values.ToHashSet(StringComparer.Ordinal);
            variables[checkpointId] = RecorderNaming.CreateCheckpointVariableName(
                step.CheckpointVariableName,
                reservedNames,
                step.PreserveVariableName);
            checkpointVariables = variables;
        }

        var gridRowAnchorNames = RecorderStatementEmitter.CreateGridRowAnchorNames(
            scenarioSteps,
            graphValidation with { CheckpointVariables = checkpointVariables });
        var spinnerKeyVariables = RecorderStatementEmitter.CreateSpinnerKeyVariables(
            graphValidation with { CheckpointVariables = checkpointVariables },
            gridRowAnchorNames);
        RecorderStatementEmitter.PrimeSpinnerKeyVariables(spinnerKeyVariables, scenarioSteps, step.StepId);
        var statement = RecorderStatementEmitter.GenerateStepStatement(
            step,
            step.Control.ProposedPropertyName,
            checkpointVariables,
            graphValidation.GeneratedValueVariables,
            graphValidation.CopiedValueVariables,
            graphValidation.GeneratedValueSeriesVariable,
            gridRowAnchorNames,
            spinnerKeyVariables: spinnerKeyVariables,
            numericTypes: RecorderStatementEmitter.CreateNumericTypes(scenarioSteps));
        return step.DefinesGeneratedValue && graphValidation.GeneratedValueSeriesVariable is { } seriesVariable
            ? $"var {seriesVariable} = RecordedValueGenerator.Start();{Environment.NewLine}{statement}"
            : statement;
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

        if (target.ScenarioGenericArity is not null
            && (snapshot.ScenarioClass.GenericArity != target.ScenarioGenericArity
                || !string.Equals(
                    snapshot.ScenarioClass.TypeParameterSignature,
                    target.ScenarioTypeParameterSignature,
                    StringComparison.Ordinal)))
        {
            return $"Scenario class '{target.ScenarioNamespace}.{target.ScenarioClassName}' no longer matches the selected generic declaration.";
        }

        return null;
    }

    private static AuthoringSaveTarget CreateSnapshotTarget(
        AuthoringTargetConfiguration target,
        AuthoringProjectSnapshot snapshot)
    {
        var reservedMethodNames = new HashSet<string>(snapshot.ExistingScenarioMethodNames, StringComparer.Ordinal);
        var timestamp = DateTimeOffset.Now;
        var methodName = RecorderNaming.EnsureUniqueName(
            RecorderNaming.CreateRecordedMethodBaseName(target.ScenarioName, timestamp),
            reservedMethodNames);
        var pageDirectory = Path.GetDirectoryName(snapshot.PageClass!.SourceFilePath)
            ?? throw new InvalidOperationException(
                $"Page source path '{snapshot.PageClass.SourceFilePath}' has no parent directory.");
        var pageFilePath = Path.Combine(pageDirectory, $"{target.PageClassName}.RecorderControls.g.cs");
        var scenarioFilePath = Path.Combine(
            target.OutputDirectory,
            $"{target.ScenarioClassName}.RecorderScenarios.g.cs");
        return new AuthoringSaveTarget(pageFilePath, scenarioFilePath, methodName);
    }

    private AuthoringSaveTarget GetOrCreateAutosaveTarget(
        AuthoringTargetConfiguration target,
        AuthoringProjectSnapshot snapshot,
        string fileSafeScenarioName,
        string? draftIdentity,
        bool reservePageFile)
    {
        var key = CreateAutosaveTargetKey(target, draftIdentity);
        if (_autosaveTargets.TryGetValue(key, out var existingRegistration))
        {
            return existingRegistration.Target;
        }

        var reservedMethodNames = new HashSet<string>(snapshot.ExistingScenarioMethodNames, StringComparer.Ordinal);
        var timestamp = DateTimeOffset.Now;
        var timestampText = timestamp.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var methodName = RecorderNaming.EnsureUniqueName(
            RecorderNaming.CreateAutosaveMethodBaseName(target.ScenarioName, timestamp),
            reservedMethodNames);
        for (var suffix = 1; ; suffix++)
        {
            var collisionSuffix = suffix == 1 ? string.Empty : $"_{suffix}";
            var pageFilePath = Path.Combine(
                target.OutputDirectory,
                $"{target.PageClassName}.{fileSafeScenarioName}.autosave.{timestampText}{collisionSuffix}.controls.g.cs.autosave");
            var scenarioFilePath = Path.Combine(
                target.OutputDirectory,
                $"{target.ScenarioClassName}.{fileSafeScenarioName}.autosave.{timestampText}{collisionSuffix}.g.cs.autosave");
            if (File.Exists(pageFilePath) || File.Exists(scenarioFilePath))
            {
                continue;
            }

            if (!TryReserveFile(scenarioFilePath))
            {
                continue;
            }

            try
            {
                if (reservePageFile && !TryReserveFile(pageFilePath))
                {
                    TryDeleteReservedFile(scenarioFilePath);
                    continue;
                }
            }
            catch
            {
                TryDeleteReservedFile(scenarioFilePath);
                throw;
            }

            var collisionSafeMethodName = suffix == 1
                ? methodName
                : RecorderNaming.EnsureUniqueName($"{methodName}_{suffix}", reservedMethodNames);
            var autosaveTarget = new AuthoringSaveTarget(pageFilePath, scenarioFilePath, collisionSafeMethodName);
            _autosaveTargets.Add(
                key,
                new AutosaveTargetRegistration(
                    CreateAutosaveDestinationKey(target),
                    autosaveTarget));
            return autosaveTarget;
        }
    }

    private bool TryMergeControlsFile(
        string pageFilePath,
        IReadOnlyList<ExistingControlInfo> generatedControls,
        CancellationToken cancellationToken,
        out IReadOnlyList<ExistingControlInfo> mergedControls,
        out string? error)
    {
        if (File.Exists(pageFilePath))
        {
            string existingSource;
            try
            {
                existingSource = File.ReadAllText(pageFilePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                mergedControls = Array.Empty<ExistingControlInfo>();
                error = $"Recorder controls file '{pageFilePath}' could not be read and was not changed: {exception.Message}";
                return false;
            }

            if (existingSource.Length > 0)
            {
                if (!existingSource.Contains(RecorderStatementEmitter.RecorderGeneratedMarker, StringComparison.Ordinal))
                {
                    mergedControls = Array.Empty<ExistingControlInfo>();
                    error = $"Recorder controls file '{pageFilePath}' is not owned by AppAutomation Recorder and was not changed.";
                    return false;
                }

                var syntaxTree = CSharpSyntaxTree.ParseText(existingSource, cancellationToken: cancellationToken);
                var syntaxError = syntaxTree.GetDiagnostics(cancellationToken)
                    .FirstOrDefault(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
                if (syntaxError is not null)
                {
                    mergedControls = Array.Empty<ExistingControlInfo>();
                    error = $"Recorder controls file '{pageFilePath}' contains invalid C# and was not changed: {syntaxError.GetMessage(CultureInfo.InvariantCulture)}";
                    return false;
                }
            }
        }

        var mergeByLocator = pageFilePath.EndsWith(".autosave", StringComparison.OrdinalIgnoreCase);
        var controlsByKey = new Dictionary<string, ExistingControlInfo>(StringComparer.Ordinal);
        foreach (var control in _scanner.ScanControlsFile(pageFilePath, cancellationToken))
        {
            controlsByKey.TryAdd(
                CreateControlsMergeKey(control, mergeByLocator),
                control);
        }

        foreach (var control in generatedControls)
        {
            controlsByKey[CreateControlsMergeKey(control, mergeByLocator)] = control;
        }

        mergedControls = controlsByKey.Values.ToArray();
        error = null;
        return true;
    }

    private static string CreateControlsMergeKey(ExistingControlInfo control, bool mergeByLocator)
        => mergeByLocator
            ? AuthoringProjectScanner.CreateControlKey(control.LocatorKind, control.LocatorValue)
            : AuthoringProjectScanner.CreateTypedControlKey(
                control.LocatorKind,
                control.LocatorValue,
                control.ControlType);

    internal static bool TryReserveFile(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return true;
        }
        catch (IOException) when (File.Exists(filePath))
        {
            return false;
        }
    }

    private static void TryDeleteReservedFile(string filePath)
    {
        try
        {
            var file = new FileInfo(filePath);
            if (file.Exists && file.Length == 0)
            {
                file.Delete();
            }
        }
        catch (IOException)
        {
            // Best effort cleanup of a zero-length reservation created by this save attempt.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of a zero-length reservation created by this save attempt.
        }
    }

    private static string CreateAutosaveTargetKey(AuthoringTargetConfiguration target, string? draftIdentity)
    {
        return string.Join(
            "\n",
            CreateAutosaveDestinationKey(target),
            target.ScenarioName,
            draftIdentity ?? string.Empty,
            target.OutputDirectory);
    }

    private static string CreateAutosaveDestinationKey(AuthoringTargetConfiguration target)
    {
        return string.Join(
            "\n",
            target.ProjectDirectory,
            target.AutosaveDirectory,
            target.PageNamespace,
            target.PageClassName,
            target.ScenarioNamespace,
            target.ScenarioClassName,
            target.ScenarioGenericArity?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            target.ScenarioTypeParameterSignature ?? string.Empty);
    }

    private static string CreateAutosaveDestinationId(AuthoringTargetConfiguration target)
    {
        var destinationKey = CreateAutosaveDestinationKey(target);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(destinationKey)));
    }

    private void CleanupAutosaveTargets(
        AuthoringTargetConfiguration target,
        ICollection<string> diagnostics)
    {
        var destinationKey = CreateAutosaveDestinationKey(target);
        var attemptedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchingKeys = _autosaveTargets
            .Where(pair => string.Equals(pair.Value.DestinationKey, destinationKey, StringComparison.Ordinal))
            .Select(static pair => pair.Key)
            .ToArray();

        foreach (var key in matchingKeys)
        {
            var targetToDelete = _autosaveTargets[key].Target;
            attemptedPaths.Add(targetToDelete.PageFilePath);
            attemptedPaths.Add(targetToDelete.ScenarioFilePath);
            var pageDeleted = TryDeleteAutosaveFile(targetToDelete.PageFilePath, diagnostics);
            var scenarioDeleted = TryDeleteAutosaveFile(targetToDelete.ScenarioFilePath, diagnostics);
            if (pageDeleted && scenarioDeleted)
            {
                _autosaveTargets.Remove(key);
            }
        }

        foreach (var filePath in FindAutosaveFilesForDestination(target, diagnostics))
        {
            if (attemptedPaths.Add(filePath))
            {
                TryDeleteAutosaveFile(filePath, diagnostics);
            }
        }
    }

    private static List<string> FindAutosaveFilesForDestination(
        AuthoringTargetConfiguration target,
        ICollection<string> diagnostics)
    {
        var destinationId = CreateAutosaveDestinationId(target);
        var files = new List<string>();

        foreach (var directory in new[] { target.AutosaveDirectory, target.OutputDirectory }
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(directory, "*.autosave", SearchOption.TopDirectoryOnly))
                {
                    if (string.Equals(
                            ReadAutosaveDestinationId(filePath, diagnostics),
                            destinationId,
                            StringComparison.Ordinal))
                    {
                        files.Add(filePath);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(
                    $"Recorder saved the final scenario but could not inspect autosave recovery directory '{directory}': {exception.Message}");
            }
        }

        // Drafts produced before destination markers were introduced are removed once by the legacy name convention.
        files.AddRange(FindLegacyAutosaveFilesForDestination(target, diagnostics));
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? ReadAutosaveDestinationId(
        string filePath,
        ICollection<string> diagnostics)
    {
        try
        {
            var markerLine = File.ReadLines(filePath)
                .Take(4)
                .FirstOrDefault(line => line.StartsWith(
                    RecorderStatementEmitter.AutosaveDestinationMarkerPrefix,
                    StringComparison.Ordinal));
            return markerLine?[RecorderStatementEmitter.AutosaveDestinationMarkerPrefix.Length..];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(
                $"Recorder saved the final scenario but could not inspect autosave recovery file '{filePath}': {exception.Message}");
            return null;
        }
    }

    private static List<string> FindLegacyAutosaveFilesForDestination(
        AuthoringTargetConfiguration target,
        ICollection<string> diagnostics)
    {
        var destinationScenarioPrefix = $"{target.ScenarioClassName}.";
        var files = new List<string>();

        foreach (var directory in new[] { target.AutosaveDirectory, target.OutputDirectory }
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                var autosaveFiles = Directory
                    .EnumerateFiles(directory, "*.autosave", SearchOption.TopDirectoryOnly)
                    .ToArray();
                foreach (var scenarioFilePath in autosaveFiles.Where(filePath =>
                             ReadAutosaveDestinationId(filePath, diagnostics) is null
                             && IsDestinationScenarioAutosave(Path.GetFileName(filePath), destinationScenarioPrefix)))
                {
                    files.Add(scenarioFilePath);
                    var pairedPageFilePath = GetPairedPageAutosavePath(
                        target.PageClassName,
                        scenarioFilePath,
                        destinationScenarioPrefix);
                    if (pairedPageFilePath is not null && File.Exists(pairedPageFilePath))
                    {
                        files.Add(pairedPageFilePath);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(
                    $"Recorder saved the final scenario but could not inspect autosave recovery directory '{directory}': {exception.Message}");
            }
        }

        return files;
    }

    private static bool IsDestinationScenarioAutosave(
        string fileName,
        string scenarioPrefix)
    {
        return fileName.StartsWith(scenarioPrefix, StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".g.cs.autosave", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetPairedPageAutosavePath(
        string pageClassName,
        string scenarioFilePath,
        string scenarioPrefix)
    {
        const string scenarioSuffix = ".g.cs.autosave";
        var scenarioFileName = Path.GetFileName(scenarioFilePath);
        if (!IsDestinationScenarioAutosave(scenarioFileName, scenarioPrefix))
        {
            return null;
        }

        var recoveryIdentityLength = scenarioFileName.Length - scenarioPrefix.Length - scenarioSuffix.Length;
        if (recoveryIdentityLength <= 0)
        {
            return null;
        }

        var recoveryIdentity = scenarioFileName.Substring(scenarioPrefix.Length, recoveryIdentityLength);
        var directory = Path.GetDirectoryName(scenarioFilePath);
        return directory is null
            ? null
            : Path.Combine(directory, $"{pageClassName}.{recoveryIdentity}.controls.g.cs.autosave");
    }

    private static bool TryDeleteAutosaveFile(string filePath, ICollection<string> diagnostics)
    {
        try
        {
            File.Delete(filePath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add($"Recorder saved the final scenario but could not delete autosave recovery file '{filePath}': {exception.Message}");
            return false;
        }
    }

    private static AuthoringTargetConfiguration ResolveTargetConfiguration(
        Window window,
        AppAutomationRecorderOptions options,
        string projectDirectory,
        string? outputDirectoryOverride,
        RecorderScenarioSaveContext? saveContext)
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var appName = entryAssembly?.GetName().Name
            ?? window.GetType().Assembly.GetName().Name
            ?? "App";

        var pageClassName = options.PageClassName ?? $"{window.GetType().Name}Page";
        var destination = saveContext?.Destination;
        var scenarioClassName = destination?.ScenarioClassName
            ?? options.ScenarioClassName
            ?? $"{window.GetType().Name}ScenariosBase";
        var pageNamespace = options.PageNamespace ?? $"{appName}.UiTests.Authoring.Pages";
        var scenarioNamespace = destination?.ScenarioNamespace
            ?? options.ScenarioNamespace
            ?? $"{appName}.UiTests.Authoring.Tests";
        var outputSubdirectory = destination?.OutputSubdirectory ?? options.OutputSubdirectory;
        var autosaveDirectory = Path.IsPathRooted(outputSubdirectory)
            ? Path.GetFullPath(outputSubdirectory)
            : Path.Combine(projectDirectory, outputSubdirectory);
        var outputDirectory = string.IsNullOrWhiteSpace(outputDirectoryOverride)
            ? autosaveDirectory
            : Path.GetFullPath(outputDirectoryOverride);

        return new AuthoringTargetConfiguration(
            projectDirectory,
            outputDirectory,
            autosaveDirectory,
            pageNamespace,
            pageClassName,
            scenarioNamespace,
            scenarioClassName,
            destination?.GenericArity,
            destination?.TypeParameterSignature,
            saveContext?.ScenarioName ?? options.ScenarioName,
            appName);
    }


    private static bool TryMergeScenarioPartial(
        string scenarioFilePath,
        AuthoringTargetConfiguration target,
        ScannedClassInfo scenarioClass,
        string methodName,
        IReadOnlyList<string> renderedStatements,
        bool containsAssertions,
        bool requiresAsync,
        bool containsRelativeDates,
        CancellationToken cancellationToken,
        out string? mergedSource,
        out string? error)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string existingSource;
        try
        {
            existingSource = File.ReadAllText(scenarioFilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            mergedSource = null;
            error = $"Recorder scenario file '{scenarioFilePath}' could not be read and was not changed: {exception.Message}";
            return false;
        }

        if (!existingSource.Contains(RecorderStatementEmitter.RecorderGeneratedMarker, StringComparison.Ordinal))
        {
            mergedSource = null;
            error = $"Recorder scenario file '{scenarioFilePath}' is not owned by AppAutomation Recorder and was not changed.";
            return false;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(existingSource, cancellationToken: cancellationToken);
        var syntaxErrors = syntaxTree.GetDiagnostics(cancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (syntaxErrors.Length > 0)
        {
            mergedSource = null;
            error = $"Recorder scenario file '{scenarioFilePath}' contains invalid C# and was not changed: {syntaxErrors[0].GetMessage(CultureInfo.InvariantCulture)}";
            return false;
        }

        var root = syntaxTree.GetCompilationUnitRoot(cancellationToken);
        var matchingClasses = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(declaration => declaration.Ancestors().OfType<TypeDeclarationSyntax>().Any() == false
                && string.Equals(GetNamespaceName(declaration), target.ScenarioNamespace, StringComparison.Ordinal)
                && string.Equals(declaration.Identifier.ValueText, target.ScenarioClassName, StringComparison.Ordinal))
            .ToArray();
        if (matchingClasses.Length != 1)
        {
            mergedSource = null;
            error = $"Recorder scenario file '{scenarioFilePath}' does not contain exactly one expected class '{target.ScenarioNamespace}.{target.ScenarioClassName}' and was not changed.";
            return false;
        }

        var existingClass = matchingClasses[0];
        var genericArity = existingClass.TypeParameterList?.Parameters.Count ?? 0;
        var typeParameterSignature = CreateTypeParameterSignature(existingClass);
        if (!existingClass.Modifiers.Any(static token => token.IsKind(SyntaxKind.PartialKeyword))
            || genericArity != scenarioClass.GenericArity
            || !string.Equals(typeParameterSignature, scenarioClass.TypeParameterSignature, StringComparison.Ordinal)
            || existingClass.OpenBraceToken.IsMissing
            || existingClass.CloseBraceToken.IsMissing)
        {
            mergedSource = null;
            error = $"Recorder scenario file '{scenarioFilePath}' has an incompatible partial class declaration and was not changed.";
            return false;
        }

        var generatedSource = RecorderStatementEmitter.GenerateScenarioPartial(
            target,
            scenarioClass,
            methodName,
            renderedStatements,
            containsAssertions,
            requiresAsync,
            containsRelativeDates,
            isAutosave: false,
            autosaveDestinationId: null,
            autosaveStateMarker: null);
        var generatedMethod = CSharpSyntaxTree.ParseText(generatedSource)
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();
        var updatedClass = existingClass.AddMembers(generatedMethod);
        var updatedRoot = root.ReplaceNode(existingClass, updatedClass);
        if (requiresAsync && !HasUsingDirective(updatedRoot, "System.Threading.Tasks"))
        {
            updatedRoot = updatedRoot.AddUsings(
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Threading.Tasks")));
        }

        if (containsAssertions && !HasUsingDirective(updatedRoot, "TUnit.Assertions"))
        {
            updatedRoot = updatedRoot.AddUsings(
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("TUnit.Assertions")));
        }

        if (containsAssertions && !HasUsingDirective(updatedRoot, "TUnit.Assertions.Extensions"))
        {
            updatedRoot = updatedRoot.AddUsings(
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("TUnit.Assertions.Extensions")));
        }

        if (containsRelativeDates && !HasUsingDirective(updatedRoot, "System"))
        {
            updatedRoot = updatedRoot.AddUsings(
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System")));
        }
        mergedSource = updatedRoot
            .NormalizeWhitespace(indentation: "    ", eol: Environment.NewLine)
            .ToFullString() + Environment.NewLine;
        error = null;
        return true;
    }


    private static bool HasUsingDirective(CompilationUnitSyntax root, string namespaceName)
    {
        return root.Usings.Any(usingDirective =>
            usingDirective.Alias is null
            && usingDirective.StaticKeyword.IsKind(SyntaxKind.None)
            && string.Equals(usingDirective.Name?.ToString(), namespaceName, StringComparison.Ordinal));
    }


    private static string GetNamespaceName(SyntaxNode node)
    {
        return string.Join(
            ".",
            node.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(static declaration => declaration.Name.ToString()));
    }

    private static string CreateTypeParameterSignature(ClassDeclarationSyntax declaration)
    {
        return string.Join(
            ",",
            declaration.TypeParameterList?.Parameters
                .Select(static parameter => parameter.Identifier.ValueText)
                ?? Array.Empty<string>());
    }


}

internal enum AuthoringSaveKind
{
    Snapshot = 0,
    Autosave = 1
}

internal sealed record AuthoringSaveTarget(
    string PageFilePath,
    string ScenarioFilePath,
    string MethodName);

internal sealed record AutosaveTargetRegistration(
    string DestinationKey,
    AuthoringSaveTarget Target);
