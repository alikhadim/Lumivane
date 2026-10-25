using System.Net;
using System.Net.Sockets;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace Lumivane.Core;

/// <summary>Where entertainment frames go. The DTLS streamer implements it; tests use a recording sink.</summary>
public interface IFrameSink : IDisposable
{
    void Send(IReadOnlyList<(byte Channel, RGB Rgb)> colors);
}

/// <summary>UDP socket as a BouncyCastle datagram transport.</summary>
public sealed class UdpDatagramTransport : DatagramTransport
{
    readonly UdpClient udp;
    readonly IPEndPoint remote;
    public UdpDatagramTransport(string host, int port)
    {
        remote = new IPEndPoint(IPAddress.Parse(host), port);
        udp = new UdpClient(remote.AddressFamily);
        udp.Connect(remote);
    }
    public int GetReceiveLimit() => 1400;
    public int GetSendLimit() => 1400;

    public int Receive(byte[] buf, int off, int len, int waitMillis)
    {
        var s = udp.Client;
        if (!s.Poll(Math.Max(1, waitMillis) * 1000, SelectMode.SelectRead)) return -1;
        try { return s.Receive(buf, off, len, SocketFlags.None); }
        catch (SocketException) { return -1; }
    }
    public int Receive(Span<byte> buffer, int waitMillis)
    {
        var s = udp.Client;
        if (!s.Poll(Math.Max(1, waitMillis) * 1000, SelectMode.SelectRead)) return -1;
        try { return s.Receive(buffer, SocketFlags.None); }
        catch (SocketException) { return -1; }
    }
    public void Send(byte[] buf, int off, int len) { try { udp.Client.Send(buf, off, len, SocketFlags.None); } catch (SocketException) { } }
    public void Send(ReadOnlySpan<byte> buffer) { try { udp.Client.Send(buffer, SocketFlags.None); } catch (SocketException) { } }
    public void Close() => udp.Dispose();
}

/// <summary>DTLS 1.2 with PSK, cipher suite TLS_PSK_WITH_AES_128_GCM_SHA256: the only suite the bridge accepts for streaming.</summary>
public static class HueDtls
{
    sealed class Client : PskTlsClient
    {
        public Client(BcTlsCrypto crypto, TlsPskIdentity id) : base(crypto, id) { }
        protected override ProtocolVersion[] GetSupportedVersions() => ProtocolVersion.DTLSv12.Only();
        protected override int[] GetSupportedCipherSuites() => new[] { CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256 };
        public override int GetHandshakeTimeoutMillis() => 8000;
    }

    public static DtlsTransport Connect(DatagramTransport transport, string identity, byte[] psk)
    {
        var crypto = new BcTlsCrypto(new SecureRandom());
        return new DtlsClientProtocol().Connect(new Client(crypto, new BasicTlsPskIdentity(identity, psk)), transport);
    }
}

/// <summary>Hue Entertainment API: DTLS 1.2 over UDP :2100 with PSK (identity = application id, key = client key).</summary>
public sealed class EntertainmentStreamer : IFrameSink
{
    readonly string host, appId, areaId;
    readonly byte[] psk;
    DtlsTransport? link;
    byte seq;
    readonly object gate = new();
    public Action<string>? OnFailure { get; set; }

    EntertainmentStreamer(string host, string appId, byte[] psk, string areaId) { this.host = host; this.appId = appId; this.psk = psk; this.areaId = areaId; }

    public static byte[]? ParseKey(string hex)
    {
        if (hex.Length != 32) return null;
        try { return Convert.FromHexString(hex); } catch (FormatException) { return null; }
    }

    public static EntertainmentStreamer? Create(string host, string appId, string clientKeyHex, string areaId) =>
        ParseKey(clientKeyHex) is { } k ? new EntertainmentStreamer(host, appId, k, areaId) : null;

    public async Task StartAsync(int port = 2100, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(8);
        var work = Task.Run(() =>
        {
            var t = new UdpDatagramTransport(host, port);
            try { return HueDtls.Connect(t, appId, psk); }
            catch { t.Close(); throw; }
        }, ct);
        var done = await Task.WhenAny(work, Task.Delay(limit, ct)).ConfigureAwait(false);
        if (done != work)
        {
            _ = work.ContinueWith(w => { if (w.IsCompletedSuccessfully) w.Result.Close(); }, TaskScheduler.Default);
            throw HueException.StreamFailed("The bridge did not answer. Make sure no other app is syncing.");
        }
        DtlsTransport t;
        try { t = await work.ConfigureAwait(false); }
        catch (Exception e) { throw HueException.StreamFailed(Describe(e)); }
        lock (gate) link = t;
    }

    public static string Describe(Exception e)
    {
        var parts = new List<string>();
        for (Exception? x = e; x != null && parts.Count < 3; x = x.InnerException) parts.Add($"{x.GetType().Name}: {x.Message}");
        var s = string.Join(" <- ", parts);
        return s.Length > 220 ? s[..220] : s;
    }

    public void Send(IReadOnlyList<(byte Channel, RGB Rgb)> colors)
    {
        lock (gate)
        {
            if (link == null) return;
            seq++;
            var pkt = StreamPacket.Build(areaId, seq, colors);
            if (pkt == null) return;
            try { link.Send(pkt, 0, pkt.Length); }
            catch (Exception e) { var l = link; link = null; try { l.Close(); } catch { } OnFailure?.Invoke(e.Message); }
        }
    }

    public void Dispose()
    {
        lock (gate) { try { link?.Close(); } catch { } link = null; }
    }
}
