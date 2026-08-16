import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'api/capture_api.dart';
import 'app/capture_app.dart';
import 'database/local_database.dart';
import 'sync/capture_repository.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  const apiUrl = String.fromEnvironment(
    'UNITATLAS_API_URL',
    defaultValue: 'http://10.0.2.2:8080',
  );
  const deviceId = String.fromEnvironment(
    'UNITATLAS_DEVICE_ID',
    defaultValue: 'UNENROLLED-ANDROID',
  );
  const accessToken = String.fromEnvironment('UNITATLAS_ACCESS_TOKEN');
  const storage = FlutterSecureStorage();
  final sessionToken = await storage.read(key: 'device_session');
  final repository = CaptureRepository(
    database: LocalDatabase(),
    api: CaptureApi(
      Uri.parse(apiUrl),
      accessToken: accessToken,
      sessionToken: sessionToken,
    ),
    deviceId: deviceId,
    storage: storage,
  );
  runApp(CaptureApp(repository: repository));
}
