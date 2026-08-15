"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";

const API = "/bff";
const eventTypes = ["QUALITY_PASSED", "PACKED", "MOVED_TO_WAREHOUSE", "SHIPPED", "RECEIVED"];

export default function EventActions({ atlasId }: { atlasId: string }) {
  const router = useRouter();
  const [message, setMessage] = useState("");

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const response = await fetch(`${API}/units/${atlasId}/events`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        eventType: form.get("eventType"), location: form.get("location"), actor: "demo.operator",
        idempotencyKey: `web:${crypto.randomUUID()}`
      })
    });
    if (!response.ok) { setMessage("Событие не записано."); return; }
    setMessage("Событие записано.");
    event.currentTarget.reset();
    router.refresh();
  }

  return <aside className="event-card"><p className="eyebrow">НОВОЕ СОБЫТИЕ</p><h2>Обновить состояние</h2><form onSubmit={submit}><label>Событие<select name="eventType">{eventTypes.map(type => <option key={type}>{type}</option>)}</select></label><label>Местоположение<input name="location" required placeholder="Warehouse A" /></label><button className="primary">Записать в ledger</button>{message && <p className="form-message">{message}</p>}</form><small>Записанное событие не редактируется и обновляет Current State.</small></aside>;
}
