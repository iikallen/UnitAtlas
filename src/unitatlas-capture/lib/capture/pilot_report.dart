import 'pending_command.dart';

class PilotReport {
  PilotReport({
    required this.startedAt,
    required this.acceptedScans,
    required this.recognitionErrors,
    required this.acknowledgedCommands,
    required this.duplicateResponses,
    required this.syncRetries,
    required this.conflicts,
    required this.pendingCommands,
    required this.p50Ms,
    required this.p95Ms,
  });

  factory PilotReport.fromCommands({
    required DateTime startedAt,
    required int acceptedScans,
    required int recognitionErrors,
    required List<PendingCommand> commands,
  }) {
    final latencies =
        commands
            .where((x) => x.syncStatus == 'ACKNOWLEDGED')
            .map((x) => x.lastLatencyMs)
            .whereType<int>()
            .toList()
          ..sort();
    return PilotReport(
      startedAt: startedAt,
      acceptedScans: acceptedScans,
      recognitionErrors: recognitionErrors,
      acknowledgedCommands: commands
          .where((x) => x.syncStatus == 'ACKNOWLEDGED')
          .length,
      duplicateResponses: commands.where((x) => x.duplicate).length,
      syncRetries: commands.fold(
        0,
        (total, x) => total + (x.attemptCount > 1 ? x.attemptCount - 1 : 0),
      ),
      conflicts: commands.where((x) => x.syncStatus == 'CONFLICT').length,
      pendingCommands: commands
          .where((x) => x.syncStatus == 'PENDING' || x.syncStatus == 'RETRY')
          .length,
      p50Ms: _percentile(latencies, 0.50),
      p95Ms: _percentile(latencies, 0.95),
    );
  }

  final DateTime startedAt;
  final int acceptedScans;
  final int recognitionErrors;
  final int acknowledgedCommands;
  final int duplicateResponses;
  final int syncRetries;
  final int conflicts;
  final int pendingCommands;
  final int? p50Ms;
  final int? p95Ms;

  int get physicalAttempts => acceptedScans + recognitionErrors;

  String toText({required String deviceId, String? station}) =>
      '''
UNITATLAS PHYSICAL SCAN REPORT
started_at=${startedAt.toUtc().toIso8601String()}
generated_at=${DateTime.now().toUtc().toIso8601String()}
device=$deviceId
station=${station ?? 'NOT_RECORDED'}
physical_attempts=$physicalAttempts
successful=$acceptedScans
recognition_errors=$recognitionErrors
acknowledged_commands=$acknowledgedCommands
duplicate_responses=$duplicateResponses
sync_retries=$syncRetries
conflicts=$conflicts
pending_commands=$pendingCommands
p50_ms=${p50Ms ?? 'NO_SAMPLES'}
p95_ms=${p95Ms ?? 'NO_SAMPLES'}
printer=RECORD_IN_RUNBOOK
symbology=RECORD_IN_RUNBOOK
network_mode=RECORD_IN_RUNBOOK''';

  static int? _percentile(List<int> sorted, double value) {
    if (sorted.isEmpty) return null;
    return sorted[(sorted.length * value).ceil() - 1];
  }
}
