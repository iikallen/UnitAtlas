"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

export function EndpointToggle({ id, enabled }: { id: string; enabled: boolean }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  async function toggle() {
    setBusy(true);
    const response = await fetch(`/bff/integration-endpoints/${id}/enabled`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ enabled: !enabled })
    });
    setBusy(false);
    if (response.ok) router.refresh();
  }
  return <button className="secondary" disabled={busy} onClick={toggle}>{busy ? "…" : enabled ? "Отключить" : "Включить"}</button>;
}

export function RetryDelivery({ endpointId, deliveryId }: { endpointId: string; deliveryId: string }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  async function retry() {
    setBusy(true);
    const response = await fetch(`/bff/integration-endpoints/${endpointId}/deliveries/${deliveryId}/retry`, { method: "POST" });
    setBusy(false);
    if (response.ok) router.refresh();
  }
  return <button className="secondary" disabled={busy} onClick={retry}>{busy ? "…" : "Повторить"}</button>;
}

export function GatewayMode({ mode }: { mode: string }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  async function change(next: string) {
    setBusy(true);
    const response = await fetch("/bff/integration-settings/regulatory-gateway", {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ mode: next })
    });
    setBusy(false);
    if (response.ok) router.refresh();
  }
  return <label className="gateway-mode">Регуляторный маршрут<select disabled={busy} value={mode} onChange={event => change(event.target.value)}><option value="NONE">NONE</option><option value="ONE_C">ONE_C</option><option value="DIRECT_IS_MPT">DIRECT_IS_MPT</option></select></label>;
}
