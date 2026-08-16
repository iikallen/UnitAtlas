import { NextRequest, NextResponse } from "next/server";
import { saveSession, takeFlow } from "../../../lib/auth";
import { newSession, type TokenResponse } from "../../../lib/oidc";

type Flow = { state: string; verifier: string; redirectUri: string; returnTo: string };

export async function GET(request: NextRequest) {
  const flow = await takeFlow<Flow>();
  const code = request.nextUrl.searchParams.get("code");
  if (!flow || !code || request.nextUrl.searchParams.get("state") !== flow.state)
    return NextResponse.json({ code: "OIDC_CALLBACK_INVALID" }, { status: 400 });

  const authority = process.env.OIDC_AUTHORITY ?? "";
  const discovery = await fetch(`${authority.replace(/\/$/, "")}/.well-known/openid-configuration`).then(response => response.json()) as { token_endpoint: string };
  const response = await fetch(discovery.token_endpoint, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "authorization_code", code, redirect_uri: flow.redirectUri, code_verifier: flow.verifier,
      client_id: process.env.OIDC_CLIENT_ID ?? "", client_secret: process.env.OIDC_CLIENT_SECRET ?? ""
    })
  });
  if (!response.ok) return NextResponse.json({ code: "OIDC_TOKEN_EXCHANGE_FAILED" }, { status: 502 });
  const token = await response.json() as TokenResponse;
  const configuredLifetime = Number(process.env.AUTH_SESSION_MAX_AGE_SECONDS ?? 28_800);
  const session = newSession(token, Math.floor(Date.now() / 1000),
    Number.isFinite(configuredLifetime) && configuredLifetime > 0 ? Math.min(configuredLifetime, 2_592_000) : 28_800);
  if (!session) return NextResponse.json({ code: "OIDC_REFRESH_TOKEN_MISSING" }, { status: 502 });
  await saveSession(session);
  const returnTo = flow.returnTo.startsWith("/") && !flow.returnTo.startsWith("//") ? flow.returnTo : "/";
  return NextResponse.redirect(new URL(returnTo, process.env.APP_BASE_URL ?? request.url));
}
