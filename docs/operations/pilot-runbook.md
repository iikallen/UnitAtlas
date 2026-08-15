# UnitAtlas v0.1 pilot runbook

## Production prerequisites

- HTTPS ingress; development demo auth выключен.
- API: `ConnectionStrings__Default`, `Authentication__Authority`, `Authentication__Audience`, `Cors__Origin`.
- Web: `API_INTERNAL_URL`, `APP_BASE_URL`, `OIDC_AUTHORITY`, `OIDC_CLIENT_ID`, `OIDC_CLIENT_SECRET`, `OIDC_REDIRECT_URI`, случайный `AUTH_SESSION_SECRET` длиной не менее 32 символов.
- Runtime PostgreSQL role не superuser. Миграции применяются отдельным release job до переключения traffic.
- Для telemetry задаётся `OTEL_EXPORTER_OTLP_ENDPOINT`; стандартные `OTEL_EXPORTER_OTLP_*` параметры управляют protocol/headers.

Секреты не сохраняются в Git, image, Compose override или логах.

## Deploy and verify

1. Сделать backup и проверить, что он читается `pg_restore --list`.
2. Сгенерировать idempotent migration SQL, проверить destructive statements и применить release role.
3. Запустить API/Web image с неизменяемым tag/sha.
4. Проверить `/health/live`, затем `/health/ready`, internal login/BFF и один опубликованный passport.
5. Проверить JSON logs по `TraceId`, OTLP delivery и отсутствие роста 5xx/429.

Pilot policies: public passport 60/min, unit search 60/min, unit lookup 120/min, event ingestion 60/min, login 20/min. Это process-wide лимиты одной реплики; при двух и более репликах их нужно перенести в gateway/shared limiter.

## Backup and restore

Production backup хранится за пределами DB host, шифруется и имеет retention. Восстановление всегда сначала выполняется в новую БД, а не поверх рабочей.

Пример rehearsal для Compose:

```powershell
docker compose exec -T db pg_dump -U unitatlas_admin -d unitatlas `
  --format=custom --no-owner --no-acl --file=/tmp/unitatlas.dump
docker compose exec -T db createdb -U unitatlas_admin -O unitatlas unitatlas_restore_rehearsal
docker compose exec -T db pg_restore -U unitatlas_admin --role=unitatlas `
  --no-owner --no-acl -d unitatlas_restore_rehearsal /tmp/unitatlas.dump
```

Сравнить counts ключевых таблиц и `__EFMigrationsHistory`, forced RLS для 14 tenant-таблиц, четыре SELECT/INSERT policies и два append-only triggers, а также нулевую видимость runtime role без tenant context. Только после успешной проверки удалить временную БД и dump.

Rehearsal 2026-08-16 на чистом seed: source и restore совпали `[units=4, trace_events=7, audit_entries=0, outbox_messages=0, migrations=5]`; 14/14 tenant-таблиц сохранили forced RLS, четыре ledger/audit policies и два mutation-blocking triggers восстановлены; runtime role без tenant context увидела `0` units. Временная БД и dump удалены, рабочий volume не изменялся.

## Incident and rollback

- `live` failed: перезапустить instance; traffic не направлять.
- `ready` failed: проверить PostgreSQL connectivity, migration version и runtime role; приложение не считать готовым.
- 401/403 spike: проверить IdP discovery/audience, clock и membership, не отключать authorization.
- 429 spike: определить endpoint/нагрузку; повышать лимит только после проверки abuse и capacity.
- 5xx: найти `traceId` в JSON log и trace backend; клиенту exception detail не раскрывать.

Rollback приложения — вернуть предыдущий image sha. Rollback schema допускается только проверенным down SQL после backup; при риске потери данных восстановить backup в новую БД и переключить connection string. `docker compose down` сохраняет volume; `down -v` запрещён для pilot/production.

## Release checklist

- CI build/test/typecheck/lint/Next build/Docker smoke зелёные.
- Clean database migration и migration rollback rehearsal зелёные.
- Tenant/RLS negative tests, public-data allow-list и production OIDC flow зелёные.
- Backup/restore rehearsal зелёная.
- PR chain review/merge выполнены; только затем annotated tag `v0.1.0` ставится на `main` и публикуется release evidence.
