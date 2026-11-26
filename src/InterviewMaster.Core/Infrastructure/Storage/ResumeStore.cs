using InterviewMaster.Domain;
using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

/// Reads the text of a PDF, one string per page. The Windows app supplies it; without one, PDFs cannot be attached.
public interface IPdfTextExtractor
{
    /// Null when the file cannot be opened as a PDF.
    IReadOnlyList<string>? PageTexts(string path);
}

public sealed class ResumeStoreException : Exception
{
    public ResumeStoreException(string message) : base(message) { }
}

/// Persists extracted resume text locally. This is the live interview CV, not Context notes.
public sealed class ResumeStore
{
    private static ResumeStore? shared;

    public static ResumeStore Shared
    {
        get => shared ??= new ResumeStore();
        set => shared = value;
    }

    private const string TextKey = "InterviewMaster.ResumeText";
    private const string PathKey = "InterviewMaster.ResumeSourcePath";
    private const string NameKey = "InterviewMaster.ResumeFileName";

    private readonly ISettingsStore? explicitStore;

    public ResumeStore(ISettingsStore? store = null, IPdfTextExtractor? pdfTextExtractor = null)
    {
        explicitStore = store;
        PdfTextExtractor = pdfTextExtractor;
    }

    public IPdfTextExtractor? PdfTextExtractor { get; set; }

    /// Raised when a resume is attached or cleared.
    public event Action? Changed;

    private ISettingsStore Store => explicitStore ?? SettingsStore.Standard;

    public string ExtractedText => Store.GetString(TextKey) ?? "";

    public string FileName => Store.GetString(NameKey) ?? "";

    public string SourcePath => Store.GetString(PathKey) ?? "";

    public bool HasResume => ExtractedText.Trim().Length > 0;

    public void Attach(string filePath)
    {
        var text = ExtractResumeText(filePath);
        if (text.Trim().Length == 0)
        {
            throw new ResumeStoreException("Could not read any text from that file.");
        }
        Store.Set(TextKey, text);
        Store.Set(PathKey, filePath);
        Store.Set(NameKey, Path.GetFileName(filePath));
        Changed?.Invoke();
    }

    public void Clear()
    {
        Store.Remove(TextKey);
        Store.Remove(PathKey);
        Store.Remove(NameKey);
        Changed?.Invoke();
    }

    public string ExtractResumeText(string filePath)
    {
        if (Path.GetExtension(filePath).ToLowerInvariant() == ".pdf")
        {
            var pages = PdfTextExtractor?.PageTexts(filePath) ?? throw new ResumeStoreException("Could not open the PDF.");
            return Briefing.ExtractResumeText(string.Join("\n", pages.Where(page => page.Length > 0)));
        }
        return Briefing.ExtractResumeText(File.ReadAllText(filePath));
    }
}
