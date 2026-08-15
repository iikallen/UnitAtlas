# UnitAtlas

Минимальный вертикальный срез платформы цифрового паспорта и прослеживаемости каждой произведённой единицы.

## Что уже работает

- каталог продуктов с GTIN;
- уникальные изделия с UnitAtlas ID, серийным номером и партией;
- неизменяемый Event Ledger;
- быстрая проекция Current State;
- защита от повторной записи события через `idempotency_key`;
- поиск/сканирование QR и Data Matrix в поддерживаемом мобильном браузере;
- публичный цифровой паспорт с timeline;
- PostgreSQL, ASP.NET Core 10, Next.js и Docker Compose.

## Запуск

Нужен только Docker Desktop:

```powershell
docker compose up --build
```

После старта:

- интерфейс: http://localhost:3000
- API: http://localhost:8080
- OpenAPI: http://localhost:8080/openapi/v1.json
- healthcheck: http://localhost:8080/health

При первом запуске API создаёт демо-компанию, продукт и три изделия.

## API

| Метод | Маршрут | Назначение |
| --- | --- | --- |
| `GET` | `/api/dashboard` | Сводка и последние изделия |
| `GET/POST` | `/api/products` | Каталог продуктов |
| `GET/POST` | `/api/units` | Список и создание изделий |
| `GET` | `/api/units/{atlasId}` | Полный цифровой паспорт |
| `POST` | `/api/units/{atlasId}/events` | Добавление события |

## Ограничения v0.1

Факт: приложение работает в demo single-tenant режиме. `tenant_id` уже есть во всех основных сущностях, но вход, пользователи и роли пока не реализованы.

Риск: публиковать эту сборку в интернет нельзя — без аутентификации tenant нельзя считать границей безопасности.

Рекомендация: перед пилотом добавить OIDC-провайдера и проверку tenant/role на каждом endpoint. Это превратит заложенную модель из логической границы в реальную защиту данных.

Отложено до подтверждённой необходимости: Flutter, offline queue, агрегация Box/Pallet, 1C/ИС МПТ, Redis, S3, Kafka, Kubernetes и ClickHouse.

## Проверка и rollback

```powershell
docker compose build
docker compose up -d
Invoke-RestMethod http://localhost:8080/health
Invoke-WebRequest http://localhost:3000
```

Остановить приложение: `docker compose down`. Удалить только локальные demo-данные: `docker compose down -v`.
