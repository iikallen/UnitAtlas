# UnitAtlas Capture

Android factory-floor client for short offline periods. It caches bootstrap reference data in SQLite, writes every operation to `pending_commands` first and replays commands in creation order. Server acknowledgement is authoritative; HTTP 409 becomes a visible `CONFLICT` row and is never overwritten locally.

Run against local demo API:

```powershell
flutter run --dart-define=UNITATLAS_API_URL=http://10.0.2.2:8080 --dart-define=UNITATLAS_DEVICE_ID=DEMO-ANDROID
```

Tenant admin first creates the matching device/station and one-time enrollment code. The app exchanges that code on its first run and keeps the revocable device session in platform secure storage. The API still requires the normal user OIDC access token outside development demo mode; `UNITATLAS_ACCESS_TOKEN` is only a local integration hook and must not be baked into a production APK.

For a pilot build, register `com.unitatlas.capture:/oauthredirect` as a public native-client redirect URI in the production IdP, enable Authorization Code + PKCE and refresh tokens, and create the ignored `android/key.properties` file with `storeFile`, `storePassword`, `keyAlias` and `keyPassword` for the pilot release key. Then build with:

```powershell
flutter build apk --release `
  --dart-define=UNITATLAS_API_URL=https://unitatlas.example.kz `
  --dart-define=UNITATLAS_DEVICE_ID=FACTORY-TSD-01 `
  --dart-define=UNITATLAS_OIDC_ISSUER=https://id.example.kz/realms/unitatlas `
  --dart-define=UNITATLAS_OIDC_CLIENT_ID=unitatlas-capture `
  --dart-define='UNITATLAS_OIDC_SCOPES=openid profile offline_access' `
  --dart-define=UNITATLAS_OIDC_REDIRECT_URI=com.unitatlas.capture:/oauthredirect
```

The native client must not have a client secret. Access, refresh and device-session tokens are kept in platform secure storage. The app refreshes the user access token before authenticated API calls. A release build fails when `android/key.properties` is absent; the repository does not contain or generate the real signing key.
