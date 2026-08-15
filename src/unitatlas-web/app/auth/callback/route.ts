import { NextRequest, NextResponse } from "next/server";
import { saveSession, takeFlow } from "../../../lib/auth";

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
  const token = await response.json() as { access_token: string; expires_in?: number };
  await saveSession(token.access_token, token.expires_in ?? 300);
  const returnTo = flow.returnTo.startsWith("/") && !flow.returnTo.startsWith("//") ? flow.returnTo : "/";
  return NextResponse.redirect(new URL(returnTo, process.env.APP_BASE_URL ?? request.url));
}
