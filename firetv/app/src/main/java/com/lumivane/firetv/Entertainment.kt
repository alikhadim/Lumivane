package com.lumivane.firetv

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.bouncycastle.tls.BasicTlsPSKIdentity
import org.bouncycastle.tls.CipherSuite
import org.bouncycastle.tls.DTLSClientProtocol
import org.bouncycastle.tls.DTLSTransport
import org.bouncycastle.tls.DatagramTransport
import org.bouncycastle.tls.PSKTlsClient
import org.bouncycastle.tls.ProtocolVersion
import org.bouncycastle.tls.UDPTransport
import org.bouncycastle.tls.crypto.TlsCrypto
import org.bouncycastle.tls.TlsPSKIdentity
import org.bouncycastle.tls.crypto.impl.bc.BcTlsCrypto
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.DatagramSocket
import java.net.InetAddress
import java.security.SecureRandom

fun parseAreas(arr: JSONArray): List<Area> = arr.objects().map { a ->
    Area(
        id = a.getString("id"),
        name = a.optJSONObject("metadata")?.optString("name").orEmpty().ifBlank { "Entertainment area" },
        active = a.optString("status") == "active",
        channels = a.optJSONArray("channels")?.objects().orEmpty().mapNotNull { c ->
            val p = c.optJSONObject("position") ?: return@mapNotNull null
            val svc = c.optJSONArray("members")?.objects()?.firstOrNull()?.optJSONObject("service")?.optString("rid")?.ifBlank { null }
            Channel(c.optInt("channel_id"), p.optDouble("x"), p.optDouble("y"), p.optDouble("z"), svc)
        }.sortedBy { it.id },
    )
}.sortedBy { it.name.lowercase() }

fun hexToBytes(hex: String): ByteArray? {
    if (hex.length % 2 != 0 || hex.isEmpty()) return null
    return try { ByteArray(hex.length / 2) { hex.substring(it * 2, it * 2 + 2).toInt(16).toByte() } } catch (e: NumberFormatException) { null }
}

/** DTLS 1.2 with PSK, cipher suite TLS_PSK_WITH_AES_128_GCM_SHA256: the only suite the bridge accepts for streaming. */
object HueDtls {
    private class Client(crypto: TlsCrypto, id: TlsPSKIdentity) : PSKTlsClient(crypto, id) {
        override fun getSupportedVersions(): Array<ProtocolVersion> = ProtocolVersion.DTLSv12.only()
        override fun getSupportedCipherSuites(): IntArray = intArrayOf(CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256)
        override fun getHandshakeTimeoutMillis(): Int = 8_000
    }

    fun connect(transport: DatagramTransport, identity: String, psk: ByteArray): DTLSTransport =
        DTLSClientProtocol().connect(Client(BcTlsCrypto(SecureRandom()), BasicTlsPSKIdentity(identity, psk)), transport)

    fun connectUdp(ip: String, port: Int, identity: String, psk: ByteArray): DTLSTransport {
        val socket = DatagramSocket()
        try {
            socket.connect(InetAddress.getByName(ip), port)
            return connect(UDPTransport(socket, 1500), identity, psk)
        } catch (e: Exception) {
            socket.close()
            throw e
        }
    }
}

private class DtlsSink(private val area: Area, private val link: DTLSTransport, private val stopArea: suspend () -> Unit) : Sink {
    @Volatile override var error: String? = null
    private var seq = 0

    override fun send(colors: Map<Int, RGB>) {
        seq = (seq + 1) and 0xFF
        val pkt = StreamPacket.build(area.id, seq, colors) ?: return
        try {
            link.send(pkt, 0, pkt.size)
        } catch (e: IOException) {
            error = "The connection to the bridge dropped."
        }
    }

    override suspend fun close() {
        runCatching { link.close() }
        runCatching { stopArea() }
    }
}

fun describe(e: Throwable): String =
    generateSequence(e) { it.cause }.take(3).joinToString(" <- ") { "${it.javaClass.simpleName}: ${it.message}" }.take(220)

class HueSync(private val client: Bridge, private val ip: String, private val clientKey: String?) : SyncBackend {

    override suspend fun areas(): List<Area> = parseAreas(client.data("entertainment_configuration"))

    private class Lit(val light: EntLight, val gamut: Gamut?)

    private suspend fun lits(): List<Lit> {
        val devices = client.data("device").objects().associateBy { it.getString("id") }
        return client.data("light").objects().mapNotNull { l ->
            val owner = l.optJSONObject("owner")?.optString("rid") ?: return@mapNotNull null
            val svc = devices[owner]?.optJSONArray("services")?.objects()?.firstOrNull { it.optString("rtype") == "entertainment" }?.optString("rid")
                ?: return@mapNotNull null
            val g = l.optJSONObject("color")?.optJSONObject("gamut")?.let { o ->
                fun xy(k: String) = o.optJSONObject(k)?.let { XY(it.optDouble("x"), it.optDouble("y")) }
                val r = xy("red"); val gr = xy("green"); val b = xy("blue")
                if (r != null && gr != null && b != null) Gamut(r, gr, b) else null
            }
            Lit(EntLight(l.getString("id"), l.optJSONObject("metadata")?.optString("name").orEmpty().ifBlank { "Light" }, svc), g)
        }.sortedBy { it.light.name.lowercase() }
    }

    override suspend fun entertainmentLights(): List<EntLight> = lits().map { it.light }

    private fun locations(members: List<Pair<EntLight, Int>>) = JSONObject().put("service_locations", JSONArray(members.map { (l, cell) ->
        val c = Cell.all[cell.coerceIn(0, Cell.all.lastIndex)]
        JSONObject()
            .put("service", JSONObject().put("rid", l.service).put("rtype", "entertainment"))
            .put("equalization_factor", 1)
            .put("positions", JSONArray().put(JSONObject().put("x", c.x).put("y", 0.8).put("z", c.z)))
    }))

    override suspend fun saveArea(areaId: String?, name: String, members: List<Pair<EntLight, Int>>) {
        val m = members.take(20)
        if (m.isEmpty()) throw HueException("Select at least one light.")
        val services = JSONArray(m.map { JSONObject().put("rid", it.first.lightId).put("rtype", "light") })
        if (areaId == null) {
            val body = JSONObject()
                .put("metadata", JSONObject().put("name", name))
                .put("configuration_type", "screen")
                .put("stream_proxy", JSONObject().put("mode", "auto"))
                .put("light_services", services)
                .put("locations", locations(m))
            client.call("POST", "entertainment_configuration", body.toString())
        } else {
            val body = JSONObject().put("locations", locations(m))
            client.call("PUT", "entertainment_configuration/$areaId", body.toString())
            // The bridge derives the lights from the locations. If it did not take the new set, replace the area.
            val now = areas().firstOrNull { it.id == areaId }
            val want = m.map { it.first.service }.toSet()
            if (now == null || now.channels.mapNotNull { it.service }.toSet() != want) {
                client.call("DELETE", "entertainment_configuration/$areaId")
                client.call("POST", "entertainment_configuration", JSONObject()
                    .put("metadata", JSONObject().put("name", name))
                    .put("configuration_type", "screen")
                    .put("stream_proxy", JSONObject().put("mode", "auto"))
                    .put("light_services", services)
                    .put("locations", locations(m)).toString())
            }
        }
    }

    override suspend fun deleteArea(areaId: String) {
        client.call("DELETE", "entertainment_configuration/$areaId")
    }

    private suspend fun stopArea(area: Area) {
        runCatching { client.call("PUT", "entertainment_configuration/${area.id}", """{"action":"stop"}""") }
    }

    override suspend fun openStream(area: Area): Sink {
        val appId = runCatching { client.applicationId() }.getOrNull()
        val key = clientKey?.let(::hexToBytes)
        if (area.active) {
            // A stale or foreign session holds the area; Start was pressed explicitly, so take it over.
            stopArea(area)
            kotlinx.coroutines.delay(400)
        }
        var detail: String
        if (appId == null || key == null) {
            detail = if (key == null) "no streaming key, pair again to enable it" else "no application id"
        } else {
            client.call("PUT", "entertainment_configuration/${area.id}", """{"action":"start"}""")
            try {
                val link = withContext(Dispatchers.IO) { HueDtls.connectUdp(ip, 2100, appId, key) }
                return DtlsSink(area, link) { stopArea(area) }
            } catch (e: Exception) {
                detail = describe(e)
            }
            stopArea(area)
        }
        return restFallback(area, detail)
    }

    /** Plain REST updates, about 8 per second across all lights: slower and less smooth than streaming, but works anywhere. */
    private suspend fun restFallback(area: Area, detail: String): Sink {
        val byService = lits().associateBy { it.light.service }
        val targets = area.channels.mapNotNull { ch -> byService[ch.service]?.let { ch.id to RestSink.Target(it.light.lightId, it.gamut) } }.toMap()
        if (targets.isEmpty()) throw HueException("The bridge did not accept the streaming connection ($detail).")
        return RestSink(client, targets, "Smooth streaming unavailable ($detail). Using basic mode.")
    }
}

/** Sync backend for demo mode: in-memory areas, no network. */
class DemoSync : SyncBackend {
    private val lights = listOf("Bar Left", "Bar Right", "Floor Lamp", "Desk Lamp", "Shelf Strip", "Bedside").mapIndexed { i, n -> EntLight("d$i", n, "s$i") }
    private var counter = 1
    private val store = mutableListOf(
        Area("00000000-0000-0000-0000-000000000001", "Living room (demo)", false, listOf(
            Channel(0, -0.9, 0.8, 0.8, "s0"), Channel(1, -0.9, 0.8, 0.0, "s1"), Channel(2, 0.0, 0.8, 0.8, "s2"),
            Channel(3, 0.9, 0.8, 0.8, "s3"), Channel(4, 0.9, 0.8, 0.0, "s4"), Channel(5, 0.0, 0.8, -0.8, "s5"),
        )),
    )

    override suspend fun areas() = store.toList()
    override suspend fun entertainmentLights() = lights
    override suspend fun saveArea(areaId: String?, name: String, members: List<Pair<EntLight, Int>>) {
        val ch = members.mapIndexed { i, (l, cell) -> Channel(i, Cell.all[cell].x, 0.8, Cell.all[cell].z, l.service) }
        val i = store.indexOfFirst { it.id == areaId }
        if (i >= 0) store[i] = store[i].copy(channels = ch)
        else store.add(Area("00000000-0000-0000-0000-%012d".format(++counter), name, false, ch))
    }
    override suspend fun deleteArea(areaId: String) { store.removeAll { it.id == areaId } }
    override suspend fun openStream(area: Area): Sink = object : Sink {
        override val error: String? = null
        override fun send(colors: Map<Int, RGB>) {}
        override suspend fun close() {}
    }
}
