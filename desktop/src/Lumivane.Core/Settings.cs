using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumivane.Core;

[JsonConverter(typeof(AudioInputConverter))]
public sealed record AudioInput(string Kind, string? App = null)
{
    public static readonly AudioInput System = new("system");
    public static readonly AudioInput Microphone = new("microphone");
    public static AudioInput ForApp(string id) => new("app", id);
}

public sealed class AudioSettings
{
    public bool Enabled { get; set; } = true;
    public AudioInput Input { get; set; } = AudioInput.System;
    /// <summary>Share of the light that comes from audio, 0 (screen only) to 1 (audio only). Movies: 0.1-0.3.</summary>
    public float Influence { get; set; } = 0.2f;
    public float BassSensitivity { get; set; } = 1;
    public float TrebleSensitivity { get; set; } = 1;
    public float BeatIntensity { get; set; } = 1;
    public float Smoothing { get; set; } = 0.5f;
    public float MusicStrength { get; set; } = 0.5f;
}

public sealed class SyncSettings : IJsonOnDeserializing, IJsonOnDeserialized
{
    public float Brightness { get; set; } = 1;
    /// <summary>Extra saturation multiplier on top of the style.</summary>
    public float Intensity { get; set; } = 1;
    public float Smoothing { get; set; } = 0.5f;
    public int Fps { get; set; } = 50;
    /// <summary>Brightness curve exponent multiplier. 1 uses the style as designed.</summary>
    public float Gamma { get; set; } = 1f;
    /// <summary>Scales how strongly the dominant color is preferred over the zone average (0.5 = style default).</summary>
    public float Dominance { get; set; } = 0.5f;
    public bool DynamicBrightness { get; set; } = true;
    public bool NightMode { get; set; }
    public bool LowLightMode { get; set; }
    public float BrightnessLimit { get; set; } = 1;
    [JsonConverter(typeof(ChannelGainConverter))]
    public Dictionary<int, float> ChannelGain { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    public ColorStyle Style { get; set; } = ColorStyle.Enhanced;
    /// <summary>Added to the style's white balance, -1 cooler to 1 warmer.</summary>
    public float Warmth { get; set; }
    public int ColorVersion { get; set; } = 2;

    public SyncSettings() { }

    /// <summary>A file without colorVersion was written by an early version.</summary>
    void IJsonOnDeserializing.OnDeserializing() => ColorVersion = 1;

    void IJsonOnDeserialized.OnDeserialized() => MigrateIfNeeded();

    internal void MigrateIfNeeded()
    {
        if (ColorVersion < 2)
        {   // v1 darkened mid-tones with gamma 1.2 and had no style
            if (Math.Abs(Gamma - 1.2f) < 0.001f) Gamma = 1f;
            ColorVersion = 2;
        }
    }

    public SyncSettings Clone() => Json.Clone(this);
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = Make();

    static JsonSerializerOptions Make()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }

    public static T Clone<T>(T v) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!;
    public static string Write<T>(T v) => JsonSerializer.Serialize(v, Options);
    public static T? Read<T>(string s) => JsonSerializer.Deserialize<T>(s, Options);
}

/// <summary>Reads and writes the same shape as the macOS app ({"system":{}}, {"app":{"_0":"id"}}), so profiles are portable.</summary>
sealed class AudioInputConverter : JsonConverter<AudioInput>
{
    public override AudioInput Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        using var doc = JsonDocument.ParseValue(ref r);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.String) return new AudioInput(root.GetString() ?? "system");
        if (root.ValueKind != JsonValueKind.Object) return AudioInput.System;
        foreach (var p in root.EnumerateObject())
        {
            if (p.NameEquals("app") && p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("_0", out var id)) return AudioInput.ForApp(id.GetString() ?? "");
            if (p.NameEquals("microphone")) return AudioInput.Microphone;
            if (p.NameEquals("system")) return AudioInput.System;
        }
        return AudioInput.System;
    }

    public override void Write(Utf8JsonWriter w, AudioInput v, JsonSerializerOptions o)
    {
        w.WriteStartObject();
        w.WritePropertyName(v.Kind);
        w.WriteStartObject();
        if (v.Kind == "app") w.WriteString("_0", v.App ?? "");
        w.WriteEndObject();
        w.WriteEndObject();
    }
}

/// <summary>Swift encodes [Int: Float] as a flat array [k, v, k, v]; objects are accepted too.</summary>
sealed class ChannelGainConverter : JsonConverter<Dictionary<int, float>>
{
    public override Dictionary<int, float> Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        var d = new Dictionary<int, float>();
        using var doc = JsonDocument.ParseValue(ref r);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            var a = root.EnumerateArray().ToList();
            for (int i = 0; i + 1 < a.Count; i += 2) d[a[i].GetInt32()] = a[i + 1].GetSingle();
        }
        else if (root.ValueKind == JsonValueKind.Object)
            foreach (var p in root.EnumerateObject()) if (int.TryParse(p.Name, out var k)) d[k] = p.Value.GetSingle();
        return d;
    }

    public override void Write(Utf8JsonWriter w, Dictionary<int, float> v, JsonSerializerOptions o)
    {
        w.WriteStartObject();
        foreach (var kv in v.OrderBy(k => k.Key)) w.WriteNumber(kv.Key.ToString(), kv.Value);
        w.WriteEndObject();
    }
}
