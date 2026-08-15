import { redirect } from "next/navigation";
import InternalPage from "../../../components/InternalPage";
import { internalApi } from "../../../lib/api";

type Me = { userSubject: string; tenantId: string; role: string; permissions: string[] };

export default async function SettingsPage() {
  const response = await internalApi("me");
  if (response.status === 401) redirect("/auth/login?returnTo=/settings");
  const me: Me | null = response.ok ? await response.json() : null;
  return <InternalPage title="Настройки доступа">{me && <dl className="passport-data"><div><dt>Subject</dt><dd>{me.userSubject}</dd></div><div><dt>Tenant</dt><dd>{me.tenantId}</dd></div><div><dt>Role</dt><dd>{me.role}</dd></div><div><dt>Permissions</dt><dd>{me.permissions.join(", ")}</dd></div></dl>}<a href="/auth/logout">Выйти</a></InternalPage>;
}
