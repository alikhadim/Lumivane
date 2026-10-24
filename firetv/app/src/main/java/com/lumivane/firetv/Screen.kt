package com.lumivane.firetv

import android.content.Context
import android.content.Intent
import android.graphics.PixelFormat
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.ImageReader
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Handler
import android.os.HandlerThread
import kotlin.math.max

/** Captures the screen at low resolution through MediaProjection and turns it into light colors with the Mac color engine. */
class ScreenPipeline(ctx: Context, code: Int, data: Intent, private val channels: List<Channel>) : Pipeline {
    private val thread = HandlerThread("lumivane-capture").apply { start() }
    private val handler = Handler(thread.looper)
    private val projection: MediaProjection
    private val reader: ImageReader
    private val display: VirtualDisplay
    private val lock = Any()
    private var frame: ByteArray? = null
    private var fw = 0
    private var fh = 0
    private var stride = 0
    private val zones = channels.map { ZoneLayout.rectFor(it) }
    private val engine = ColorEngine()
    @Volatile override var inputLuma: Float? = null
        private set

    init {
        val mpm = ctx.getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
        projection = mpm.getMediaProjection(code, data)
        projection.registerCallback(object : MediaProjection.Callback() {
            override fun onStop() { SyncHub.stop() }
        }, handler)
        val dm = ctx.resources.displayMetrics
        val w = 192
        val h = (w * dm.heightPixels.toFloat() / dm.widthPixels).toInt().coerceAtLeast(64)
        reader = ImageReader.newInstance(w, h, PixelFormat.RGBA_8888, 2)
        reader.setOnImageAvailableListener({ r ->
            val img = r.acquireLatestImage() ?: return@setOnImageAvailableListener
            try {
                val p = img.planes[0]
                val buf = p.buffer
                synchronized(lock) {
                    val need = buf.remaining()
                    if (frame?.size != need) frame = ByteArray(need)
                    buf.get(frame!!, 0, need)
                    fw = img.width; fh = img.height; stride = p.rowStride
                }
            } finally {
                img.close()
            }
        }, handler)
        display = projection.createVirtualDisplay(
            "lumivane", w, h, dm.densityDpi, DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR, reader.surface, null, handler,
        )
    }

    override fun step(dt: Float, t: Float, settings: SyncSettings): Map<Int, RGB> {
        val raw = synchronized(lock) {
            val f = frame ?: return@synchronized null
            ZoneAccumulator.reduceRgba(f, fw, fh, stride, zones)
        } ?: return channels.associate { it.id to RGB.Black }
        val stats = channels.mapIndexed { i, c -> c.id to ZoneAccumulator.stats(raw, i) }
        inputLuma = (stats.sumOf { it.second.brightness.toDouble() } / max(1, stats.size)).toFloat()
        return engine.process(stats, dt, settings)
    }

    override fun close() {
        runCatching { display.release() }
        runCatching { reader.close() }
        runCatching { projection.stop() }
        thread.quitSafely()
    }
}
