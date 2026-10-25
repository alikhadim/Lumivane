package com.lumivane.firetv

import org.bouncycastle.tls.CipherSuite
import org.bouncycastle.tls.DTLSServerProtocol
import org.bouncycastle.tls.DatagramTransport
import org.bouncycastle.tls.PSKTlsServer
import org.bouncycastle.tls.ProtocolVersion
import org.bouncycastle.tls.TlsPSKIdentityManager
import org.bouncycastle.tls.crypto.impl.bc.BcTlsCrypto
import org.json.JSONArray
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.security.SecureRandom
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

class SyncTest {
    private val areaId = "12345678-1234-1234-1234-123456789abc"

    private fun solid(w: Int, h: Int, r: Int, g: Int, b: Int): ByteArray {
        val px = ByteArray(w * h * 4)
        for (i in 0 until w * h) { px[i * 4] = r.toByte(); px[i * 4 + 1] = g.toByte(); px[i * 4 + 2] = b.toByte(); px[i * 4 + 3] = 0xFF.toByte() }
        return px
    }

    @Test fun hsvRoundTrip() {
        for (c in listOf(RGB(1f, 0f, 0f), RGB(0.2f, 0.6f, 0.9f), RGB(0.5f, 0.5f, 0.5f), RGB(0.9f, 0.8f, 0.1f))) {
            val (h, s, v) = ColorMath.hsv(c)
            val o = ColorMath.rgb(h, s, v)
            assertEquals(c.r, o.r, 1e-4f); assertEquals(c.g, o.g, 1e-4f); assertEquals(c.b, o.b, 1e-4f)
        }
    }

    @Test fun zoneStatsOfSolidRed() {
        val raw = ZoneAccumulator.reduceRgba(solid(32, 18, 255, 0, 0), 32, 18, 32 * 4, listOf(ZoneRect(0f, 0f, 1f, 1f)))
        val st = ZoneAccumulator.stats(raw, 0)
        assertEquals(1f, st.average.r, 1e-4f)
        assertEquals(1f, st.dominant.r, 1e-4f)
        assertEquals(1f, st.dominance, 1e-4f)
        assertEquals(0f, st.dominant.g, 1e-4f)
    }

    @Test fun zoneStatsOfBlackIsEmptyColor() {
        val raw = ZoneAccumulator.reduceRgba(solid(16, 9, 0, 0, 0), 16, 9, 64, listOf(ZoneRect(0f, 0f, 1f, 1f)))
        val st = ZoneAccumulator.stats(raw, 0)
        assertEquals(0f, st.brightness, 1e-6f)
        assertEquals(0f, st.dominance, 1e-6f)
    }

    @Test fun rowStrideIsHonored() {
        val w = 8; val h = 4; val stride = 48
        val px = ByteArray(stride * h)
        for (y in 0 until h) for (x in 0 until w) { val i = y * stride + x * 4; px[i] = 0; px[i + 1] = 0xFF.toByte(); px[i + 2] = 0; px[i + 3] = 0xFF.toByte() }
        val st = ZoneAccumulator.stats(ZoneAccumulator.reduceRgba(px, w, h, stride, listOf(ZoneRect(0f, 0f, 1f, 1f))), 0)
        assertEquals(1f, st.average.g, 1e-4f)
        assertEquals(0f, st.average.r, 1e-4f)
    }

    @Test fun engineBrightScreenGivesLightAndBlackGivesDark() {
        val engine = ColorEngine()
        val bright = ZoneStats(RGB(0.8f, 0.7f, 0.6f), RGB(0.9f, 0.5f, 0.2f), 0.5f, 0.7f, 0.3f)
        var out = emptyMap<Int, RGB>()
        repeat(100) { out = engine.process(listOf(0 to bright), 0.02f, SyncSettings()) }
        assertTrue(out.getValue(0).luma > 0.3f)
        repeat(200) { out = engine.process(listOf(0 to ZoneStats.Empty), 0.02f, SyncSettings()) }
        assertTrue(out.getValue(0).luma < 0.02f)
    }

    @Test fun engineRespectsBrightnessCeiling() {
        val engine = ColorEngine()
        val white = ZoneStats(RGB(1f, 1f, 1f), RGB(1f, 1f, 1f), 0f, 1f, 0f)
        var out = emptyMap<Int, RGB>()
        val cfg = SyncSettings(brightness = 0.5f, smoothing = 0f)
        repeat(300) { out = engine.process(listOf(0 to white), 0.02f, cfg) }
        assertTrue(out.getValue(0).let { maxOf(it.r, it.g, it.b) } <= 0.5001f)
        val low = SyncSettings(lowLightMode = true, smoothing = 0f)
        repeat(300) { out = engine.process(listOf(0 to white), 0.02f, low) }
        assertTrue(out.getValue(0).let { maxOf(it.r, it.g, it.b) } <= 0.4501f)
    }

    @Test fun streamPacketLayout() {
        val p = StreamPacket.build(areaId, 7, mapOf(1 to RGB(1f, 0f, 0.5f), 0 to RGB(0f, 1f, 0f)))!!
        assertEquals(52 + 14, p.size)
        assertEquals("HueStream", String(p, 0, 9))
        assertEquals(2, p[9].toInt()); assertEquals(0, p[10].toInt()); assertEquals(7, p[11].toInt())
        assertEquals(areaId, String(p, 16, 36))
        assertEquals(0, p[52].toInt())
        assertEquals(0xFF, p[55].toInt() and 0xFF); assertEquals(0xFF, p[56].toInt() and 0xFF)
        assertEquals(1, p[59].toInt())
        assertEquals(0xFF, p[60].toInt() and 0xFF)
        assertEquals(0x80, p[64].toInt() and 0xFF)
    }

    @Test fun streamPacketRejectsBadInput() {
        assertNull(StreamPacket.build("short", 0, mapOf(0 to RGB.Black)))
        assertNull(StreamPacket.build(areaId, 0, (0..20).associateWith { RGB.Black }))
    }

    @Test fun parsesAreas() {
        val a = parseAreas(JSONArray("""[{"id":"A1","metadata":{"name":"TV"},"status":"inactive","channels":[
          {"channel_id":1,"position":{"x":0.5,"y":0.8,"z":0.1}},{"channel_id":0,"position":{"x":-0.5,"y":0.8,"z":0.2}}]}]"""))
        assertEquals("TV", a[0].name)
        assertEquals(listOf(0, 1), a[0].channels.map { it.id })
        assertEquals(-0.5, a[0].channels[0].x, 1e-9)
        assertEquals(false, a[0].active)
    }

    @Test fun channelsMapToScreenZones() {
        val topLeft = ZoneLayout.rectFor(Channel(0, -0.9, 0.8, 0.9))
        assertEquals(0f, topLeft.x0, 0f); assertEquals(0f, topLeft.y0, 0f)
        val bottomRight = ZoneLayout.rectFor(Channel(1, 0.9, 0.8, -0.9))
        assertEquals(1f, bottomRight.x1, 0f); assertEquals(1f, bottomRight.y1, 0f)
        val mid = ZoneLayout.rectFor(Channel(2, 0.0, 0.8, 0.0))
        assertEquals(0.33f, mid.x0, 1e-6f); assertEquals(0.67f, mid.y1, 1e-6f)
    }

    @Test fun effectsStayInRange() {
        for (k in SourceKind.entries.filter { it.isAmbient }) {
            for (t in listOf(0f, 1.3f, 20f, 500f)) {
                val c = Effects.color(k, t, Channel(3, -0.4, 0.8, 0.2))
                for (v in listOf(c.r, c.g, c.b)) assertTrue("$k $t $v", v in 0f..1f)
                assertTrue(c.luma > 0.02f)
            }
        }
    }

    @Test fun parsesHexKeys() {
        assertArrayEquals(byteArrayOf(0x0a, 0xff.toByte()), hexToBytes("0aff"))
        assertNull(hexToBytes("zz"))
        assertNull(hexToBytes("abc"))
        assertNull(hexToBytes(""))
    }

    private class Mem(private val inQ: LinkedBlockingQueue<ByteArray>, private val outQ: LinkedBlockingQueue<ByteArray>) : DatagramTransport {
        override fun getReceiveLimit() = 1400
        override fun getSendLimit() = 1400
        override fun receive(buf: ByteArray, off: Int, len: Int, waitMillis: Int): Int {
            val p = inQ.poll(maxOf(waitMillis, 1).toLong(), TimeUnit.MILLISECONDS) ?: return -1
            val n = minOf(len, p.size)
            System.arraycopy(p, 0, buf, off, n)
            return n
        }
        override fun send(buf: ByteArray, off: Int, len: Int) { outQ.add(buf.copyOfRange(off, off + len)) }
        override fun close() {}
    }

    private class Server(key: ByteArray, id: String) : PSKTlsServer(BcTlsCrypto(SecureRandom()), object : TlsPSKIdentityManager {
        override fun getHint(): ByteArray? = null
        override fun getPSK(identity: ByteArray?): ByteArray? = if (identity != null && String(identity) == id) key else null
    }) {
        override fun getSupportedVersions(): Array<ProtocolVersion> = ProtocolVersion.DTLSv12.only()
        override fun getSupportedCipherSuites(): IntArray = intArrayOf(CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256)
    }

    @Test fun dtlsPskHandshakeCarriesStreamPackets() {
        val key = ByteArray(16) { (it * 7 + 1).toByte() }
        val c2s = LinkedBlockingQueue<ByteArray>()
        val s2c = LinkedBlockingQueue<ByteArray>()
        val got = AtomicReference<ByteArray?>()
        val failure = AtomicReference<Throwable?>()
        val srv = Thread {
            try {
                val t = DTLSServerProtocol().accept(Server(key, "hue-app-id"), Mem(c2s, s2c))
                val buf = ByteArray(1400)
                val n = t.receive(buf, 0, buf.size, 8000)
                if (n > 0) got.set(buf.copyOf(n))
            } catch (e: Throwable) { failure.set(e) }
        }.apply { isDaemon = true; start() }

        val link = HueDtls.connect(Mem(s2c, c2s), "hue-app-id", key)
        val pkt = StreamPacket.build(areaId, 1, mapOf(0 to RGB(0.25f, 0.5f, 0.75f)))!!
        link.send(pkt, 0, pkt.size)
        srv.join(10_000)
        failure.get()?.let { throw AssertionError("server failed", it) }
        assertNotNull(got.get())
        assertArrayEquals(pkt, got.get())
        link.close()
    }

    @Test(expected = Exception::class) fun dtlsRejectsWrongKey() {
        val c2s = LinkedBlockingQueue<ByteArray>()
        val s2c = LinkedBlockingQueue<ByteArray>()
        Thread {
            runCatching { DTLSServerProtocol().accept(Server(ByteArray(16) { 1 }, "hue-app-id"), Mem(c2s, s2c)) }
        }.apply { isDaemon = true; start() }
        HueDtls.connect(Mem(s2c, c2s), "hue-app-id", ByteArray(16) { 2 })
    }

    @Test fun xyOfWhiteIsNearD65() {
        val p = Xy.of(RGB(1f, 1f, 1f), null)
        assertEquals(0.3227, p.x, 0.005); assertEquals(0.3290, p.y, 0.005)
    }

    @Test fun xyIsClampedIntoGamut() {
        val gamut = Gamut(XY(0.675, 0.322), XY(0.409, 0.518), XY(0.167, 0.04))
        val p = Xy.of(RGB(0f, 1f, 0f), gamut)
        assertTrue(p.y <= 0.518 + 1e-6)
        val inside = Xy.of(RGB(1f, 1f, 1f), gamut)
        assertEquals(Xy.of(RGB(1f, 1f, 1f), null).x, inside.x, 1e-9)
    }

    @Test fun cellsRoundTrip() {
        Cell.all.forEachIndexed { i, c -> assertEquals(i, Cell.nearest(c.x, c.z)) }
        assertEquals(setOf(0, 1, 2, 3, 4, 5, 6, 7, 8), Cell.defaultOrder.toSet())
    }

    @Test fun areaChannelsKeepLightService() {
        val a = parseAreas(JSONArray("""[{"id":"A1","metadata":{"name":"TV"},"channels":[
          {"channel_id":0,"position":{"x":0,"y":0.8,"z":0},"members":[{"service":{"rid":"S9","rtype":"entertainment"},"index":0}]}]}]"""))
        assertEquals("S9", a[0].channels[0].service)
    }

    @Test fun describesNestedErrors() {
        val d = describe(java.io.IOException("outer", IllegalStateException("inner")))
        assertTrue(d.contains("IOException: outer") && d.contains("IllegalStateException: inner"))
    }

    private class FakeCapture(var luma: Float?) : Pipeline {
        override val inputLuma get() = luma
        override fun step(dt: Float, t: Float, settings: SyncSettings) = mapOf(0 to RGB(0.9f, 0.1f, 0.1f))
    }

    private class FixedAmbient : Pipeline {
        override fun step(dt: Float, t: Float, settings: SyncSettings) = mapOf(0 to RGB(0.1f, 0.1f, 0.9f))
    }

    private fun run(g: GuardedPipeline, seconds: Float, dt: Float = 0.05f): Map<Int, RGB> {
        var out = emptyMap<Int, RGB>()
        repeat((seconds / dt).toInt()) { out = g.step(dt, 0f, SyncSettings()) }
        return out
    }

    @Test fun guardKeepsPictureWhileLit() {
        val g = GuardedPipeline(FakeCapture(0.3f), FixedAmbient())
        val c = run(g, 10f)
        assertNull(g.notice)
        assertEquals(0.9f, c.getValue(0).r, 1e-4f)
    }

    @Test fun guardFallsBackAfterThreeSecondsOfBlack() {
        val cap = FakeCapture(0f)
        val g = GuardedPipeline(cap, FixedAmbient())
        run(g, 2.5f)
        assertNull(g.notice)
        val c = run(g, 3f)
        assertEquals(GuardedPipeline.MESSAGE, g.notice)
        assertEquals(0.9f, c.getValue(0).b, 1e-4f)
    }

    @Test fun briefBlackScenesDoNotTrigger() {
        val cap = FakeCapture(0.3f)
        val g = GuardedPipeline(cap, FixedAmbient())
        repeat(5) { cap.luma = 0f; run(g, 2f); cap.luma = 0.2f; run(g, 2f) }
        assertNull(g.notice)
    }

    @Test fun guardReturnsToPictureAndBlendsSmoothly() {
        val cap = FakeCapture(0f)
        val g = GuardedPipeline(cap, FixedAmbient())
        run(g, 6f)
        assertEquals(GuardedPipeline.MESSAGE, g.notice)
        cap.luma = 0.3f
        var prev = run(g, 0.05f).getValue(0).r
        var maxJump = 0f
        repeat(80) { val r = run(g, 0.05f).getValue(0).r; maxJump = maxOf(maxJump, kotlin.math.abs(r - prev)); prev = r }
        assertNull(g.notice)
        assertEquals(0.9f, prev, 1e-4f)
        assertTrue("jump $maxJump", maxJump < 0.1f)
    }

    @Test fun missingFramesCountAsBlack() {
        val g = GuardedPipeline(FakeCapture(null), FixedAmbient())
        run(g, 10f)
        assertNull(g.notice)
    }

    @Test fun movieAmbientChangesSceneWithoutJumps() {
        val ch = Channel(0, -0.5, 0.8, 0.0)
        var prev = Effects.color(SourceKind.MovieAmbient, 0f, ch)
        var maxJump = 0f
        var t = 0f
        while (t < Effects.SCENE_SECONDS * 6) {
            t += 0.05f
            val c = Effects.color(SourceKind.MovieAmbient, t, ch)
            maxJump = maxOf(maxJump, kotlin.math.abs(c.r - prev.r), kotlin.math.abs(c.g - prev.g), kotlin.math.abs(c.b - prev.b))
            prev = c
        }
        assertTrue("jump $maxJump", maxJump < 0.03f)
        assertTrue(Effects.color(SourceKind.MovieAmbient, 1f, ch) != Effects.color(SourceKind.MovieAmbient, Effects.SCENE_SECONDS * 2 + 1f, ch))
    }

    @Test fun vividPlusIsMoreSaturatedThanAccurate() {
        fun satOf(style: ColorStyle): Float {
            val e = ColorEngine()
            val settings = SyncSettings(style = style, smoothing = 0f)
            var out = RGB.Black
            repeat(60) {
                val stats = listOf(0 to ZoneStats(RGB(0.5f, 0.35f, 0.3f), RGB(0.5f, 0.35f, 0.3f), 0.4f, 0.5f, 0.4f))
                out = e.process(stats, 0.05f, settings).getValue(0)
            }
            val mx = maxOf(out.r, out.g, out.b); val mn = minOf(out.r, out.g, out.b)
            return if (mx <= 0f) 0f else (mx - mn) / mx
        }
        assertTrue(satOf(ColorStyle.VividPlus) > satOf(ColorStyle.Accurate))
    }
}
