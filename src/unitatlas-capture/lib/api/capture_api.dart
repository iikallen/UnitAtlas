import 'dart:convert';

import 'package:http/http.dart' as http;

import '../capture/pending_command.dart';

class CaptureApi {
  CaptureApi(this.baseUri, {http.Client? client})
    : _client = client ?? http.Client();

  final Uri baseUri;
  final http.Client _client;

  Future<Map<String, dynamic>> bootstrap() async =>
      _json(await _client.get(baseUri.resolve('/api/v1/capture/bootstrap')));

  Future<Map<String, dynamic>> sync(PendingCommand command) async {
    final response = await _client.post(
      baseUri.resolve('/api/v1/capture/sync'),
      headers: {'content-type': 'application/json'},
      body: jsonEncode(command.toRequest()),
    );
    return _json(response);
  }

  Future<Map<String, dynamic>> resolve(String code) async => _json(
    await _client.post(
      baseUri.resolve('/api/v1/capture/resolve'),
      headers: {'content-type': 'application/json'},
      body: jsonEncode({'code': code}),
    ),
  );

  Map<String, dynamic> _json(http.Response response) {
    final body = jsonDecode(response.body) as Map<String, dynamic>;
    if (response.statusCode >= 400) {
      throw CaptureApiException(response.statusCode, body);
    }
    return body;
  }
}

class CaptureApiException implements Exception {
  const CaptureApiException(this.statusCode, this.body);
  final int statusCode;
  final Map<String, dynamic> body;
}
