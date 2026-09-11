using Microsoft.Extensions.Logging;

namespace AppAutomation.Recorder.Avalonia.Tests;

internal sealed class RecorderCaptureTestLogger : ILogger
{
    public List<(EventId EventId, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((eventId, formatter(state, exception)));
    }
}
