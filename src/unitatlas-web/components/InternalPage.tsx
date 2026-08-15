import Link from "next/link";

export default function InternalPage({ title, children }: { title: string; children: React.ReactNode }) {
  return <main className="passport-shell"><Link href="/" className="back">← Обзор</Link><section className="passport"><header><div className="passport-brand"><span>U</span> UNITATLAS</div><nav><Link href="/units">Изделия</Link> · <Link href="/products">Продукция</Link> · <Link href="/packaging">Упаковка</Link> · <Link href="/events">События</Link> · <Link href="/settings">Настройки</Link></nav></header><h1>{title}</h1>{children}</section></main>;
}
