using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace Lumivane.Core;

public static class BridgeDiscovery
{
    sealed record CloudEntry(string Id, string InternalIpAddress);

    /// <summary>https://discovery.meethue.com</summary>
    public static async Task<List<BridgeInfo>> CloudAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var json = await http.GetStringAsync("https://discovery.meethue.com", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray()
                .Select(e => new BridgeInfo(e.GetProperty("id").GetString()!.ToUpperInvariant(), e.GetProperty("internalipaddress").GetString()!)).ToList();
        }
        catch (Exception) { return new(); }
    }

    /// <summary>mDNS, service type _hue._tcp.local. Sends one PTR query on every IPv4 interface and collects A records from the answers.</summary>
    public static async Task<List<BridgeInfo>> MdnsAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var found = new Dictionary<string, BridgeInfo>();
        var end = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(4));
        var query = MdnsPacket.BuildQuery("_hue._tcp.local");
        var group = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);
        var sockets = new List<UdpClient>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                {
                    try
                    {
                        var s = new UdpClient(new IPEndPoint(ua.Address, 0));
                        s.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                        await s.SendAsync(query, query.Length, group).ConfigureAwait(false);
                        sockets.Add(s);
                    }
                    catch (SocketException) { }
                }
            }
            var tasks = sockets.Select(async s =>
            {
                while (DateTime.UtcNow < end && !ct.IsCancellationRequested)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(end - DateTime.UtcNow > TimeSpan.Zero ? end - DateTime.UtcNow : TimeSpan.FromMilliseconds(1));
                    try
                    {
                        var r = await s.ReceiveAsync(cts.Token).ConfigureAwait(false);
                        foreach (var b in MdnsPacket.ParseHueAnswers(r.Buffer)) lock (found) found[b.Ip] = b;
                    }
                    catch (OperationCanceledException) { break; }
                    catch (SocketException) { break; }
                }
            });
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally { foreach (var s in sockets) s.Dispose(); }
        return found.Values.ToList();
    }

    /// <summary>Local discovery (mDNS). The Philips cloud lookup only runs when <paramref name="allowCloud"/> is true, because it
    /// sends this network's public IP address to Philips. Every candidate is verified against the bridge itself.</summary>
    public static async Task<List<BridgeInfo>> DiscoverAsync(bool allowCloud = false, CancellationToken ct = default)
    {
        var m = MdnsAsync(ct: ct);
        var c = allowCloud ? CloudAsync(ct) : Task.FromResult(new List<BridgeInfo>());
        var candidates = (await m.ConfigureAwait(false)).Concat(await c.ConfigureAwait(false));
        var unique = candidates.GroupBy(x => x.Ip).Select(g => g.First()).ToList();
        var probes = await Task.WhenAll(unique.Select(async x => (await HueClient.ProbeAsync(x.Ip, ct).ConfigureAwait(false))?.Info));
        return probes.Where(p => p != null).Select(p => p!).OrderBy(p => p.Ip, StringComparer.Ordinal).ToList();
    }
}

/// <summary>Just enough DNS wire format for one PTR query and the answers to it.</summary>
public static class MdnsPacket
{
    public static byte[] BuildQuery(string name)
    {
        var ms = new MemoryStream();
        void U16(int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        U16(0); U16(0); U16(1); U16(0); U16(0); U16(0);
        foreach (var label in name.Split('.')) { var b = System.Text.Encoding.ASCII.GetBytes(label); ms.WriteByte((byte)b.Length); ms.Write(b); }
        ms.WriteByte(0);
        U16(12); U16(0x8001);   // PTR, IN, unicast-response requested
        return ms.ToArray();
    }

    /// <summary>Extracts bridges from an mDNS response: A records give the address, TXT records the bridge id.</summary>
    public static List<BridgeInfo> ParseHueAnswers(byte[] p)
    {
        var res = new List<BridgeInfo>();
        try
        {
            int pos = 12;
            int qd = U16(p, 4), an = U16(p, 6) + U16(p, 8) + U16(p, 10);
            for (int i = 0; i < qd; i++) { pos = SkipName(p, pos) + 4; }
            var addrs = new Dictionary<string, string>();     // host name -> ip
            string? bridgeId = null; bool isHue = false;
            for (int i = 0; i < an && pos < p.Length; i++)
            {
                var name = ReadName(p, pos, out pos);
                int type = U16(p, pos), rdlen = U16(p, pos + 8), rd = pos + 10;
                pos = rd + rdlen;
                if (type == 12 && name.EndsWith("_hue._tcp.local", StringComparison.OrdinalIgnoreCase)) isHue = true;
                else if (type == 1 && rdlen == 4) addrs[name.ToLowerInvariant()] = new IPAddress(p.AsSpan(rd, 4)).ToString();
                else if (type == 16 && name.Contains("_hue._tcp", StringComparison.OrdinalIgnoreCase))
                {
                    isHue = true;
                    for (int q = rd; q < rd + rdlen;)
                    {
                        int l = p[q++]; var s = System.Text.Encoding.UTF8.GetString(p, q, l); q += l;
                        if (s.StartsWith("bridgeid=", StringComparison.OrdinalIgnoreCase)) bridgeId = s[9..].ToUpperInvariant();
                    }
                }
                else if (type == 33 && name.Contains("_hue._tcp", StringComparison.OrdinalIgnoreCase)) isHue = true;
            }
            if (isHue) foreach (var ip in addrs.Values.Distinct()) res.Add(new BridgeInfo(bridgeId ?? "", ip));
        }
        catch (IndexOutOfRangeException) { }
        catch (ArgumentException) { }
        return res;
    }

    static int U16(byte[] p, int o) => (p[o] << 8) | p[o + 1];

    static int SkipName(byte[] p, int o)
    {
        while (true)
        {
            int l = p[o];
            if (l == 0) return o + 1;
            if ((l & 0xC0) == 0xC0) return o + 2;
            o += l + 1;
        }
    }

    static string ReadName(byte[] p, int o, out int next)
    {
        var parts = new List<string>(); int jumps = 0; next = -1;
        while (true)
        {
            int l = p[o];
            if (l == 0) { if (next < 0) next = o + 1; break; }
            if ((l & 0xC0) == 0xC0)
            {
                if (next < 0) next = o + 2;
                o = ((l & 0x3F) << 8) | p[o + 1];
                if (++jumps > 16) break;
                continue;
            }
            parts.Add(System.Text.Encoding.UTF8.GetString(p, o + 1, l)); o += l + 1;
        }
        return string.Join(".", parts);
    }
}
