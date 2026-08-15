# ADR 0003: идемпотентный ledger и production data primitives

- Статус: принят
- Дата: 2026-08-15

## Решение

- `IdempotencyRecord` хранит tenant/key, operation, request hash, resource ID, HTTP status и срок жизни.
- Повтор идентичного запроса возвращает ID и status первого результата; другой body с тем же key получает `409 IDEMPOTENCY_KEY_REUSED`.
- Event ingestion блокирует строку Unit `FOR UPDATE`, поэтому параллельные разные keys получают последовательные sequence без гонки.
- TraceEvent и idempotency, audit и outbox записываются одной транзакцией.
- Добавлены Site, Location, Lot, ProductIdentifier, UnitIdentifier, AuditEntry, PublicPassportConfig, OutboxMessage и ExternalReference.
- Legacy GTIN/SKU/AtlasId/Serial/Lot переносятся миграцией; строковые поля пока сохраняются для обратной совместимости API.
- Все новые tenant-таблицы защищены composite FK/query filters/forced RLS.
- `IIntegrationAdapter` остаётся портом; 1С/ИС МПТ adapters не входят в v0.1.
- Outbound integration port принимает versioned `WebhookEnvelope`; конкретная доставка остаётся задачей adapter/worker.
- TraceEvent получает UUIDv7; RLS разрешает ledger/audit только `SELECT` и `INSERT`, а statement triggers запрещают UPDATE/DELETE/TRUNCATE даже привилегированному SQL-клиенту.

## Риски

Очистка истёкших idempotency records и доставка outbox ещё требуют фонового worker. Строковые legacy identifiers нельзя удалить до миграции API/frontend. Metadata JSONB не заменяет typed core columns.

## Проверка и rollback

Проверяются identical replay, key/body conflict, конкурентные одинаковые и разные keys, upgrade/backfill существующей БД и clean install. Down-migration удаляет новые projections/tables и возвращает legacy строки; созданные после upgrade данные в новых таблицах при rollback будут потеряны, поэтому production down требует backup.
