import { createHash, randomBytes, randomUUID } from "node:crypto";
import { NextRequest, NextResponse } from "next/server";
import { saveFlow } from "../../../lib/auth";
import { takeLoginPermit } from "../../../lib/rate-limit";

export async function GET(request: NextRequest) {
  if (!takeLoginPermit()) return NextResponse.json({
    type: "about:blank", title: "Too Many Requests", status: 429,
    code: "RATE_LIMITED", traceId: randomUUID()
  }, { status: 429, headers: { "Retry-After": "60" } });
  if (process.env.AUTH_DEMO_MODE === "true") return NextResponse.redirect(new URL("/", request.url));
  const authority = process.env.OIDC_AUTHORITY ?? "";
  const clientId = process.env.OIDC_CLIENT_ID ?? "";
  const appBaseUrl = process.env.APP_BASE_URL ?? request.url;
  const redirectUri = process.env.OIDC_REDIRECT_URI ?? new URL("/auth/callback", appBaseUrl).toString();
  if (!authority || !clientId) return NextResponse.json({ code: "OIDC_NOT_CONFIGURED" }, { status: 503 });
  const discovery = await fetch(`${authority.replace(/\/$/, "")}/.well-known/openid-configuration`).then(response => response.json()) as { authorization_endpoint: string };
  const state = randomBytes(24).toString("base64url");
  const verifier = randomBytes(48).toString("base64url");
  const challenge = createHash("sha256").update(verifier).digest("base64url");
  await saveFlow({ state, verifier, redirectUri, returnTo: request.nextUrl.searchParams.get("returnTo") ?? "/" });
  const target = new URL(discovery.authorization_endpoint);
  target.search = new URLSearchParams({
    client_id: clientId, redirect_uri: redirectUri, response_type: "code", scope: "openid profile",
    state, code_challenge: challenge, code_challenge_method: "S256"
  }).toString();
  return NextResponse.redirect(target);
}
