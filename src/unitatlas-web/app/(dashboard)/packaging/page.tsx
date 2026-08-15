import Link from "next/link";
import { redirect } from "next/navigation";
import InternalPage from "../../../components/InternalPage";
import { internalApi } from "../../../lib/api";
import PackagingActions from "./PackagingActions";

type LogisticUnit = {
  code: string;
  type: string;
  sscc?: string | null;
  children: { kind: string; code: string; product?: string | null; serial?: string | null }[];
  events: { id: string; action: string; occurredAt: string; actorSubject: string; sourceSystem: string }[];
};

export default async function PackagingPage({ searchParams }: { searchParams: Promise<{ code?: string }> }) {
  const { code } = await searchParams;
  let current: LogisticUnit | null = null;
  let notFound = false;
  if (code) {
    const response = await internalApi(`logistic-units/${encodeURIComponent(code)}`);
    if (response.status === 401) redirect(`/auth/login?returnTo=${encodeURIComponent(`/packaging?code=${code}`)}`);
    if (response.ok) current = await response.json();
    else if (response.status === 404) notFound = true;
  }

  return <InternalPage title="Упаковка и агрегация">
    <section className="scan-card">
      <div><p className="eyebrow">ПОИСК УПАКОВКИ</p><h2>Короб, паллета или контейнер</h2><p>Введите внутренний код логистической единицы.</p></div>
      <form action="/packaging" method="get">
        <label><span>⌕</span><input name="code" defaultValue={code ?? ""} placeholder="BOX-2026-0001" /></label>
        <button type="submit">Открыть</button>
      </form>
    </section>

    {notFound && <div className="error" role="alert">Логистическая единица не найдена.</div>}

    {current && <section className="panel">
      <div className="panel-head"><div><p className="eyebrow">ТЕКУЩАЯ ПРОЕКЦИЯ</p><h2>{current.code}</h2></div><span>{current.type}{current.sscc ? ` · SSCC ${current.sscc}` : ""}</span></div>
      <div className="table-wrap"><table><thead><tr><th>Тип</th><th>Код</th><th>Продукт</th><th>Серийный номер</th></tr></thead><tbody>
        {current.children.map(child => <tr key={`${child.kind}:${child.code}`}><td>{child.kind}</td><td>{child.kind === "UNIT" ? <Link href={`/u/${child.code}`}>{child.code}</Link> : <Link href={`/packaging?code=${encodeURIComponent(child.code)}`}>{child.code}</Link>}</td><td>{child.product ?? "—"}</td><td>{child.serial ?? "—"}</td></tr>)}
      </tbody></table></div>
      {current.children.length === 0 && <div className="empty">Упаковка пока пуста.</div>}
    </section>}

    {current && <section className="panel">
      <div className="panel-head"><div><p className="eyebrow">IMMUTABLE LEDGER</p><h2>История агрегации</h2></div><span>{current.events.length} событий</span></div>
      <div className="table-wrap"><table><thead><tr><th>Действие</th><th>Время события</th><th>Источник</th><th>Actor</th></tr></thead><tbody>
        {current.events.map(item => <tr key={item.id}><td>{item.action}</td><td>{new Date(item.occurredAt).toLocaleString("ru-RU")}</td><td>{item.sourceSystem}</td><td>{item.actorSubject}</td></tr>)}
      </tbody></table></div>
    </section>}

    <PackagingActions currentCode={current?.code} />
  </InternalPage>;
}
