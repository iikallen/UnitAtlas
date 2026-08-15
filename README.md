# UnitAtlas

Минимальный вертикальный срез платформы цифрового паспорта и прослеживаемости произведённой единицы.

## Текущее состояние

- каталог продуктов, изделия и append-only журнал событий;
- детерминированная проекция текущего состояния по `(occurred_at, sequence)`;
- tenant-aware ключи и связи в PostgreSQL;
- generic OIDC bearer validation, membership roles/permissions и PostgreSQL RLS;
- ASP.NET Core 10, Next.js, EF Core migrations и Docker Compose;
- unit- и HTTP integration-тесты в CI.

Архитектурные границы описаны в [ADR 0001](docs/adr/0001-modular-monolith.md), а прогресс к пилоту — в [матрице v0.1](docs/architecture/v0.1-definition-of-done.md).

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

Compose применяет миграции и добавляет demo-данные. В остальных окружениях обе операции выключены по умолчанию.

Compose также включает development-only authentication: запрос без заголовков выполняется как demo Owner. Для negative tests доступны `X-Demo-Subject` и `X-Demo-Tenant`. Эта схема регистрируется только при одновременных `ASPNETCORE_ENVIRONMENT=Development` и `Authentication__DemoMode=true`.

В production API требует настройки `Authentication__Authority` и `Authentication__Audience`, валидирует OIDC access token и ожидает claims `sub` и `tenant_id`. Пользователь должен иметь соответствующую запись `TenantMembership`.

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

Риск: внешний Identity Provider и production memberships не создаются репозиторием автоматически, а Next.js ещё не реализует confidential OIDC/BFF session. Development demo auth нельзя включать в публичном окружении.

Минимальный следующий шаг: перевести Next.js на confidential OIDC/BFF и отделить anonymous public passport от internal API. Это исключит privileged browser→API доступ.

## Rollback

`docker compose down` останавливает сервисы и сохраняет данные. `docker compose down -v` удаляет только volume текущего Compose-проекта и предназначен для одноразового demo/CI окружения.
