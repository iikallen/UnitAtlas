import 'dart:async';

import 'package:flutter/services.dart';

abstract interface class ScanSource {
  Stream<String> get scans;
  Future<void> dispose();
}

class KeyboardWedgeScanSource implements ScanSource {
  final _controller = StreamController<String>.broadcast();
  @override
  Stream<String> get scans => _controller.stream;
  void submit(String value) {
    if (value.trim().isNotEmpty) _controller.add(value.trim());
  }

  @override
  Future<void> dispose() => _controller.close();
}

class CameraScanSource implements ScanSource {
  final _controller = StreamController<String>.broadcast();
  @override
  Stream<String> get scans => _controller.stream;
  void submit(String value) => _controller.add(value);
  @override
  Future<void> dispose() => _controller.close();
}

class AndroidIntentScanSource implements ScanSource {
  AndroidIntentScanSource() {
    _channel.setMethodCallHandler((call) async {
      if (call.method == 'scan' && call.arguments is String) {
        _controller.add(call.arguments as String);
      }
    });
  }
  static const _channel = MethodChannel('unitatlas/scanner');
  final _controller = StreamController<String>.broadcast();
  @override
  Stream<String> get scans => _controller.stream;
  @override
  Future<void> dispose() async {
    _channel.setMethodCallHandler(null);
    await _controller.close();
  }
}
