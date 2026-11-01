import { Button, Card, Input, Label } from '@nihasdesk/ui';
import { useEffect, useId } from 'react';
import { useTranslation } from 'react-i18next';
import type { Conversation } from './types';

/**
 * Create order (PRD 4.4): name and phone from the chat, the rest with the Orders module (4.9).
 * Placeholder for now: nothing is saved. A team-created order will be a confirmed order; the AI
 * still only creates order requests (CLAUDE.md rule 5).
 */
export function CreateOrderModal({
  conversation,
  onClose,
}: {
  conversation: Conversation;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const ids = { title: useId(), name: useId(), phone: useId() };

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-foreground/40 p-4">
      <Card
        role="dialog"
        aria-modal="true"
        aria-labelledby={ids.title}
        className="flex w-full max-w-md flex-col gap-4"
      >
        <h2 id={ids.title} className="text-base font-semibold">
          {t('actions.order.title')}
        </h2>
        <div className="flex flex-col gap-1">
          <Label htmlFor={ids.name}>{t('actions.order.name')}</Label>
          <Input id={ids.name} className="h-9" readOnly value={conversation.contact.name} />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor={ids.phone}>{t('actions.order.phone')}</Label>
          <Input id={ids.phone} className="h-9" readOnly value={conversation.contact.phone ?? ''} />
        </div>
        <p role="note" className="rounded-md bg-subtle px-3 py-2 text-[13px] text-secondary-foreground">
          {t('actions.order.comingSoon')}
        </p>
        <div className="flex justify-end">
          <Button type="button" variant="outline" autoFocus onClick={onClose}>
            {t('actions.close')}
          </Button>
        </div>
      </Card>
    </div>
  );
}
