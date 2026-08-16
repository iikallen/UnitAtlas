import assert from "node:assert/strict";
import test from "node:test";
import { newSession, refreshOidcSession } from "../lib/oidc.ts";

test("refresh grant rotates the refresh token and keeps it server-side", async () => {
  const initial = newSession({
    access_token: "access-1", refresh_token: "refresh-1", expires_in: 60, refresh_expires_in: 3600
  }, 1000, 1800)!;
  let form: URLSearchParams | undefined;
  const fetcher = async (input: string, init?: RequestInit) => {
    if (input.endsWith("/.well-known/openid-configuration"))
      return Response.json({ token_endpoint: "https://issuer.example/token" });
    form = new URLSearchParams(init?.body?.toString());
    return Response.json({ access_token: "access-2", refresh_token: "refresh-2", expires_in: 120 });
  };

  const refreshed = await refreshOidcSession(initial, {
    authority: "https://issuer.example", clientId: "unitatlas", clientSecret: "secret"
  }, fetcher, 1100);

  assert.equal(refreshed?.accessToken, "access-2");
  assert.equal(refreshed?.refreshToken, "refresh-2");
  assert.equal(refreshed?.accessExpiresAt, 1220);
  assert.equal(refreshed?.sessionExpiresAt, 4600);
  assert.equal(form?.get("grant_type"), "refresh_token");
  assert.equal(form?.get("refresh_token"), "refresh-1");
  assert.equal(form?.get("client_id"), "unitatlas");
  assert.equal(form?.get("client_secret"), "secret");
});

test("failed refresh does not create a session", async () => {
  const session = newSession({ access_token: "access", refresh_token: "refresh" }, 1000, 1800)!;
  const refreshed = await refreshOidcSession(session, {
    authority: "https://issuer.example", clientId: "unitatlas", clientSecret: "secret"
  }, async () => new Response(null, { status: 503 }), 1100);
  assert.equal(refreshed, null);
  assert.equal(newSession({ access_token: "access-only" }, 1000, 1800), null);
});
