import { Button } from '@nihasdesk/ui';
import { AtSign, X } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useMentionStore } from './mentions';

/** Tells this member someone @mentioned them in a team note (PRD 4.4). */
export function MentionAlert() {
  const { t } = useTranslation();
  const latest = useMentionStore((state) => state.latest);
  const dismiss = useMentionStore((state) => state.dismiss);

  return (
    <div role="status" aria-live="polite" className="fixed right-4 bottom-4 z-50">
      {latest && (
        <div className="flex items-center gap-3 rounded-lg border bg-surface px-4 py-3 text-[13px] shadow-lg">
          <AtSign className="size-4 shrink-0 text-secondary-foreground" aria-hidden="true" />
          <span>{t('notes.mentionedYou')}</span>
          <Button asChild size="sm" variant="outline" onClick={dismiss}>
            <Link to={`/inbox/${encodeURIComponent(latest.conversationId)}`}>
              {t('notes.openConversation')}
            </Link>
          </Button>
          <Button
            variant="ghost"
            size="icon"
            className="size-7"
            aria-label={t('notes.dismiss')}
            onClick={dismiss}
          >
            <X aria-hidden="true" />
          </Button>
        </div>
      )}
    </div>
  );
}
