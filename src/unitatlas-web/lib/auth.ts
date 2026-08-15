import { createCipheriv, createDecipheriv, createHash, randomBytes } from "node:crypto";
import { cookies } from "next/headers";

const sessionCookie = "unitatlas_session";
const flowCookie = "unitatlas_oidc_flow";

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

export async function accessToken() {
  if (process.env.AUTH_DEMO_MODE === "true") return null;
  const session = unseal<{ accessToken: string; expiresAt: number }>((await cookies()).get(sessionCookie)?.value);
  return session && session.expiresAt > Date.now() / 1000 + 30 ? session.accessToken : null;
}

export async function saveSession(token: string, expiresIn: number) {
  (await cookies()).set(sessionCookie, seal({ accessToken: token, expiresAt: Math.floor(Date.now() / 1000) + expiresIn }), {
    httpOnly: true, secure: process.env.NODE_ENV === "production", sameSite: "lax", path: "/", maxAge: expiresIn
  });
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
