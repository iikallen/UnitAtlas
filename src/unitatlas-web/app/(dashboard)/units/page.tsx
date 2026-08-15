import Link from "next/link";
import { redirect } from "next/navigation";
import InternalPage from "../../../components/InternalPage";
import { internalApi } from "../../../lib/api";

type Unit = { atlasId: string; serial: string; product: string; status: string; location: string };

export default async function UnitsPage() {
  const response = await internalApi("units");
  if (response.status === 401) redirect("/auth/login?returnTo=/units");
  const units: Unit[] = response.ok ? await response.json() : [];
  return <InternalPage title="Изделия"><div className="table-wrap"><table><thead><tr><th>UnitAtlas ID</th><th>Продукт</th><th>Статус</th><th>Местоположение</th></tr></thead><tbody>{units.map(unit => <tr key={unit.atlasId}><td><Link href={`/u/${unit.atlasId}`}>{unit.atlasId}</Link><small>{unit.serial}</small></td><td>{unit.product}</td><td>{unit.status}</td><td>{unit.location}</td></tr>)}</tbody></table></div></InternalPage>;
}
