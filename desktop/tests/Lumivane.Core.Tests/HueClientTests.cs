using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Lumivane.Core;
using Xunit;

namespace Lumivane.Core.Tests;

/// <summary>Tiny in-process stand-in for a Hue Bridge (plain HTTP; the client takes a base URL override).</summary>
sealed class FakeBridgeServer : IDisposable
{
    readonly HttpListener l = new();
    public string Url { get; }
    public List<(string Method, string Path, string Body, string? Key)> Requests { get; } = new();
    public Func<string, string, string, (int Status, string Body)?> Override = (_, _, _) => null;
    public FakeBridgeServer()
    {
        int port; using (var s = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) { s.Start(); port = ((IPEndPoint)s.LocalEndpoint).Port; }
        Url = $"http://127.0.0.1:{port}"; l.Prefixes.Add(Url + "/"); l.Start();
        _ = Task.Run(Loop);
    }
    async Task Loop()
    {
        while (l.IsListening)
        {
            HttpListenerContext c;
            try { c = await l.GetContextAsync(); } catch { return; }
            var body = await new StreamReader(c.Request.InputStream).ReadToEndAsync();
            var path = c.Request.Url!.AbsolutePath; var method = c.Request.HttpMethod;
            lock (Requests) Requests.Add((method, path, body, c.Request.Headers["hue-application-key"]));
            var (status, resp) = Override(method, path, body) ?? Route(method, path);
            c.Response.StatusCode = status;
            if (path == "/auth/v1") c.Response.Headers["hue-application-id"] = "the-app-id";
            var bytes = Encoding.UTF8.GetBytes(resp);
            c.Response.ContentType = "application/json";
            await c.Response.OutputStream.WriteAsync(bytes); c.Response.Close();
        }
    }
    static (int, string) Route(string method, string path) => (method, path) switch
    {
        ("GET", "/api/config") => (200, """{"name":"Hue Bridge","swversion":"1976154040","apiversion":"1.65.0","bridgeid":"001788FFFE123456","modelid":"BSB002"}"""),
        ("GET", "/clip/v2/resource/light") => (200, """{"errors":[],"data":[{"id":"l1","owner":{"rid":"d1","rtype":"device"},"metadata":{"name":"Bar","archetype":"hue_play"},"on":{"on":true},"dimming":{"brightness":70.5},"color":{"gamut":{"red":{"x":0.675,"y":0.322},"green":{"x":0.409,"y":0.518},"blue":{"x":0.167,"y":0.04}}}},{"id":"l2","owner":{"rid":"d2","rtype":"device"},"metadata":{"name":"Lamp"},"on":{"on":false}}]}"""),
        ("GET", "/clip/v2/resource/device") => (200, """{"errors":[],"data":[{"id":"d1","services":[{"rid":"l1","rtype":"light"},{"rid":"e1","rtype":"entertainment"}]},{"id":"d2","services":[{"rid":"l2","rtype":"light"}]}]}"""),
        ("GET", "/clip/v2/resource/room") => (200, """{"errors":[],"data":[{"id":"r1","metadata":{"name":"Living","archetype":"living_room"},"children":[{"rid":"d1","rtype":"device"},{"rid":"d2","rtype":"device"}],"services":[{"rid":"gl1","rtype":"grouped_light"}]}]}"""),
        ("GET", "/clip/v2/resource/zone") => (200, """{"errors":[],"data":[]}"""),
        ("GET", "/clip/v2/resource/scene") => (200, """{"errors":[],"data":[{"id":"s1","metadata":{"name":"Relax"},"group":{"rid":"r1","rtype":"room"},"status":{"active":"static"}}]}"""),
        ("GET", "/clip/v2/resource/entertainment_configuration") => (200, """{"errors":[],"data":[{"id":"a1","metadata":{"name":"TV"},"configuration_type":"screen","status":"inactive","channels":[{"channel_id":0,"position":{"x":-0.9,"y":0.8,"z":0},"members":[{"service":{"rid":"e1","rtype":"entertainment"},"index":0}]}]}]}"""),
        ("POST", "/api") => (200, """[{"success":{"username":"user123","clientkey":"00112233445566778899AABBCCDDEEFF"}}]"""),
        ("GET", "/auth/v1") => (200, "{}"),
        _ => (200, """{"errors":[],"data":[{"rid":"x","rtype":"y"}]}"""),
    };
    public void Dispose() { l.Stop(); l.Close(); }
}

public class HueClientTests
{
    static HueClient Client(FakeBridgeServer s, bool authed = true) => new(new BridgeInfo("", "127.0.0.1"), authed ? new Credentials("appkey", "ck") : null, baseUrl: s.Url);

    [Fact] public async Task ConfigAndDiagnostics()
    {
        using var s = new FakeBridgeServer(); using var c = Client(s);
        var (info, ms) = await c.ConfigAsync();
        Assert.Equal("001788FFFE123456", info.Id); Assert.Equal("BSB002", info.Model); Assert.True(ms >= 0);
        var d = await c.DiagnosticsAsync();
        Assert.True(d.Reachable); Assert.Equal(2, d.LightCount); Assert.Equal(1, d.EntertainmentLightCount); Assert.Single(d.Areas);
    }

    [Fact] public async Task ParsesLightsGroupsScenesAreas()
    {
        using var s = new FakeBridgeServer(); using var c = Client(s);
        var lights = await c.LightsAsync();
        Assert.Equal("e1", lights[0].EntertainmentServiceId); Assert.Null(lights[1].EntertainmentServiceId);
        Assert.Equal(70.5, lights[0].Brightness); Assert.NotNull(lights[0].Gamut); Assert.True(lights[0].IsOn); Assert.False(lights[1].IsOn);
        var groups = await c.GroupsAsync();
        Assert.Equal(new[] { "l1", "l2" }, groups[0].LightIds); Assert.Equal("gl1", groups[0].GroupedLightId);
        var scenes = await c.ScenesAsync(); Assert.True(scenes[0].IsActive);
        var areas = await c.AreasAsync(); Assert.Equal("e1", areas[0].Channels[0].Services[0]);
        Assert.All(s.Requests.Where(r => r.Path.StartsWith("/clip")), r => Assert.Equal("appkey", r.Key));
    }

    [Fact] public async Task PairReturnsCredentials()
    {
        using var s = new FakeBridgeServer(); using var c = Client(s, authed: false);
        var cr = await c.PairAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("user123", cr.AppKey); Assert.Equal(32, cr.ClientKey.Length);
        Assert.Equal(cr, c.Credentials);
    }

    [Fact] public async Task PairWaitsForLinkButton()
    {
        using var s = new FakeBridgeServer();
        s.Override = (m, p, _) => m == "POST" && p == "/api" ? (200, """[{"error":{"type":101,"description":"link button not pressed"}}]""") : null;
        using var c = Client(s, authed: false);
        var e = await Assert.ThrowsAsync<HueException>(() => c.PairAsync(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(HueErrorKind.LinkButtonNotPressed, e.Kind);
    }

    [Fact] public async Task ApplicationIdComesFromHeader()
    {
        using var s = new FakeBridgeServer(); using var c = Client(s);
        Assert.Equal("the-app-id", await c.ApplicationIdAsync());
    }

    [Fact] public async Task AuthedCallWithoutCredentialsThrows()
    {
        using var s = new FakeBridgeServer(); using var c = Client(s, authed: false);
        var e = await Assert.ThrowsAsync<HueException>(() => c.LightsAsync());
        Assert.Equal(HueErrorKind.NoCredentials, e.Kind);
    }

    [Fact] public async Task BridgeErrorBodyIsSurfaced()
    {
        using var s = new FakeBridgeServer();
        s.Override = (m, p, _) => m == "PUT" ? (400, """{"errors":[{"description":"invalid parameter: locations"}],"data":[]}""") : null;
        using var c = Client(s);
        var e = await Assert.ThrowsAsync<HueException>(() => c.SetStreamingAsync("a1", true));
        Assert.Equal(HueErrorKind.Api, e.Kind); Assert.Contains("invalid parameter", e.Message);
    }

    [Fact] public async Task UnreachableBridgeIsReported()
    {
        using var c = new HueClient(new BridgeInfo("", "127.0.0.1"), new Credentials("k", "c"), baseUrl: "http://127.0.0.1:1");
        var e = await Assert.ThrowsAsync<HueException>(() => c.ConfigAsync());
        Assert.Equal(HueErrorKind.Unreachable, e.Kind);
        Assert.False((await c.DiagnosticsAsync()).Reachable);
        Assert.Null(await HueClient.ProbeAsync("127.0.0.1:1"));
    }

    [Fact] public async Task SetLightSendsXyAndClampedBrightness()
    {
        using var s = new FakeBridgeServer(); using var c = Client(s);
        await c.SetLightAsync("l1", true, 0, new RGB(1, 0, 0), null, 150);
        var r = s.Requests.Last(); Assert.Equal("PUT", r.Method); Assert.Equal("/clip/v2/resource/light/l1", r.Path);
        var j = JsonNode.Parse(r.Body)!;
        Assert.True(j["on"]!["on"]!.GetValue<bool>()); Assert.Equal(1, j["dimming"]!["brightness"]!.GetValue<double>()); Assert.Equal(150, j["dynamics"]!["duration"]!.GetValue<int>());
        Assert.InRange(j["color"]!["xy"]!["x"]!.GetValue<double>(), 0.68, 0.71);
    }

    [Fact] public async Task CreateAreaBodyAndUpdateAreaUsesLocationsOnly()
    {
        using var s = new FakeBridgeServer(); using var c = Client(s);
        var lights = new[] { new HueLight("l1", "A", EntertainmentServiceId: "e1"), new HueLight("l2", "B", EntertainmentServiceId: "e2") };
        await c.CreateAreaAsync("Lumivane", lights);
        var post = JsonNode.Parse(s.Requests.Last().Body)!;
        Assert.Equal("screen", post["configuration_type"]!.GetValue<string>());
        Assert.Equal(2, post["light_services"]!.AsArray().Count);
        var locs = post["locations"]!["service_locations"]!.AsArray();
        Assert.Equal(-0.9, locs[0]!["positions"]![0]!["x"]!.GetValue<double>(), 3); Assert.Equal(0.9, locs[1]!["positions"]![0]!["x"]!.GetValue<double>(), 3);

        // the bridge already holds e1 only, so the new set {e1, e2} does not match: PUT (locations only), then delete + recreate
        await c.UpdateAreaAsync("a1", "Lumivane", lights);
        var put = s.Requests.First(r => r.Method == "PUT" && r.Path.EndsWith("/a1"));
        var pj = JsonNode.Parse(put.Body)!.AsObject();
        Assert.True(pj.ContainsKey("locations")); Assert.False(pj.ContainsKey("light_services"));
        Assert.Contains(s.Requests, r => r.Method == "DELETE" && r.Path.EndsWith("/entertainment_configuration/a1"));
        Assert.True(s.Requests.Count(r => r.Method == "POST" && r.Path.EndsWith("entertainment_configuration")) >= 2);
    }

    [Fact] public async Task UpdateAreaDoesNotRecreateWhenBridgeTookTheChange()
    {
        using var s = new FakeBridgeServer(); using var c = Client(s);
        await c.UpdateAreaAsync("a1", "TV", new[] { new HueLight("l1", "A", EntertainmentServiceId: "e1") });
        Assert.DoesNotContain(s.Requests, r => r.Method == "DELETE");
    }
}

public class MdnsTests
{
    static byte[] Name(string n) { var ms = new MemoryStream(); foreach (var l in n.Split('.')) { ms.WriteByte((byte)l.Length); ms.Write(Encoding.ASCII.GetBytes(l)); } ms.WriteByte(0); return ms.ToArray(); }

    static byte[] Response()
    {
        var ms = new MemoryStream();
        void U16(int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        void U32(int v) { U16(v >> 16); U16(v & 0xFFFF); }
        U16(0); U16(0x8400); U16(0); U16(3); U16(0); U16(0);
        // PTR _hue._tcp.local -> bridge._hue._tcp.local
        ms.Write(Name("_hue._tcp.local")); U16(12); U16(1); U32(120);
        var target = Name("bridge._hue._tcp.local"); U16(target.Length); ms.Write(target);
        // TXT with bridgeid
        ms.Write(Name("bridge._hue._tcp.local")); U16(16); U16(1); U32(120);
        var txt = Encoding.ASCII.GetBytes("bridgeid=001788fffe123456"); U16(txt.Length + 1); ms.WriteByte((byte)txt.Length); ms.Write(txt);
        // A
        ms.Write(Name("philips-hue.local")); U16(1); U16(1); U32(120); U16(4); ms.Write(new byte[] { 192, 168, 1, 50 });
        return ms.ToArray();
    }

    [Fact] public void QueryIsWellFormed()
    {
        var q = MdnsPacket.BuildQuery("_hue._tcp.local");
        Assert.Equal(1, q[5]);                       // one question
        Assert.Equal(12, q[^3]);                     // PTR
    }

    [Fact] public void ParsesBridgeFromResponse()
    {
        var b = MdnsPacket.ParseHueAnswers(Response());
        Assert.Single(b); Assert.Equal("192.168.1.50", b[0].Ip); Assert.Equal("001788FFFE123456", b[0].Id);
    }

    [Fact] public void IgnoresGarbageAndOtherServices()
    {
        Assert.Empty(MdnsPacket.ParseHueAnswers(new byte[] { 1, 2, 3 }));
        Assert.Empty(MdnsPacket.ParseHueAnswers(new byte[200]));
    }
}
