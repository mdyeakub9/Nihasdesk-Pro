import { Button, Card } from '@nihasdesk/ui';
import { useEffect, useId, useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useCreateOrder, type Order } from '../orders/api';
import { OrderForm } from '../orders/OrderForm';
import type { Conversation } from './types';

/**
 * Create order from the chat (PRD 4.4, 4.9): name and phone from the contact, products, address and
 * zone; the total comes from the server. A person creates it, so it is a confirmed order and takes
 * the stock. The AI only creates order requests (CLAUDE.md rule 5).
 */
export function CreateOrderModal({
  conversation,
  onClose,
}: {
  conversation: Conversation;
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

  // Rendered on <body>: it is opened from the composer, and a <form> inside the composer's <form>
  // would make the browser submit (reload the page) instead of saving the order.
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
            <p>{t('actions.order.created', { number: created.number })}</p>
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
              conversationId: conversation.id,
              customerName: conversation.contact.name,
              phone: conversation.contact.phone ?? '',
              street: '',
              postalCode: '',
              city: conversation.contact.city ?? '',
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
