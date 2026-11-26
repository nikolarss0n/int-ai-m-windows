using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

public enum ApiKeyType
{
    Anthropic,
    Groq,

    /// xAI Grok (console.x.ai) — preferred for interview classify/answer.
    Xai,

    /// OpenAI (platform.openai.com) — optional GPT answer model.
    OpenAI
}

public static class ApiKeyTypes
{
    public static readonly IReadOnlyList<ApiKeyType> AllCases = new[] { ApiKeyType.Anthropic, ApiKeyType.Groq, ApiKeyType.Xai, ApiKeyType.OpenAI };

    public static string RawValue(this ApiKeyType type) => type switch
    {
        ApiKeyType.Anthropic => "ANTHROPIC_API_KEY",
        ApiKeyType.Groq => "GROQ_API_KEY",
        ApiKeyType.Xai => "XAI_API_KEY",
        _ => "OPENAI_API_KEY"
    };
}

/// Centralized API key management - reads from the .interview-master-keys file in the user's profile.
/// Environment variables with the same names override the file.
public sealed class ApiKeyManager
{
    private static ApiKeyManager? shared;

    public static ApiKeyManager Shared
    {
        get => shared ??= new ApiKeyManager();
        set => shared = value;
    }

    /// Raised after keys are changed in Settings, so clients are rebuilt with the new keys.
    public static event Action? KeysUpdated;

    public static void NotifyKeysUpdated() => KeysUpdated?.Invoke();

    private static readonly char[] NewlineCharacters = { '\n', '\r', '\u000B', '\u000C', '\u0085', (char)0x2028, (char)0x2029 };

    private readonly Dictionary<string, string> keys = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private readonly string filePath;
    private readonly Func<string, string?> environment;

    public ApiKeyManager(string? filePath = null, Func<string, string?>? environment = null)
    {
        this.filePath = filePath ?? AppPaths.KeysFile;
        this.environment = environment ?? Environment.GetEnvironmentVariable;
        LoadKeys();
    }

    public string FilePath => filePath;

    /// Reads the key file and the environment again, replacing what was loaded before.
    public void Reload() => LoadKeys();

    private void LoadKeys()
    {
        var loaded = new Dictionary<string, string>(StringComparer.Ordinal);
        string? content = null;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Print($"⚠️ ApiKeyManager: Could not read {filePath}");
        }

        foreach (var line in (content ?? "").Split(NewlineCharacters))
        {
            var trimmed = line.TrimInline();
            if (trimmed.Length == 0 || trimmed.HasPrefix("#")) continue;

            var start = 0;
            while (start < trimmed.Length && trimmed[start] == '=') start++;
            var separator = trimmed.IndexOf('=', start);
            if (separator < 0 || separator == trimmed.Length - 1) continue;
            var key = trimmed[start..separator].TrimInline();
            var value = trimmed[(separator + 1)..].TrimInline();
            if (value.Length > 0) loaded[key] = value;
        }

        // Environment overrides file (useful for XAI_API_KEY without editing the key file).
        foreach (var type in ApiKeyTypes.AllCases)
        {
            var fromEnvironment = environment(type.RawValue())?.Trim();
            if (!string.IsNullOrEmpty(fromEnvironment)) loaded[type.RawValue()] = fromEnvironment;
        }

        lock (gate)
        {
            keys.Clear();
            foreach (var (key, value) in loaded) keys[key] = value;
        }
        Log.Print($"✅ ApiKeyManager: Loaded {loaded.Count} keys from file/env");
    }

    public string? GetKey(ApiKeyType type)
    {
        lock (gate) return keys.GetValueOrDefault(type.RawValue());
    }

    public bool SetKey(string key, ApiKeyType type)
    {
        lock (gate) keys[type.RawValue()] = key;
        return true;
    }

    public bool HasKey(ApiKeyType type)
    {
        lock (gate) return keys.TryGetValue(type.RawValue(), out var key) && key.Length > 0;
    }

    public bool DeleteKey(ApiKeyType type)
    {
        lock (gate) keys.Remove(type.RawValue());
        return true;
    }
}
