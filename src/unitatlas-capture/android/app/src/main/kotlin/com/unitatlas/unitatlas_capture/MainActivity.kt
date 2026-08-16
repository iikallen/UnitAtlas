package com.unitatlas.unitatlas_capture

import android.content.Intent
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    private var scannerChannel: MethodChannel? = null

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        scannerChannel = MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "unitatlas/scanner")
        emitScan(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        emitScan(intent)
    }

    private fun emitScan(intent: Intent?) {
        val value = intent?.getStringExtra("com.symbol.datawedge.data_string")
            ?: intent?.getStringExtra("data")
            ?: intent?.getStringExtra("barcode")
        if (!value.isNullOrBlank()) scannerChannel?.invokeMethod("scan", value)
    }
}
