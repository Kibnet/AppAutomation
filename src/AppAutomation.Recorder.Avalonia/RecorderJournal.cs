namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderJournal
{
    private const int StepMoveHistoryLimit = 100;
    private readonly List<RecordedStep> _steps = new();
    private readonly List<Guid[]> _moveHistory = new();
    private readonly IReadOnlyList<RecordedStep> _view;
    private RecorderJournalContext? _context;

    internal RecorderJournal() => _view = _steps.AsReadOnly();

    internal IReadOnlyList<RecordedStep> Steps => _view;
    internal long ContentRevision { get; private set; }
    internal int EditRevision { get; private set; }
    internal bool CanUndoMove => _moveHistory.Count > 0;
    internal RecorderJournalContext Context => _context ??= CreateJournalContext();

    internal int IndexOf(Guid stepId) => _steps.FindIndex(step => step.StepId == stepId);

    internal void Add(RecordedStep step)
    {
        _steps.Add(step);
        ContentChanged();
    }

    internal void RemoveAt(int index)
    {
        _steps.RemoveAt(index);
        ContentChanged();
    }

    internal void Replace(int index, RecordedStep step)
    {
        if (_steps[index] == step) return;
        _steps[index] = step;
        ContentChanged();
    }

    internal RecorderScenarioGraphValidationResult RevalidateStep(int index, RecordedStep step)
    {
        var persistability = _steps.Select(static item => item.CanPersist).ToArray();
        _steps[index] = step;
        return ValidateGraph(persistability);
    }

    internal void ReplaceAll(IEnumerable<RecordedStep> steps)
    {
        var replacement = steps.ToArray();
        if (_steps.SequenceEqual(replacement)) return;
        _steps.Clear();
        _steps.AddRange(replacement);
        ContentChanged();
    }

    internal void Clear()
    {
        if (_steps.Count == 0) return;
        _steps.Clear();
        ContentChanged();
    }

    internal void ClearMoveHistory() => _moveHistory.Clear();

    internal bool CanMove(int sourceIndex, int targetIndex) =>
        sourceIndex >= 0 && sourceIndex < _steps.Count
        && targetIndex >= 0 && targetIndex < _steps.Count && sourceIndex != targetIndex;

    internal RecordedStep? Move(int sourceIndex, int targetIndex, bool rememberMove)
    {
        if (!CanMove(sourceIndex, targetIndex)) return null;
        if (rememberMove)
        {
            if (_moveHistory.Count == StepMoveHistoryLimit) _moveHistory.RemoveAt(0);
            _moveHistory.Add(_steps.Select(static step => step.StepId).ToArray());
        }
        else _moveHistory.Clear();
        var step = _steps[sourceIndex];
        _steps.RemoveAt(sourceIndex);
        _steps.Insert(targetIndex, step);
        ContentChanged();
        return step;
    }

    internal bool UndoMove()
    {
        if (!CanUndoMove) return false;
        var previousOrder = _moveHistory[^1];
        var currentSteps = _steps.ToDictionary(static step => step.StepId);
        if (previousOrder.Any(id => !currentSteps.ContainsKey(id)))
        {
            _moveHistory.Clear();
            return false;
        }
        _moveHistory.RemoveAt(_moveHistory.Count - 1);
        var restored = new List<RecordedStep>(_steps.Count);
        foreach (var id in previousOrder)
        {
            restored.Add(currentSteps[id]);
            currentSteps.Remove(id);
        }
        // Newly recorded steps keep their relative order after the saved move is undone.
        restored.AddRange(_steps.Where(step => currentSteps.ContainsKey(step.StepId)));
        ReplaceAll(restored);
        return true;
    }

    private void InvalidateContext()
    {
        EditRevision++;
        _context = null;
    }

    private void ContentChanged()
    {
        ContentRevision++;
        InvalidateContext();
    }

    private (RecordedStep[] Preview, RecordedStep[] Graph) ReadGraphInput()
    {
        var preview = _steps.Where(static step => !step.IsIgnored).ToArray();
        return (preview, preview.Where(static step => step.CanPersist).ToArray());
    }

    private RecorderJournalContext CreateJournalContext()
    {
        var (preview, graph) = ReadGraphInput();
        return CreateJournalContext(preview, graph, RecorderScenarioGraphValidator.Validate(graph));
    }

    private static RecorderJournalContext CreateJournalContext(
        RecordedStep[] previewSteps, RecordedStep[] graphSteps, RecorderScenarioGraphValidationResult graphValidation)
    {
        if (!graphValidation.Success)
        {
            graphSteps = graphSteps.Where(step => !graphValidation.StepErrors.ContainsKey(step.StepId)).ToArray();
        }

        var checkpoints = graphSteps
            .Where(static step => step.ActionKind == RecordedActionKind.CaptureCheckpoint
                && step.CheckpointId is not null
                && step.ValueKind is not null)
            .Select(step => new RecorderCheckpointOption(
                step.CheckpointId!.Value,
                graphValidation.CheckpointVariables.TryGetValue(step.CheckpointId.Value, out var variableName)
                    ? variableName
                    : step.CheckpointVariableName ?? "checkpointValue",
                step.ValueKind!.Value,
                step.Control.ProposedPropertyName))
            .ToArray();
        var generatedValues = graphSteps
            .Where(static step => step.ActionKind == RecordedActionKind.EnterText
                && step.DefinesGeneratedValue
                && step.GeneratedValueId is not null
                && step.GeneratedValueOrdinal is > 0)
            .Select(step => new RecorderGeneratedValueOption(
                step.GeneratedValueId!.Value,
                graphValidation.GeneratedValueVariables.TryGetValue(step.GeneratedValueId.Value, out var variableName)
                    ? variableName
                    : step.GeneratedValueVariableName ?? $"generatedValue{step.GeneratedValueOrdinal}",
                step.GeneratedValueOrdinal!.Value,
                step.StringValue ?? string.Empty, step.GeneratedValueKind))
            .ToArray();
        var copiedValues = graphSteps
            .Where(static step => step.ActionKind == RecordedActionKind.CaptureCopiedValue
                && step.CopiedValueId is not null
                && step.ValueKind is RecorderValueKind.Text or RecorderValueKind.GridCellText)
            .Select(step => new RecorderCopiedValueOption(
                step.CopiedValueId!.Value,
                graphValidation.CopiedValueVariables.TryGetValue(step.CopiedValueId.Value, out var variableName)
                    ? variableName
                    : step.CopiedValueVariableName ?? "copiedValue",
                step.ValueKind!.Value,
                step.Control.ProposedPropertyName,
                step.StringValue ?? string.Empty))
            .ToArray();

        return new RecorderJournalContext(
            previewSteps,
            graphValidation,
            checkpoints,
            generatedValues,
            copiedValues,
            checkpoints.ToDictionary(static option => option.CheckpointId),
            generatedValues.ToDictionary(static option => option.GeneratedValueId),
            copiedValues.ToDictionary(static option => option.CopiedValueId));
    }

    internal RecorderScenarioGraphValidationResult ValidateGraph() =>
        ValidateGraph(_steps.Select(static step => step.CanPersist).ToArray());

    private RecorderScenarioGraphValidationResult ValidateGraph(bool[] persistability)
    {
        InvalidateContext();
        for (var index = 0; index < _steps.Count; index++)
        {
            var step = _steps[index];
            if (step.IsIgnored || step.ValidationBeforeGraphError is null)
            {
                continue;
            }

            _steps[index] = RestoreValidationBeforeGraphError(step);
        }

        var (preview, graph) = ReadGraphInput();
        var validation = RecorderScenarioGraphValidator.Validate(graph);
        if (!validation.Success)
        {
            foreach (var entry in validation.StepErrors)
            {
                var index = IndexOf(entry.Key);
                if (index < 0) continue;

                var step = _steps[index];
                _steps[index] = step with
                {
                    ValidationStatus = RecorderValidationStatus.Invalid,
                    ValidationMessage = entry.Value,
                    CanPersist = false,
                    ReviewState = RecorderStepReviewState.NeedsReview,
                    FailureCode = "checkpoint-graph-invalid",
                    ValidationBeforeGraphError = step.ValidationBeforeGraphError ?? new RecorderStepValidationState(
                        step.ValidationStatus, step.ValidationMessage, step.CanPersist, step.ReviewState, step.FailureCode)
                };
            }
            (preview, graph) = ReadGraphInput();
        }

        _context = CreateJournalContext(preview, graph, validation);
        if (!persistability.SequenceEqual(_steps.Select(static step => step.CanPersist))) ContentRevision++;
        return validation;
    }

    internal static RecordedStep RestoreValidationBeforeGraphError(RecordedStep step)
    {
        if (step.ValidationBeforeGraphError is not { } validation)
        {
            return step;
        }

        return step with
        {
            ValidationStatus = validation.ValidationStatus,
            ValidationMessage = validation.ValidationMessage,
            CanPersist = validation.CanPersist,
            ReviewState = validation.ReviewState,
            FailureCode = validation.FailureCode,
            ValidationBeforeGraphError = null
        };
    }
}

internal sealed record RecorderJournalContext(
    RecordedStep[] PreviewSteps,
    RecorderScenarioGraphValidationResult GraphValidation,
    RecorderCheckpointOption[] Checkpoints,
    RecorderGeneratedValueOption[] GeneratedValues,
    RecorderCopiedValueOption[] CopiedValues,
    IReadOnlyDictionary<Guid, RecorderCheckpointOption> CheckpointsById,
    IReadOnlyDictionary<Guid, RecorderGeneratedValueOption> GeneratedValuesById,
    IReadOnlyDictionary<Guid, RecorderCopiedValueOption> CopiedValuesById);
