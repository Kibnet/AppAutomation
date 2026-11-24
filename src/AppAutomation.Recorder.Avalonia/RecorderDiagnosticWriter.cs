using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AppAutomation.Recorder.Avalonia;

internal sealed class RecorderDiagnosticWriter
{
    private static readonly TimeSpan WriteLockTimeout = TimeSpan.FromSeconds(1);

    private readonly ILogger _logger;
    private readonly string _scenarioName;
    private readonly string _configuredFilePath;
    private readonly Lazy<(string FilePath, string MutexName)> _target;

    internal RecorderDiagnosticWriter(AppAutomationRecorderOptions options, RecorderOutputDescription output, ILogger logger)
    {
        _logger = logger;
        _scenarioName = options.ScenarioName;
        _configuredFilePath = ResolveDiagnosticLogFilePath(options, output);
        _target = new Lazy<(string FilePath, string MutexName)>(ResolveWriteTarget);
        SetEnabled(options.DiagnosticLog.WriteToFile);
    }

    internal string FilePath => _target.IsValueCreated ? _target.Value.FilePath : _configuredFilePath;
    internal bool IsEnabled { get; private set; }
    internal int EntryCount { get; private set; }

    internal void SetEnabled(bool enabled)
    {
        IsEnabled = enabled && TryWriteToLog(entry: null);
    }

    internal bool Append(EventId eventId, string diagnostic) =>
        IsEnabled && TryWriteToLog(string.Join(
            Environment.NewLine,
            $"[{DateTimeOffset.UtcNow:O}] EventId={eventId.Id} EventName={eventId.Name}",
            diagnostic,
            string.Empty,
            new string('-', 80),
            string.Empty));

    private bool TryWriteToLog(string? entry)
    {
        try
        {
            var target = _target.Value;
            using var mutex = new Mutex(false, target.MutexName);
            var ownsMutex = false;
            try
            {
                try { ownsMutex = mutex.WaitOne(WriteLockTimeout); }
                catch (AbandonedMutexException) { ownsMutex = true; }
                if (!ownsMutex)
                    throw new TimeoutException("Another recorder did not release the diagnostic log in time.");

                if (entry is null && File.Exists(target.FilePath)) return true;
                if (Path.GetDirectoryName(target.FilePath) is { Length: > 0 } directory)
                    Directory.CreateDirectory(directory);

                using (var stream = File.Open(target.FilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                {
                    var isEmpty = stream.Seek(0, SeekOrigin.End) == 0;
                    using var writer = new StreamWriter(stream, leaveOpen: true);
                    if (isEmpty)
                    {
                        writer.Write(string.Join(
                            Environment.NewLine,
                            "AppAutomation recorder diagnostic log",
                            $"ScenarioName: {_scenarioName}",
                            $"CreatedUtc: {DateTimeOffset.UtcNow:O}",
                            string.Empty));
                    }
                    if (entry is not null) writer.Write(entry);
                }

                if (entry is not null) EntryCount++;
                return true;
            }
            finally
            {
                if (ownsMutex) mutex.ReleaseMutex();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                RecorderDiagnosticsEventIds.DiagnosticsSnapshotFailed,
                ex,
                "Failed to {Operation} recorder diagnostic log file '{DiagnosticLogFilePath}': {Message}",
                entry is null ? "initialize" : "append to",
                FilePath,
                ex.Message);
            return false;
        }
    }

    private (string FilePath, string MutexName) ResolveWriteTarget()
    {
        var path = Path.GetFullPath(_configuredFilePath);
        var windows = OperatingSystem.IsWindows();
        var pathKey = windows ? path.ToUpperInvariant() : path;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pathKey)));
        var mutexName = (windows ? @"Global\" : string.Empty) + "AppAutomation.Recorder.Diagnostics." + hash;
        return (path, mutexName);
    }

    private static string ResolveDiagnosticLogFilePath(
        AppAutomationRecorderOptions options,
        RecorderOutputDescription outputDescription)
    {
        if (!string.IsNullOrWhiteSpace(options.DiagnosticLog.FilePath))
        {
            return options.DiagnosticLog.FilePath;
        }

        var directory = !string.IsNullOrWhiteSpace(outputDescription.OutputDirectory)
            ? outputDescription.OutputDirectory
            : Path.Combine(Path.GetTempPath(), "AppAutomation", "Recorder");
        var scenarioName = RecorderNaming.CreateFileSafeName(options.ScenarioName, "scenario");
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(directory, $"{scenarioName}.{timestamp}.recorder-diagnostics.log");
    }
}
