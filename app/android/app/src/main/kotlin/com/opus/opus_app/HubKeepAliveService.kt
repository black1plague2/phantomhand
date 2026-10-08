package com.opus.opus_app

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.os.PowerManager

/**
 * Runs while the hub runs, and does nothing but exist. A foreground service keeps the app's process out of the "cached"
 * class, so Android does not freeze it (and the hub's sockets with it) a few seconds after another app takes the screen;
 * the wake lock keeps the CPU answering the headset's pings once the screen is off. Started and stopped by the Dart hub
 * controller through MainActivity's "opus/hub_keepalive" channel.
 *
 * Measured on the team phone (Android 15) on 2026-10-09: a root job brings another app to the front every 10 minutes; the
 * hub stopped answering each time until the operator app was opened again, and the headset showed a lost link.
 */
class HubKeepAliveService : Service() {
    private var wakeLock: PowerManager.WakeLock? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        try {
            val channel = "hub"
            val builder = if (Build.VERSION.SDK_INT >= 26) {
                getSystemService(NotificationManager::class.java)
                    .createNotificationChannel(NotificationChannel(channel, "Hub", NotificationManager.IMPORTANCE_LOW))
                Notification.Builder(this, channel)
            } else {
                @Suppress("DEPRECATION") Notification.Builder(this)
            }
            val notification = builder
                .setContentTitle("Chetna: the hub is running")
                .setContentText("The headset stays connected while another app is on screen.")
                .setSmallIcon(R.mipmap.ic_launcher)
                .setOngoing(true)
                .build()
            if (Build.VERSION.SDK_INT >= 29) startForeground(1, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
            else startForeground(1, notification)
            if (wakeLock == null) {
                // ponytail: 12 h ceiling, so a phone left on the Monitor tab does not hold the CPU awake for ever
                wakeLock = (getSystemService(POWER_SERVICE) as PowerManager)
                    .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "opus:hub").apply { acquire(12 * 60 * 60 * 1000L) }
            }
        } catch (e: Exception) {
            // refused by the system: the hub runs on as before, unprotected, instead of the app dying here
            stopSelf()
        }
        return START_NOT_STICKY
    }

    // swiped out of the recent apps: the hub went with the Dart isolate
    override fun onTaskRemoved(rootIntent: Intent?) {
        stopSelf()
    }

    override fun onDestroy() {
        wakeLock?.let { if (it.isHeld) it.release() }
        wakeLock = null
        super.onDestroy()
    }
}
