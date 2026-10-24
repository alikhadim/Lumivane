package com.lumivane.firetv

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import org.json.JSONObject
import kotlin.math.abs
import kotlin.math.max

/** Fallback when DTLS streaming is unavailable: sets light colors one request at a time, most-changed light first. */
class RestSink(private val client: Bridge, private val targets: Map<Int, Target>, override val note: String) : Sink {
    class Target(val lightId: String, val gamut: Gamut?)

    @Volatile override var error: String? = null
    @Volatile private var latest: Map<Int, RGB> = emptyMap()
    private val sent = HashMap<Int, RGB>()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    init {
        scope.launch {
            var failures = 0
            while (isActive) {
                val want = latest
                val next = targets.keys.filter { it in want }.maxByOrNull { delta(want.getValue(it), sent[it]) }
                if (next == null || delta(want.getValue(next), sent[next]) < 0.03f) { delay(40); continue }
                val c = want.getValue(next)
                try {
                    client.call("PUT", "light/${targets.getValue(next).lightId}", body(c, targets.getValue(next).gamut))
                    sent[next] = c
                    failures = 0
                } catch (e: kotlinx.coroutines.CancellationException) {
                    throw e
                } catch (e: Exception) {
                    if (++failures >= 5) { error = e.message ?: "The bridge stopped answering."; return@launch }
                }
                delay(80)
            }
        }
    }

    private fun delta(a: RGB, b: RGB?): Float = if (b == null) 1f else max(abs(a.r - b.r), max(abs(a.g - b.g), abs(a.b - b.b)))

    private fun body(c: RGB, gamut: Gamut?): String {
        val peak = max(c.r, max(c.g, c.b))
        val j = JSONObject().put("dynamics", JSONObject().put("duration", 250))
        if (peak < 0.02f) return j.put("on", JSONObject().put("on", false)).toString()
        val xy = Xy.of(RGB(c.r / peak, c.g / peak, c.b / peak), gamut)
        return j.put("on", JSONObject().put("on", true))
            .put("dimming", JSONObject().put("brightness", (peak * 100.0).coerceIn(1.0, 100.0)))
            .put("color", JSONObject().put("xy", JSONObject().put("x", xy.x).put("y", xy.y)))
            .toString()
    }

    override fun send(colors: Map<Int, RGB>) { latest = colors }

    override suspend fun close() { scope.cancel() }
}
