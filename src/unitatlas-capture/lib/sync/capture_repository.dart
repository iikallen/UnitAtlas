import 'dart:convert';

import '../api/capture_api.dart';
import '../capture/pending_command.dart';
import '../database/local_database.dart';

class CaptureRepository {
  CaptureRepository({
    required this.database,
    required this.api,
    required this.deviceId,
  });

  final LocalDatabase database;
  final CaptureApi api;
  final String deviceId;

  Future<void> bootstrap() async =>
      database.cacheBootstrap(await api.bootstrap());

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

  Future<void> sync() async {
    for (final command in await database.pending()) {
      try {
        await api.sync(command);
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
  }
}
