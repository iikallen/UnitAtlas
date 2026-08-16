import { redirect } from "next/navigation";
import InternalPage from "../../../components/InternalPage";
import { internalApi } from "../../../lib/api";
import { EndpointToggle, GatewayMode, RetryDelivery } from "./IntegrationActions";

type Endpoint = {
  id: string; system: string; adapter: string; baseAddress: string; hasSecretRef: boolean; enabled: boolean;
  lastSuccessfulDelivery: string | null; backlog: number; retryCount: number; deadLetters: number; deliveryLagSeconds: number;
};
type Delivery = { id: string; type: string; status: string; attemptCount: number; lastErrorCode: string | null; createdAt: string };

export default async function IntegrationsPage() {
  const [endpointResponse, gatewayResponse] = await Promise.all([
    internalApi("integration-endpoints"), internalApi("integration-settings/regulatory-gateway")
  ]);
  if (endpointResponse.status === 401 || gatewayResponse.status === 401) redirect("/auth/login?returnTo=/integrations");
  const endpoints: Endpoint[] = endpointResponse.ok ? await endpointResponse.json() : [];
  const gateway: { mode: string } = gatewayResponse.ok ? await gatewayResponse.json() : { mode: "NONE" };
  const deliveries = new Map<string, Delivery[]>();
  await Promise.all(endpoints.map(async endpoint => {
    const response = await internalApi(`integration-endpoints/${endpoint.id}/deliveries`);
    deliveries.set(endpoint.id, response.ok ? await response.json() : []);
  }));
  const totals = endpoints.reduce((value, endpoint) => ({
    backlog: value.backlog + endpoint.backlog,
    retries: value.retries + endpoint.retryCount,
    dead: value.dead + endpoint.deadLetters
  }), { backlog: 0, retries: 0, dead: 0 });

  return <InternalPage title="Операции интеграций">
    <section className="stats integration-stats">
      <article><span>Системы</span><strong>{endpoints.length}</strong><small>{endpoints.filter(x => x.enabled).length} включено</small></article>
      <article><span>Backlog</span><strong>{totals.backlog}</strong><small>ожидают доставки</small></article>
      <article><span>Повторные попытки</span><strong>{totals.retries}</strong><small>после первой отправки</small></article>
      <article><span>Dead letters</span><strong>{totals.dead}</strong><small>требуют оператора</small></article>
    </section>
    <section className="panel">
      <div className="panel-head"><div><p className="eyebrow">REGULATORY ROUTING</p><h2>Единственный активный gateway</h2></div><GatewayMode mode={gateway.mode} /></div>
      <p className="panel-note">ONE_C и DIRECT_IS_MPT — взаимоисключающие tenant-режимы. Direct IS MPT adapter отложен до sandbox credentials.</p>
    </section>
    <section className="panel">
      <div className="panel-head"><div><p className="eyebrow">DELIVERY RUNTIME</p><h2>Настроенные системы</h2></div></div>
      <div className="table-wrap"><table><thead><tr><th>Система</th><th>Состояние</th><th>Последняя доставка</th><th>Backlog / retry / dead</th><th>Действие</th></tr></thead><tbody>
        {endpoints.map(endpoint => <tr key={endpoint.id}><td><strong>{endpoint.system}</strong><small>{endpoint.adapter} · {endpoint.baseAddress}</small><small>{endpoint.hasSecretRef ? "SecretRef настроен" : "Без SecretRef"}</small></td><td><span className={`status ${endpoint.enabled ? "received" : ""}`}>{endpoint.enabled ? "Enabled" : "Disabled"}</span></td><td>{endpoint.lastSuccessfulDelivery ? new Date(endpoint.lastSuccessfulDelivery).toLocaleString("ru-RU") : "—"}<small>lag {Math.round(endpoint.deliveryLagSeconds)} s</small></td><td>{endpoint.backlog} / {endpoint.retryCount} / {endpoint.deadLetters}</td><td><EndpointToggle id={endpoint.id} enabled={endpoint.enabled} /></td></tr>)}
      </tbody></table></div>
      {endpoints.length === 0 && <div className="empty">Integration endpoints ещё не настроены.</div>}
    </section>
    {endpoints.map(endpoint => {
      const dead = (deliveries.get(endpoint.id) ?? []).filter(delivery => delivery.status === "DeadLetter");
      return dead.length > 0 && <section className="panel" key={`dead:${endpoint.id}`}><div className="panel-head"><div><p className="eyebrow">DEAD LETTERS</p><h2>{endpoint.system}</h2></div></div><div className="table-wrap"><table><thead><tr><th>Event</th><th>Ошибка</th><th>Попытки</th><th>Создано</th><th></th></tr></thead><tbody>{dead.map(delivery => <tr key={delivery.id}><td>{delivery.type}</td><td>{delivery.lastErrorCode ?? "—"}</td><td>{delivery.attemptCount}</td><td>{new Date(delivery.createdAt).toLocaleString("ru-RU")}</td><td><RetryDelivery endpointId={endpoint.id} deliveryId={delivery.id} /></td></tr>)}</tbody></table></div></section>;
    })}
  </InternalPage>;
}
