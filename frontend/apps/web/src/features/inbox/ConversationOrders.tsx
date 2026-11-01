import { Badge } from '@nihasdesk/ui';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { formatMoney } from '../catalog/money';
import { useConversationOrders } from '../orders/api';
import { statusTone } from '../orders/status';

/** This chat's orders in the customer panel (PRD 4.9), each linking to its page. */
export function ConversationOrders({ conversationId }: { conversationId: string }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';
  const { data: orders = [] } = useConversationOrders(conversationId);

  if (orders.length === 0) return <p>{t('customer.ordersHint')}</p>;

  return (
    <ul aria-label={t('customer.orders')} className="flex flex-col gap-1.5">
      {orders.map((order) => (
        <li key={order.id} className="flex items-center gap-2">
          <Link to={`/orders/${order.id}`} className="font-medium text-foreground hover:underline">
            #{order.number}
          </Link>
          <Badge tone={statusTone[order.status]}>{t(`orders.statuses.${order.status}`)}</Badge>
          <span className="flex-1 text-right">{formatMoney(order.total, order.currency, language)}</span>
        </li>
      ))}
    </ul>
  );
}

/** "2 orders" next to the title. */
export function ConversationOrderCount({ conversationId }: { conversationId: string }) {
  const { t } = useTranslation();
  const { data: orders = [] } = useConversationOrders(conversationId);
  return <>{t('customer.orderCount', { count: orders.length })}</>;
}
