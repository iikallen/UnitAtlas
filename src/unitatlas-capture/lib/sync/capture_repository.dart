import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../api/capture_api.dart';
import '../capture/pending_command.dart';
import '../database/local_database.dart';

class CaptureRepository {
  CaptureRepository({
    required this.database,
    required this.api,
    required this.deviceId,
    required this.storage,
  });

  final LocalDatabase database;
  final CaptureApi api;
  final String deviceId;
  final FlutterSecureStorage storage;
  String? station;

  bool get isEnrolled => api.hasSession;

  Future<void> enroll(String code) async {
    final response = await api.enroll(deviceId, code.trim());
    final token = response['sessionToken'] as String;
    api.sessionToken = token;
    await storage.write(key: 'device_session', value: token);
    await bootstrap();
  }

  Future<void> bootstrap() async {
    final response = await api.bootstrap();
    final stationRow = response['station'] as Map<String, dynamic>?;
    station = stationRow == null
        ? null
        : '${stationRow['code']} · ${stationRow['name']}';
    await database.cacheBootstrap(response);
    await pullChanges();
  }

  Future<void> pullChanges() async {
    var hasMore = true;
    while (hasMore) {
      final response = await api.changes(await database.checkpoint());
      await database.applyChanges(response);
      hasMore = response['hasMore'] as bool? ?? false;
    }
  }

  Future<void> queueAggregation({
    required String parentCode,
    required List<String> unitAtlasIds,
    required List<String> logisticUnitCodes,
  }) => database.enqueue(
    PendingCommand.aggregation(
      deviceId: deviceId,
      parentCode: parentCode,
      unitAtlasIds: unitAtlasIds,
      logisticUnitCodes: logisticUnitCodes,
    ),
  );

  Future<void> queueTrace({
    required String unitAtlasId,
    required String eventType,
    required String location,
  }) => database.enqueue(
    PendingCommand.trace(
      deviceId: deviceId,
      unitAtlasId: unitAtlasId,
      eventType: eventType,
      location: location,
    ),
  );

  Future<void> queueProduction({
    required String scannedCode,
    required String location,
  }) => database.enqueue(
    PendingCommand.production(
      deviceId: deviceId,
      scannedCode: scannedCode,
      location: location,
    ),
  );

  Future<void> sync() async {
    for (final command in await database.pending()) {
      try {
        if (command.commandType == 'PRODUCTION') {
          await api.production(command);
        } else {
          await api.sync(command);
        }
        await database.updateResult(command.id, 'ACKNOWLEDGED', null);
      } on CaptureApiException catch (error) {
        var detail = error.body;
        if (error.statusCode == 409) {
          final units = command.payload['unitAtlasIds'] as List<dynamic>? ?? [];
          if (units.isNotEmpty) {
            try {
              detail = {
                ...detail,
                'server': await api.resolve(units.first as String),
              };
            } on CaptureApiException {
              // Keep the original authoritative conflict when resolution fails.
            }
          }
          await database.updateResult(
            command.id,
            'CONFLICT',
            jsonEncode(detail),
          );
          continue;
        }
        await database.updateResult(command.id, 'RETRY', jsonEncode(detail));
        break;
      } catch (error) {
        await database.updateResult(command.id, 'RETRY', error.toString());
        break;
      }
    }
    await pullChanges();
  }
}
