import { NextRequest, NextResponse } from "next/server";
import { needsRenewal, readSession, sessionCookie } from "./lib/auth";

export function proxy(request: NextRequest) {
  if (process.env.AUTH_DEMO_MODE === "true" || request.nextUrl.pathname.startsWith("/bff/"))
    return NextResponse.next();
  const session = readSession(request.cookies.get(sessionCookie)?.value);
  if (!needsRenewal(session)) return NextResponse.next();
  const refresh = new URL("/auth/refresh", request.url);
  refresh.searchParams.set("returnTo", `${request.nextUrl.pathname}${request.nextUrl.search}`);
  return NextResponse.redirect(refresh);
}

export const config = {
  matcher: ["/((?!auth/|p/|_next/|mark.svg|favicon.ico).*)"]
};
