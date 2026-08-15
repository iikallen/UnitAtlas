# ADR 0004: internal/public API и confidential BFF

- Статус: принят
- Дата: 2026-08-15

## Решение

- Все privileged endpoints перенесены в authenticated `/api/v1`.
- Anonymous surface ограничен `/api/public/passports/{publicId}` и возвращает allow-listed projection без actor, location, tenant и ERP-данных.
- `public_passport_configs` — минимальный lookup по случайному public ID; это единственная tenant-related таблица без RLS. Остальные данные читаются после установки tenant context и остаются под forced RLS.
- Browser вызывает same-origin `/bff/*`; Route Handler добавляет bearer token server-side.
- Next.js реализует confidential Authorization Code + PKCE. Flow state/verifier и access token шифруются AES-GCM и хранятся в `HttpOnly`, `SameSite=Lax`, production `Secure` cookies.
- Development Compose явно включает demo BFF mode без внешнего IdP.
- Dashboard разделён на overview, products, units, events, settings, internal passport и public passport routes.

## Риски

Cookie session ограничена размером browser cookie и не поддерживает refresh token rotation. Для IdP с крупными access tokens потребуется server-side session store. `public_passport_configs` нельзя использовать для произвольных joins или возвращать клиенту целиком.

## Проверка и rollback

Проверяются 404 legacy API, authenticated internal API, same-origin BFF, anonymous public API/page и отсутствие sensitive полей. TypeScript, ESLint и Next production build обязательны. Down-migration возвращает RLS на public lookup; откат frontend/API выполняется одним PR revert.
