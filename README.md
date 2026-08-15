# UnitAtlas

Минимальный вертикальный срез платформы цифрового паспорта и прослеживаемости произведённой единицы.

## Текущее состояние

- каталог продуктов, изделия и DB-enforced append-only журнал событий/audit;
- детерминированная проекция текущего состояния по `(occurred_at, sequence)`;
- tenant-aware ключи и связи в PostgreSQL;
- generic OIDC bearer validation, membership roles/permissions и PostgreSQL RLS;
- Site/Location/Lot, extensible identifiers, audit, outbox и public passport config;
- integration port + versioned webhook envelope без 1С/ИС МПТ внутри core;
- request-hash idempotency с 409 при повторном key и другом body;
- versioned internal API, allow-listed public passport и Next.js confidential OIDC/BFF;
- Problem Details, cursor pagination, rate limits, security headers и JSON logs;
- OpenTelemetry HTTP/Npgsql traces и application metrics с optional OTLP export;
- ASP.NET Core 10, Next.js, EF Core migrations и Docker Compose;
- unit- и HTTP integration-тесты в CI.

Архитектурные границы описаны в [ADR 0001](docs/adr/0001-modular-monolith.md), прогресс к пилоту — в [матрице v0.1](docs/architecture/v0.1-definition-of-done.md), эксплуатация — в [pilot runbook](docs/operations/pilot-runbook.md).

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
