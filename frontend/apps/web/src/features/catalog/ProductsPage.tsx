import { Badge, Button, cn, Input } from '@nihasdesk/ui';
import { Package, Plus, Search, Upload } from 'lucide-react';
import { useDeferredValue, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useSession } from '../session/useSession';
import { productStatuses, useProducts, type ProductStatus, type ProductSummary } from './api';
import { formatMoney } from './money';
import { canManageCatalog, statusTone } from './roles';

function Price({ product }: { product: ProductSummary }) {
  const { i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';
  if (product.priceMin === null || !product.currency) return <span>—</span>;
  const min = formatMoney(product.priceMin, product.currency, language);
  return (
    <span>
      {product.priceMax !== null && product.priceMax !== product.priceMin
        ? `${min} – ${formatMoney(product.priceMax, product.currency, language)}`
        : min}
    </span>
  );
}

/** Products (ADR 0018): search, status tabs, and links to the editor and the CSV import. */
export function ProductsPage() {
  const { t } = useTranslation();
  const { data: session } = useSession();
  const [status, setStatus] = useState<ProductStatus | 'all'>('all');
  const [search, setSearch] = useState('');
  const query = useDeferredValue(search.trim());
  const products = useProducts(query, status);
  const items = products.data?.pages.flatMap((page) => page.items) ?? [];
  const manage = canManageCatalog(session?.role);

  return (
    <div className="min-h-0 flex-1 overflow-y-auto">
      <div className="flex flex-col gap-6 p-4 md:p-8">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="flex-1 text-[28px] leading-9 font-semibold">{t('nav.products')}</h1>
          {manage && (
            <>
              <Button asChild variant="outline">
                <Link to="/products/import">
                  <Upload aria-hidden="true" />
                  {t('products.importCsv')}
                </Link>
              </Button>
              <Button asChild>
                <Link to="/products/new">
                  <Plus aria-hidden="true" />
                  {t('products.add')}
                </Link>
              </Button>
            </>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <div className="relative min-w-60 flex-1">
            <Search
              className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
              aria-hidden="true"
            />
            <Input
              type="search"
              aria-label={t('products.search')}
              placeholder={t('products.searchPlaceholder')}
              className="pl-9"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </div>
          <div role="group" aria-label={t('products.status')} className="flex gap-1">
            {(['all', ...productStatuses] as const).map((value) => (
              <button
                key={value}
                type="button"
                aria-pressed={status === value}
                onClick={() => setStatus(value)}
                className={cn(
                  'h-8 rounded-md border px-3 text-[13px] font-medium',
                  status === value
                    ? 'border-transparent bg-selected text-selected-foreground'
                    : 'bg-surface hover:bg-subtle',
                )}
              >
                {t(`products.statuses.${value}`)}
              </button>
            ))}
          </div>
        </div>

        {products.isPending ? (
          <div aria-busy="true" className="h-40 rounded-lg border bg-surface" />
        ) : items.length === 0 ? (
          <div className="flex flex-col items-center gap-2 rounded-lg border bg-surface p-10 text-center">
            <Package className="size-8 text-muted-foreground" strokeWidth={1.5} aria-hidden="true" />
            <h2 className="text-base font-semibold">
              {query || status !== 'all' ? t('products.noMatch') : t('products.empty.title')}
            </h2>
            {!query && status === 'all' && (
              <p className="max-w-md text-[13px] text-secondary-foreground">{t('products.empty.text')}</p>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto rounded-lg border bg-surface">
            <table className="w-full min-w-160 text-left text-[13px]">
              <caption className="sr-only">{t('nav.products')}</caption>
              <thead className="border-b text-xs text-secondary-foreground">
                <tr>
                  <th className="px-4 py-2 font-medium">{t('products.columns.product')}</th>
                  <th className="px-4 py-2 font-medium">{t('products.columns.status')}</th>
                  <th className="px-4 py-2 font-medium">{t('products.columns.price')}</th>
                  <th className="px-4 py-2 font-medium">{t('products.columns.stock')}</th>
                  <th className="px-4 py-2 font-medium">{t('products.columns.variants')}</th>
                </tr>
              </thead>
              <tbody>
                {items.map((product) => (
                  <tr key={product.id} className="border-b last:border-b-0 hover:bg-subtle">
                    <td className="px-4 py-3">
                      <Link to={`/products/${product.id}`} className="font-medium hover:underline">
                        {product.title}
                      </Link>
                      <div className="text-xs text-muted-foreground">
                        {[product.vendor, product.category ?? product.productType]
                          .filter(Boolean)
                          .join(' · ') || product.handle}
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <Badge tone={statusTone[product.status]}>
                        {t(`products.statuses.${product.status}`)}
                      </Badge>
                    </td>
                    <td className="px-4 py-3">
                      <Price product={product} />
                    </td>
                    <td className="px-4 py-3">
                      {product.stock === null
                        ? t('products.notTracked')
                        : t('products.inStock', { count: product.stock })}
                    </td>
                    <td className="px-4 py-3">{product.variantCount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {products.hasNextPage && (
          <div className="flex justify-center">
            <Button
              variant="outline"
              disabled={products.isFetchingNextPage}
              onClick={() => void products.fetchNextPage()}
            >
              {t('products.loadMore')}
            </Button>
          </div>
        )}
      </div>
    </div>
  );
}
