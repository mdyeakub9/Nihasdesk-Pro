import { Button, cn } from '@nihasdesk/ui';
import { Sparkles, UserCheck } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useMembers } from '../team/api';
import { useUpdateConversation } from './api';
import type { Conversation } from './types';

/**
 * Who answers the customer right now (PRD 4.4): "AI" or the team member's name. One click takes
 * over or returns the conversation to the AI. Replaces the "AI is answering" banner.
 */
export function ResponderChip({ conversation }: { conversation: Conversation }) {
  const { t } = useTranslation();
  const update = useUpdateConversation(conversation.id);
  const { data: members } = useMembers();
  const ai = conversation.responder === 'ai';
  const person =
    members?.find((m) => m.userId === conversation.assigneeId)?.name ?? t('conversation.responder.team');
  const blocked = !ai && !conversation.aiEnabled;

  return (
    <Button
      size="sm"
      variant="outline"
      className={cn('rounded-full', ai && 'bg-selected text-selected-foreground')}
      disabled={update.isPending || blocked}
      title={blocked ? t('conversation.aiOffHint') : undefined}
      aria-label={
        ai ? t('conversation.responder.aiAction') : t('conversation.responder.personAction', { name: person })
      }
      onClick={() => update.mutate({ responder: ai ? 'human' : 'ai' })}
    >
      {ai ? <Sparkles aria-hidden="true" /> : <UserCheck aria-hidden="true" />}
      {ai ? t('conversation.responder.ai') : person}
    </Button>
  );
}
