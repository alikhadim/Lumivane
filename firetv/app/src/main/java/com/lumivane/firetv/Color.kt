package com.lumivane.firetv

import kotlin.math.exp
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow

// Port of the Mac app's color pipeline (HueCore/Color.swift), without the audio path.

data class RGB(val r: Float, val g: Float, val b: Float) {
    val luma: Float get() = 0.2126f * r + 0.7152f * g + 0.0722f * b
    fun mixed(o: RGB, t: Float) = RGB(r + (o.r - r) * t, g + (o.g - g) * t, b + (o.b - b) * t)
    fun scaled(k: Float) = RGB(r * k, g * k, b * k)
    fun clamped() = RGB(r.coerceIn(0f, 1f), g.coerceIn(0f, 1f), b.coerceIn(0f, 1f))

    companion object { val Black = RGB(0f, 0f, 0f) }
}

data class HSV(val h: Float, val s: Float, val v: Float)

object ColorMath {
    fun hsv(c: RGB): HSV {
        val mx = max(c.r, max(c.g, c.b))
        val mn = min(c.r, min(c.g, c.b))
        val d = mx - mn
        var h = 0f
        if (d > 0) {
            h = when (mx) {
                c.r -> ((c.g - c.b) / d).let { if (it < 0) it + 6 else it }
                c.g -> (c.b - c.r) / d + 2
                else -> (c.r - c.g) / d + 4
            } / 6f
        }
        return HSV(h, if (mx > 0) d / mx else 0f, mx)
    }

    fun rgb(h: Float, s: Float, v: Float): RGB {
        val hh = h.coerceIn(0f, 0.99999f) * 6
        val i = hh.toInt()
        val f = hh - i
        val p = v * (1 - s)
        val q = v * (1 - f * s)
        val t = v * (1 - (1 - f) * s)
        return when (i) {
            0 -> RGB(v, t, p)
            1 -> RGB(q, v, p)
            2 -> RGB(p, v, t)
            3 -> RGB(p, q, v)
            4 -> RGB(t, p, v)
            else -> RGB(v, p, q)
        }
    }
}

data class ZoneRect(val x0: Float, val y0: Float, val x1: Float, val y1: Float)

data class ZoneStats(val average: RGB, val dominant: RGB, val dominance: Float, val brightness: Float, val saturation: Float) {
    companion object { val Empty = ZoneStats(RGB.Black, RGB.Black, 0f, 0f, 0f) }
}

/** Raw layout per zone (52 floats): [0..2] RGB sum, [3] pixel count, [4..15] hue-bin weights, [16..51] hue-bin weighted RGB sums. */
object ZoneAccumulator {
    const val STRIDE = 52
    const val BINS = 12
    private const val MIN_V = 0.08f
    private const val MIN_S = 0.2f

    /** [px] holds RGBA bytes, [rowStride] is in bytes. */
    fun reduceRgba(px: ByteArray, width: Int, height: Int, rowStride: Int, zones: List<ZoneRect>, step: Int = 1): FloatArray {
        val out = FloatArray(zones.size * STRIDE)
        val st = max(1, step)
        zones.forEachIndexed { zi, z ->
            val x0 = min((z.x0 * width).toInt(), width - 1)
            val y0 = min((z.y0 * height).toInt(), height - 1)
            val x1 = max(x0 + 1, min(width, (z.x1 * width).toInt()))
            val y1 = max(y0 + 1, min(height, (z.y1 * height).toInt()))
            val o = zi * STRIDE
            var y = y0
            while (y < y1) {
                var x = x0
                while (x < x1) {
                    val i = y * rowStride + x * 4
                    val c = RGB((px[i].toInt() and 0xFF) / 255f, (px[i + 1].toInt() and 0xFF) / 255f, (px[i + 2].toInt() and 0xFF) / 255f)
                    out[o] += c.r; out[o + 1] += c.g; out[o + 2] += c.b; out[o + 3] += 1f
                    val (h, s, v) = ColorMath.hsv(c)
                    if (v >= MIN_V && s >= MIN_S) {
                        val w = s * v
                        val bin = min(BINS - 1, (h * BINS).toInt())
                        out[o + 4 + bin] += w
                        out[o + 16 + bin * 3] += c.r * w; out[o + 17 + bin * 3] += c.g * w; out[o + 18 + bin * 3] += c.b * w
                    }
                    x += st
                }
                y += st
            }
        }
        return out
    }

    fun stats(raw: FloatArray, zone: Int): ZoneStats {
        val o = zone * STRIDE
        if (o + STRIDE > raw.size || raw[o + 3] <= 0f) return ZoneStats.Empty
        val n = raw[o + 3]
        val avg = RGB(raw[o] / n, raw[o + 1] / n, raw[o + 2] / n)
        var best = 0
        for (b in 1 until BINS) if (raw[o + 4 + b] > raw[o + 4 + best]) best = b
        val w0 = raw[o + 4 + best]
        if (w0 <= 0f) return ZoneStats(avg, avg, 0f, avg.luma, ColorMath.hsv(avg).s)
        // Best hue bin plus any neighbour with at least a third of its weight, so a hue on a bin edge is not split.
        var w = w0
        var r = raw[o + 16 + best * 3]
        var g = raw[o + 17 + best * 3]
        var b = raw[o + 18 + best * 3]
        for (d in intArrayOf(-1, 1)) {
            val k = (best + d + BINS) % BINS
            if (raw[o + 4 + k] > 0.35f * w0) {
                w += raw[o + 4 + k]; r += raw[o + 16 + k * 3]; g += raw[o + 17 + k * 3]; b += raw[o + 18 + k * 3]
            }
        }
        return ZoneStats(avg, RGB(r / w, g / w, b / w), min(1f, w / n), avg.luma, ColorMath.hsv(avg).s)
    }
}

enum class ColorStyle(val title: String, val summary: String) {
    Accurate("Accurate", "Colors as they appear on screen"),
    Enhanced("Enhanced", "Richer, more vivid color"),
    Cinematic("Cinematic", "Warmer, deeper contrast, slower fades"),
    Gaming("Gaming", "Aggressive color, fastest response"),
    VividPlus("Vivid+", "Punchy wall color over accuracy");

    class Params(
        val pick: Float, val satPow: Float, val satGain: Float, val valuePow: Float, val contrast: Float,
        val warmth: Float, val tauScale: Float, val slewScale: Float, val autoGain: Float,
    )

    val params: Params
        get() = when (this) {
            Accurate -> Params(0.15f, 1.0f, 1.0f, 1.0f, 0f, 0f, 1.0f, 1f, 0.25f)
            Enhanced -> Params(0.65f, 0.72f, 1.12f, 0.78f, 0.1f, 0f, 0.8f, 1f, 0.6f)
            Cinematic -> Params(0.5f, 0.85f, 1.05f, 0.9f, 0.35f, 0.22f, 1.5f, 1f, 0.6f)
            Gaming -> Params(0.85f, 0.6f, 1.2f, 0.7f, 0.15f, 0f, 0.35f, 2f, 0.3f)
            VividPlus -> Params(0.8f, 0.55f, 1.25f, 0.62f, 0.2f, 0f, 0.7f, 1.3f, 0.8f)
        }
}

data class SyncSettings(
    val brightness: Float = 1f,
    val intensity: Float = 1f,
    val smoothing: Float = 0.5f,
    val fps: Int = 50,
    val gamma: Float = 1f,
    val dominance: Float = 0.5f,
    val dynamicBrightness: Boolean = true,
    val nightMode: Boolean = false,
    val lowLightMode: Boolean = false,
    val brightnessLimit: Float = 1f,
    val style: ColorStyle = ColorStyle.Enhanced,
    val warmth: Float = 0f,
)

data class Profile(val name: String, val blurb: String, val settings: SyncSettings) {
    companion object {
        val presets = listOf(
            Profile("Movie", "Cinematic color, slow fades", SyncSettings(style = ColorStyle.Cinematic, smoothing = 0.7f, dominance = 0.6f)),
            Profile("Gaming", "Fast response, vivid color", SyncSettings(style = ColorStyle.Gaming, smoothing = 0.15f, fps = 60, dynamicBrightness = false)),
            Profile("Relaxed", "Dim, calm ambient glow", SyncSettings(style = ColorStyle.Enhanced, smoothing = 0.9f, brightness = 0.4f, lowLightMode = true, intensity = 0.9f, dominance = 0.7f)),
        )
    }
}

private fun smoothstep(a: Float, b: Float, x: Float): Float {
    val t = ((x - a) / (b - a)).coerceIn(0f, 1f)
    return t * t * (3 - 2 * t)
}

/**
 * Turns per-channel zone stats into light colors: pick dominant vs average, shape saturation and brightness,
 * smooth in HSV (hue along the shortest arc, brightness rate-limited), then white balance and brightness ceiling.
 */
class ColorEngine {
    private class Chan(var h: Float, var s: Float, var v: Float)

    private val state = HashMap<Int, Chan>()
    private var vAvg = 0.4f
    private var autoGain = 1f

    fun reset() { state.clear(); vAvg = 0.4f; autoGain = 1f }

    fun process(channels: List<Pair<Int, ZoneStats>>, dtIn: Float, s: SyncSettings): Map<Int, RGB> {
        val dt = dtIn.coerceIn(0.001f, 0.25f)
        val p = s.style.params

        class Target(val id: Int, val h: Float, val s: Float, val v: Float)
        val targets = channels.map { (id, st) ->
            val evidence = smoothstep(0.03f, 0.2f, st.dominance)
            val k = min(1f, p.pick * (0.5f + s.dominance) * evidence)
            val (h, sat, v) = ColorMath.hsv(st.average.mixed(st.dominant, k))
            // The saturation lift fades out for near-neutral colors, so white and gray stay neutral.
            val lifted = min(1f, sat.pow(p.satPow) * p.satGain * s.intensity)
            Target(id, h, sat + (lifted - sat) * smoothstep(0.03f, 0.15f, sat), v)
        }
        if (s.dynamicBrightness && p.autoGain > 0 && targets.isNotEmpty()) {
            val f = targets.sumOf { it.v.toDouble() }.toFloat() / targets.size
            vAvg += (f - vAvg) * (1 - exp(-dt / 3))
            val tg = (0.5f / max(vAvg, 0.08f)).pow(p.autoGain).coerceIn(0.85f, 1.8f)
            autoGain += (tg - autoGain) * (1 - exp(-dt / 2))
        } else {
            autoGain = 1f
        }

        val tau = (0.04f + s.smoothing * 0.76f) * p.tauScale
        val slew = (1.5f + (1 - s.smoothing) * 6.5f) * p.slewScale
        val hueRate = (1.5f + (1 - s.smoothing) * 4f) * p.slewScale
        val cap = ceiling(s)
        val wb = (p.warmth + s.warmth).coerceIn(-1f, 1f)
        val out = HashMap<Int, RGB>()
        for (t in targets) {
            var x = min(t.v * autoGain, 1f)
            x += p.contrast * (x * x * (3 - 2 * x) - x)
            val vt = x.pow(p.valuePow * s.gamma) * smoothstep(0.02f, 0.07f, x)

            val c = state.getOrPut(t.id) { Chan(t.h, t.s, 0f) }
            val tv = if (vt > c.v) tau * 0.5f else tau * 1.5f
            var nv = c.v + (vt - c.v) * (1 - exp(-dt / max(tv, 0.001f)))
            nv = nv.coerceIn(c.v - slew * dt, c.v + slew * dt)
            val ns = c.s + (t.s - c.s) * (1 - exp(-dt / max(tau * 0.7f, 0.001f)))
            if (c.s < 0.06f || c.v < 0.02f) {
                c.h = t.h
            } else if (vt > 0.02f && t.s > 0.05f) {
                var d = t.h - c.h
                if (d > 0.5f) d -= 1f else if (d < -0.5f) d += 1f
                val conf = min(1f, t.s * 3)
                var step = d * (1 - exp(-dt / max(tau * 0.6f / conf, 0.001f)))
                step = step.coerceIn(-hueRate * dt, hueRate * dt)
                c.h += step
                c.h -= floor(c.h)
            }
            c.s = ns
            c.v = nv

            var o = ColorMath.rgb(c.h, c.s, c.v)
            if (wb > 0) o = RGB(o.r, o.g * (1 - 0.10f * wb), o.b * (1 - 0.40f * wb))
            else if (wb < 0) o = RGB(o.r * (1 + 0.25f * wb), o.g * (1 + 0.05f * wb), o.b)
            if (s.lowLightMode) o = RGB(o.r.pow(0.8f), o.g.pow(0.8f), o.b.pow(0.8f))
            if (s.nightMode) o = RGB(o.r, o.g * 0.9f, o.b * 0.7f)
            out[t.id] = o.scaled(min(s.brightness, cap)).clamped()
        }
        return out
    }

    companion object {
        /** Hard ceiling for any output component: brightness limiter, night and low-light caps. */
        fun ceiling(s: SyncSettings): Float {
            var cap = min(s.brightnessLimit, 1f)
            if (s.lowLightMode) cap = min(cap, 0.45f)
            if (s.nightMode) cap = min(cap, 0.3f)
            return cap
        }
    }
}


data class XY(val x: Double, val y: Double)
data class Gamut(val red: XY, val green: XY, val blue: XY)

object Xy {
    private fun toLinear(v: Float): Double = v.toDouble().let { if (it > 0.04045) Math.pow((it + 0.055) / 1.055, 2.4) else it / 12.92 }

    /** sRGB (gamma encoded, 0..1) to CIE xy with the Philips wide-gamut D65 matrix, clamped to the light's gamut. */
    fun of(c: RGB, gamut: Gamut?): XY {
        val r = toLinear(c.r); val g = toLinear(c.g); val b = toLinear(c.b)
        val x = r * 0.664511 + g * 0.154324 + b * 0.162028
        val y = r * 0.283881 + g * 0.668433 + b * 0.047685
        val z = r * 0.000088 + g * 0.072310 + b * 0.986039
        val sum = x + y + z
        val p = if (sum > 0) XY(x / sum, y / sum) else XY(0.3127, 0.3290)
        return if (gamut == null) p else clamp(p, gamut)
    }

    private fun cross(a: XY, b: XY) = a.x * b.y - a.y * b.x
    private fun sub(a: XY, b: XY) = XY(a.x - b.x, a.y - b.y)
    private fun d2(a: XY, b: XY) = (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y)

    fun clamp(p: XY, g: Gamut): XY {
        val v1 = sub(g.green, g.red); val v2 = sub(g.blue, g.red); val q = sub(p, g.red)
        val den = cross(v1, v2)
        val s = cross(q, v2) / den
        val t = cross(v1, q) / den
        if (s >= 0 && t >= 0 && s + t <= 1) return p
        fun closest(a: XY, b: XY): XY {
            val ap = sub(p, a); val ab = sub(b, a)
            val u = ((ap.x * ab.x + ap.y * ab.y) / (ab.x * ab.x + ab.y * ab.y)).coerceIn(0.0, 1.0)
            return XY(a.x + ab.x * u, a.y + ab.y * u)
        }
        return listOf(closest(g.red, g.green), closest(g.green, g.blue), closest(g.blue, g.red)).minByOrNull { d2(it, p) }!!
    }
}
