using System.Text.Json;
using InterviewMaster.Domain;
using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

/// Isolated practice history. Lives under Documents\InterviewMaster\practice\, not live memory.json.
public sealed class PracticeStore
{
    private static PracticeStore? shared;

    public static PracticeStore Shared => shared ??= new PracticeStore();

    private readonly object gate = new();
    private readonly string runsFilePath;
    private readonly string inProgressFilePath;
    private List<PracticeRunRecord> runs = new();
    private PracticeSessionSnapshot? inProgress;

    /// A malformed history must never be replaced by a new, apparently empty history.
    /// Relaunch after repairing or removing the file is required before writes resume.
    private bool runsLoadFailed;

    /// Snapshot writes stay blocked only when malformed data could not be moved to safety.
    private bool inProgressLoadFailed;

    /// An injectable directory keeps persistence verifiable without touching the
    /// learner's real history. Production uses the same historical location.
    public PracticeStore(string? directory = null)
    {
        var folder = directory ?? AppPaths.PracticeDirectory;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
        runsFilePath = Path.Combine(folder, "runs.json");
        inProgressFilePath = Path.Combine(folder, "in-progress.json");
        LoadRuns();
        LoadInProgress();
    }

    public List<PracticeRunRecord> AllRuns()
    {
        lock (gate) return runs.ToList();
    }

    public bool Append(PracticeRunRecord run)
    {
        lock (gate)
        {
            if (runsLoadFailed)
            {
                Log.Print("⚠️ PracticeStore refused to save because runs.json did not load successfully.");
                return false;
            }
            var existingIndex = runs.FindIndex(item => item.Id == run.Id);
            if (existingIndex >= 0)
            {
                if (runs[existingIndex] == run) return true;
                var previous = runs[existingIndex];
                runs[existingIndex] = run;
                if (SaveRuns()) return true;
                runs[existingIndex] = previous;
                return false;
            }
            runs.Add(run);
            if (SaveRuns()) return true;
            runs.RemoveAt(runs.Count - 1);
            return false;
        }
    }

    public PracticeSessionSnapshot? InProgressSession()
    {
        lock (gate) return inProgress;
    }

    /// Atomically replaces the single resumable session. Completed history remains
    /// in runs.json so older app versions can continue to read it.
    public bool SaveInProgressSession(PracticeSessionSnapshot snapshot)
    {
        lock (gate)
        {
            if (inProgressLoadFailed)
            {
                Log.Print("⚠️ PracticeStore refused to save because malformed in-progress data could not be preserved.");
                return false;
            }
            if (!SaveInProgress(snapshot)) return false;
            inProgress = snapshot;
            return true;
        }
    }

    public bool ClearInProgressSession()
    {
        lock (gate)
        {
            if (!File.Exists(inProgressFilePath))
            {
                inProgress = null;
                inProgressLoadFailed = false;
                return true;
            }
            try
            {
                File.Delete(inProgressFilePath);
                inProgress = null;
                inProgressLoadFailed = false;
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Log.Print($"⚠️ PracticeStore failed to clear in-progress session: {error.Message}");
                return false;
            }
        }
    }

    public List<PracticeProgressInput> ProgressInputs(string? packId = null) =>
        AllRuns()
            .Select(run => new PracticeProgressInput(run.FinishedAt, run.OverallScore, run.PackId))
            .Where(input => packId is null || input.PackId == packId)
            .ToList();

    private void LoadRuns()
    {
        if (!File.Exists(runsFilePath)) return;
        try
        {
            runs = JsonSerializer.Deserialize<List<PracticeRunRecord>>(File.ReadAllBytes(runsFilePath), PracticeJson.StoreOptions)
                ?? throw new JsonException("Expected a list of runs.");
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            runsLoadFailed = true;
            Log.Print($"⚠️ PracticeStore failed to load: {error.Message}");
        }
    }

    private void LoadInProgress()
    {
        if (!File.Exists(inProgressFilePath)) return;
        try
        {
            inProgress = JsonSerializer.Deserialize<PracticeSessionSnapshot>(File.ReadAllBytes(inProgressFilePath), PracticeJson.StoreOptions)
                ?? throw new JsonException("Expected a practice session.");
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            var recoveryPath = Path.Combine(
                Path.GetDirectoryName(inProgressFilePath) ?? ".",
                $"in-progress-recovery-{Guid.NewGuid().ToString().ToUpperInvariant()}.json");
            try
            {
                File.Move(inProgressFilePath, recoveryPath);
                Log.Print($"⚠️ PracticeStore preserved malformed in-progress session at {recoveryPath}: {error.Message}");
            }
            catch (Exception recoveryError) when (recoveryError is IOException or UnauthorizedAccessException)
            {
                inProgressLoadFailed = true;
                Log.Print($"⚠️ PracticeStore failed to load or preserve in-progress session: {error.Message}; {recoveryError.Message}");
            }
        }
    }

    private bool SaveRuns()
    {
        try
        {
            AtomicFile.WriteAllBytes(runsFilePath, JsonSerializer.SerializeToUtf8Bytes(runs, PracticeJson.StoreOptions));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Print($"⚠️ PracticeStore failed to save: {error.Message}");
            return false;
        }
    }

    private bool SaveInProgress(PracticeSessionSnapshot snapshot)
    {
        try
        {
            AtomicFile.WriteAllBytes(inProgressFilePath, JsonSerializer.SerializeToUtf8Bytes(snapshot, PracticeJson.StoreOptions));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Print($"⚠️ PracticeStore failed to save in-progress session: {error.Message}");
            return false;
        }
    }
}
