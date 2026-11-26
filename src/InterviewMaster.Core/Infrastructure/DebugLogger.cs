using System.Globalization;
using System.Runtime.CompilerServices;
using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

public enum LogCategory
{
    Audio,
    Transcription,
    Classification,
    Answer,
    Stream,
    Error,
    Delegate
}

/// Simple file logger for debugging main functionality.
/// Log file is cleared on each app start.
public sealed class DebugLogger
{
    public static DebugLogger Shared { get; set; } = new();

    private readonly string logFilePath;
    private readonly object gate = new();
    private Task tail = Task.CompletedTask;

    /// Read and written in the write queue. False when the folder cannot hold a file; every line still
    /// reaches the console sink, so nothing is lost by not trying.
    private bool canWriteFile = true;

    /// The log goes in the working directory, as on macOS, unless another file is named.
    public DebugLogger(string? logFilePath = null)
    {
        this.logFilePath = logFilePath ?? Path.Combine(Directory.GetCurrentDirectory(), "interview_debug.log");
    }

    public string LogFilePath => logFilePath;

    /// Clear the log file (call on app start).
    public void Clear()
    {
        Enqueue(() =>
        {
            try
            {
                File.WriteAllText(logFilePath, "");
                // The log holds what was said in the interview, so only this user may read it.
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(logFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                canWriteFile = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                canWriteFile = false;
            }
        });
        Flush();
        Log("=== Interview Master Started ===");
    }

    /// Log a message with timestamp.
    public void Log(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var entry = $"[{timestamp}] [{SourceName(file)}:{line}] {message}\n";
        Enqueue(() =>
        {
            if (!canWriteFile) return;
            try
            {
                File.AppendAllText(logFilePath, entry);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        });

        // Also print to the console sink for immediate feedback.
        Support.Log.Print("📝 " + message);
    }

    /// Log with category prefix.
    public void Log(LogCategory category, string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Log($"{Prefix(category)} {message}", file, line);

    /// Waits until every line logged so far is in the file.
    public void Flush()
    {
        Task pending;
        lock (gate) pending = tail;
        pending.Wait();
    }

    public static string Prefix(LogCategory category) => category switch
    {
        LogCategory.Audio => "🎤 AUDIO:",
        LogCategory.Transcription => "📝 TRANSCRIBE:",
        LogCategory.Classification => "🏷️ CLASSIFY:",
        LogCategory.Answer => "💬 ANSWER:",
        LogCategory.Stream => "📡 STREAM:",
        LogCategory.Error => "❌ ERROR:",
        _ => "📤 DELEGATE:"
    };

    private void Enqueue(Action work)
    {
        lock (gate)
        {
            tail = tail.ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private static string SourceName(string file)
    {
        var name = file[(file.LastIndexOfAny(new[] { '/', '\\' }) + 1)..];
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
