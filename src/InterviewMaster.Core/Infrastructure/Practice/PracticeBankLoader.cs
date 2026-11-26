using System.Text.Json;
using InterviewMaster.Domain;
using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

public static class PracticeBankLoader
{
    public static PracticeBankFile Decode(ReadOnlySpan<byte> data) =>
        JsonSerializer.Deserialize<PracticeBankFile>(data, PracticeJson.Options) ?? throw new JsonException("Expected a practice bank.");

    public static PracticeBankFile LoadFile(string path) => Decode(File.ReadAllBytes(path));

    public static PracticeBankFile Load() => Load(CandidateDirectories());

    public static PracticeBankFile Load(IEnumerable<string> directories)
    {
        foreach (var directory in directories)
        {
            var bank = LoadDirectory(directory);
            if (!bank.Packs.IsEmpty)
            {
                Log.Print($"📚 Practice bank loaded: {bank.Packs.Count} packs, {bank.Positions.Count} positions from {directory}");
                return bank;
            }
        }
        Log.Print("⚠️ Practice bank.json not found; using built-in AWS/Models/Angular packs");
        return new PracticeBankFile
        {
            Packs = new ValueList<PracticeTopicPack>(PracticeTopicPack.All),
            Positions = PracticeTopicPack.All
                .Select(pack => new PracticePosition { Id = pack.Id, Title = pack.Title, PackId = pack.Id, GroupIds = ValueList<string>.Empty })
                .ToArray()
        };
    }

    public static PracticeBankFile LoadDirectory(string directory)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(directory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new PracticeBankFile { Packs = ValueList<PracticeTopicPack>.Empty };
        }

        var packs = new List<PracticeTopicPack>();
        var positions = new List<PracticePosition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var bankFiles = files
            .Where(path => Path.GetExtension(path).ToLowerInvariant() == ".json")
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal);
        foreach (var path in bankFiles)
        {
            PracticeBankFile bank;
            try
            {
                bank = LoadFile(path);
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var pack in bank.Packs)
            {
                if (seen.Add(pack.Id)) packs.Add(pack);
            }
            positions.AddRange(bank.Positions);
        }
        return new PracticeBankFile { Packs = packs, Positions = positions };
    }

    /// Beside the executable first, where the app ships its banks, then the working directory.
    private static List<string> CandidateDirectories()
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        return new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "practice"),
            Path.Combine(AppContext.BaseDirectory, "Resources", "practice"),
            Path.Combine(workingDirectory, "Resources", "practice"),
            Path.Combine(workingDirectory, "practice")
        };
    }
}
