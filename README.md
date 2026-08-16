# UnitAtlas

Минимальный вертикальный срез платформы цифрового паспорта и прослеживаемости произведённой единицы.

## Текущее состояние

- каталог продуктов, изделия и DB-enforced append-only журнал событий/audit;
- детерминированная проекция текущего состояния по `(occurred_at, sequence)`;
- tenant-aware ключи и связи в PostgreSQL;
- generic OIDC bearer validation, membership roles/permissions и PostgreSQL RLS;
- Site/Location/Lot, extensible identifiers, audit, outbox и public passport config;
- integration port + versioned webhook envelope без 1С/ИС МПТ внутри core;
- EPCIS 2.0.1 JSON/JSON-LD capture/export для ObjectEvent и AggregationEvent;
- reference 1C adapter поверх durable Inbox/Outbox без зависимости core от редакции 1С;
- integration operations workspace `/integrations`, delivery metrics, dead-letter retry и single regulatory gateway mode;
- idempotent label/print jobs для Unit и logistics labels с явными `INTERNAL | GS1` профилями и append-only попытками;
- Android Flutter Capture с SQLite command queue, ordered replay и явными sync conflicts без silent overwrite;
- request-hash idempotency с 409 при повторном key и другом body;
- versioned internal API, allow-listed public passport и Next.js confidential OIDC/BFF;
- Problem Details, cursor pagination, rate limits, security headers и JSON logs;
- OpenTelemetry HTTP/Npgsql traces и application metrics с optional OTLP export;
- ASP.NET Core 10, Next.js, EF Core migrations и Docker Compose;
- unit- и HTTP integration-тесты в CI.

Архитектурные границы описаны в [ADR 0001](docs/adr/0001-modular-monolith.md), проверяемая цель factory pilot — в [матрице v0.4](docs/architecture/v0.4-definition-of-done.md), эксплуатация — в [factory-pilot runbook](docs/operations/factory-pilot-runbook.md).

## Запуск

Нужен Docker Desktop:

```powershell
docker compose up --build --wait
```

После старта:

- интерфейс: http://localhost:3000
- API: http://localhost:8080
- OpenAPI: http://localhost:8080/openapi/v1.json
- liveness: http://localhost:8080/health/live
- readiness: http://localhost:8080/health/ready
- internal passport: http://localhost:3000/u/UA-KZ-2026-0000058219
- public passport: http://localhost:3000/p/demo-x200-58219

Compose применяет миграции и добавляет demo-данные. В остальных окружениях обе операции выключены по умолчанию.

Compose также включает development-only authentication: запрос без заголовков выполняется как demo Owner. Для negative tests доступны `X-Demo-Subject` и `X-Demo-Tenant`. Эта схема регистрируется только при одновременных `ASPNETCORE_ENVIRONMENT=Development` и `Authentication__DemoMode=true`.

В production API требует настройки `Authentication__Authority` и `Authentication__Audience`, валидирует OIDC access token и ожидает claims `sub` и `tenant_id`. Пользователь должен иметь соответствующую запись `TenantMembership`.

Next.js BFF в production требует `APP_BASE_URL`, `OIDC_AUTHORITY`, `OIDC_CLIENT_ID`, `OIDC_CLIENT_SECRET`, `OIDC_REDIRECT_URI` и случайный `AUTH_SESSION_SECRET`. Authorization Code + PKCE выполняется только server-side; access token хранится в зашифрованной `HttpOnly` cookie. Browser обращается к `/bff/*`, а не к API origin.

Internal API: `/api/v1/*`. Anonymous API: только `/api/public/passports/{publicId}`. Public response не содержит actor, внутренних location, tenant, lot, SKU или ERP identifiers.

Read contracts: `/api/v1/sites`, `/api/v1/locations`, `/api/v1/units/{atlasId}/events` и `/api/v1/passports/{atlasId}`. Новые TraceEvent получают UUIDv7; correction выполняется новым событием, UPDATE/DELETE/TRUNCATE ledger и audit запрещены DB triggers.

EPCIS supported subset: `GET/POST /api/v1/epcis/documents`. Export возвращает tenant ledger как EPCISDocument; capture принимает один ObjectEvent или AggregationEvent. Это не полный EPCIS Repository: Query/Subscriptions и XML/WSDL отложены.

1C import: `POST /api/v1/integration-inbox/{system}/1c` с `X-External-Message-Id`. Reference-контракт поддерживает `product.upsert`, `production.completed`, `shipment.recorded` и `receipt.recorded`. Concrete pilot profile `ONEC_UPP_KZ_1_3_HTTP_JSON_V1` добавляет `production_order.completed`: один idempotent запрос связывает внешний заказ с Lot/batch, создаёт 1–1000 Units и один GS1 Data Matrix print job. Контракт расширения описан в [pilot profile](docs/integrations/onec-upp-kz-1.3-pilot-profile.md); это UnitAtlas extension для 1С:УПП Казахстан 1.3, а не заявление об официальной сертификации 1С.

Integration operations: `/integrations` показывает enabled state, last success, backlog, retry/dead-letter counts и позволяет безопасно повторить dead letter. Tenant mode `NONE | ONE_C | DIRECT_IS_MPT` исключает одновременный regulatory route; direct IS MPT adapter не входит в v0.3.0.

Printing: `GET /api/v1/print-setup`, `POST/GET /api/v1/print-jobs` и `POST /api/v1/print-jobs/{id}/attempts`. `INTERNAL` payload не выдаётся за GS1; профиль `GS1` требует licensed Company Prefix, а GTIN/SSCC должны совпадать с ним и проходить check digit.

Capture baseline: `GET /api/v1/capture/bootstrap`, `POST /resolve` и `POST /sync`. Клиент в `src/unitatlas-capture` сначала сохраняет UUIDv7-команду в SQLite и только затем отправляет её; повтор использует тот же server idempotency key, а 409 остаётся видимым конфликтом.

Scan workflows: Capture принимает camera, keyboard-wedge и Android intent scans, разбирает UnitAtlas/GS1 DataMatrix/EAN/GS1-128/SSCC и даёт task-oriented экраны ОТК, упаковки, паллетизации, перемещения, отгрузки, приёмки и поиска. `POST /api/v1/capture/quality` и `/move` используют тот же immutable trace ledger.

Device/station boundary: tenant admin создаёт `Device`, `Station` и одноразовый `DeviceEnrollment`; `POST /api/v1/capture/enroll` выдаёт revocable session token, который хранится клиентом в platform secure storage. Все Capture-команды требуют `X-UnitAtlas-Device-Session`, а server автоматически записывает device/station/readPoint/businessLocation. `GET /api/v1/capture/changes?after=<syncToken>` читает только новые outbox projections по монотонному token и не выгружает Event Ledger.

## Проверка

```powershell
docker compose up -d --build --wait
docker run --rm --add-host=host.docker.internal:host-gateway `
  -e UNITATLAS_TEST_URL=http://host.docker.internal:8080 `
  -v "${PWD}:/work" -w /work mcr.microsoft.com/dotnet/sdk:10.0 `
  dotnet test UnitAtlas.slnx --configuration Release
```

## Миграции

Создание миграции:

```powershell
docker run --rm -v "${PWD}:/work" -w /work mcr.microsoft.com/dotnet/sdk:10.0 `
  sh -lc "dotnet tool restore && dotnet tool run dotnet-ef migrations add NAME --project src/UnitAtlas.Infrastructure --startup-project src/UnitAtlas.Infrastructure --output-dir Persistence/Migrations"
```

В production сначала генерируется и проверяется SQL; API не должен самостоятельно менять схему:

```powershell
dotnet tool run dotnet-ef migrations script --idempotent --project src/UnitAtlas.Infrastructure --startup-project src/UnitAtlas.Infrastructure
dotnet tool run dotnet-ef migrations script InitialArchitecture 0 --project src/UnitAtlas.Infrastructure --startup-project src/UnitAtlas.Infrastructure
```

## Ограничения

Факт: API имеет generic OIDC bearer validation, permission policies, tenant context, EF query filters, composite FK и forced PostgreSQL RLS. Runtime DB-role не является superuser.

Риск: внешний Identity Provider, client registration и production memberships не создаются репозиторием автоматически. BFF пока не обновляет истёкший access token через refresh token; после expiry пользователь входит повторно. Development demo auth нельзя включать в публичном окружении.

Rate limits локальны одной pilot-реплике. Перед горизонтальным масштабированием их следует перенести в gateway/shared store. OTLP export включается через `OTEL_EXPORTER_OTLP_ENDPOINT`.

## Rollback

`docker compose down` останавливает сервисы и сохраняет данные. `docker compose down -v` удаляет только volume текущего Compose-проекта и предназначен для одноразового demo/CI окружения.
