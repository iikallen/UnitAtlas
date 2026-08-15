# ADR 0002: OIDC, membership и tenant isolation

- Статус: принят
- Дата: 2026-08-15

## Контекст

Наличие `tenant_id` само по себе не создаёт security boundary. В demo API выбирал единственный tenant, а PostgreSQL runtime-пользователь был superuser и мог обходить любую RLS-политику.

## Решение

- Production API валидирует bearer access token через generic OIDC Authority/Audience и читает claims `sub` и `tenant_id`.
- `TenantMembership` связывает subject, tenant и одну роль; роль преобразуется в permission claims и проверяется ASP.NET Core policies.
- Scoped `TenantContext` обязателен для internal `/api` endpoints.
- EF query filters и composite FK предотвращают обычные cross-tenant ошибки приложения.
- PostgreSQL `ENABLE/FORCE ROW LEVEL SECURITY` использует session setting `app.current_tenant` как второй барьер.
- API подключается non-superuser ролью. Admin role используется только при первом создании dev-БД.
- Development demo handler существует только при двух явных development-настройках; production без Authority/Audience не стартует.

## Ограничения и риски

RLS защищает от пропущенного tenant predicate, но не от компрометации DB credentials или SQL injection, способной изменить session setting. OIDC Provider, client registration и memberships должны управляться инфраструктурой конкретного pilot-окружения. Next.js BFF/session остаются отдельным этапом.

## Проверка

- runtime DB-role: `usesuper=false`;
- без `app.current_tenant` tenant-таблицы возвращают 0 строк;
- mismatched subject/tenant получает 403;
- Viewer читает свой tenant, но не создаёт Product;
- clean forward и rollback второй миграции.

## Rollback

Down-migration удаляет `TenantMembership`, политики и отключает RLS, возвращая схему к `InitialArchitecture`. После rollback нельзя публиковать API: tenant снова перестаёт быть enforced security boundary.
