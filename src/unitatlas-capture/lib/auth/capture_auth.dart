import 'package:flutter_appauth/flutter_appauth.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

class CaptureAuth {
  CaptureAuth({
    required this.issuer,
    required this.clientId,
    required this.redirectUri,
    required this.storage,
    required this.staticAccessToken,
    this.scopes = defaultScopes,
    FlutterAppAuth? appAuth,
  }) : _appAuth = appAuth ?? const FlutterAppAuth();

  static const _accessTokenKey = 'oidc_access_token';
  static const _refreshTokenKey = 'oidc_refresh_token';
  static const _expiryKey = 'oidc_access_token_expiry';
  static const defaultScopes = ['openid', 'profile', 'offline_access'];

  final String issuer;
  final String clientId;
  final String redirectUri;
  final FlutterSecureStorage storage;
  final String staticAccessToken;
  final List<String> scopes;
  final FlutterAppAuth _appAuth;

  String? _accessToken;
  String? _refreshToken;
  DateTime? _expiry;

  bool get configured =>
      issuer.isNotEmpty && clientId.isNotEmpty && redirectUri.isNotEmpty;
  bool get isAuthenticated =>
      staticAccessToken.isNotEmpty ||
      _refreshToken?.isNotEmpty == true ||
      _hasUsableAccessToken;
  bool get requiresSignIn => configured && !isAuthenticated;
  bool get _hasUsableAccessToken =>
      _accessToken?.isNotEmpty == true &&
      (_expiry == null ||
          _expiry!.isAfter(DateTime.now().add(const Duration(seconds: 30))));

  Future<void> restore() async {
    _accessToken = await storage.read(key: _accessTokenKey);
    _refreshToken = await storage.read(key: _refreshTokenKey);
    _expiry = DateTime.tryParse(await storage.read(key: _expiryKey) ?? '');
  }

  Future<void> signIn() async {
    if (!configured) throw StateError('OIDC configuration is incomplete');
    final result = await _appAuth.authorizeAndExchangeCode(
      AuthorizationTokenRequest(
        clientId,
        redirectUri,
        issuer: issuer,
        scopes: scopes,
      ),
    );
    if (result.accessToken == null) return;
    await _save(
      result.accessToken,
      result.refreshToken,
      result.accessTokenExpirationDateTime,
    );
  }

  Future<String?> bearerToken() async {
    if (staticAccessToken.isNotEmpty) return staticAccessToken;
    if (_hasUsableAccessToken) return _accessToken;
    if (_refreshToken == null || !configured) return null;

    final result = await _appAuth.token(
      TokenRequest(
        clientId,
        redirectUri,
        issuer: issuer,
        refreshToken: _refreshToken,
        scopes: scopes,
      ),
    );
    if (result.accessToken == null) {
      await signOut();
      return null;
    }
    await _save(
      result.accessToken,
      result.refreshToken ?? _refreshToken,
      result.accessTokenExpirationDateTime,
    );
    return _accessToken;
  }

  Future<void> signOut() async {
    _accessToken = null;
    _refreshToken = null;
    _expiry = null;
    await Future.wait([
      storage.delete(key: _accessTokenKey),
      storage.delete(key: _refreshTokenKey),
      storage.delete(key: _expiryKey),
    ]);
  }

  Future<void> _save(
    String? accessToken,
    String? refreshToken,
    DateTime? expiry,
  ) async {
    _accessToken = accessToken;
    _refreshToken = refreshToken;
    _expiry = expiry;
    await Future.wait([
      storage.write(key: _accessTokenKey, value: accessToken),
      storage.write(key: _refreshTokenKey, value: refreshToken),
      storage.write(key: _expiryKey, value: expiry?.toIso8601String()),
    ]);
  }
}
