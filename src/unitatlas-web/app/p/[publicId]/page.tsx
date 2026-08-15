import Link from "next/link";
import { publicApi } from "../../../lib/api";

type PublicPassport = {
  publicId: string;
  authenticity: string;
  product: { name: string; gtin: string };
  serial: string;
  manufacturedAt: string;
  state: { status: string; updatedAt: string };
  timeline: { code: string; occurredAt: string }[];
};

export default async function PublicPassportPage({ params }: { params: Promise<{ publicId: string }> }) {
  const { publicId } = await params;
  const response = await publicApi(`passports/${encodeURIComponent(publicId)}`);
  if (!response.ok) return <main className="passport-shell"><Link href="/" className="back">← UnitAtlas</Link><section className="passport"><h1>Паспорт не опубликован</h1></section></main>;
  const data: PublicPassport = await response.json();
  return <main className="passport-shell">
    <section className="passport">
      <header><div className="passport-brand"><span>U</span> UNITATLAS</div><span className="verified">✓ {data.authenticity.toUpperCase()}</span></header>
      <div className="passport-title"><div><p className="eyebrow">ПУБЛИЧНЫЙ ЦИФРОВОЙ ПАСПОРТ</p><h1>{data.product.name}</h1></div><div className="passport-status"><span>ТЕКУЩИЙ СТАТУС</span><strong>{data.state.status}</strong></div></div>
      <dl className="passport-data"><div><dt>Serial</dt><dd>{data.serial}</dd></div><div><dt>GTIN</dt><dd>{data.product.gtin}</dd></div><div><dt>Произведён</dt><dd>{new Date(data.manufacturedAt).toLocaleString("ru-RU")}</dd></div><div><dt>Подлинность</dt><dd>Подтверждена UnitAtlas</dd></div></dl>
      <section><p className="eyebrow">ПУБЛИЧНАЯ ИСТОРИЯ</p><h2>Timeline</h2><ol className="timeline">{data.timeline.map((event, index) => <li key={`${event.code}-${event.occurredAt}`} className={index === 0 ? "latest" : ""}><i></i><div><strong>{event.code.replaceAll("_", " ")}</strong><small>{new Date(event.occurredAt).toLocaleString("ru-RU")}</small></div></li>)}</ol></section>
    </section>
    <p className="passport-foot">Публичная проекция не содержит сотрудников, внутренних координат и ERP-данных</p>
  </main>;
}
