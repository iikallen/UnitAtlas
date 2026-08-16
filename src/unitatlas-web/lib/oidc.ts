export type OidcSession = {
  accessToken: string;
  refreshToken: string;
  accessExpiresAt: number;
  sessionExpiresAt: number;
};

export type TokenResponse = {
  access_token?: string;
  refresh_token?: string;
  expires_in?: number;
  refresh_expires_in?: number;
};

type Fetcher = (input: string, init?: RequestInit) => Promise<Response>;
type RefreshConfiguration = { authority: string; clientId: string; clientSecret: string };

export function newSession(token: TokenResponse, now: number, fallbackSessionSeconds: number): OidcSession | null {
  if (!token.access_token || !token.refresh_token) return null;
  return {
    accessToken: token.access_token,
    refreshToken: token.refresh_token,
    accessExpiresAt: now + seconds(token.expires_in, 300, 86_400),
    sessionExpiresAt: now + seconds(token.refresh_expires_in, fallbackSessionSeconds, 2_592_000)
  };
}

export async function refreshOidcSession(
  session: OidcSession,
  configuration: RefreshConfiguration,
  fetcher: Fetcher = fetch,
  now = Math.floor(Date.now() / 1000)
): Promise<OidcSession | null> {
  if (session.sessionExpiresAt <= now || !configuration.authority || !configuration.clientId || !configuration.clientSecret)
    return null;
  try {
    const discoveryResponse = await fetcher(`${configuration.authority.replace(/\/$/, "")}/.well-known/openid-configuration`, {
      cache: "no-store", signal: AbortSignal.timeout(10_000)
    });
    if (!discoveryResponse.ok) return null;
    const discovery = await discoveryResponse.json() as { token_endpoint?: string };
    if (!discovery.token_endpoint) return null;
    const response = await fetcher(discovery.token_endpoint, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        grant_type: "refresh_token",
        refresh_token: session.refreshToken,
        client_id: configuration.clientId,
        client_secret: configuration.clientSecret
      }),
      cache: "no-store", signal: AbortSignal.timeout(10_000)
    });
    if (!response.ok) return null;
    const token = await response.json() as TokenResponse;
    if (!token.access_token) return null;
    return {
      accessToken: token.access_token,
      refreshToken: token.refresh_token || session.refreshToken,
      accessExpiresAt: now + seconds(token.expires_in, 300, 86_400),
      sessionExpiresAt: token.refresh_expires_in
        ? now + seconds(token.refresh_expires_in, 0, 2_592_000)
        : session.sessionExpiresAt
    };
  } catch {
    return null;
  }
}

function seconds(value: number | undefined, fallback: number, maximum: number) {
  return Number.isFinite(value) && value! > 0 ? Math.min(Math.floor(value!), maximum) : fallback;
}
