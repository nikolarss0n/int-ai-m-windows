namespace InterviewMaster.Support;

/// Key-value preferences, standing in for `UserDefaults`. Keys keep the names the macOS app uses.
public interface ISettingsStore
{
    string? GetString(string key);
    bool GetBool(string key);
    double? GetDouble(string key);
    void Set(string key, string value);
    void Set(string key, bool value);
    void Set(string key, double value);
    void Remove(string key);
}

public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public InMemorySettingsStore() { }

    public InMemorySettingsStore(IEnumerable<KeyValuePair<string, string>> initial)
    {
        foreach (var (key, value) in initial) values[key] = value;
    }

    public string? GetString(string key)
    {
        lock (gate) return values.TryGetValue(key, out var value) ? value as string : null;
    }

    public bool GetBool(string key)
    {
        lock (gate) return values.TryGetValue(key, out var value) && value is true;
    }

    public double? GetDouble(string key)
    {
        lock (gate) return values.TryGetValue(key, out var value) && value is double number ? number : null;
    }

    public void Set(string key, string value)
    {
        lock (gate) values[key] = value;
    }

    public void Set(string key, bool value)
    {
        lock (gate) values[key] = value;
    }

    public void Set(string key, double value)
    {
        lock (gate) values[key] = value;
    }

    public void Remove(string key)
    {
        lock (gate) values.Remove(key);
    }
}

/// The app sets `Standard` to a file-backed store before anything reads a setting.
public static class SettingsStore
{
    public static ISettingsStore Standard { get; set; } = new InMemorySettingsStore();
}
