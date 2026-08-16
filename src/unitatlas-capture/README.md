# UnitAtlas Capture

Android factory-floor client for short offline periods. It caches bootstrap reference data in SQLite, writes every operation to `pending_commands` first and replays commands in creation order. Server acknowledgement is authoritative; HTTP 409 becomes a visible `CONFLICT` row and is never overwritten locally.

Run against local demo API:

```powershell
flutter run --dart-define=UNITATLAS_API_URL=http://10.0.2.2:8080 --dart-define=UNITATLAS_DEVICE_ID=DEMO-ANDROID
```

Tenant admin first creates the matching device/station and one-time enrollment code. The app exchanges that code on its first run and keeps the revocable device session in platform secure storage. The API still requires the normal user OIDC access token outside development demo mode; `UNITATLAS_ACCESS_TOKEN` is only a local integration hook and must not be baked into a production APK.
