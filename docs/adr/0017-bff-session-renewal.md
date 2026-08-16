# ADR 0017: BFF session renewal

## Context

The confidential Next.js BFF previously stored only an access token. A factory shift was forced to authenticate again after access-token expiry. Tokens must remain unavailable to browser JavaScript, and the single-replica pilot does not justify a new distributed session store.

## Decision

- Request `offline_access` through Authorization Code + PKCE and require the token response to include a refresh token.
- Store access and refresh tokens only in the existing AES-256-GCM sealed `HttpOnly`, `Secure`, `SameSite=Lax` cookie.
- Renew within Route Handlers, authenticate the confidential client at the token endpoint and persist a rotated refresh token when returned.
- Redirect an expiring server-rendered page through `/auth/refresh`; BFF requests renew directly. Return targets must be local absolute paths.
- Bound the fallback session to eight hours, configurable with `AUTH_SESSION_MAX_AGE_SECONDS`, with a hard 30-day maximum.
- Coalesce concurrent refreshes in memory. This is valid only while the first pilot remains single-replica.

## Consequences

An access-token expiry no longer interrupts an active shift. No token is returned to client-side code. The production IdP client must permit the `offline_access` consent and `client_secret_post` refresh grant. If sealed tokens exceed browser cookie limits or the Web tier becomes multi-replica, move sessions to a shared server-side store.

## Rollback

Roll back the Web image. Existing sealed cookies from this version will be ignored by the older session shape and users will authenticate again; API and database state are unchanged.
