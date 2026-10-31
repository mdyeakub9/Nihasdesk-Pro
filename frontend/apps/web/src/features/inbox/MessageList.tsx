import { Avatar, Badge, Button, cn } from '@nihasdesk/ui';
import { AlertCircle, CheckCheck, Clock, ExternalLink, Lock, Pin, ShoppingBag, Sparkles } from 'lucide-react';
import { Fragment, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { formatDay, formatPrice, formatTime, isSameDay } from '../../lib/format';
import { useOlderMessages } from './api';
import type { Conversation, Message, ProductCard } from './types';

function ProductMessage({ product }: { product: ProductCard }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';
  return (
    <div className="flex w-full max-w-90 flex-col gap-2 rounded-lg border bg-surface p-3">
      <div className="flex items-center gap-2">
        <ShoppingBag className="size-4 text-secondary-foreground" aria-hidden="true" />
        <span className="flex-1 text-sm font-medium">{product.name}</span>
        <Badge tone={product.inStock ? 'success' : 'warning'}>
          {t(product.inStock ? 'message.inStock' : 'message.outOfStock')}
        </Badge>
      </div>
      <p className="text-[13px] text-secondary-foreground">
        {product.variant} · {formatPrice(product.price.amount, product.price.currency, language)} ·{' '}
        {t('message.sku')} {product.sku}
      </p>
      <div>
        <Button size="sm" variant="outline" disabled title={t('comingSoon.short')}>
          <ExternalLink aria-hidden="true" />
          {t('message.viewDetails')}
        </Button>
      </div>
    </div>
  );
}

function MessageItem({ message, contactName }: { message: Message; contactName: string }) {
  const { t, i18n } = useTranslation();
  const time = formatTime(message.sentAt, i18n.resolvedLanguage ?? 'en');
  const fromCustomer = message.author === 'customer';

  if (fromCustomer) {
    return (
      <li className="flex items-start gap-3">
        <Avatar name={contactName} />
        <div className="max-w-100 rounded-lg bg-subtle px-3 py-2">
          <p className="text-sm">{message.text}</p>
          <time dateTime={message.sentAt} className="text-xs text-muted-foreground">
            {time}
          </time>
        </div>
      </li>
    );
  }

  const sender = message.author === 'ai' ? t('message.aiName') : (message.authorName ?? t('message.team'));

  if (message.internal) {
    // Team-only note (PRD 4.4): a distinct bubble, never sent to the customer or the AI.
    return (
      <li className="flex flex-col items-end gap-1">
        <div
          role="note"
          aria-label={t('message.internalNote')}
          className="max-w-125 rounded-lg border border-dashed bg-warning px-3 py-2"
        >
          <p className="flex items-center gap-1 text-xs font-medium text-warning-foreground">
            <Lock className="size-3.5" aria-hidden="true" />
            {t('message.teamOnly')}
            {message.pinned && (
              <>
                {' '}
                · <Pin className="size-3.5" aria-hidden="true" />
                {t('message.pinned')}
              </>
            )}
          </p>
          <p className="whitespace-pre-wrap break-words text-sm">{message.text}</p>
        </div>
        <p className="flex items-center gap-1 text-xs text-muted-foreground">
          {message.author === 'ai' && <Sparkles className="size-3.5" aria-hidden="true" />}
          {sender} · <time dateTime={message.sentAt}>{time}</time>
        </p>
      </li>
    );
  }

  return (
    <li className="flex flex-col items-end gap-1">
      {message.product && <ProductMessage product={message.product} />}
      {message.text && (
        <div className="max-w-125 rounded-lg bg-selected px-3 py-2">
          <p className="text-sm">{message.text}</p>
        </div>
      )}
      <p className="flex items-center gap-1 text-xs text-muted-foreground">
        {message.author === 'ai' && <Sparkles className="size-3.5" aria-hidden="true" />}
        {sender} · <time dateTime={message.sentAt}>{time}</time>
        {message.delivery && (
          <>
            {' '}
            ·{' '}
            <span className={cn(message.delivery === 'failed' && 'font-medium text-destructive')}>
              {t(`message.delivery.${message.delivery}`)}
            </span>
            {message.delivery === 'failed' ? (
              <AlertCircle className="size-3.5 text-destructive" aria-hidden="true" />
            ) : message.delivery === 'pending' ? (
              <Clock className="size-3.5" aria-hidden="true" />
            ) : (
              <CheckCheck className="size-3.5" aria-hidden="true" />
            )}
          </>
        )}
      </p>
    </li>
  );
}

export function MessageList({ conversation }: { conversation: Conversation }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';
  const endRef = useRef<HTMLLIElement>(null);
  const today = new Date().toISOString();
  const older = useOlderMessages(conversation.id);
  const lastId = conversation.messages.at(-1)?.id;

  // Follow new messages at the bottom; loading older ones above doesn't jump.
  useEffect(() => {
    endRef.current?.scrollIntoView?.({ block: 'end' });
  }, [lastId]);

  return (
    <ol aria-label={t('message.history')} className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto p-6">
      {conversation.olderCursor && (
        <li className="flex justify-center">
          <Button
            size="sm"
            variant="ghost"
            disabled={older.isPending}
            onClick={() => conversation.olderCursor && older.mutate(conversation.olderCursor)}
          >
            {t('message.loadOlder')}
          </Button>
        </li>
      )}
      {conversation.messages.map((message, index) => {
        const previous = conversation.messages[index - 1];
        const newDay = !previous || !isSameDay(previous.sentAt, message.sentAt);
        return (
          <Fragment key={message.id}>
            {newDay && (
              <li className="text-center text-xs text-muted-foreground">
                {isSameDay(message.sentAt, today)
                  ? t('message.today', { date: formatDay(message.sentAt, language) })
                  : formatDay(message.sentAt, language)}
              </li>
            )}
            <MessageItem message={message} contactName={conversation.contactName} />
          </Fragment>
        );
      })}
      <li ref={endRef} aria-hidden="true" />
    </ol>
  );
}
