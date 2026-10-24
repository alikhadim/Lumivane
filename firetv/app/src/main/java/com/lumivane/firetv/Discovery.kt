package com.lumivane.firetv

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.net.wifi.WifiManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.withContext
import org.json.JSONArray
import java.net.Inet4Address
import java.net.URL
import javax.net.ssl.HttpsURLConnection

object Discovery {

    /** Emits IPv4 addresses of Hue bridges announcing _hue._tcp on the local network. */
    @Suppress("DEPRECATION")
    fun scan(ctx: Context): Flow<String> = callbackFlow {
        val app = ctx.applicationContext
        val nsd = app.getSystemService(Context.NSD_SERVICE) as NsdManager
        val lock = (app.getSystemService(Context.WIFI_SERVICE) as? WifiManager)
            ?.createMulticastLock("lumivane")?.apply { setReferenceCounted(false); acquire() }
        val queue = ArrayDeque<NsdServiceInfo>()
        val mutex = Any()
        var busy = false

        fun pump() {
            val next = synchronized(mutex) {
                if (busy || queue.isEmpty()) null else { busy = true; queue.removeFirst() }
            } ?: return
            nsd.resolveService(next, object : NsdManager.ResolveListener {
                override fun onResolveFailed(info: NsdServiceInfo, code: Int) {
                    synchronized(mutex) { busy = false }
                    pump()
                }
                override fun onServiceResolved(info: NsdServiceInfo) {
                    (info.host as? Inet4Address)?.hostAddress?.let { trySend(it) }
                    synchronized(mutex) { busy = false }
                    pump()
                }
            })
        }

        val listener = object : NsdManager.DiscoveryListener {
            override fun onStartDiscoveryFailed(type: String, code: Int) { close() }
            override fun onStopDiscoveryFailed(type: String, code: Int) {}
            override fun onDiscoveryStarted(type: String) {}
            override fun onDiscoveryStopped(type: String) {}
            override fun onServiceLost(info: NsdServiceInfo) {}
            override fun onServiceFound(info: NsdServiceInfo) {
                synchronized(mutex) { queue.addLast(info) }
                pump()
            }
        }
        nsd.discoverServices("_hue._tcp", NsdManager.PROTOCOL_DNS_SD, listener)
        awaitClose {
            runCatching { nsd.stopServiceDiscovery(listener) }
            runCatching { lock?.release() }
        }
    }

    /** Philips discovery service. The request reveals the public IP of this network, so it is only used on request. */
    suspend fun online(): List<String> = withContext(Dispatchers.IO) {
        val c = URL("https://discovery.meethue.com").openConnection() as HttpsURLConnection
        try {
            c.connectTimeout = 5_000
            c.readTimeout = 5_000
            JSONArray(c.inputStream.bufferedReader().use { it.readText() }).objects()
                .map { it.optString("internalipaddress") }.filter(::isIPv4)
        } finally {
            c.disconnect()
        }
    }
}
