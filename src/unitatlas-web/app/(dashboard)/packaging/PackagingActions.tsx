"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";

export default function PackagingActions({ currentCode }: { currentCode?: string }) {
  const router = useRouter();
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function createLogisticUnit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError("");
    const form = new FormData(event.currentTarget);
    const code = String(form.get("code") ?? "").trim();
    const response = await fetch("/bff/logistic-units", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        code,
        type: form.get("type"),
        sscc: String(form.get("sscc") ?? "").trim() || null
      })
    });
    setBusy(false);
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      setError(problem.title ?? "Не удалось создать логистическую единицу.");
      return;
    }
    router.push(`/packaging?code=${encodeURIComponent(code)}`);
    router.refresh();
  }

  async function aggregate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!currentCode) return;
    setBusy(true);
    setError("");
    const form = new FormData(event.currentTarget);
    const unitAtlasIds = String(form.get("unitAtlasIds") ?? "")
      .split(/[\s,;]+/).map(value => value.trim()).filter(Boolean);
    const logisticUnitCodes = String(form.get("logisticUnitCodes") ?? "")
      .split(/[\s,;]+/).map(value => value.trim()).filter(Boolean);
    const response = await fetch(`/bff/logistic-units/${encodeURIComponent(currentCode)}/aggregations`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        action: form.get("action"),
        idempotencyKey: crypto.randomUUID(),
        unitAtlasIds,
        logisticUnitCodes
      })
    });
    setBusy(false);
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      setError(problem.title ?? "Операция агрегации не выполнена.");
      return;
    }
    event.currentTarget.reset();
    router.refresh();
  }

  return <>
    {error && <div className="error" role="alert">{error}</div>}
    <section className="panel">
      <div className="panel-head"><div><p className="eyebrow">ЛОГИСТИЧЕСКАЯ ЕДИНИЦА</p><h2>Создать короб или паллету</h2></div></div>
      <form className="dialog" onSubmit={createLogisticUnit}>
        <label>Код<input name="code" required placeholder="BOX-2026-0001" /></label>
        <label>Тип<select name="type" defaultValue="BOX"><option value="BOX">BOX</option><option value="PALLET">PALLET</option><option value="CONTAINER">CONTAINER</option></select></label>
        <label>SSCC, если используется<input name="sscc" inputMode="numeric" placeholder="18 цифр" /></label>
        <button className="primary" disabled={busy} type="submit">{busy ? "Сохранение…" : "Создать"}</button>
      </form>
    </section>

    {currentCode && <section className="panel">
      <div className="panel-head"><div><p className="eyebrow">АГРЕГАЦИЯ</p><h2>Упаковка / распаковка</h2></div></div>
      <form className="dialog" onSubmit={aggregate}>
        <label>Действие<select name="action" defaultValue="ADD"><option value="ADD">ADD — добавить</option><option value="DELETE">DELETE — извлечь</option></select></label>
        <label>UnitAtlas ID изделий<textarea name="unitAtlasIds" rows={3} placeholder="UA-KZ-2026-0000058219" /></label>
        <label>Коды вложенных коробов / паллет<textarea name="logisticUnitCodes" rows={3} placeholder="BOX-2026-0001" /></label>
        <button className="primary" disabled={busy} type="submit">{busy ? "Запись…" : "Записать событие"}</button>
      </form>
    </section>}
  </>;
}
