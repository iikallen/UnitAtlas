import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:unitatlas_capture/api/capture_api.dart';
import 'package:unitatlas_capture/capture/pending_command.dart';

void main() {
  test('enrollment omits device session and enrolled calls send it', () async {
    final requests = <http.Request>[];
    final api = CaptureApi(
      Uri.parse('https://unitatlas.test'),
      accessToken: 'user-token',
      sessionToken: 'device-token',
      client: MockClient((request) async {
        requests.add(request);
        return http.Response(
          request.url.path.endsWith('/enroll')
              ? '{"sessionToken":"new-token"}'
              : '{"changes":[],"nextToken":"7","hasMore":false}',
          200,
          headers: {'content-type': 'application/json'},
        );
      }),
    );

    await api.enroll('ZEBRA-1', 'one-time-code');
    await api.changes('7');
    await api.production(
      PendingCommand.production(
        deviceId: 'ZEBRA-1',
        scannedCode: 'SERIAL-1',
        location: 'Line 1',
      ),
    );

    expect(requests.first.headers['authorization'], 'Bearer user-token');
    expect(
      requests.first.headers,
      isNot(contains('x-unitatlas-device-session')),
    );
    expect(requests.last.headers['x-unitatlas-device-session'], 'device-token');
    expect(requests.last.url.path, '/api/v1/capture/production');
  });
}
