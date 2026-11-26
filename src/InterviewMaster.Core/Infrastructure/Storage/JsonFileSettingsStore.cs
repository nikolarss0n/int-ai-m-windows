using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

/// Preferences kept in one JSON file, standing in for `UserDefaults`. Every change is written at once,
/// so a crash loses nothing that was set.
public sealed class JsonFileSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string path;
    private readonly object gate = new();
    private readonly JsonObject values;

    public JsonFileSettingsStore(string? path = null)
    {
        this.path = path ?? AppPaths.SettingsFile;
        values = Load(this.path);
    }

    public string? GetString(string key)
    {
        lock (gate)
        {
            if (values[key] is not JsonValue value) return null;
            if (value.TryGetValue<string>(out var text)) return text;
            return value.TryGetValue<double>(out var number) ? number.ToString("R", CultureInfo.InvariantCulture) : null;
        }
    }

    public bool GetBool(string key)
    {
        lock (gate)
        {
            if (values[key] is not JsonValue value) return false;
            if (value.TryGetValue<bool>(out var flag)) return flag;
            if (value.TryGetValue<double>(out var number)) return number != 0;
            return value.TryGetValue<string>(out var text) && text.ToLowerInvariant() is "true" or "yes" or "1";
        }
    }

    public double? GetDouble(string key)
    {
        lock (gate)
        {
            if (values[key] is not JsonValue value) return null;
            if (value.TryGetValue<double>(out var number)) return number;
            return value.TryGetValue<string>(out var text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        }
    }

    public void Set(string key, string value) => Store(key, JsonValue.Create(value));

    public void Set(string key, bool value) => Store(key, JsonValue.Create(value));

    public void Set(string key, double value) => Store(key, JsonValue.Create(value));

    public void Remove(string key)
    {
        lock (gate)
        {
            if (values.Remove(key)) Save();
        }
    }

    private void Store(string key, JsonNode? value)
    {
        lock (gate)
        {
            values[key] = value;
            Save();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            AtomicFile.WriteAllText(path, values.ToJsonString(WriteOptions));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Print($"⚠️ Settings could not be saved: {error.Message}");
        }
    }

    private static JsonObject Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllBytes(path)) is JsonObject loaded) return loaded;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Print($"⚠️ Settings could not be read, starting from defaults: {error.Message}");
        }
        return new JsonObject();
    }
}
