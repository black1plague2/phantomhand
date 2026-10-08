package com.opus.opus_app

import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.view.WindowManager
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Keep the screen on while the app is in the foreground: the operator watches the live card and rarely
        // touches the phone, and a dimmed screen mid-run loses the run. The flag only acts while this window is
        // visible; the normal screen timeout applies again once the app is left. A window flag, so no plugin.
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
    }

    // The Dart hub controller starts and stops the service that keeps the hub answering while another app has the screen.
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "opus/hub_keepalive").setMethodCallHandler { call, result ->
            val service = Intent(this, HubKeepAliveService::class.java)
            try {
                when (call.method) {
                    "start" -> if (Build.VERSION.SDK_INT >= 26) startForegroundService(service) else startService(service)
                    "stop" -> stopService(service)
                    else -> return@setMethodCallHandler result.notImplemented()
                }
                result.success(true)
            } catch (e: Exception) {
                result.success(false)   // refused (the app was not in front): the hub runs on, unprotected
            }
        }
    }

    override fun onDestroy() {
        stopService(Intent(this, HubKeepAliveService::class.java))   // the hub lives in this activity's Dart isolate
        super.onDestroy()
    }
}
