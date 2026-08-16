import 'package:flutter/material.dart';

import 'api/capture_api.dart';
import 'app/capture_app.dart';
import 'database/local_database.dart';
import 'sync/capture_repository.dart';

void main() {
  const apiUrl = String.fromEnvironment(
    'UNITATLAS_API_URL',
    defaultValue: 'http://10.0.2.2:8080',
  );
  const deviceId = String.fromEnvironment(
    'UNITATLAS_DEVICE_ID',
    defaultValue: 'UNENROLLED-ANDROID',
  );
  final repository = CaptureRepository(
    database: LocalDatabase(),
    api: CaptureApi(Uri.parse(apiUrl)),
    deviceId: deviceId,
  );
  runApp(CaptureApp(repository: repository));
}
