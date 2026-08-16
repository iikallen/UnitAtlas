import { NextRequest, NextResponse } from "next/server";
import { clearSession, renewSession } from "../../../lib/auth";

export async function GET(request: NextRequest) {
  const requested = request.nextUrl.searchParams.get("returnTo") ?? "/";
  const returnTo = requested.startsWith("/") && !requested.startsWith("//") ? requested : "/";
  if (await renewSession()) return NextResponse.redirect(new URL(returnTo, process.env.APP_BASE_URL ?? request.url));
  await clearSession();
  const login = new URL("/auth/login", process.env.APP_BASE_URL ?? request.url);
  login.searchParams.set("returnTo", returnTo);
  return NextResponse.redirect(login);
}
