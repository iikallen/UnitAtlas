import 'package:flutter/material.dart';

import '../workflows/task_home.dart';
import '../sync/capture_repository.dart';

class CaptureApp extends StatelessWidget {
  const CaptureApp({super.key, required this.repository});
  final CaptureRepository repository;

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'UnitAtlas Capture',
    theme: ThemeData(
      colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xff3346a8)),
      useMaterial3: true,
    ),
    home: TaskHome(repository: repository),
  );
}
