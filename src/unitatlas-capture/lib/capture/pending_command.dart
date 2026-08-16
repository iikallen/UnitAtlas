import 'dart:convert';

import 'package:uuid/uuid.dart';

class PendingCommand {
  PendingCommand({
    required this.id,
    required this.deviceId,
    required this.commandType,
    required this.payload,
    required this.createdAt,
    this.syncStatus = 'PENDING',
    this.attemptCount = 0,
    this.lastError,
    this.lastLatencyMs,
    this.duplicate = false,
  });

  factory PendingCommand.aggregation({
    required String deviceId,
    required String parentCode,
    required List<String> unitAtlasIds,
    required List<String> logisticUnitCodes,
  }) => PendingCommand(
    id: const Uuid().v7(),
    deviceId: deviceId,
    commandType: 'AGGREGATE',
    payload: {
      'parentCode': parentCode,
      'action': 'ADD',
      'unitAtlasIds': unitAtlasIds,
      'logisticUnitCodes': logisticUnitCodes,
      'occurredAt': DateTime.now().toUtc().toIso8601String(),
    },
    createdAt: DateTime.now().toUtc(),
  );

  factory PendingCommand.trace({
    required String deviceId,
    required String unitAtlasId,
    required String eventType,
    required String location,
  }) => PendingCommand(
    id: const Uuid().v7(),
    deviceId: deviceId,
    commandType: 'TRACE_EVENT',
    payload: {
      'unitAtlasId': unitAtlasId,
      'eventType': eventType,
      'location': location,
      'occurredAt': DateTime.now().toUtc().toIso8601String(),
    },
    createdAt: DateTime.now().toUtc(),
  );

  factory PendingCommand.production({
    required String deviceId,
    required String scannedCode,
    required String location,
  }) => PendingCommand(
    id: const Uuid().v7(),
    deviceId: deviceId,
    commandType: 'PRODUCTION',
    payload: {
      'scannedCode': scannedCode,
      'location': location,
      'occurredAt': DateTime.now().toUtc().toIso8601String(),
    },
    createdAt: DateTime.now().toUtc(),
  );

  factory PendingCommand.fromDatabase(Map<String, Object?> row) =>
      PendingCommand(
        id: row['id']! as String,
        deviceId: row['device_id']! as String,
        commandType: row['command_type']! as String,
        payload:
            jsonDecode(row['payload_json']! as String) as Map<String, dynamic>,
        createdAt: DateTime.parse(row['created_at']! as String),
        syncStatus: row['sync_status']! as String,
        attemptCount: row['attempt_count']! as int,
        lastError: row['last_error'] as String?,
        lastLatencyMs: row['last_latency_ms'] as int?,
        duplicate: (row['duplicate'] as int? ?? 0) == 1,
      );

  final String id;
  final String deviceId;
  final String commandType;
  final Map<String, dynamic> payload;
  final DateTime createdAt;
  final String syncStatus;
  final int attemptCount;
  final String? lastError;
  final int? lastLatencyMs;
  final bool duplicate;

  Map<String, Object?> toDatabase() => {
    'id': id,
    'device_id': deviceId,
    'command_type': commandType,
    'payload_json': jsonEncode(payload),
    'created_at': createdAt.toIso8601String(),
    'sync_status': syncStatus,
    'attempt_count': attemptCount,
    'last_error': lastError,
    'last_latency_ms': lastLatencyMs,
    'duplicate': duplicate ? 1 : 0,
  };

  Map<String, dynamic> toRequest() => {
    'commandId': id,
    'deviceId': deviceId,
    'commandType': commandType,
    ...payload,
  };
}
