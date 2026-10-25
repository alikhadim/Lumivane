using System.Text.Json;

namespace Lumivane.Core;

/// <summary>Small JSON key-value store in the app data folder. Writes are atomic (temp file + move). Off while Demo Mode is on.</summary>
public sealed class Store
{
    readonly string dir;
    readonly object gate = new();
    public bool Persist { get; set; } = true;

    public Store(string dir) { this.dir = dir; Directory.CreateDirectory(dir); }

    string PathFor(string key) => Path.Combine(dir, key + ".json");

    public T? Get<T>(string key)
    {
        try
        {
            lock (gate)
            {
                var p = PathFor(key);
                return File.Exists(p) ? Json.Read<T>(File.ReadAllText(p)) : default;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return default; }
    }

    public void Set<T>(string key, T? value)
    {
        if (!Persist) return;
        try
        {
            lock (gate)
            {
                var p = PathFor(key);
                if (value is null) { if (File.Exists(p)) File.Delete(p); return; }
                var tmp = p + ".tmp";
                File.WriteAllText(tmp, Json.Write(value));
                File.Move(tmp, p, overwrite: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public bool GetBool(string key) => Get<bool>(key);
    public int GetInt(string key) => Get<int>(key);
}

/// <summary>In-memory secrets for tests.</summary>
public sealed class MemorySecretStore : ISecretStore
{
    readonly Dictionary<string, string> d = new();
    public string? Load(string key) => d.TryGetValue(key, out var v) ? v : null;
    public void Save(string key, string value) => d[key] = value;
    public void Delete(string key) => d.Remove(key);
}

/// <summary>Command-line options, used to produce screenshots and for UI testing.</summary>
public sealed class LaunchOptions
{
    public bool Demo { get; init; }
    public bool AutoSync { get; init; }
    public string? Section { get; init; }
    public string? MetricsLog { get; init; }
    public string? Screenshot { get; init; }
    public string? DataDir { get; init; }
    public string? Appearance { get; init; }
    /// <summary>Opens the first tab of the launch section on this index (screenshots and UI tests).</summary>
    public int? Tab { get; init; }

    public static LaunchOptions Parse(IReadOnlyList<string> a)
    {
        string? Val(string name) { int i = a.ToList().IndexOf(name); return i >= 0 && i + 1 < a.Count ? a[i + 1] : null; }
        return new LaunchOptions
        {
            Demo = a.Contains("--demo"), AutoSync = a.Contains("--sync"), Section = Val("--section"),
            MetricsLog = Val("--metrics-log"), Screenshot = Val("--screenshot"), DataDir = Val("--data-dir"), Appearance = Val("--appearance"), Tab = int.TryParse(Val("--tab"), out var tab) ? tab : null,
        };
    }
}
