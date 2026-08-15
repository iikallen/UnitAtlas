"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";

const API = "/bff";

type Unit = {
  atlasId: string; serial: string; lot: string; product: string; sku: string;
  gtin: string; status: string; location: string; updatedAt: string;
};
type Dashboard = {
  totalUnits: number; products: number; events: number;
  statuses: { status: string; count: number }[]; recentUnits: Unit[];
};
type Product = { id: string; sku: string; name: string; gtin: string };

export default function Home() {
  const router = useRouter();
  const [dashboard, setDashboard] = useState<Dashboard>();
  const [products, setProducts] = useState<Product[]>([]);
  const [units, setUnits] = useState<Unit[]>([]);
  const [query, setQuery] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [showCreate, setShowCreate] = useState(false);
  const [scanning, setScanning] = useState(false);
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);

  const load = useCallback(async (search = "") => {
    setLoading(true);
    setError("");
    try {
      const [dashboardResponse, productsResponse, unitsResponse] = await Promise.all([
        fetch(`${API}/dashboard`, { cache: "no-store" }),
        fetch(`${API}/products`, { cache: "no-store" }),
        fetch(`${API}/units?query=${encodeURIComponent(search)}`, { cache: "no-store" })
      ]);
      if ([dashboardResponse, productsResponse, unitsResponse].some(response => response.status === 401)) {
        router.push("/auth/login");
        return;
      }
      if (!dashboardResponse.ok || !productsResponse.ok || !unitsResponse.ok) throw new Error("API unavailable");
      setDashboard(await dashboardResponse.json());
      setProducts(await productsResponse.json());
      setUnits(await unitsResponse.json());
    } catch {
      setError("Не удалось подключиться к UnitAtlas API.");
    } finally {
      setLoading(false);
    }
  }, [router]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);
  useEffect(() => () => streamRef.current?.getTracks().forEach(track => track.stop()), []);

  async function scan() {
    const BrowserBarcodeDetector = (window as unknown as {
      BarcodeDetector?: new (options: { formats: string[] }) => { detect(source: CanvasImageSource): Promise<{ rawValue: string }[]> }
    }).BarcodeDetector;
    if (!BrowserBarcodeDetector) {
      setError("Этот браузер не поддерживает нативное сканирование. Введите код вручную.");
      return;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" } });
      streamRef.current = stream;
      setScanning(true);
      await new Promise(requestAnimationFrame);
      if (!videoRef.current) return;
      videoRef.current.srcObject = stream;
      await videoRef.current.play();
      const detector = new BrowserBarcodeDetector({ formats: ["qr_code", "data_matrix"] });
      const timer = window.setInterval(async () => {
        if (!videoRef.current) return;
        const codes = await detector.detect(videoRef.current);
        if (!codes[0]?.rawValue) return;
        window.clearInterval(timer);
        stopScan();
        const code = codes[0].rawValue.split("/").pop() ?? codes[0].rawValue;
        setQuery(code);
        void load(code);
      }, 350);
    } catch {
      stopScan();
      setError("Камера недоступна. Разрешите доступ или введите код вручную.");
    }
  }

  function stopScan() {
    streamRef.current?.getTracks().forEach(track => track.stop());
    streamRef.current = null;
    setScanning(false);
  }

  async function createUnit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const response = await fetch(`${API}/units`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ productId: data.get("productId"), serial: data.get("serial"), lot: data.get("lot") })
    });
    if (!response.ok) { setError("Изделие не создано. Проверьте серийный номер."); return; }
    setShowCreate(false);
    event.currentTarget.reset();
    await load();
  }

  return (
    <main className="shell">
      <aside className="sidebar">
        <div className="brand"><span>U</span><strong>UNITATLAS</strong></div>
        <nav>
          <a className="active" href="#overview">Обзор</a>
          <Link href="/units">Изделия</Link>
          <Link href="/products">Продукция</Link>
          <Link href="/events">События</Link>
          <Link href="/settings">Настройки</Link>
        </nav>
        <div className="tenant"><span>AM</span><div><strong>Atlas Manufacturing</strong><small>Factory #1</small></div></div>
      </aside>

      <section className="content">
        <header className="topbar">
          <div><p className="eyebrow">ЦЕНТР УПРАВЛЕНИЯ</p><h1>Добро пожаловать в UnitAtlas</h1></div>
          <button className="primary" onClick={() => setShowCreate(true)}>+ Добавить изделие</button>
        </header>

        {error && <div className="error" role="alert">{error}<button onClick={() => setError("")}>×</button></div>}

        <section className="hero" id="overview">
          <div><span className="live">● LIVE TRACEABILITY</span><h2>Каждое изделие.<br />Вся история.</h2><p>Цифровой паспорт продукции от производственной линии до конечного получателя.</p></div>
          <div className="orbit" aria-hidden="true"><i>UA</i><span></span><span></span><span></span></div>
        </section>

        <section className="stats" aria-label="Сводка">
          <article><span>Всего изделий</span><strong>{dashboard?.totalUnits ?? "—"}</strong><small>уникальных единиц</small></article>
          <article><span>События ledger</span><strong>{dashboard?.events ?? "—"}</strong><small>неизменяемая история</small></article>
          <article><span>В пути</span><strong>{dashboard?.statuses.find(x => x.status === "Shipped")?.count ?? 0}</strong><small>отгружено</small></article>
          <article><span>Продукция</span><strong>{dashboard?.products ?? "—"}</strong><small>позиций каталога</small></article>
        </section>

        <section className="scan-card">
          <div><p className="eyebrow">БЫСТРЫЙ ПОИСК</p><h2>Найдите цифровой паспорт</h2><p>UnitAtlas ID, серийный номер или GTIN</p></div>
          <form onSubmit={event => { event.preventDefault(); void load(query); }}>
            <label><span>⌕</span><input value={query} onChange={event => setQuery(event.target.value)} placeholder="UA-KZ-2026-0000058219" /></label>
            <button type="submit">Найти</button>
            <button type="button" className="scan" onClick={() => void scan()}>▣ Сканировать</button>
          </form>
        </section>

        {scanning && <div className="modal"><div className="camera"><video ref={videoRef} playsInline muted /><div className="scan-line"></div><button onClick={stopScan}>Закрыть камеру</button></div></div>}

        <section className="panel" id="units">
          <div className="panel-head"><div><p className="eyebrow">ПОСЛЕДНИЕ ОБНОВЛЕНИЯ</p><h2>Изделия</h2></div><span>{loading ? "Обновление…" : `${units.length} записей`}</span></div>
          <div className="table-wrap"><table><thead><tr><th>UnitAtlas ID</th><th>Продукт</th><th>Статус</th><th>Местоположение</th><th>Обновлено</th></tr></thead>
            <tbody>{units.map(unit => <tr key={unit.atlasId}><td><Link href={`/u/${unit.atlasId}`}>{unit.atlasId}</Link><small>{unit.serial}</small></td><td>{unit.product}<small>{unit.lot}</small></td><td><span className={`status ${unit.status.toLowerCase().replaceAll(" ", "-")}`}>{unit.status}</span></td><td>{unit.location}</td><td>{new Date(unit.updatedAt).toLocaleString("ru-RU", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" })}</td></tr>)}</tbody>
          </table></div>
          {!loading && units.length === 0 && <div className="empty">Ничего не найдено. Проверьте код.</div>}
        </section>

        <section className="products" id="products"><div><p className="eyebrow">КАТАЛОГ</p><h2>Продукция</h2></div>{products.map(product => <article key={product.id}><span>GTIN {product.gtin}</span><strong>{product.name}</strong><small>{product.sku}</small></article>)}</section>
      </section>

      {showCreate && <div className="modal" onMouseDown={event => event.target === event.currentTarget && setShowCreate(false)}><form className="dialog" onSubmit={createUnit}><button className="close" type="button" onClick={() => setShowCreate(false)}>×</button><p className="eyebrow">НОВОЕ ИЗДЕЛИЕ</p><h2>Создать цифровой паспорт</h2><label>Продукт<select name="productId" required>{products.map(product => <option key={product.id} value={product.id}>{product.name}</option>)}</select></label><label>Серийный номер<input name="serial" required placeholder="X200-260816-00045" /></label><label>Партия<input name="lot" required placeholder="LOT-260816-A" /></label><button className="primary" type="submit">Создать изделие</button></form></div>}
    </main>
  );
}
