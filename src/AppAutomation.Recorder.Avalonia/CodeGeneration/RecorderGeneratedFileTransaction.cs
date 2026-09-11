using System.Text;

namespace AppAutomation.Recorder.Avalonia.CodeGeneration;

internal static class RecorderGeneratedFileTransaction
{
    public static async Task WriteAsync(
        IReadOnlyList<(string Path, string Source)> sources,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var files = sources.Select(static source => new PendingFile(source.Path, source.Source)).ToArray();
        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var stream = new FileStream(
                    file.TemporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 4096, useAsync: true);
                file.TemporaryFileCreated = true;
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                await writer.WriteAsync(file.Source.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                file.ReplacedExistingFile = File.Exists(file.Path);
                if (file.ReplacedExistingFile)
                {
                    File.Replace(file.TemporaryPath, file.Path, file.BackupPath);
                }
                else
                {
                    File.Move(file.TemporaryPath, file.Path);
                }

                file.Published = true;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var file in Enumerable.Reverse(files).Where(static file => file.Published))
            {
                try
                {
                    if (!file.ReplacedExistingFile)
                    {
                        File.Delete(file.Path);
                    }
                    else if (File.Exists(file.Path))
                    {
                        File.Replace(file.BackupPath, file.Path, destinationBackupFileName: null);
                    }
                    else
                    {
                        File.Move(file.BackupPath, file.Path);
                    }
                }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                {
                    file.PreserveBackup = true;
                    rollbackErrors.Add(new IOException(
                        file.ReplacedExistingFile
                            ? $"Could not restore recorder file '{file.Path}'. Its previous content is retained at '{file.BackupPath}'."
                            : $"Could not remove the newly saved recorder file '{file.Path}' during rollback.",
                        rollbackError));
                }
            }

            if (rollbackErrors.Count > 0)
            {
                throw new IOException(
                    $"Recorder save failed: {exception.Message} Rollback also failed: {string.Join(" ", rollbackErrors.Select(static error => error.Message))}",
                    new AggregateException([exception, .. rollbackErrors]));
            }

            throw;
        }
        finally
        {
            foreach (var file in files)
            {
                if (file.TemporaryFileCreated)
                {
                    TryDelete(file.TemporaryPath, diagnostics);
                }

                if (file.Published && !file.PreserveBackup)
                {
                    TryDelete(file.BackupPath, diagnostics);
                }
            }
        }
    }

    private static void TryDelete(string path, ICollection<string> diagnostics)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add($"Recorder temporary file '{path}' could not be removed: {exception.Message}");
        }
    }

    private sealed class PendingFile(string path, string source)
    {
        public string Path { get; } = path;
        public string Source { get; } = source;
        public string TemporaryPath { get; } = $"{path}.{Guid.NewGuid():N}.recorder.tmp";
        public string BackupPath { get; } = $"{path}.{Guid.NewGuid():N}.recorder.bak";
        public bool TemporaryFileCreated { get; set; }
        public bool ReplacedExistingFile { get; set; }
        public bool Published { get; set; }
        public bool PreserveBackup { get; set; }
    }
}
