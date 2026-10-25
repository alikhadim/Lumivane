using System.Net;
using System.Net.Sockets;
using Lumivane.Core;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Xunit;

namespace Lumivane.Core.Tests;

/// <summary>Server side of the loopback: a UDP socket that learns the client from its first datagram.</summary>
sealed class ServerTransport : DatagramTransport
{
    readonly Socket sock = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    EndPoint? peer;
    public int Port { get; }
    public ServerTransport() { sock.Bind(new IPEndPoint(IPAddress.Loopback, 0)); Port = ((IPEndPoint)sock.LocalEndPoint!).Port; }
    public int GetReceiveLimit() => 1400;
    public int GetSendLimit() => 1400;
    public int Receive(byte[] buf, int off, int len, int waitMillis) => Receive(buf.AsSpan(off, len), waitMillis);
    public int Receive(Span<byte> buffer, int waitMillis)
    {
        if (!sock.Poll(Math.Max(1, waitMillis) * 1000, SelectMode.SelectRead)) return -1;
        EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
        var tmp = new byte[2048];
        int n = sock.ReceiveFrom(tmp, ref ep);
        peer = ep;
        tmp.AsSpan(0, Math.Min(n, buffer.Length)).CopyTo(buffer);
        return Math.Min(n, buffer.Length);
    }
    public void Send(byte[] buf, int off, int len) { if (peer != null) sock.SendTo(buf, off, len, SocketFlags.None, peer); }
    public void Send(ReadOnlySpan<byte> buffer) { if (peer != null) sock.SendTo(buffer, SocketFlags.None, peer); }
    public void Close() => sock.Dispose();
}

sealed class PskServer : PskTlsServer
{
    public PskServer(byte[] key, string identity) : base(new BcTlsCrypto(new SecureRandom()), new Mgr(key, identity)) { }
    protected override ProtocolVersion[] GetSupportedVersions() => ProtocolVersion.DTLSv12.Only();
    protected override int[] GetSupportedCipherSuites() => new[] { CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256 };
    sealed class Mgr : TlsPskIdentityManager
    {
        readonly byte[] key; readonly string id;
        public Mgr(byte[] k, string i) { key = k; id = i; }
        public byte[]? GetHint() => null;
        public byte[]? GetPsk(byte[] identity) => System.Text.Encoding.UTF8.GetString(identity) == id ? key : null;
    }
}

public class DtlsTests
{
    const string Area = "12345678-1234-1234-1234-123456789abc";
    const string AppId = "app-id-0123456789";
    const string KeyHex = "00112233445566778899AABBCCDDEEFF";

    static (Task<byte[]> received, ServerTransport transport) StartServer(string keyHex, string identity)
    {
        var t = new ServerTransport();
        var task = Task.Run(() =>
        {
            var dtls = new DtlsServerProtocol().Accept(new PskServer(Convert.FromHexString(keyHex), identity), t);
            var buf = new byte[1500];
            int n = dtls.Receive(buf, 0, buf.Length, 5000);
            return n > 0 ? buf[..n] : Array.Empty<byte>();
        });
        return (task, t);
    }

    [Fact] public async Task StreamsFramesOverDtlsPsk()
    {
        var (rx, srv) = StartServer(KeyHex, AppId);
        using var s = EntertainmentStreamer.Create("127.0.0.1", AppId, KeyHex, Area)!;
        await s.StartAsync(srv.Port);
        s.Send(new (byte, RGB)[] { (0, new RGB(1, 0, 0)), (1, new RGB(0, 0, 1)) });
        var got = await rx.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(52 + 14, got.Length);
        Assert.Equal("HueStream", System.Text.Encoding.ASCII.GetString(got, 0, 9));
        Assert.Equal(Area, System.Text.Encoding.ASCII.GetString(got, 16, 36));
        Assert.Equal(new byte[] { 0, 0xFF, 0xFF, 0, 0, 0, 0 }, got[52..59]);
        srv.Close();
    }

    [Fact] public async Task WrongKeyIsRejected()
    {
        var (rx, srv) = StartServer(KeyHex, AppId);
        using var s = EntertainmentStreamer.Create("127.0.0.1", AppId, "FFEEDDCCBBAA99887766554433221100", Area)!;
        var e = await Assert.ThrowsAsync<HueException>(() => s.StartAsync(srv.Port, TimeSpan.FromSeconds(8)));
        Assert.Equal(HueErrorKind.StreamFailed, e.Kind);
        srv.Close();
        try { await rx; } catch { }
    }

    [Fact] public void BadClientKeyIsRejected()
    {
        Assert.Null(EntertainmentStreamer.Create("127.0.0.1", AppId, "xyz", Area));
        Assert.Null(EntertainmentStreamer.Create("127.0.0.1", AppId, "0011", Area));
    }
}
