import { accessToken } from "./auth";

function apiBase() {
  return process.env.API_INTERNAL_URL ?? "http://localhost:8080";
}

export async function internalApi(path: string, init?: RequestInit, renewSession = false) {
  const token = await accessToken(renewSession);
  if (process.env.AUTH_DEMO_MODE !== "true" && !token) return new Response(null, { status: 401 });
  const headers = new Headers(init?.headers);
  if (token) headers.set("Authorization", `Bearer ${token}`);
  return fetch(`${apiBase()}/api/v1/${path}`, { ...init, headers, cache: "no-store" });
}

export function publicApi(path: string) {
  return fetch(`${apiBase()}/api/public/${path}`, { cache: "no-store" });
}
