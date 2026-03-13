using System.Text;

namespace AppAutomation.TestHost.Avalonia;

public sealed class TemporaryDirectory : IDisposable
{
    private bool _disposed;
    private bool _preserved;

    private TemporaryDirectory(string fullPath)
    {
        FullPath = fullPath;
    }

    public string FullPath { get; }

    public static TemporaryDirectory Create(string prefix = "AppAutomation")
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? "AppAutomation" : prefix.Trim();
        var fullPath = Path.Combine(
            Path.GetTempPath(),
            normalizedPrefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fullPath);
        return new TemporaryDirectory(fullPath);
    }

    public string CreateDirectory(string relativePath)
    {
        var path = GetPath(relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public string WriteTextFile(string relativePath, string content, Encoding? encoding = null)
    {
        var path = GetPath(relativePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, encoding ?? Encoding.UTF8);
        return path;
    }

    public void Preserve()
    {
        _preserved = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);

        if (_preserved)
        {
            return;
        }

        try
        {
            if (Directory.Exists(FullPath))
            {
                Directory.Delete(FullPath, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for temp workspace.
        }
    }

    private string GetPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path is required.", nameof(relativePath));
        }

        return Path.GetFullPath(Path.Combine(FullPath, relativePath));
    }
}
