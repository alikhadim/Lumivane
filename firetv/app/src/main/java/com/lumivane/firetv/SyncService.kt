package com.lumivane.firetv

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

/** Keeps the sync alive while another app is in front. */
class SyncService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val req = SyncHub.take()
        if (req == null) {
            stopSelf()
            return START_NOT_STICKY
        }
        val type = if (req.source == SourceKind.Screen) ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION else ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC
        try {
            ServiceCompat.startForeground(this, 1, notification(req.source.title), type)
        } catch (e: Exception) {
            SyncHub.fail("Android did not allow the sync service to start: ${e.message ?: "unknown reason"}")
            stopSelf()
            return START_NOT_STICKY
        }
        SyncHub.job = scope.launch { SyncHub.run(applicationContext, req) }.also { it.invokeOnCompletion { stopSelf() } }
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        SyncHub.job?.cancel()
        scope.cancel()
        super.onDestroy()
    }

    private fun notification(source: String): Notification {
        val id = "sync"
        if (Build.VERSION.SDK_INT >= 26) {
            (getSystemService(NOTIFICATION_SERVICE) as NotificationManager)
                .createNotificationChannel(NotificationChannel(id, "Sync", NotificationManager.IMPORTANCE_LOW))
        }
        return NotificationCompat.Builder(this, id)
            .setSmallIcon(applicationInfo.icon)
            .setContentTitle("Lumivane is syncing")
            .setContentText(source)
            .setOngoing(true)
            .build()
    }
}
