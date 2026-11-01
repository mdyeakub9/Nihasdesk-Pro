import { Button } from '@nihasdesk/ui';
import { ShoppingBag, X } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useOrderRequestStore } from './requests';

/** Tells the team a new AI order request waits for approval (PRD 4.9). */
export function OrderRequestAlert() {
  const { t } = useTranslation();
  const latest = useOrderRequestStore((state) => state.latest);
  const dismiss = useOrderRequestStore((state) => state.dismiss);

  return (
    <div role="status" aria-live="polite" className="fixed right-4 bottom-20 z-50">
      {latest && (
        <div className="flex items-center gap-3 rounded-lg border bg-surface px-4 py-3 text-[13px] shadow-lg">
          <ShoppingBag className="size-4 shrink-0 text-secondary-foreground" aria-hidden="true" />
          <span>{t('orders.alert.text')}</span>
          <Button asChild size="sm" variant="outline" onClick={dismiss}>
            <Link to={`/orders/${encodeURIComponent(latest.orderId)}`}>{t('orders.alert.open')}</Link>
          </Button>
          <Button
            variant="ghost"
            size="icon"
            className="size-7"
            aria-label={t('orders.alert.dismiss')}
            onClick={dismiss}
          >
            <X aria-hidden="true" />
          </Button>
        </div>
      )}
    </div>
  );
}
