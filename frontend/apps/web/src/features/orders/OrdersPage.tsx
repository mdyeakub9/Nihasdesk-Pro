import { Badge, Button, cn } from '@nihasdesk/ui';
import { Plus, ShoppingBag, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { formatAge } from '../../lib/format';
import { formatMoney } from '../catalog/money';
import { CreateOrderDialog } from './CreateOrderDialog';
import { orderStatuses, useOrderCounts, useOrders, type OrderStatus } from './api';
import { statusTone } from './status';

/** Orders (PRD 4.9): Pending approval / Confirmed / Rejected; AI requests wait for a person. */
export function OrdersPage() {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';
  const [status, setStatus] = useState<OrderStatus>('pending');
  const [creating, setCreating] = useState(false);
  const { data: counts } = useOrderCounts();
  const orders = useOrders(status);
  const items = orders.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <div className="min-h-0 flex-1 overflow-y-auto">
      <div className="flex flex-col gap-6 p-4 md:p-8">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="flex-1 text-[28px] leading-9 font-semibold">{t('nav.orders')}</h1>
          <Button onClick={() => setCreating(true)}>
            <Plus aria-hidden="true" />
            {t('orders.new')}
          </Button>
        </div>
        {creating && <CreateOrderDialog onClose={() => setCreating(false)} />}

        <div role="group" aria-label={t('orders.status')} className="flex flex-wrap gap-1">
          {orderStatuses.map((value) => (
            <button
              key={value}
              type="button"
              aria-pressed={status === value}
              onClick={() => setStatus(value)}
              className={cn(
                'flex h-8 items-center gap-2 rounded-md border px-3 text-[13px] font-medium',
                status === value
                  ? 'border-transparent bg-selected text-selected-foreground'
                  : 'bg-surface hover:bg-subtle',
              )}
            >
              {t(`orders.statuses.${value}`)}
              {counts && <span className="text-xs text-muted-foreground">{counts[value]}</span>}
            </button>
          ))}
        </div>

        {orders.isPending ? (
          <div aria-busy="true" className="h-40 rounded-lg border bg-surface" />
        ) : items.length === 0 ? (
          <div className="flex flex-col items-center gap-2 rounded-lg border bg-surface p-10 text-center">
            <ShoppingBag className="size-8 text-muted-foreground" strokeWidth={1.5} aria-hidden="true" />
            <h2 className="text-base font-semibold">{t(`orders.empty.${status}`)}</h2>
          </div>
        ) : (
          <div className="overflow-x-auto rounded-lg border bg-surface">
            <table className="w-full min-w-160 text-left text-[13px]">
              <caption className="sr-only">{t(`orders.statuses.${status}`)}</caption>
              <thead className="border-b text-xs text-secondary-foreground">
                <tr>
                  <th className="px-4 py-2 font-medium">{t('orders.columns.order')}</th>
                  <th className="px-4 py-2 font-medium">{t('orders.columns.customer')}</th>
                  <th className="px-4 py-2 font-medium">{t('orders.columns.items')}</th>
                  <th className="px-4 py-2 font-medium">{t('orders.columns.total')}</th>
                  <th className="px-4 py-2 font-medium">{t('orders.columns.created')}</th>
                </tr>
              </thead>
              <tbody>
                {items.map((order) => (
                  <tr key={order.id} className="border-b last:border-b-0 hover:bg-subtle">
                    <td className="px-4 py-3">
                      <Link to={`/orders/${order.id}`} className="font-medium hover:underline">
                        #{order.number}
                      </Link>
                      <div className="mt-1 flex items-center gap-1">
                        <Badge tone={statusTone[order.status]}>{t(`orders.statuses.${order.status}`)}</Badge>
                        {order.source === 'ai' && (
                          <Badge tone="ai">
                            <Sparkles aria-hidden="true" />
                            {t('orders.byAi')}
                          </Badge>
                        )}
                      </div>
                    </td>
                    <td className="px-4 py-3">{order.customerName}</td>
                    <td className="px-4 py-3">{order.itemCount}</td>
                    <td className="px-4 py-3">{formatMoney(order.total, order.currency, language)}</td>
                    <td className="px-4 py-3 text-secondary-foreground">
                      {t('orders.ago', { age: formatAge(order.createdAt, language) })}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {orders.hasNextPage && (
          <div className="flex justify-center">
            <Button
              variant="outline"
              disabled={orders.isFetchingNextPage}
              onClick={() => void orders.fetchNextPage()}
            >
              {t('orders.more')}
            </Button>
          </div>
        )}
      </div>
    </div>
  );
}
