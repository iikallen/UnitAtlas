import { createCipheriv, createDecipheriv, createHash, randomBytes } from "node:crypto";
import { cookies } from "next/headers";
import { refreshOidcSession, type OidcSession } from "./oidc";

export const sessionCookie = "unitatlas_session";
const flowCookie = "unitatlas_oidc_flow";
const refreshes = new Map<string, Promise<OidcSession | null>>();

function key() {
  const secret = process.env.AUTH_SESSION_SECRET;
  if ((!secret || secret.length < 32) && process.env.AUTH_DEMO_MODE !== "true")
    throw new Error("AUTH_SESSION_SECRET must contain at least 32 characters");
  return createHash("sha256").update(secret ?? "development-demo-only").digest();
}

export function seal(value: object) {
  const iv = randomBytes(12);
  const cipher = createCipheriv("aes-256-gcm", key(), iv);
  const encrypted = Buffer.concat([cipher.update(JSON.stringify(value)), cipher.final()]);
  return Buffer.concat([iv, cipher.getAuthTag(), encrypted]).toString("base64url");
}

export function unseal<T>(value?: string): T | null {
  if (!value) return null;
  try {
    const data = Buffer.from(value, "base64url");
    const decipher = createDecipheriv("aes-256-gcm", key(), data.subarray(0, 12));
    decipher.setAuthTag(data.subarray(12, 28));
    return JSON.parse(Buffer.concat([decipher.update(data.subarray(28)), decipher.final()]).toString()) as T;
  } catch {
    return null;
  }
}

export function readSession(value?: string) {
  const session = unseal<Partial<OidcSession>>(value);
  return session && typeof session.accessToken === "string" && typeof session.refreshToken === "string"
    && typeof session.accessExpiresAt === "number" && typeof session.sessionExpiresAt === "number"
    ? session as OidcSession
    : null;
}

export function needsRenewal(session: OidcSession | null, now = Math.floor(Date.now() / 1000)) {
  return Boolean(session && session.sessionExpiresAt > now && session.accessExpiresAt <= now + 30);
}

export async function accessToken(renew = false) {
  if (process.env.AUTH_DEMO_MODE === "true") return null;
  const session = readSession((await cookies()).get(sessionCookie)?.value);
  const now = Math.floor(Date.now() / 1000);
  if (session && session.accessExpiresAt > now + 30 && session.sessionExpiresAt > now) return session.accessToken;
  if (!renew || !session) return null;
  const refreshed = await refresh(session);
  if (!refreshed) {
    await clearSession();
    return null;
  }
  await saveSession(refreshed);
  return refreshed.accessToken;
}

export async function saveSession(session: OidcSession) {
  const maxAge = Math.max(1, session.sessionExpiresAt - Math.floor(Date.now() / 1000));
  (await cookies()).set(sessionCookie, seal(session), {
    httpOnly: true, secure: process.env.NODE_ENV === "production", sameSite: "lax", path: "/", maxAge
  });
}

export async function renewSession() {
  const session = readSession((await cookies()).get(sessionCookie)?.value);
  if (!session) return false;
  const refreshed = await refresh(session);
  if (!refreshed) return false;
  await saveSession(refreshed);
  return true;
}

async function refresh(session: OidcSession) {
  const id = createHash("sha256").update(session.refreshToken).digest("hex");
  const active = refreshes.get(id);
  if (active) return active;
  const operation = refreshOidcSession(session, {
    authority: process.env.OIDC_AUTHORITY ?? "",
    clientId: process.env.OIDC_CLIENT_ID ?? "",
    clientSecret: process.env.OIDC_CLIENT_SECRET ?? ""
  }).finally(() => refreshes.delete(id));
  refreshes.set(id, operation);
  return operation;
}

export async function saveFlow(flow: object) {
  (await cookies()).set(flowCookie, seal(flow), {
    httpOnly: true, secure: process.env.NODE_ENV === "production", sameSite: "lax", path: "/auth/callback", maxAge: 600
  });
}

export async function takeFlow<T>() {
  const store = await cookies();
  const flow = unseal<T>(store.get(flowCookie)?.value);
  store.delete(flowCookie);
  return flow;
}

export async function clearSession() {
  (await cookies()).delete(sessionCookie);
}
