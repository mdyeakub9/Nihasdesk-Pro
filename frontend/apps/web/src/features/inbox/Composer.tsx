import { Button, FieldError, Textarea } from '@nihasdesk/ui';
import { Send } from 'lucide-react';
import { useState, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { authErrorMessage, isErrorCode } from '../auth/errors';
import { useSendMessage } from './api';
import type { Conversation } from './types';

/**
 * Replies to the customer (PRD 4.4). Team notes are written from the side panel; the channel is
 * shown in the header. While the AI answers, sending takes the conversation over.
 */
export function Composer({ conversation }: { conversation: Conversation }) {
  const { t } = useTranslation();
  const send = useSendMessage(conversation.id);
  const [text, setText] = useState('');
  const takesOver = conversation.responder === 'ai';

  function submit(event?: FormEvent) {
    event?.preventDefault();
    const value = text.trim();
    if (!value || send.isPending) return;
    send.mutate(value, { onSuccess: () => setText('') });
  }

  function onKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      submit();
    }
  }

  return (
    <form onSubmit={submit} className="flex flex-col gap-3 border-t p-4">
      <Textarea
        aria-label={t('composer.reply')}
        placeholder={t('composer.replyPlaceholder')}
        value={text}
        onChange={(event) => setText(event.target.value)}
        onKeyDown={onKeyDown}
        rows={2}
      />
      {send.error && (
        <FieldError>
          {isErrorCode(send.error, 'conversation.outside_window')
            ? t('composer.outsideWindow')
            : authErrorMessage(send.error, t)}
        </FieldError>
      )}
      <div className="flex items-center justify-end gap-3">
        <span className="text-xs text-muted-foreground">{t('composer.enterToSend')}</span>
        <Button type="submit" disabled={!text.trim() || send.isPending}>
          <Send aria-hidden="true" />
          {t(takesOver ? 'composer.sendAndTakeOver' : 'composer.send')}
        </Button>
      </div>
    </form>
  );
}
