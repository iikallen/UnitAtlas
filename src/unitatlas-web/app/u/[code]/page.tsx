import Link from "next/link";
import EventActions from "./EventActions";

const API = process.env.API_INTERNAL_URL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080/api";

type Passport = {
  atlasId: string; serial: string; lot: string; manufacturedAt: string;
  product: { name: string; sku: string; gtin: string };
  state: { status: string; location: string; updatedAt: string };
  events: { id: string; eventType: string; location: string; actor: string; occurredAt: string }[];
};

export default async function PassportPage({ params }: { params: Promise<{ code: string }> }) {
  const { code } = await params;
  const response = await fetch(`${API}/units/${encodeURIComponent(code)}`, { cache: "no-store" });
  if (!response.ok) return <main className="passport-shell"><Link href="/" className="back">← UnitAtlas</Link><section className="passport"><h1>Изделие не найдено</h1><p>Проверьте UnitAtlas ID и повторите поиск.</p></section></main>;
  const data: Passport = await response.json();

  return (
    <main className="passport-shell">
      <Link href="/" className="back">← Вернуться в UnitAtlas</Link>
      <section className="passport">
        <header><div className="passport-brand"><span>U</span> UNITATLAS</div><span className="verified">✓ AUTHENTIC</span></header>
        <div className="passport-title"><div><p className="eyebrow">ЦИФРОВОЙ ПАСПОРТ</p><h1>{data.product.name}</h1><p>{data.product.sku}</p></div><div className="passport-status"><span>ТЕКУЩИЙ СТАТУС</span><strong>{data.state.status}</strong><small>{data.state.location}</small></div></div>
        <dl className="passport-data"><div><dt>UnitAtlas ID</dt><dd>{data.atlasId}</dd></div><div><dt>Serial</dt><dd>{data.serial}</dd></div><div><dt>GTIN</dt><dd>{data.product.gtin}</dd></div><div><dt>Партия</dt><dd>{data.lot}</dd></div><div><dt>Произведён</dt><dd>{new Date(data.manufacturedAt).toLocaleString("ru-RU")}</dd></div><div><dt>Производитель</dt><dd>Atlas Manufacturing</dd></div></dl>
        <div className="passport-grid"><section><p className="eyebrow">ИСТОРИЯ ИЗДЕЛИЯ</p><h2>Timeline</h2><ol className="timeline">{data.events.map((event, index) => <li key={event.id} className={index === 0 ? "latest" : ""}><i></i><div><strong>{event.eventType.replaceAll("_", " ")}</strong><span>{event.location}</span><small>{new Date(event.occurredAt).toLocaleString("ru-RU")} · {event.actor}</small></div></li>)}</ol></section><EventActions atlasId={data.atlasId} /></div>
      </section>
      <p className="passport-foot">Запись сформирована UnitAtlas · Event ledger защищён от повторных событий</p>
    </main>
  );
}
