import { internalApi } from "../../../lib/api";

async function proxy(request: Request, { params }: { params: Promise<{ path: string[] }> }) {
  const { path } = await params;
  const source = new URL(request.url);
  const body = request.method === "GET" || request.method === "HEAD" ? undefined : await request.arrayBuffer();
  const response = await internalApi(`${path.map(encodeURIComponent).join("/")}${source.search}`, {
    method: request.method,
    headers: request.headers.get("content-type") ? { "Content-Type": request.headers.get("content-type")! } : undefined,
    body
  }, true);
  const headers = new Headers({ "Content-Type": response.headers.get("content-type") ?? "application/json" });
  for (const name of ["X-Next-Cursor", "Retry-After"])
    if (response.headers.has(name)) headers.set(name, response.headers.get(name)!);
  return new Response(response.body, {
    status: response.status,
    headers
  });
}

export const GET = proxy;
export const POST = proxy;
