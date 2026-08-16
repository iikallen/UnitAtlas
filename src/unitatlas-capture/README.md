# UnitAtlas Capture

Android factory-floor client for short offline periods. It caches bootstrap reference data in SQLite, writes every operation to `pending_commands` first and replays commands in creation order. Server acknowledgement is authoritative; HTTP 409 becomes a visible `CONFLICT` row and is never overwritten locally.

Run against local demo API:

```powershell
flutter run --dart-define=UNITATLAS_API_URL=http://10.0.2.2:8080 --dart-define=UNITATLAS_DEVICE_ID=DEMO-ANDROID
```

Production device enrollment and OIDC session acquisition are not simulated in this slice; they are gates of the device/station stage.
