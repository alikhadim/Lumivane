package com.lumivane.firetv

import android.content.Context
import android.content.Intent
import androidx.core.content.ContextCompat
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext
import kotlin.math.PI
import kotlin.math.max
import kotlin.math.sin

data class Channel(val id: Int, val x: Double, val y: Double, val z: Double, val service: String? = null)
data class Area(val id: String, val name: String, val active: Boolean, val channels: List<Channel>)

interface Sink {
    /** Set when the stream broke after it was opened. */
    val error: String?
    /** Shown to the user while running, for example when a fallback is in use. */
    val note: String? get() = null
    fun send(colors: Map<Int, RGB>)
    suspend fun close()
}

/** A light that can take part in an entertainment area. */
data class EntLight(val lightId: String, val name: String, val service: String)

/** Where a light sits relative to the screen. Hue positions: x -1 left to 1 right, z -1 floor to 1 ceiling. */
data class Cell(val label: String, val x: Double, val z: Double) {
    companion object {
        val all = listOf(
            Cell("Top left", -0.9, 0.8), Cell("Top", 0.0, 0.8), Cell("Top right", 0.9, 0.8),
            Cell("Left", -0.9, 0.0), Cell("Center", 0.0, 0.0), Cell("Right", 0.9, 0.0),
            Cell("Bottom left", -0.9, -0.8), Cell("Bottom", 0.0, -0.8), Cell("Bottom right", 0.9, -0.8),
        )
        /** Surround order used when areas are created automatically. */
        val defaultOrder = listOf(3, 5, 1, 7, 0, 2, 6, 8, 4)
        fun nearest(x: Double, z: Double): Int =
            all.indices.minByOrNull { (all[it].x - x) * (all[it].x - x) + (all[it].z - z) * (all[it].z - z) } ?: 4
    }
}

interface SyncBackend {
    suspend fun areas(): List<Area>
    suspend fun entertainmentLights(): List<EntLight>
    /** Creates the area when [areaId] is null, otherwise replaces its lights and positions. [members] maps light to [Cell] index. */
    suspend fun saveArea(areaId: String?, name: String, members: List<Pair<EntLight, Int>>)
    suspend fun deleteArea(areaId: String)
    suspend fun openStream(area: Area): Sink
}

enum class SourceKind(val title: String, val blurb: String) {
    Screen("Screen", "Follows what is on the TV"),
    Aurora("Aurora", "Slow green and violet waves"),
    Candle("Candle", "Warm flicker"),
    ColorLoop("Color loop", "Slowly cycling colors"),
}

object ZoneLayout {
    /** 3x3 grid cell containing the point, so edge lights get edge zones and center lights the center cell. */
    fun rect(u: Double, v: Double): ZoneRect {
        fun band(t: Double) = if (t < 0.34) 0f to 0.34f else if (t > 0.66) 0.66f to 1f else 0.33f to 0.67f
        val (x0, x1) = band(u)
        val (y0, y1) = band(v)
        return ZoneRect(x0, y0, x1, y1)
    }

    /** Hue positions: x -1 (left) to 1 (right), z -1 (floor) to 1 (ceiling). */
    fun rectFor(c: Channel): ZoneRect = rect(((c.x + 1) / 2).coerceIn(0.0, 1.0), ((1 - c.z) / 2).coerceIn(0.0, 1.0))
}

object StreamPacket {
    /** Hue Entertainment API v2 message: 16-byte header, 36-byte area id, 7 bytes per channel (RGB, 16 bit big endian). */
    fun build(areaId: String, sequence: Int, colors: Map<Int, RGB>): ByteArray? {
        val id = areaId.toByteArray(Charsets.US_ASCII)
        if (id.size != 36 || colors.size > 20) return null
        val out = ByteArray(16 + 36 + colors.size * 7)
        "HueStream".toByteArray(Charsets.US_ASCII).copyInto(out)
        out[9] = 0x02; out[10] = 0x00; out[11] = sequence.toByte()
        id.copyInto(out, 16)
        var o = 52
        for ((ch, c) in colors.toSortedMap()) {
            out[o++] = ch.toByte()
            for (v in floatArrayOf(c.r, c.g, c.b)) {
                val u = (v.coerceIn(0f, 1f) * 65535f).let { Math.round(it) }
                out[o++] = (u shr 8).toByte(); out[o++] = (u and 0xFF).toByte()
            }
        }
        return out
    }
}

/** Procedural sources that need no screen capture. Positions follow the Hue area layout. */
object Effects {
    fun color(kind: SourceKind, t: Float, c: Channel): RGB {
        val x = c.x.toFloat()
        val z = c.z.toFloat()
        return when (kind) {
            SourceKind.Aurora -> {
                val hue = 0.42f + 0.2f * sin(t * 0.13f + x * 1.4f) + 0.08f * sin(t * 0.07f + z * 2f)
                val v = 0.55f + 0.35f * sin(t * 0.35f + x * 2.2f + c.id)
                ColorMath.rgb(hue - kotlin.math.floor(hue), 0.85f, v.coerceIn(0.1f, 1f))
            }
            SourceKind.Candle -> {
                val n = 0.5f + 0.5f * (0.5f * sin(t * 7.3f + c.id * 13f) + 0.3f * sin(t * 11.9f + c.id * 5f) + 0.2f * sin(t * 3.1f + c.id))
                ColorMath.rgb(0.065f + 0.015f * n, 0.9f, (0.45f + 0.45f * n).coerceIn(0f, 1f))
            }
            SourceKind.ColorLoop -> {
                val hue = t * 0.04f + (x + 1f) * 0.1f
                ColorMath.rgb(hue - kotlin.math.floor(hue), 1f, 0.9f)
            }
            SourceKind.Screen -> RGB.Black
        }
    }
}

/** One step of a running sync: produces the color for each channel. */
interface Pipeline {
    fun step(dt: Float, t: Float, settings: SyncSettings): Map<Int, RGB>
    /** Mean luma of the last input frame, or null for sources without a picture. */
    val inputLuma: Float? get() = null
    fun close() {}
}

class EffectPipeline(private val kind: SourceKind, private val channels: List<Channel>) : Pipeline {
    override fun step(dt: Float, t: Float, settings: SyncSettings): Map<Int, RGB> {
        val k = minOf(settings.brightness, ColorEngine.ceiling(settings))
        return channels.associate { it.id to Effects.color(kind, t, it).scaled(k).clamped() }
    }
}

enum class SyncState { Idle, Starting, Running }

data class SyncStatus(
    val state: SyncState = SyncState.Idle,
    val label: String = "",
    val message: String? = null,
    val warning: String? = null,
)

class SyncRequest(
    val backend: SyncBackend,
    val area: Area,
    val source: SourceKind,
    val projectionCode: Int = 0,
    val projectionData: Intent? = null,
)

/** Process-wide sync state. The foreground service runs the loop; the UI reads the flows. */
object SyncHub {
    val status = MutableStateFlow(SyncStatus())
    val live = MutableStateFlow<Map<Int, RGB>>(emptyMap())
    val settings = MutableStateFlow(Profile.presets[0].settings)

    @Volatile private var pending: SyncRequest? = null
    @Volatile var job: Job? = null

    fun start(ctx: Context, req: SyncRequest) {
        if (status.value.state != SyncState.Idle) return
        pending = req
        status.value = SyncStatus(SyncState.Starting, label = req.source.title)
        ContextCompat.startForegroundService(ctx, Intent(ctx, SyncService::class.java))
    }

    fun stop() { job?.cancel() }

    fun fail(message: String) { status.value = SyncStatus(message = message) }

    internal fun take(): SyncRequest? = pending.also { pending = null }

    internal suspend fun run(ctx: Context, req: SyncRequest) {
        var sink: Sink? = null
        var pipe: Pipeline? = null
        try {
            sink = req.backend.openStream(req.area)
            pipe = when (req.source) {
                SourceKind.Screen -> ScreenPipeline(ctx, req.projectionCode, requireNotNull(req.projectionData), req.area.channels)
                else -> EffectPipeline(req.source, req.area.channels)
            }
            status.value = SyncStatus(SyncState.Running, label = req.source.title, warning = sink.note)
            loop(sink, pipe)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            status.value = SyncStatus(message = e.message ?: "Sync stopped unexpectedly.")
        } finally {
            withContext(NonCancellable) {
                runCatching { pipe?.close() }
                runCatching { sink?.close() }
                live.value = emptyMap()
                if (status.value.state != SyncState.Idle) status.value = SyncStatus(message = status.value.message)
            }
        }
    }

    private suspend fun loop(sink: Sink, pipe: Pipeline) {
        val start = System.nanoTime()
        var last = start
        var lastViz = 0L
        var darkSince = -1L
        while (currentCoroutineContext().isActive) {
            val s = settings.value
            val frameNs = 1_000_000_000L / s.fps.coerceIn(10, 60)
            val now = System.nanoTime()
            val dt = (now - last) / 1e9f
            last = now
            val colors = pipe.step(dt, (now - start) / 1e9f, s)
            sink.send(colors)
            sink.error?.let { throw HueException(it) }
            if (now - lastViz > 80_000_000L) { live.value = colors; lastViz = now }

            val luma = pipe.inputLuma
            if (luma != null) {
                if (luma < 0.004f) { if (darkSince < 0) darkSince = now } else darkSince = -1
                val dark = darkSince >= 0 && now - darkSince > 6_000_000_000L
                val w = if (dark) "The captured picture is black. Protected apps such as Netflix or Prime Video typically block capture." else sink.note
                if (status.value.warning != w) status.value = status.value.copy(warning = w)
            }
            val spent = System.nanoTime() - now
            delay(max(1L, (frameNs - spent) / 1_000_000L))
        }
    }
}
