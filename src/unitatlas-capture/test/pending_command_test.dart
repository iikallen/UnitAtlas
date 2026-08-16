import 'package:flutter_test/flutter_test.dart';
import 'package:unitatlas_capture/capture/pending_command.dart';

void main() {
  test('offline aggregation command keeps a UUIDv7 and deterministic body', () {
    final command = PendingCommand.aggregation(
      deviceId: 'TC22-014',
      parentCode: 'BOX-10',
      unitAtlasIds: ['UA-1'],
      logisticUnitCodes: const [],
    );
    expect(command.id[14], '7');
    expect(command.toRequest()['commandId'], command.id);
    expect(command.toRequest()['parentCode'], 'BOX-10');
    expect(command.syncStatus, 'PENDING');
  });

  test('production confirmation stays queued until reconnect', () {
    final command = PendingCommand.production(
      deviceId: 'TC22-014',
      scannedCode: 'SERIAL-1',
      location: 'Line 1',
    );
    expect(command.toRequest()['commandType'], 'PRODUCTION');
    expect(command.toRequest()['scannedCode'], 'SERIAL-1');
    expect(command.syncStatus, 'PENDING');
  });
}
