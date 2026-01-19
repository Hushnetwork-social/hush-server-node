using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Captures log output from HushServerNode during integration tests.
/// Logs are stored and can be retrieved for diagnostic output when tests fail.
/// </summary>
internal sealed class DiagnosticCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _logEntries = new();
    private readonly LogLevel _minimumLevel;
    private const int MaxEntries = 1000;

    public DiagnosticCapture(LogLevel minimumLevel = LogLevel.Information)
    {
        _minimumLevel = minimumLevel;
    }

    /// <summary>
    /// Gets all captured log entries as a single string.
    /// </summary>
    public string GetCapturedLogs()
    {
        var sb = new StringBuilder();
        foreach (var entry in _logEntries)
        {
            sb.AppendLine(entry);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Gets the count of captured log entries.
    /// </summary>
    public int EntryCount => _logEntries.Count;

    /// <summary>
    /// Clears all captured log entries.
    /// </summary>
    public void Clear()
    {
        _logEntries.Clear();
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new DiagnosticLogger(this, categoryName, _minimumLevel);
    }

    public void Dispose()
    {
        // No resources to dispose
    }

    internal void AddEntry(string entry)
    {
        // Prevent unbounded memory growth
        while (_logEntries.Count >= MaxEntries)
        {
            _logEntries.TryDequeue(out _);
        }
        _logEntries.Enqueue(entry);
    }

    private sealed class DiagnosticLogger : ILogger
    {
        private readonly DiagnosticCapture _capture;
        private readonly string _categoryName;
        private readonly LogLevel _minimumLevel;

        public DiagnosticLogger(DiagnosticCapture capture, string categoryName, LogLevel minimumLevel)
        {
            _capture = capture;
            _categoryName = categoryName;
            _minimumLevel = minimumLevel;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            var timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
            var levelShort = logLevel switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "???"
            };

            var entry = $"[{timestamp}] [{levelShort}] {_categoryName}: {message}";
            if (exception != null)
            {
                entry += Environment.NewLine + exception.ToString();
            }

            _capture.AddEntry(entry);
        }
    }
}
