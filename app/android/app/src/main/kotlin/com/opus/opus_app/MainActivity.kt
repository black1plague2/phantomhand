package com.opus.opus_app

import android.os.Bundle
import android.view.WindowManager
import io.flutter.embedding.android.FlutterActivity

class MainActivity : FlutterActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Keep the screen on while the app is in the foreground: the operator watches the live card and rarely
        // touches the phone, and a dimmed screen mid-run loses the run. The flag only acts while this window is
        // visible; the normal screen timeout applies again once the app is left. A window flag, so no plugin.
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
    }
}
