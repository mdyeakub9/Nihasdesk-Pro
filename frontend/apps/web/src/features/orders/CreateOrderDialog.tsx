import { Button, Card } from '@nihasdesk/ui';
import { useEffect, useId, useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useCreateOrder, type Order } from './api';
import { OrderForm } from './OrderForm';

/** What is known before the form opens: the chat's contact, or nothing for an order taken by phone. */
export type OrderPrefill = {
  conversationId: string | null;
  customerName: string;
  phone: string;
  city: string;
};

const empty: OrderPrefill = { conversationId: null, customerName: '', phone: '', city: '' };

/**
 * Create a team order (PRD 4.4, 4.9), from a chat or from the Orders page: products, address and
 * zone; the total comes from the server. A person creates it, so it is confirmed and takes the
 * stock; an order from a chat sends the customer its summary there. The AI only creates requests.
 */
export function CreateOrderDialog({
  prefill = empty,
  onClose,
}: {
  prefill?: OrderPrefill;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const create = useCreateOrder();
  const [created, setCreated] = useState<Order | null>(null);
  const titleId = useId();

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  // Rendered on <body>: it can be opened from the composer, and a <form> inside the composer's
  // <form> would make the browser submit (reload the page) instead of saving the order.
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-foreground/40 p-4">
      <Card
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="my-8 flex w-full max-w-xl flex-col gap-4"
      >
        <h2 id={titleId} className="text-base font-semibold">
          {t('actions.order.title')}
        </h2>
        {created ? (
          <div role="status" className="flex flex-col gap-3 text-[13px]">
            <p>
              {t('actions.order.created', { number: created.number })}
              {created.conversationId && <> {t('actions.order.summarySent')}</>}
            </p>
            <div className="flex justify-end gap-2">
              <Button asChild variant="outline">
                <Link to={`/orders/${created.id}`}>{t('actions.order.open')}</Link>
              </Button>
              <Button onClick={onClose}>{t('actions.close')}</Button>
            </div>
          </div>
        ) : (
          <OrderForm
            initial={{
              conversationId: prefill.conversationId,
              customerName: prefill.customerName,
              phone: prefill.phone,
              street: '',
              postalCode: '',
              city: prefill.city,
              province: null,
              country: 'IT',
              shippingZoneId: '',
              notes: null,
            }}
            initialLines={[]}
            submitLabel={t('actions.order.create')}
            pending={create.isPending}
            error={create.error}
            onSubmit={(input) => create.mutate(input, { onSuccess: setCreated })}
            onCancel={onClose}
          />
        )}
      </Card>
    </div>,
    document.body,
  );
}
