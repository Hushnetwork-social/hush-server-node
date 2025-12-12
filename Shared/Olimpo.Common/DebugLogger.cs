using System.Collections.Concurrent;
using System.Diagnostics;

namespace Olimpo;

/// <summary>
/// Debug logger with timestamps and elapsed time tracking.
/// Set Enabled = true to see debug logs in the console.
/// Outputs to both Console (for Browser) and Debug (for Desktop/VS Output).
/// </summary>
public static class DebugLogger
{
    /// <summary>
    /// Set to true to enable debug logging.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Tracks active operations for elapsed time measurement.
    /// Key: operationId, Value: Stopwatch
    /// </summary>
    private static readonly ConcurrentDictionary<string, Stopwatch> _operations = new();

    /// <summary>
    /// Gets the current timestamp in mm:ss.fff format.
    /// </summary>
    private static string Timestamp => DateTime.Now.ToString("mm:ss.fff");

    /// <summary>
    /// Write to both Console (Browser) and Debug (Desktop/VS Output).
    /// </summary>
    private static void WriteLog(string message)
    {
        Console.WriteLine(message);
        Debug.WriteLine(message);
    }

    /// <summary>
    /// Log a simple message with timestamp.
    /// </summary>
    public static void Log(string tag, string message)
    {
        if (Enabled)
        {
            WriteLog($"[{Timestamp}] [{tag}] {message}");
        }
    }

    /// <summary>
    /// Log a formatted message with timestamp.
    /// </summary>
    public static void Log(string tag, string message, params object[] args)
    {
        if (Enabled)
        {
            WriteLog($"[{Timestamp}] [{tag}] {string.Format(message, args)}");
        }
    }

    /// <summary>
    /// Start tracking an operation. Returns an operationId to use with LogEnd.
    /// </summary>
    /// <param name="tag">The log tag (e.g., component name)</param>
    /// <param name="operationName">Name of the operation being tracked</param>
    /// <returns>Operation ID to pass to LogEnd</returns>
    public static string LogStart(string tag, string operationName)
    {
        var operationId = $"{tag}:{operationName}:{Guid.NewGuid():N}";

        if (Enabled)
        {
            var sw = Stopwatch.StartNew();
            _operations[operationId] = sw;
            WriteLog($"[{Timestamp}] [{tag}] >> START: {operationName}");
        }

        return operationId;
    }

    /// <summary>
    /// Log a checkpoint within an operation (shows elapsed time since start).
    /// </summary>
    /// <param name="operationId">The operation ID from LogStart</param>
    /// <param name="tag">The log tag</param>
    /// <param name="checkpointMessage">Description of the checkpoint</param>
    public static void LogCheckpoint(string operationId, string tag, string checkpointMessage)
    {
        if (Enabled && _operations.TryGetValue(operationId, out var sw))
        {
            WriteLog($"[{Timestamp}] [{tag}] -- {checkpointMessage} (elapsed: {sw.ElapsedMilliseconds}ms)");
        }
        else if (Enabled)
        {
            WriteLog($"[{Timestamp}] [{tag}] -- {checkpointMessage}");
        }
    }

    /// <summary>
    /// End tracking an operation and log the total elapsed time.
    /// </summary>
    /// <param name="operationId">The operation ID from LogStart</param>
    /// <param name="tag">The log tag</param>
    /// <param name="operationName">Name of the operation (for the log message)</param>
    public static void LogEnd(string operationId, string tag, string operationName)
    {
        if (Enabled)
        {
            if (_operations.TryRemove(operationId, out var sw))
            {
                sw.Stop();
                WriteLog($"[{Timestamp}] [{tag}] << END: {operationName} (total: {sw.ElapsedMilliseconds}ms)");
            }
            else
            {
                WriteLog($"[{Timestamp}] [{tag}] << END: {operationName}");
            }
        }
    }

    /// <summary>
    /// Log an async operation boundary (before await).
    /// Helps identify where async context switches happen.
    /// </summary>
    public static void LogAwait(string tag, string awaitDescription)
    {
        if (Enabled)
        {
            var threadInfo = Environment.CurrentManagedThreadId;
            WriteLog($"[{Timestamp}] [{tag}] AWAIT: {awaitDescription} [Thread:{threadInfo}]");
        }
    }

    /// <summary>
    /// Log when returning from an await (after async operation completes).
    /// </summary>
    public static void LogAwaitResume(string tag, string awaitDescription)
    {
        if (Enabled)
        {
            var threadInfo = Environment.CurrentManagedThreadId;
            WriteLog($"[{Timestamp}] [{tag}] RESUMED: {awaitDescription} [Thread:{threadInfo}]");
        }
    }
}
