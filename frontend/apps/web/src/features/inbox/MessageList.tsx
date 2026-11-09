import { Avatar, Button, cn } from '@nihasdesk/ui';
import { AlertCircle, CheckCheck, Clock, Lock, ShoppingBag, Sparkles } from 'lucide-react';
import { Fragment, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { formatDay, formatPrice, formatTime, isSameDay } from '../../lib/format';
import { useAiReplies, type RatedReply } from '../ai/api';
import { AiReplyTools } from '../ai/TracePanel';
import { useOlderMessages } from './api';
import { MessageMedia } from './MessageMedia';
import type { Conversation, Message, ProductCard } from './types';

function ProductMessage({ product }: { product: ProductCard }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';
  // As it was sent: catalog name and price at that moment (ADR 0024).
  const details = [
    product.variant,
    formatPrice(product.price.amount, product.price.currency, language),
    product.sku ? `${t('message.sku')} ${product.sku}` : null,
  ].filter(Boolean);
  return (
    <div
      role="group"
      aria-label={t('message.productCard', { name: product.name })}
      className="flex w-full max-w-90 flex-col gap-1 rounded-lg border bg-surface p-3"
    >
      <div className="flex items-center gap-2">
        <ShoppingBag className="size-4 text-secondary-foreground" aria-hidden="true" />
        <span className="flex-1 text-sm font-medium">{product.name}</span>
      </div>
      <p className="text-[13px] text-secondary-foreground">{details.join(' · ')}</p>
    </div>
  );
}

function MessageItem({
  message,
  contactName,
  conversationId,
  aiReply,
}: {
  message: Message;
  contactName: string;
  conversationId: string;
  /** Set for AI answers that have a trace: 👍/👎 and "Why this answer" (PRD 4.8). */
  aiReply?: RatedReply | undefined;
}) {
  const { t, i18n } = useTranslation();
  const time = formatTime(message.sentAt, i18n.resolvedLanguage ?? 'en');
  const fromCustomer = message.author === 'customer';

  if (fromCustomer) {
    return (
      <li className="flex items-start gap-3">
        <Avatar name={contactName} />
        <div className="flex max-w-100 flex-col gap-1 rounded-lg bg-subtle px-3 py-2">
          {message.attachments && <MessageMedia attachments={message.attachments} />}
          {/* A media-only message has no text (ADR 0031); old rows still read "[attachment]". */}
          {message.text && <p className="text-sm">{message.text}</p>}
          <time dateTime={message.sentAt} className="text-xs text-muted-foreground">
            {time}
          </time>
        </div>
      </li>
    );
  }

  const sender = message.author === 'ai' ? t('message.aiName') : (message.authorName ?? t('message.team'));

  if (message.internal) {
    // The AI's note to the team (e.g. why it handed over): team-only, never sent to the customer.
    // Team notes are not shown here; they live in the side panel's Notes section (ADR 0021).
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
      {!message.product && message.text && (
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
      {aiReply && (
        <AiReplyTools conversationId={conversationId} messageId={message.id} rating={aiReply.rating} />
      )}
    </li>
  );
}

export function MessageList({ conversation }: { conversation: Conversation }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';
  const endRef = useRef<HTMLLIElement>(null);
  const today = new Date().toISOString();
  const older = useOlderMessages(conversation.id);
  const aiReplies = useAiReplies(conversation.id);
  const rated = new Map(aiReplies.data?.map((reply) => [reply.messageId, reply]));
  // Team notes live in the side panel's Notes section only (ADR 0021); the AI's handoff notes stay.
  const messages = conversation.messages.filter((m) => !(m.internal && m.author === 'agent'));
  const lastId = messages.at(-1)?.id;

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
      {messages.map((message, index) => {
        const previous = messages[index - 1];
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
            <MessageItem
              message={message}
              contactName={conversation.contactName}
              conversationId={conversation.id}
              aiReply={rated.get(message.id)}
            />
          </Fragment>
        );
      })}
      <li ref={endRef} aria-hidden="true" />
    </ol>
  );
}
