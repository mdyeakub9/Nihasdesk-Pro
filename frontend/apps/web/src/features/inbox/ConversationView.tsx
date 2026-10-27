import { Avatar, Button } from '@nihasdesk/ui';
import { ArrowLeft, Check, RotateCcw, Sparkles, UserCheck, UserPlus, X } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { ChannelIcon } from '../../components/ChannelIcon';
import { useConversation, useUpdateConversation } from './api';
import { Composer } from './Composer';
import { CustomerPanel } from './CustomerPanel';
import { MessageList } from './MessageList';

export function ConversationView({ id }: { id: string }) {
  const { t } = useTranslation();
  const { data: conversation, isPending } = useConversation(id);
  const update = useUpdateConversation(id);

  if (isPending) {
    return <div className="flex-1 bg-surface" aria-busy="true" />;
  }

  if (!conversation) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-3 bg-surface p-8 text-center">
        <h2 className="text-base font-semibold">{t('conversation.notFound')}</h2>
        <Button asChild variant="outline" size="sm">
          <Link to="/inbox">{t('conversation.backToInbox')}</Link>
        </Button>
      </div>
    );
  }

  const done = conversation.status === 'done';
  const aiAnswering = conversation.responder === 'ai';

  return (
    <div className="flex min-w-0 flex-1 flex-col">
      <header className="flex min-h-16 flex-wrap items-center gap-3 border-b bg-surface px-4 py-2">
        <Button asChild variant="ghost" size="icon" className="lg:hidden">
          <Link to="/inbox" aria-label={t('conversation.backToInbox')}>
            <ArrowLeft aria-hidden="true" />
          </Link>
        </Button>
        <Avatar name={conversation.contactName} size="md" />
        <div className="flex min-w-0 flex-1 flex-col">
          <h2 className="truncate text-base font-semibold">{conversation.contactName}</h2>
          <p className="flex items-center gap-1.5 text-xs text-secondary-foreground">
            <ChannelIcon channel={conversation.channel} />
            {t(`channels.${conversation.channel}`)} ·{' '}
            {t(aiAnswering ? 'conversation.aiAnswering' : 'conversation.teamAnswering')}
            {conversation.handoffCount > 0 && (
              <> · {t('conversation.handoffs', { count: conversation.handoffCount })}</>
            )}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button
            size="sm"
            variant={done ? 'outline' : 'default'}
            disabled={update.isPending}
            onClick={() => update.mutate({ status: done ? 'open' : 'done' })}
          >
            {done ? <RotateCcw aria-hidden="true" /> : <Check aria-hidden="true" />}
            {t(done ? 'conversation.reopen' : 'conversation.done')}
          </Button>
          {aiAnswering ? (
            <Button
              size="sm"
              variant="outline"
              disabled={update.isPending}
              onClick={() => update.mutate({ responder: 'human' })}
            >
              <UserCheck aria-hidden="true" />
              {t('conversation.takeOver')}
            </Button>
          ) : (
            <Button
              size="sm"
              variant="outline"
              disabled={update.isPending || !conversation.aiEnabled}
              title={conversation.aiEnabled ? undefined : t('conversation.aiOffHint')}
              onClick={() => update.mutate({ responder: 'ai' })}
            >
              <Sparkles aria-hidden="true" />
              {t('conversation.returnToAi')}
            </Button>
          )}
          <Button size="sm" variant="outline" disabled title={t('comingSoon.short')}>
            <UserPlus aria-hidden="true" />
            {t('conversation.assign')}
          </Button>
          <Button asChild variant="outline" size="icon" className="hidden lg:inline-flex">
            <Link to="/inbox" aria-label={t('conversation.close')}>
              <X aria-hidden="true" />
            </Link>
          </Button>
        </div>
      </header>
      <div className="flex min-h-0 flex-1">
        <div className="flex min-w-0 flex-1 flex-col bg-surface">
          <MessageList conversation={conversation} />
          <Composer conversation={conversation} />
        </div>
        <aside
          aria-label={t('customer.title')}
          className="hidden w-80 shrink-0 overflow-y-auto border-l bg-surface xl:block"
        >
          <CustomerPanel conversation={conversation} />
        </aside>
      </div>
    </div>
  );
}
