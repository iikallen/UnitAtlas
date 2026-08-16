import 'dart:convert';

import 'package:http/http.dart' as http;

import '../capture/pending_command.dart';

class CaptureApi {
  CaptureApi(
    this.baseUri, {
    http.Client? client,
    this.accessTokenProvider,
    this.sessionToken,
  }) : _client = client ?? http.Client();

  final Uri baseUri;
  final http.Client _client;
  final Future<String?> Function()? accessTokenProvider;
  String? sessionToken;

  bool get hasSession => sessionToken?.isNotEmpty == true;

  Future<Map<String, dynamic>> enroll(
    String deviceCode,
    String enrollmentCode,
  ) async => _json(
    await _client.post(
      baseUri.resolve('/api/v1/capture/enroll'),
      headers: await _headers(json: true, includeSession: false),
      body: jsonEncode({
        'deviceCode': deviceCode,
        'enrollmentCode': enrollmentCode,
      }),
    ),
  );

  Future<Map<String, dynamic>> bootstrap() async => _json(
    await _client.get(
      baseUri.resolve('/api/v1/capture/bootstrap'),
      headers: await _headers(),
    ),
  );

  Future<Map<String, dynamic>> changes(String after) async => _json(
    await _client.get(
      baseUri.resolve('/api/v1/capture/changes?after=$after'),
      headers: await _headers(),
    ),
  );

  Future<Map<String, dynamic>> sync(PendingCommand command) async {
    final response = await _client.post(
      baseUri.resolve('/api/v1/capture/sync'),
      headers: await _headers(json: true),
      body: jsonEncode(command.toRequest()),
    );
    return _json(response);
  }

  Future<Map<String, dynamic>> production(PendingCommand command) async {
    final response = await _client.post(
      baseUri.resolve('/api/v1/capture/production'),
      headers: await _headers(json: true),
      body: jsonEncode(command.toRequest()),
    );
    return _json(response);
  }

  Future<Map<String, dynamic>> resolve(String code) async => _json(
    await _client.post(
      baseUri.resolve('/api/v1/capture/resolve'),
      headers: await _headers(json: true),
      body: jsonEncode({'code': code}),
    ),
  );

  Future<Map<String, String>> _headers({
    bool json = false,
    bool includeSession = true,
  }) async {
    final accessToken = await accessTokenProvider?.call();
    return {
      if (json) 'content-type': 'application/json',
      if (accessToken?.isNotEmpty == true)
        'authorization': 'Bearer $accessToken',
      if (includeSession && sessionToken?.isNotEmpty == true)
        'X-UnitAtlas-Device-Session': sessionToken!,
    };
  }

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
