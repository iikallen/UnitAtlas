import { redirect } from "next/navigation";
import InternalPage from "../../../components/InternalPage";
import { internalApi } from "../../../lib/api";

type Product = { id: string; sku: string; name: string; gtin: string };

export default async function ProductsPage() {
  const response = await internalApi("products");
  if (response.status === 401) redirect("/auth/login?returnTo=/products");
  const products: Product[] = response.ok ? await response.json() : [];
  return <InternalPage title="Продукция"><section className="products">{products.map(product => <article key={product.id}><span>GTIN {product.gtin}</span><strong>{product.name}</strong><small>{product.sku}</small></article>)}</section></InternalPage>;
}
