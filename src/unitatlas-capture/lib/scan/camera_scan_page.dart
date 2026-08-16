import 'dart:async';

import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import 'scan_source.dart';

class CameraScanPage extends StatefulWidget {
  const CameraScanPage({super.key});
  @override
  State<CameraScanPage> createState() => _CameraScanPageState();
}

class _CameraScanPageState extends State<CameraScanPage> {
  final source = CameraScanSource();
  StreamSubscription<String>? subscription;
  bool accepted = false;

  @override
  void initState() {
    super.initState();
    subscription = source.scans.listen((value) {
      if (mounted) Navigator.pop(context, value);
    });
  }

  @override
  void dispose() {
    subscription?.cancel();
    source.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Сканировать код')),
    body: MobileScanner(
      onDetect: (capture) {
        if (accepted) return;
        final value = capture.barcodes.firstOrNull?.rawValue;
        if (value == null) return;
        accepted = true;
        source.submit(value);
      },
    ),
  );
}
