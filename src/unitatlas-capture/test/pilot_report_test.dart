import 'package:flutter_test/flutter_test.dart';
import 'package:unitatlas_capture/capture/pending_command.dart';
import 'package:unitatlas_capture/capture/pilot_report.dart';

void main() {
  test('physical scan report calculates retries and nearest-rank latency', () {
    PendingCommand command({
      required String id,
      required String status,
      required int attempts,
      int? latency,
      bool duplicate = false,
    }) => PendingCommand(
      id: id,
      deviceId: 'TC22-014',
      commandType: 'TRACE_EVENT',
      payload: const {},
      createdAt: DateTime.utc(2026, 8, 16),
      syncStatus: status,
      attemptCount: attempts,
      lastLatencyMs: latency,
      duplicate: duplicate,
    );

    final report = PilotReport.fromCommands(
      startedAt: DateTime.utc(2026, 8, 16),
      acceptedScans: 997,
      recognitionErrors: 3,
      commands: [
        command(id: '1', status: 'ACKNOWLEDGED', attempts: 2, latency: 100),
        command(
          id: '2',
          status: 'ACKNOWLEDGED',
          attempts: 3,
          latency: 200,
          duplicate: true,
        ),
        command(id: '3', status: 'CONFLICT', attempts: 1),
        command(id: '4', status: 'RETRY', attempts: 1),
      ],
    );

    expect(report.physicalAttempts, 1000);
    expect(report.acknowledgedCommands, 2);
    expect(report.duplicateResponses, 1);
    expect(report.syncRetries, 3);
    expect(report.conflicts, 1);
    expect(report.pendingCommands, 1);
    expect(report.p50Ms, 100);
    expect(report.p95Ms, 200);
    expect(
      report.toText(deviceId: 'TC22-014'),
      contains('recognition_errors=3'),
    );
  });
}
