import { Badge, Button, Card, FieldError, Label, Textarea } from '@nihasdesk/ui';
import { ArrowLeft, Check, MessageSquare, Pencil, Sparkles, X } from 'lucide-react';
import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { formatDay, formatTime } from '../../lib/format';
import { formatMoney } from '../catalog/money';
import { useApproveOrder, useOrder, useRejectOrder, useUpdateOrder, type Order } from './api';
import { orderErrorMessage } from './errors';
import { OrderForm } from './OrderForm';
import { statusTone } from './status';

/** Approve, edit then approve, or reject a pending request (PRD 4.9). Prices come from the server. */
function Decision({ order, onEdit }: { order: Order; onEdit: () => void }) {
  const { t } = useTranslation();
  const approve = useApproveOrder(order.id);
  const reject = useRejectOrder(order.id);
  const [rejecting, setRejecting] = useState(false);
  const [reason, setReason] = useState('');
  const reasonId = useId();
  const error = approve.error ?? reject.error;

  return (
    <Card className="flex flex-col gap-3">
      <p className="text-[13px] text-secondary-foreground">{t('orders.detail.decide')}</p>
      {rejecting ? (
        <div className="flex flex-col gap-2">
          <Label htmlFor={reasonId}>{t('orders.detail.reason')}</Label>
          <Textarea id={reasonId} rows={2} value={reason} onChange={(e) => setReason(e.target.value)} />
          <div className="flex gap-2">
            <Button
              variant="destructive"
              disabled={!reason.trim() || reject.isPending}
              onClick={() => reject.mutate(reason.trim(), { onSuccess: () => setRejecting(false) })}
            >
              {t('orders.detail.confirmReject')}
            </Button>
            <Button variant="ghost" onClick={() => setRejecting(false)}>
              {t('orders.cancel')}
            </Button>
          </div>
        </div>
      ) : (
        <div className="flex flex-wrap gap-2">
          <Button disabled={approve.isPending} onClick={() => approve.mutate(undefined)}>
            <Check aria-hidden="true" />
            {t('orders.detail.approve')}
          </Button>
          <Button variant="outline" onClick={onEdit}>
            <Pencil aria-hidden="true" />
            {t('orders.detail.edit')}
          </Button>
          <Button variant="outline" onClick={() => setRejecting(true)}>
            <X aria-hidden="true" />
            {t('orders.detail.reject')}
          </Button>
        </div>
      )}
      {error && <FieldError>{orderErrorMessage(error, t)}</FieldError>}
    </Card>
  );
}

/** One order or order request (PRD 4.9): items, customer, address, conversation link. */
export function OrderDetailPage() {
  const { orderId = '' } = useParams();
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';
  const { data: order, isPending } = useOrder(orderId);
  const update = useUpdateOrder(orderId);
  const [editing, setEditing] = useState(false);

  if (isPending) return <div className="flex-1" aria-busy="true" />;
  if (!order)
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-3 p-8 text-center">
        <h1 className="text-base font-semibold">{t('orders.detail.notFound')}</h1>
        <Button asChild variant="outline" size="sm">
          <Link to="/orders">{t('orders.detail.back')}</Link>
        </Button>
      </div>
    );

  const money = (cents: number) => formatMoney(cents, order.currency, language);

  return (
    <div className="min-h-0 flex-1 overflow-y-auto">
      <div className="flex max-w-3xl flex-col gap-6 p-4 md:p-8">
        <div className="flex flex-wrap items-center gap-3">
          <Button asChild variant="ghost" size="icon">
            <Link to="/orders" aria-label={t('orders.detail.back')}>
              <ArrowLeft aria-hidden="true" />
            </Link>
          </Button>
          <h1 className="text-[28px] leading-9 font-semibold">#{order.number}</h1>
          <Badge tone={statusTone[order.status]}>{t(`orders.statuses.${order.status}`)}</Badge>
          {order.source === 'ai' && (
            <Badge tone="ai">
              <Sparkles aria-hidden="true" />
              {t('orders.byAi')}
            </Badge>
          )}
          <span className="flex-1" />
          {order.conversationId && (
            <Button asChild variant="outline" size="sm">
              <Link to={`/inbox/${order.conversationId}`}>
                <MessageSquare aria-hidden="true" />
                {t('orders.detail.openChat')}
              </Link>
            </Button>
          )}
        </div>

        {order.status === 'pending' && !editing && <Decision order={order} onEdit={() => setEditing(true)} />}
        {order.status === 'rejected' && order.rejectReason && (
          <p role="note" className="rounded-md bg-subtle px-3 py-2 text-[13px]">
            {t('orders.detail.rejectedBecause', { reason: order.rejectReason })}
          </p>
        )}

        {editing ? (
          <Card>
            <OrderForm
              initial={{
                conversationId: order.conversationId,
                customerName: order.customerName,
                phone: order.phone,
                street: order.street,
                postalCode: order.postalCode,
                city: order.city,
                province: order.province,
                country: order.country,
                shippingZoneId: order.shippingZoneId,
                notes: order.notes,
              }}
              initialLines={order.items.map((i) => ({
                productId: i.productId,
                variantId: i.variantId,
                label: i.variant ? `${i.title} — ${i.variant}` : i.title,
                quantity: i.quantity,
              }))}
              submitLabel={t('orders.detail.saveChanges')}
              pending={update.isPending}
              error={update.error}
              onSubmit={(input) => update.mutate(input, { onSuccess: () => setEditing(false) })}
              onCancel={() => setEditing(false)}
            />
          </Card>
        ) : (
          <>
            <Card className="flex flex-col gap-2">
              <h2 className="text-sm font-medium">{t('orders.form.products')}</h2>
              <table className="w-full text-left text-[13px]">
                <caption className="sr-only">{t('orders.form.products')}</caption>
                <tbody>
                  {order.items.map((item) => (
                    <tr key={item.variantId} className="border-b last:border-b-0">
                      <td className="py-2">
                        {item.title}
                        {item.variant && <span className="text-secondary-foreground"> — {item.variant}</span>}
                        {item.sku && <div className="text-xs text-muted-foreground">SKU {item.sku}</div>}
                      </td>
                      <td className="py-2 text-right">
                        {item.quantity} × {money(item.unitPrice)}
                      </td>
                      <td className="py-2 text-right font-medium">{money(item.lineTotal)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <dl className="grid grid-cols-[1fr_auto] gap-x-4 gap-y-1 border-t pt-2 text-[13px]">
                <dt>{t('orders.subtotal')}</dt>
                <dd>{money(order.subtotal)}</dd>
                <dt>{t('orders.shipping', { zone: order.shippingZoneName })}</dt>
                <dd>{money(order.shippingCost)}</dd>
                <dt className="font-semibold">{t('orders.totalCod')}</dt>
                <dd className="font-semibold">{money(order.total)}</dd>
              </dl>
            </Card>

            <Card className="grid gap-4 text-[13px] sm:grid-cols-2">
              <div>
                <h2 className="mb-1 text-sm font-medium">{t('orders.form.customer')}</h2>
                <p>{order.customerName}</p>
                <p>{order.phone}</p>
              </div>
              <div>
                <h2 className="mb-1 text-sm font-medium">{t('orders.detail.address')}</h2>
                <p>{order.street}</p>
                <p>
                  {order.postalCode} {order.city}
                  {order.province ? ` (${order.province})` : ''} · {order.country}
                </p>
              </div>
              {order.notes && (
                <div className="sm:col-span-2">
                  <h2 className="mb-1 text-sm font-medium">{t('orders.form.notes')}</h2>
                  <p className="whitespace-pre-line">{order.notes}</p>
                </div>
              )}
              <p className="text-xs text-muted-foreground sm:col-span-2">
                {t('orders.detail.created', {
                  day: formatDay(order.createdAt, language),
                  time: formatTime(order.createdAt, language),
                })}
              </p>
            </Card>
          </>
        )}
      </div>
    </div>
  );
}
