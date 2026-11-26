using InterviewMaster.Support;

namespace InterviewMaster.Domain;

/// One step's code inside a file, with where it starts so line numbers match the file once it is pasted.
/// <param name="FirstLine">Zero-based line of the file this chunk starts on.</param>
/// <param name="RepeatsEarlierCode">The model repeated code from an earlier chunk, so appending it would duplicate lines.</param>
public sealed record CodeChunk(int Number, string Why, string Code, int FirstLine, bool RepeatsEarlierCode);

/// Consecutive steps that write the same file, read as that file.
public sealed record CodeFileSection(string FileName, string Language, ValueList<CodeChunk> Chunks)
{
    /// The whole file. A blank line separates chunks, and `FirstLine` counts it.
    public string Code => string.Join("\n\n", Chunks.Select(chunk => chunk.Code).Where(code => code.Length > 0));
}

/// What moved between two versions of a file, by zero-based line of the new version.
public sealed class CodeLineChanges : IEquatable<CodeLineChanges>
{
    public HashSet<int> Changed { get; } = new();

    /// Lines were removed just before this line. It can be one past the last line.
    public HashSet<int> RemovedBefore { get; } = new();

    public bool Equals(CodeLineChanges? other) =>
        other is not null && Changed.SetEquals(other.Changed) && RemovedBefore.SetEquals(other.RemovedBefore);

    public override bool Equals(object? obj) => obj is CodeLineChanges other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Changed.Count, RemovedBefore.Count);
}

public static class CodeFiles
{
    /// Steps are appended chunks, so neighbours in one file join into one block. A file that comes back
    /// after another file starts a new section, because the steps are kept in typing order.
    public static List<CodeFileSection> GroupCodeSteps(IEnumerable<CodingInterviewStep> steps)
    {
        var sections = new List<(string FileName, string Language, List<CodeChunk> Chunks)>();
        var nextLine = 0;
        var seenLines = new HashSet<string>();

        foreach (var step in steps)
        {
            if (sections.Count == 0 || sections[^1].FileName != step.FileName)
            {
                sections.Add((step.FileName, step.Language, new List<CodeChunk>()));
                nextLine = 0;
                seenLines = new HashSet<string>();
            }
            var lines = step.Code.Length == 0 ? new List<string>() : step.Code.SplitKeepingEmpty('\n');
            var meaningful = lines.Select(line => line.TrimInline()).Where(line => line.Length > 2).ToList();
            var repeated = meaningful.Count(seenLines.Contains);
            sections[^1].Chunks.Add(new CodeChunk(
                step.Number, step.Why, step.Code, nextLine,
                meaningful.Count >= 2 && repeated * 2 >= meaningful.Count));
            seenLines.UnionWith(meaningful);
            if (lines.Count > 0) nextLine += lines.Count + 1;
        }
        return sections.Select(section => new CodeFileSection(section.FileName, section.Language, section.Chunks)).ToList();
    }

    /// Compares two versions of a file line by line. Returns null for a rewrite, where marking nearly every
    /// line says nothing, and for files too long to compare while the interface waits.
    public static CodeLineChanges? LineChanges(string old, string @new)
    {
        var oldLines = old.Split('\n');
        var newLines = @new.Split('\n');
        if ((long)oldLines.Length * newLines.Length > 400_000) return null;

        var lengths = new int[oldLines.Length + 1, newLines.Length + 1];
        for (var a = oldLines.Length - 1; a >= 0; a--)
        {
            for (var b = newLines.Length - 1; b >= 0; b--)
            {
                lengths[a, b] = oldLines[a] == newLines[b]
                    ? lengths[a + 1, b + 1] + 1
                    : Math.Max(lengths[a + 1, b], lengths[a, b + 1]);
            }
        }

        static bool IsBlank(string line) => line.TrimInline().Length == 0;

        var changes = new CodeLineChanges();
        var keptLines = 0;
        var i = 0;
        var j = 0;
        var removedInGap = 0;
        var addedInGap = 0;

        void CloseGap(int line)
        {
            if (removedInGap > addedInGap) changes.RemovedBefore.Add(line);
            removedInGap = 0;
            addedInGap = 0;
        }

        while (i < oldLines.Length || j < newLines.Length)
        {
            if (i < oldLines.Length && j < newLines.Length && oldLines[i] == newLines[j])
            {
                CloseGap(j);
                if (!IsBlank(newLines[j])) keptLines++;
                i++;
                j++;
            }
            else if (j < newLines.Length && (i == oldLines.Length || lengths[i, j + 1] >= lengths[i + 1, j]))
            {
                if (!IsBlank(newLines[j]))
                {
                    changes.Changed.Add(j);
                    addedInGap++;
                }
                j++;
            }
            else
            {
                if (!IsBlank(oldLines[i])) removedInGap++;
                i++;
            }
        }
        CloseGap(newLines.Length);

        var total = keptLines + changes.Changed.Count;
        if (total <= 0 || keptLines * 10 < total * 4) return null;
        return changes;
    }
}
