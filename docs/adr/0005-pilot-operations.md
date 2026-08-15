# ADR 0005: pilot safeguards и observability

- Статус: принят
- Дата: 2026-08-16

## Решение

- API возвращает RFC 9457 Problem Details с устойчивыми `code` и `traceId`; необработанные исключения не раскрываются клиенту.
- Встроенный ASP.NET Core fixed-window limiter защищает public passport, поиск, scan lookup и event ingestion. Next login имеет такой же однопроцессный pilot limiter.
- API и Web отправляют CSP, `nosniff`, Referrer-Policy, Permissions-Policy и HSTS; CORS допускает только настроенный Web origin.
- Console logs имеют JSON-формат и scopes `TraceId`, `RequestId`, `TenantId`, `UserSubject`, `AtlasId`; запись события добавляет `EventId`.
- OpenTelemetry собирает ASP.NET Core HTTP, HttpClient и Npgsql traces, HTTP/Npgsql/custom metrics и exception/event counters. OTLP export включается только при наличии `OTEL_EXPORTER_OTLP_ENDPOINT`.
- `/api/v1/units` использует cursor pagination по уникальному `AtlasId`; cursor возвращается в `X-Next-Cursor`.

## Ограничения

Rate limits локальны одному процессу и намеренно не требуют Redis для одной pilot-реплики. Перед горизонтальным масштабированием политики переносятся в общий gateway или distributed store. Npgsql tracing следует экспериментальной DB semantic convention OpenTelemetry.

## Проверка и rollback

CI проверяет Problem Details, security headers, cursor, login 429, clean build/tests и Docker smoke. Rollback приложения — предыдущий image/commit; schema в этом ADR не меняется.
