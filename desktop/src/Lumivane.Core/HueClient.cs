using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lumivane.Core;

public enum HueErrorKind { LinkButtonNotPressed, NoCredentials, Unreachable, Api, BadResponse, StreamFailed }

public sealed class HueException : Exception
{
    public HueErrorKind Kind { get; }
    HueException(HueErrorKind kind, string message) : base(message) { Kind = kind; }
    public static HueException LinkButton() => new(HueErrorKind.LinkButtonNotPressed, "Press the round button on top of your Hue Bridge, then try again.");
    public static HueException NoCredentials() => new(HueErrorKind.NoCredentials, "This computer is not paired with a bridge yet.");
    public static HueException Unreachable(string m) => new(HueErrorKind.Unreachable, $"Can't reach your Hue Bridge. Check that it is powered and on the same network. ({m})");
    public static HueException Api(string m) => new(HueErrorKind.Api, $"The bridge said: {m}");
    public static HueException BadResponse(int code) => new(HueErrorKind.BadResponse, $"Unexpected answer from the bridge (HTTP {code}).");
    public static HueException StreamFailed(string m) => new(HueErrorKind.StreamFailed, $"Couldn't start the light stream. {m}");
}

/// <summary>Everything the sync engine and UI need from a bridge. Lets tests and demo mode substitute a fake.</summary>
public interface IHueBridge
{
    BridgeInfo Bridge { get; }
    Task<(BridgeInfo Info, double LatencyMs)> ConfigAsync(CancellationToken ct = default);
    Task<IReadOnlyList<HueLight>> LightsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<HueGroup>> GroupsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<HueScene>> ScenesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EntertainmentArea>> AreasAsync(CancellationToken ct = default);
    Task<Diagnostics> DiagnosticsAsync(CancellationToken ct = default);
    Task<string> ApplicationIdAsync(CancellationToken ct = default);
    Task SetLightAsync(string id, bool? on = null, double? brightness = null, RGB? rgb = null, Gamut? gamut = null, int? durationMs = null, CancellationToken ct = default);
    Task SetGroupAsync(string groupedLightId, bool? on = null, double? brightness = null, CancellationToken ct = default);
    Task RecallAsync(string sceneId, CancellationToken ct = default);
    Task CreateZoneAsync(string name, IReadOnlyList<string> lightIds, CancellationToken ct = default);
    Task UpdateZoneAsync(string id, string? name = null, IReadOnlyList<string>? lightIds = null, CancellationToken ct = default);
    Task DeleteZoneAsync(string id, CancellationToken ct = default);
    Task SetStreamingAsync(string areaId, bool active, CancellationToken ct = default);
    Task CreateAreaAsync(string name, IReadOnlyList<HueLight> lights, CancellationToken ct = default);
    Task UpdateAreaAsync(string id, string name, IReadOnlyList<HueLight> lights, CancellationToken ct = default);
}

/// <summary>Hue API v2 client. The bridge presents a certificate signed by a private Signify CA (or self-signed on old firmware),
/// so trust is limited to the one bridge address the user selected.</summary>
public sealed class HueClient : IHueBridge, IDisposable
{
    public BridgeInfo Bridge { get; }
    public Credentials? Credentials { get; set; }
    readonly HttpClient http;

    public HueClient(BridgeInfo bridge, Credentials? credentials = null, HttpMessageHandler? handler = null, string? baseUrl = null)
    {
        Bridge = bridge; Credentials = credentials;
        handler ??= new SocketsHttpHandler
        {
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },   // scoped: this client only ever talks to Bridge.Ip
            MaxConnectionsPerServer = 4,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(5), BaseAddress = new Uri(baseUrl ?? $"https://{bridge.Ip}") };
    }

    public void Dispose() => http.Dispose();

    // ---- transport ----

    async Task<(string Body, int Status, HttpResponseHeaders Headers)> RawAsync(HttpMethod method, string path, string? body = null, bool authed = true, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(method, path);
        if (authed)
        {
            if (Credentials is not { } c) throw HueException.NoCredentials();
            req.Headers.Add("hue-application-key", c.AppKey);
        }
        if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (text, (int)resp.StatusCode, resp.Headers);
        }
        catch (HueException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { throw HueException.Unreachable(e.InnerException?.Message ?? e.Message); }
    }

    static string? ErrorOf(JsonNode? root) => root?["errors"]?.AsArray().FirstOrDefault()?["description"]?.GetValue<string>();

    async Task<JsonArray> ListAsync(string path, CancellationToken ct)
    {
        var (d, status, _) = await RawAsync(HttpMethod.Get, $"/clip/v2/resource/{path}", ct: ct).ConfigureAwait(false);
        if (status is < 200 or >= 300) throw HueException.BadResponse(status);
        JsonNode? root;
        try { root = JsonNode.Parse(d); } catch (JsonException) { throw HueException.BadResponse(status); }
        if (ErrorOf(root) is { } e) throw HueException.Api(e);
        return root?["data"] as JsonArray ?? new JsonArray();
    }

    async Task WriteAsync(HttpMethod method, string path, JsonNode json, CancellationToken ct)
    {
        var (d, status, _) = await RawAsync(method, $"/clip/v2/resource/{path}", json.ToJsonString(), ct: ct).ConfigureAwait(false);
        JsonNode? root = null;
        try { root = JsonNode.Parse(d); } catch (JsonException) { }
        if (ErrorOf(root) is { } e) throw HueException.Api(e);
        if (status is < 200 or >= 300) throw HueException.BadResponse(status);
    }

    // ---- bridge ----

    /// <summary>Unauthenticated bridge info; null if the address isn't a Hue Bridge.</summary>
    public static async Task<(BridgeInfo Info, double LatencyMs)?> ProbeAsync(string ip, CancellationToken ct = default)
    {
        using var c = new HueClient(new BridgeInfo("", ip));
        try { return await c.ConfigAsync(ct).ConfigureAwait(false); } catch (Exception) { return null; }
    }

    public async Task<(BridgeInfo Info, double LatencyMs)> ConfigAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var (d, status, _) = await RawAsync(HttpMethod.Get, "/api/config", authed: false, ct: ct).ConfigureAwait(false);
        double ms = sw.Elapsed.TotalMilliseconds;
        JsonNode? c = null;
        try { c = JsonNode.Parse(d); } catch (JsonException) { }
        if (status != 200 || c?["bridgeid"]?.GetValue<string>() is not { } bid) throw HueException.BadResponse(status);
        return (new BridgeInfo(bid, Bridge.Ip, Str(c, "name"), Str(c, "swversion"), Str(c, "apiversion"), Str(c, "modelid")), ms);
    }

    static string? Str(JsonNode? n, string key) { try { return n?[key]?.GetValue<string>(); } catch (InvalidOperationException) { return null; } }

    /// <summary>Polls until the link button is pressed or the timeout passes.</summary>
    public async Task<Credentials> PairAsync(TimeSpan? timeout = null, string deviceType = "lumivane#desktop", CancellationToken ct = default)
    {
        var body = new JsonObject { ["devicetype"] = deviceType, ["generateclientkey"] = true }.ToJsonString();
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(40));
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var (d, _, _) = await RawAsync(HttpMethod.Post, "/api", body, authed: false, ct: ct).ConfigureAwait(false);
            try
            {
                if (JsonNode.Parse(d) is JsonArray { Count: > 0 } items)
                {
                    var it = items[0]!;
                    if (it["success"] is { } s && Str(s, "clientkey") is { } ck && Str(s, "username") is { } user)
                    {
                        var cr = new Credentials(user, ck);
                        Credentials = cr;
                        return cr;
                    }
                    if (it["error"] is { } er && er["type"]?.GetValue<int>() != 101) throw HueException.Api(Str(er, "description") ?? "pairing failed");
                }
            }
            catch (JsonException) { }
            if (DateTime.UtcNow > deadline) throw HueException.LinkButton();
            await Task.Delay(1500, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Needed as the DTLS PSK identity.</summary>
    public async Task<string> ApplicationIdAsync(CancellationToken ct = default)
    {
        var (_, status, headers) = await RawAsync(HttpMethod.Get, "/auth/v1", ct: ct).ConfigureAwait(false);
        if (headers.TryGetValues("hue-application-id", out var v) && v.FirstOrDefault() is { } id) return id;
        throw HueException.BadResponse(status);
    }

    // ---- read ----

    static IEnumerable<JsonNode> Nodes(JsonArray a) => a.Where(n => n != null).Select(n => n!);
    static string? Rid(JsonNode? n) => Str(n, "rid");

    public async Task<IReadOnlyList<HueLight>> LightsAsync(CancellationToken ct = default)
    {
        var lt = ListAsync("light", ct); var dt = ListAsync("device", ct);
        var lights = await lt.ConfigureAwait(false); var devices = await dt.ConfigureAwait(false);
        var entByDevice = new Dictionary<string, string>();
        foreach (var dev in Nodes(devices))
            if (Str(dev, "id") is { } id && dev["services"] is JsonArray svcs)
                foreach (var s in Nodes(svcs)) if (Str(s, "rtype") == "entertainment" && Rid(s) is { } rid) { entByDevice[id] = rid; break; }
        var o = new List<HueLight>();
        foreach (var x in Nodes(lights))
        {
            Gamut? g = null;
            if (x["color"]?["gamut"] is JsonNode gm)
            {
                static XY P(JsonNode? n) => new(n?["x"]?.GetValue<double>() ?? 0, n?["y"]?.GetValue<double>() ?? 0);
                g = new Gamut(P(gm["red"]), P(gm["green"]), P(gm["blue"]));
            }
            var owner = Rid(x["owner"]);
            o.Add(new HueLight(Str(x, "id") ?? "", Str(x["metadata"], "name") ?? "Light", Str(x["metadata"], "archetype"),
                x["on"]?["on"]?.GetValue<bool>() ?? false, x["dimming"]?["brightness"]?.GetValue<double>() ?? 100, g,
                owner != null && entByDevice.TryGetValue(owner, out var es) ? es : null));
        }
        return o;
    }

    public async Task<IReadOnlyList<HueGroup>> GroupsAsync(CancellationToken ct = default)
    {
        var rt = ListAsync("room", ct); var zt = ListAsync("zone", ct); var dt = ListAsync("device", ct);
        var rooms = await rt.ConfigureAwait(false); var zones = await zt.ConfigureAwait(false); var devices = await dt.ConfigureAwait(false);
        var lightByDevice = new Dictionary<string, string>();
        foreach (var dev in Nodes(devices))
            if (Str(dev, "id") is { } id && dev["services"] is JsonArray svcs)
                foreach (var s in Nodes(svcs)) if (Str(s, "rtype") == "light" && Rid(s) is { } rid) { lightByDevice[id] = rid; break; }
        HueGroup Map(JsonNode g, GroupKind k)
        {
            var ids = new List<string>();
            if (g["children"] is JsonArray ch)
                foreach (var c in Nodes(ch))
                {
                    var rtype = Str(c, "rtype"); var rid = Rid(c);
                    if (rid == null) continue;
                    if (rtype == "light") ids.Add(rid);
                    else if (rtype == "device" && lightByDevice.TryGetValue(rid, out var l)) ids.Add(l);
                }
            string? gl = null;
            if (g["services"] is JsonArray ss) foreach (var s in Nodes(ss)) if (Str(s, "rtype") == "grouped_light") { gl = Rid(s); break; }
            return new HueGroup(Str(g, "id") ?? "", Str(g["metadata"], "name") ?? (k == GroupKind.Room ? "Room" : "Zone"), k, Str(g["metadata"], "archetype"), ids, gl);
        }
        return Nodes(rooms).Select(g => Map(g, GroupKind.Room)).Concat(Nodes(zones).Select(g => Map(g, GroupKind.Zone))).ToList();
    }

    public async Task<IReadOnlyList<HueScene>> ScenesAsync(CancellationToken ct = default) =>
        Nodes(await ListAsync("scene", ct).ConfigureAwait(false)).Select(s =>
            new HueScene(Str(s, "id") ?? "", Str(s["metadata"], "name") ?? "Scene", Rid(s["group"]) ?? "", Str(s["group"], "rtype") ?? "",
                (Str(s["status"], "active") ?? "inactive") != "inactive")).ToList();

    public async Task<IReadOnlyList<EntertainmentArea>> AreasAsync(CancellationToken ct = default) =>
        Nodes(await ListAsync("entertainment_configuration", ct).ConfigureAwait(false)).Select(a =>
        {
            var chans = new List<EntertainmentChannel>();
            if (a["channels"] is JsonArray cs)
                foreach (var c in Nodes(cs))
                {
                    var p = c["position"];
                    chans.Add(new EntertainmentChannel(c["channel_id"]?.GetValue<int>() ?? 0, p?["x"]?.GetValue<double>() ?? 0, p?["y"]?.GetValue<double>() ?? 0, p?["z"]?.GetValue<double>() ?? 0,
                        (c["members"] as JsonArray)?.Select(m => Rid(m?["service"])).Where(x => x != null).Select(x => x!).ToList()));
                }
            return new EntertainmentArea(Str(a, "id") ?? "", Str(a["metadata"], "name") ?? "Entertainment area", Str(a, "configuration_type") ?? "other", Str(a, "status") == "active", chans);
        }).ToList();

    public async Task<Diagnostics> DiagnosticsAsync(CancellationToken ct = default)
    {
        (BridgeInfo Info, double LatencyMs) cfg;
        try { cfg = await ConfigAsync(ct).ConfigureAwait(false); }
        catch (Exception) when (!ct.IsCancellationRequested) { return new Diagnostics(Bridge, false, null, 0, 0, Array.Empty<EntertainmentArea>()); }
        IReadOnlyList<HueLight> lights = Array.Empty<HueLight>(); IReadOnlyList<EntertainmentArea> areas = Array.Empty<EntertainmentArea>();
        try { lights = await LightsAsync(ct).ConfigureAwait(false); } catch (Exception) when (!ct.IsCancellationRequested) { }
        try { areas = await AreasAsync(ct).ConfigureAwait(false); } catch (Exception) when (!ct.IsCancellationRequested) { }
        return new Diagnostics(cfg.Info, true, cfg.LatencyMs, lights.Count, lights.Count(l => l.SupportsEntertainment), areas);
    }

    // ---- control ----

    public Task SetLightAsync(string id, bool? on = null, double? brightness = null, RGB? rgb = null, Gamut? gamut = null, int? durationMs = null, CancellationToken ct = default)
    {
        var j = new JsonObject();
        if (on is bool o) j["on"] = new JsonObject { ["on"] = o };
        if (brightness is double b) j["dimming"] = new JsonObject { ["brightness"] = Math.Clamp(b, 1, 100) };
        if (rgb is RGB c) { var p = ColorMath.ToXY(c, gamut); j["color"] = new JsonObject { ["xy"] = new JsonObject { ["x"] = p.X, ["y"] = p.Y } }; }
        if (durationMs is int d) j["dynamics"] = new JsonObject { ["duration"] = d };
        return WriteAsync(HttpMethod.Put, $"light/{id}", j, ct);
    }

    public Task SetGroupAsync(string groupedLightId, bool? on = null, double? brightness = null, CancellationToken ct = default)
    {
        var j = new JsonObject();
        if (on is bool o) j["on"] = new JsonObject { ["on"] = o };
        if (brightness is double b) j["dimming"] = new JsonObject { ["brightness"] = Math.Clamp(b, 1, 100) };
        return WriteAsync(HttpMethod.Put, $"grouped_light/{groupedLightId}", j, ct);
    }

    public Task RecallAsync(string sceneId, CancellationToken ct = default) =>
        WriteAsync(HttpMethod.Put, $"scene/{sceneId}", new JsonObject { ["recall"] = new JsonObject { ["action"] = "active" } }, ct);

    static JsonArray LightRefs(IEnumerable<string> ids) => new(ids.Select(i => (JsonNode)new JsonObject { ["rid"] = i, ["rtype"] = "light" }).ToArray());

    public Task CreateZoneAsync(string name, IReadOnlyList<string> lightIds, CancellationToken ct = default) =>
        WriteAsync(HttpMethod.Post, "zone", new JsonObject { ["metadata"] = new JsonObject { ["name"] = name, ["archetype"] = "other" }, ["children"] = LightRefs(lightIds) }, ct);

    public Task UpdateZoneAsync(string id, string? name = null, IReadOnlyList<string>? lightIds = null, CancellationToken ct = default)
    {
        var j = new JsonObject();
        if (name != null) j["metadata"] = new JsonObject { ["name"] = name };
        if (lightIds != null) j["children"] = LightRefs(lightIds);
        return WriteAsync(HttpMethod.Put, $"zone/{id}", j, ct);
    }

    public async Task DeleteZoneAsync(string id, CancellationToken ct = default) =>
        await RawAsync(HttpMethod.Delete, $"/clip/v2/resource/zone/{id}", ct: ct).ConfigureAwait(false);

    public Task SetStreamingAsync(string areaId, bool active, CancellationToken ct = default) =>
        WriteAsync(HttpMethod.Put, $"entertainment_configuration/{areaId}", new JsonObject { ["action"] = active ? "start" : "stop" }, ct);

    static JsonObject Locations(IReadOnlyList<HueLight> lights)
    {
        int n = Math.Max(lights.Count, 1);
        var arr = new JsonArray();
        for (int i = 0; i < lights.Count; i++)
        {
            if (lights[i].EntertainmentServiceId is not { } svc) continue;
            double x = n == 1 ? 0 : -0.9 + 1.8 * i / (n - 1);
            arr.Add(new JsonObject
            {
                ["service"] = new JsonObject { ["rid"] = svc, ["rtype"] = "entertainment" },
                ["equalization_factor"] = 1,
                ["positions"] = new JsonArray(new JsonObject { ["x"] = x, ["y"] = 0.8, ["z"] = 0.0 }),
            });
        }
        return new JsonObject { ["service_locations"] = arr };
    }

    JsonObject AreaBody(string name, IReadOnlyList<HueLight> lights) => new()
    {
        ["metadata"] = new JsonObject { ["name"] = name }, ["configuration_type"] = "screen", ["stream_proxy"] = new JsonObject { ["mode"] = "auto" },
        ["light_services"] = LightRefs(lights.Select(l => l.Id)), ["locations"] = Locations(lights),
    };

    /// <summary>Creates a screen area from the given entertainment-capable lights, spread left to right in front of the screen.</summary>
    public Task CreateAreaAsync(string name, IReadOnlyList<HueLight> lights, CancellationToken ct = default) =>
        WriteAsync(HttpMethod.Post, "entertainment_configuration", AreaBody(name, lights), ct);

    /// <summary>Replaces the lights of an existing area; positions are reset to an even left-to-right spread. The bridge rejects
    /// light_services on PUT (HTTP 400), so only the locations are sent, and the area is recreated if the bridge did not take the new set.</summary>
    public async Task UpdateAreaAsync(string id, string name, IReadOnlyList<HueLight> lights, CancellationToken ct = default)
    {
        await WriteAsync(HttpMethod.Put, $"entertainment_configuration/{id}", new JsonObject { ["locations"] = Locations(lights) }, ct).ConfigureAwait(false);
        var now = (await AreasAsync(ct).ConfigureAwait(false)).FirstOrDefault(a => a.Id == id);
        var want = lights.Select(l => l.EntertainmentServiceId).Where(s => s != null).ToHashSet();
        if (now == null || !now.Channels.SelectMany(c => c.Services).ToHashSet().SetEquals(want!))
        {
            await RawAsync(HttpMethod.Delete, $"/clip/v2/resource/entertainment_configuration/{id}", ct: ct).ConfigureAwait(false);
            await CreateAreaAsync(name, lights, ct).ConfigureAwait(false);
        }
    }
}
