using System.Globalization;
using System.Text.Json.Serialization;

namespace Lumivane.Core;

public sealed class BridgeInfo
{
    public string Id { get; set; } = "";
    public string Ip { get; set; } = "";
    public string? Name { get; set; }
    public string? Firmware { get; set; }
    public string? ApiVersion { get; set; }
    public string? Model { get; set; }
    public BridgeInfo() { }
    public BridgeInfo(string id, string ip, string? name = null, string? firmware = null, string? apiVersion = null, string? model = null)
    {
        Id = string.IsNullOrEmpty(id) ? ip : id; Ip = ip; Name = name; Firmware = firmware; ApiVersion = apiVersion; Model = model;
    }
}

public sealed record Credentials(string AppKey, string ClientKey);

public readonly record struct XY(double X, double Y);

public sealed record Gamut(XY Red, XY Green, XY Blue);

public sealed record HueLight(string Id, string Name, string? Archetype = null, bool IsOn = false, double Brightness = 100,
                              Gamut? Gamut = null, string? EntertainmentServiceId = null)
{
    public bool SupportsEntertainment => EntertainmentServiceId != null;
}

public sealed record EntertainmentChannel(int Id, double X, double Y, double Z, IReadOnlyList<string>? ServiceIds = null)
{
    public IReadOnlyList<string> Services => ServiceIds ?? Array.Empty<string>();
}

public sealed record EntertainmentArea(string Id, string Name, string Type, bool IsActive, IReadOnlyList<EntertainmentChannel> Channels);

public sealed record HueScene(string Id, string Name, string GroupId, string GroupType, bool IsActive);

public enum GroupKind { Room, Zone }

public sealed record HueGroup(string Id, string Name, GroupKind Kind, string? Archetype, IReadOnlyList<string> LightIds, string? GroupedLightId);

public sealed record Diagnostics(BridgeInfo Bridge, bool Reachable, double? LatencyMs, int LightCount, int EntertainmentLightCount, IReadOnlyList<EntertainmentArea> Areas)
{
    public string Text => Report(false);

    /// <summary>Plain-text summary for support requests. With <paramref name="redacted"/>, network and device identifiers are masked
    /// and area names are replaced by counts.</summary>
    public string Report(bool redacted)
    {
        var ip = redacted ? MaskIP(Bridge.Ip) : Bridge.Ip;
        var id = redacted ? MaskID(Bridge.Id) : Bridge.Id;
        var areaText = Areas.Count == 0 ? "none" : string.Join(", ", Areas.Select((a, i) => $"{(redacted ? $"Area {i + 1}" : a.Name)} ({a.Channels.Count} ch)"));
        var lat = LatencyMs is double l ? l.ToString("F1", CultureInfo.InvariantCulture) + " ms" : "n/a";
        return string.Join("\n",
            $"Bridge IP:                 {ip}",
            $"Bridge ID:                 {id}",
            $"Model:                     {Bridge.Model ?? "unknown"}",
            $"Firmware (swversion):      {Bridge.Firmware ?? "unknown"}",
            $"API version:               {Bridge.ApiVersion ?? "unknown"}",
            $"Reachable:                 {(Reachable ? "yes" : "no")}",
            $"Response latency:          {lat}",
            $"Connected lights:          {LightCount}",
            $"Entertainment-capable:     {EntertainmentLightCount}",
            $"Entertainment areas:       {areaText}");
    }

    internal static string MaskIP(string ip)
    {
        var p = ip.Split('.');
        return p.Length == 4 ? $"{p[0]}.{p[1]}.x.x" : "x.x.x.x";
    }

    internal static string MaskID(string id) => id.Length > 4 ? id[..4] + new string('x', id.Length - 4) : id;
}
