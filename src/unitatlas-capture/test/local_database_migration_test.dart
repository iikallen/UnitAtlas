import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:sqflite_common_ffi/sqflite_ffi.dart';
import 'package:unitatlas_capture/capture/pending_command.dart';
import 'package:unitatlas_capture/database/local_database.dart';

void main() {
  sqfliteFfiInit();

  test('v1 queue survives the pilot metrics migration', () async {
    final directory = await Directory.systemTemp.createTemp(
      'unitatlas-capture-',
    );
    addTearDown(() => directory.delete(recursive: true));
    final path = '${directory.path}/capture.db';
    final old = await databaseFactoryFfi.openDatabase(
      path,
      options: OpenDatabaseOptions(
        version: 1,
        onCreate: (db, _) async {
          await db.execute('''CREATE TABLE pending_commands (
            id TEXT PRIMARY KEY,
            device_id TEXT NOT NULL,
            command_type TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            sync_status TEXT NOT NULL,
            attempt_count INTEGER NOT NULL,
            last_error TEXT
          )''');
          await db.insert('pending_commands', {
            'id': '018f0000-0000-7000-8000-000000000001',
            'device_id': 'TC22-014',
            'command_type': 'TRACE_EVENT',
            'payload_json': '{}',
            'created_at': DateTime.now().toUtc().toIso8601String(),
            'sync_status': 'PENDING',
            'attempt_count': 0,
          });
        },
      ),
    );
    await old.close();

    final database = LocalDatabase(factory: databaseFactoryFfi, path: path);
    addTearDown(database.close);
    final commands = await database.allCommands();

    expect(commands, hasLength(1));
    expect(commands.single.syncStatus, 'PENDING');
    expect(commands.single.lastLatencyMs, isNull);
    await database.resetPilotReport();
    final measured = PendingCommand(
      id: '018f0000-0000-7000-8000-000000000002',
      deviceId: 'TC22-014',
      commandType: 'TRACE_EVENT',
      payload: const {},
      createdAt: DateTime.now().toUtc(),
    );
    await database.enqueue(measured);
    await database.recordAcceptedScan();
    await database.recordRecognitionError();
    await database.updateResult(
      measured.id,
      'ACKNOWLEDGED',
      null,
      latencyMs: 125,
      duplicate: true,
    );
    final report = await database.pilotReport();
    expect(report.acceptedScans, 1);
    expect(report.recognitionErrors, 1);
    expect(report.acknowledgedCommands, 1);
    expect(report.duplicateResponses, 1);
    expect(report.p50Ms, 125);
  });
}
