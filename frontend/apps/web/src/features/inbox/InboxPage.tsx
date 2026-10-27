import { cn } from '@nihasdesk/ui';
import { Inbox } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { ConversationList } from './ConversationList';
import { ConversationView } from './ConversationView';

export function InboxPage() {
  const { t } = useTranslation();
  const { conversationId } = useParams();

  return (
    <div className="flex min-h-0 flex-1">
      {/* On narrow screens the list and the open conversation take turns. */}
      <div className={cn('w-full shrink-0 border-r lg:block lg:w-90', conversationId ? 'hidden' : 'block')}>
        <ConversationList selectedId={conversationId} />
      </div>
      <div className={cn('min-w-0 flex-1', conversationId ? 'flex' : 'hidden lg:flex')}>
        {conversationId ? (
          <ConversationView key={conversationId} id={conversationId} />
        ) : (
          <div className="flex flex-1 flex-col items-center justify-center gap-2 bg-surface p-8 text-center">
            <Inbox className="size-8 text-muted-foreground" strokeWidth={1.5} aria-hidden="true" />
            <h2 className="text-base font-semibold">{t('inbox.empty.title')}</h2>
            <p className="max-w-sm text-[13px] text-secondary-foreground">{t('inbox.empty.text')}</p>
          </div>
        )}
      </div>
    </div>
  );
}
