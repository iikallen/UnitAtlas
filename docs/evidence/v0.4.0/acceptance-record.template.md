# UnitAtlas v0.4.0 Factory Pilot acceptance record

Template status: `UNEXECUTED`

Pilot/customer reference (sanitized): `NOT RECORDED`

Acceptance owner: `NOT RECORDED`

Planned test window (UTC): `NOT RECORDED`

## Frozen software baseline

| Field | Value |
| --- | --- |
| RC tag | `v0.4.0-rc.1` |
| Git commit | `NOT RECORDED` |
| API image digest | `NOT RECORDED` |
| Web image digest | `NOT RECORDED` |
| CI run URL | `NOT RECORDED` |
| Known limitations | `NOT RECORDED` |

Feature freeze permits only reviewed P0/P1 pilot fixes. Any code change creates a new RC and invalidates results tied to the previous commit.

## Threshold contract — approve before testing

| Metric | Pass threshold | Approved by / UTC |
| --- | --- | --- |
| 100-label print and scan-back | `100 printed; 100 resolved to the intended Unit` | `NOT APPROVED` |
| Lost accepted commands | `0` | `NOT APPROVED` |
| Duplicate canonical ledger events | `0` | `NOT APPROVED` |
| Recognition errors in 1,000 physical attempts | `NOT APPROVED` | `NOT APPROVED` |
| Duplicate API responses in 1,000 attempts | `NOT APPROVED` | `NOT APPROVED` |
| Sync retries in 1,000 attempts | `NOT APPROVED` | `NOT APPROVED` |
| Acknowledgment p50 | `NOT APPROVED` | `NOT APPROVED` |
| Acknowledgment p95 | `NOT APPROVED` | `NOT APPROVED` |
| Offline queue drain after reconnect | `NOT APPROVED` | `NOT APPROVED` |

## Pilot hardware manifest

### Printer and label

| Field | Value |
| --- | --- |
| Manufacturer / model / sanitized asset ID | `NOT RECORDED` |
| Firmware / DPI | `NOT RECORDED` |
| Printer language (`ZPL`, `TSPL`, `EPL`, other) | `NOT RECORDED` |
| Interface (`Ethernet`, `USB`, `Bluetooth`, other) | `NOT RECORDED` |
| Label width × height / material / ribbon | `NOT RECORDED` |
| Calibrated speed / darkness | `NOT RECORDED` |
| Unit symbology / logistics symbology | `NOT RECORDED` |

### Capture device

| Field | Value |
| --- | --- |
| Manufacturer / model / sanitized asset ID | `NOT RECORDED` |
| Android / security patch / firmware | `NOT RECORDED` |
| Scan source (`camera`, `keyboard wedge`, `intent`) | `NOT RECORDED` |
| Scanner configuration/profile revision | `NOT RECORDED` |
| Network mode used in each run | `NOT RECORDED` |

Do not record SSIDs, passwords, private addresses or device serial numbers that the customer treats as confidential.

## Production identity and APK

| Field | Value |
| --- | --- |
| Package / versionName / versionCode | `NOT RECORDED` |
| APK SHA-256 | `NOT RECORDED` |
| Signing certificate SHA-256 fingerprint | `NOT RECORDED` |
| Build commit | `NOT RECORDED` |
| OIDC issuer / public client ID (sanitized if required) | `NOT RECORDED` |
| Redirect URI | `com.unitatlas.capture:/oauthredirect` |
| Authorization Code + PKCE / refresh token | `NOT VERIFIED` |
| Embedded client secret absent | `NOT VERIFIED` |
| Real-device login / refresh / sign-out / session revocation | `NOT TESTED` |

Do not commit the APK, signing key, access/refresh tokens or OIDC client secret.

## Identifier mode

| Field | Value |
| --- | --- |
| Mode | `NOT RECORDED` (`INTERNAL` or `GS1`) |
| Licensed GS1 Company Prefix evidence reference | `NOT APPLICABLE / NOT RECORDED` |
| GTIN/SSCC ownership and check-digit validation | `NOT TESTED` |

If licensed GCP evidence is unavailable, select `INTERNAL`; never describe internal codes as GS1 identifiers.

## 1C compatibility

| Field | Value |
| --- | --- |
| Contract | `ONEC_UPP_KZ_1_3_HTTP_JSON_V1` |
| Customer configuration / release | `NOT RECORDED` |
| 1C platform version / relevant extensions | `NOT RECORDED` |
| UnitAtlas extension version / SHA-256 | `NOT RECORDED` |
| Sanitized test database reference | `NOT RECORDED` |
| Product/order import | `NOT TESTED` |
| Shipment acknowledgment | `NOT TESTED` |
| Result / approver / UTC | `NOT TESTED` |

## Physical acceptance results

| Gate | Result | Evidence reference / notes |
| --- | --- | --- |
| Production login and token refresh on real TSD | `NOT TESTED` | `NOT RECORDED` |
| Test print and scan-back | `NOT TESTED` | `NOT RECORDED` |
| 100 Unit labels printed and scanned | `NOT TESTED` | `NOT RECORDED` |
| Unit → Box → Pallet | `NOT TESTED` | `NOT RECORDED` |
| Continue work with Wi-Fi unavailable | `NOT TESTED` | `NOT RECORDED` |
| Reconnect and exactly-once replay | `NOT TESTED` | `NOT RECORDED` |
| Intentional conflict visible; no silent overwrite | `NOT TESTED` | `NOT RECORDED` |
| Shipment → Outbox → real 1C acknowledgment | `NOT TESTED` | `NOT RECORDED` |
| EPCIS export and public/internal passports | `NOT TESTED` | `NOT RECORDED` |
| FORCE RLS / audit / append-only invariants | `NOT TESTED` | `NOT RECORDED` |

### 1,000-scan report

Paste the unedited sanitized report copied from Capture, then record the independent observed attempt count.

```text
NOT RECORDED
```

Independent physical attempt count: `NOT RECORDED`

Threshold comparison: `NOT EVALUATED`

Result / approver / UTC: `NOT TESTED`

## Backup/restore rehearsal

| Field | Value |
| --- | --- |
| Source release commit/image | `NOT RECORDED` |
| Backup SHA-256 / access-controlled evidence reference | `NOT RECORDED` |
| Source and restored row-count comparison | `NOT TESTED` |
| Migration history / FORCE RLS / tenant isolation | `NOT TESTED` |
| Append-only triggers | `NOT TESTED` |
| Result / approver / UTC | `NOT TESTED` |

## Release decision

| Field | Value |
| --- | --- |
| All gates complete | `NO` |
| Open P0/P1 defects | `NOT RECORDED` |
| Accepted known limitations | `NOT RECORDED` |
| Decision | `DO NOT PROMOTE` |
| Release manager / customer approver / UTC | `NOT APPROVED` |

Until this record is complete and approved, `v0.4.0-rc.1` remains a release candidate and `v0.4.0` must not be tagged.
