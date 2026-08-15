import Link from "next/link";
import { redirect } from "next/navigation";
import InternalPage from "../../../components/InternalPage";
import { internalApi } from "../../../lib/api";

type Dashboard = { recentUnits: { atlasId: string; product: string; status: string; updatedAt: string }[] };

export default async function EventsPage() {
  const response = await internalApi("dashboard");
  if (response.status === 401) redirect("/auth/login?returnTo=/events");
  const data: Dashboard = response.ok ? await response.json() : { recentUnits: [] };
  return <InternalPage title="Последние события"><ol className="timeline">{data.recentUnits.map(unit => <li key={unit.atlasId}><i></i><div><strong>{unit.status}</strong><span>{unit.product}</span><small><Link href={`/u/${unit.atlasId}`}>{unit.atlasId}</Link> · {new Date(unit.updatedAt).toLocaleString("ru-RU")}</small></div></li>)}</ol></InternalPage>;
}
