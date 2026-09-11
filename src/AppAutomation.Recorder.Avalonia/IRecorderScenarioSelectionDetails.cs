namespace AppAutomation.Recorder.Avalonia;

internal interface IRecorderScenarioSelectionDetails
{
    bool IsScenarioSelectionEnabled { get; }

    bool IsScanning { get; }

    string? ScenarioSelectionError { get; }

    IReadOnlyList<RecordedScenarioDestination> ScenarioDestinations { get; }

    RecordedScenarioDestination? SelectedScenarioDestination { get; }

    string ScenarioName { get; }

    bool CanStartRecording { get; }

    bool CanChangeScenarioTarget { get; }

    bool CanRestoreAutosave { get; }

    bool TrySelectScenarioDestination(RecordedScenarioDestination? destination);

    bool TrySetScenarioName(string? scenarioName);

    Task<bool> RestoreAutosaveAsync(CancellationToken cancellationToken = default);
}
