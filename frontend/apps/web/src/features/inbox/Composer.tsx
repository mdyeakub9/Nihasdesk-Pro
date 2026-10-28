import { Button, FieldError, Tabs, TabsList, TabsTrigger, Textarea } from '@nihasdesk/ui';
import { Send, Sparkles } from 'lucide-react';
import { useState, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { ChannelIcon } from '../../components/ChannelIcon';
import { authErrorMessage, isErrorCode } from '../auth/errors';
import { useSendMessage } from './api';
import type { Conversation } from './types';

type Mode = 'reply' | 'note';

export function Composer({ conversation }: { conversation: Conversation }) {
  const { t } = useTranslation();
  const send = useSendMessage(conversation.id);
  const [mode, setMode] = useState<Mode>('reply');
  const [text, setText] = useState('');

  function submit(event?: FormEvent) {
    event?.preventDefault();
    const value = text.trim();
    if (!value || send.isPending) return;
    send.mutate({ text: value, internal: mode === 'note' }, { onSuccess: () => setText('') });
  }

  function onKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      submit();
    }
  }

  return (
    <div className="flex flex-col gap-3 border-t p-4">
      {conversation.responder === 'ai' && mode === 'reply' && (
        <p className="flex items-center gap-2 rounded-md bg-selected px-3 py-2 text-[13px] text-selected-foreground">
          <Sparkles className="size-4 shrink-0" aria-hidden="true" />
          {t('composer.aiPreparing')}
        </p>
      )}
      <form onSubmit={submit} className="flex flex-col gap-3">
        <div className="flex items-center gap-2">
          <Tabs
            value={mode}
            onValueChange={(value) => {
              setMode(value as Mode);
              send.reset();
            }}
          >
            <TabsList aria-label={t('composer.mode')}>
              <TabsTrigger value="reply">{t('composer.reply')}</TabsTrigger>
              <TabsTrigger value="note">{t('composer.note')}</TabsTrigger>
            </TabsList>
          </Tabs>
          <span className="flex-1" />
          {mode === 'reply' && (
            <span className="flex items-center gap-1.5 text-xs text-secondary-foreground">
              <ChannelIcon channel={conversation.channel} />
              {t(`channels.${conversation.channel}`)}
            </span>
          )}
        </div>
        <Textarea
          aria-label={t(mode === 'reply' ? 'composer.reply' : 'composer.note')}
          placeholder={t(mode === 'reply' ? 'composer.replyPlaceholder' : 'composer.notePlaceholder')}
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
            {t(mode === 'reply' ? 'composer.send' : 'composer.saveNote')}
          </Button>
        </div>
      </form>
    </div>
  );
}
