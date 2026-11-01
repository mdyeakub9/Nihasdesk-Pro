import { Button, FieldError, Textarea } from '@nihasdesk/ui';
import { Send } from 'lucide-react';
import { useId, useRef, useState, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { authErrorMessage, isErrorCode } from '../auth/errors';
import { fillSavedReply, matchSavedReplies, useSavedReplies, type SavedReply } from '../saved-replies/api';
import { useSession } from '../session/useSession';
import { useMembers } from '../team/api';
import { useSendMessage } from './api';
import { ActionsMenu, type Action } from './ActionsMenu';
import { CreateOrderModal } from './CreateOrderModal';
import { useDraftStore } from './drafts';
import { ProductPicker } from './ProductPicker';
import { SaveAsKnowledge } from './SaveAsKnowledge';
import { SavedReplyPicker } from './SavedReplyPicker';
import { useSuggestionStore } from './suggestions';
import { useSendTyping, useTypingMembers } from './typing';
import type { Conversation } from './types';

/** "/ship" right before the caret, at the start or after a space: a saved-reply command. */
const slashCommand = /(?:^|\s)\/([a-z0-9-]{0,30})$/i;

/**
 * Replies to the customer (PRD 4.4): one Send for everything ("Send & take over" while the AI
 * answers), a draft kept per conversation, `/` for saved replies (inserted, never sent), and
 * "X is typing…". Ctrl+Enter sends; Enter is a new line. Team notes are in the side panel.
 */
export function Composer({ conversation }: { conversation: Conversation }) {
  const { t } = useTranslation();
  const send = useSendMessage(conversation.id);
  const { data: session } = useSession();
  const { data: members = [] } = useMembers();
  const { data: savedReplies = [] } = useSavedReplies();
  const text = useDraftStore((state) => state.drafts[conversation.id] ?? '');
  const setDraft = useDraftStore((state) => state.setDraft);
  const sendTyping = useSendTyping(conversation.id);
  const typing = useTypingMembers(conversation.id, session?.user.id);
  const [query, setQuery] = useState<string | null>(null);
  const [panel, setPanel] = useState<'products' | 'order' | null>(null);
  const [active, setActive] = useState(0);
  const box = useRef<HTMLTextAreaElement>(null);
  const listId = useId();
  const takesOver = conversation.responder === 'ai';
  const inserted = useSuggestionStore((state) => state.inserted[conversation.id]);
  const setInserted = useSuggestionStore((state) => state.setInserted);
  const setOffer = useSuggestionStore((state) => state.setOffer);
  // A new offer starts the form fresh.
  const offerKey = useSuggestionStore((state) => state.offer?.answer ?? 'none');
  // Teaching the AI is Owner/Admin work (`ai.teach`); the API checks it too.
  const canTeach = session?.role === 'owner' || session?.role === 'admin';

  const matches = query === null ? [] : matchSavedReplies(savedReplies, query);
  const open = matches.length > 0;
  const typingNames = typing.map((id) => members.find((m) => m.userId === id)?.name ?? t('message.team'));

  function change(value: string, caret: number) {
    setDraft(conversation.id, value);
    const match = slashCommand.exec(value.slice(0, caret));
    setQuery(match ? (match[1] ?? '') : null);
    setActive(0);
    if (value.trim()) sendTyping();
  }

  /** The + menu (PRD 4.4). "Saved reply" starts a `/` command, like typing it. */
  function pick(action: Action) {
    if (action === 'savedReply') {
      const next = text && !/\s$/.test(text) ? `${text} /` : `${text}/`;
      setDraft(conversation.id, next);
      setQuery('');
      setActive(0);
      requestAnimationFrame(() => box.current?.setSelectionRange(next.length, next.length));
      box.current?.focus();
      return;
    }
    setPanel(action);
  }

  function insert(reply: SavedReply) {
    const filled = fillSavedReply(reply, {
      language: conversation.contact.language,
      firstName: conversation.contact.name.split(' ')[0] ?? '',
      shopName: session?.workspace.name ?? '',
    });
    const caret = box.current?.selectionStart ?? text.length;
    const before = text.slice(0, caret).replace(/\/[a-z0-9-]{0,30}$/i, '');
    setDraft(conversation.id, before + filled + text.slice(caret));
    setQuery(null);
    box.current?.focus();
  }

  function submit(event?: FormEvent) {
    event?.preventDefault();
    const value = text.trim();
    if (!value || send.isPending) return;
    send.mutate(value, {
      onSuccess: () => {
        setDraft(conversation.id, '');
        offerKnowledge(value);
      },
    });
  }

  /** An AI suggestion was inserted and then edited: Owner/Admin may keep the answer (ADR 0023). */
  function offerKnowledge(sent: string) {
    const suggestion = inserted;
    setInserted(conversation.id, null);
    if (!suggestion || sent === suggestion.trim() || !canTeach) return;
    const question = conversation.messages.findLast((m) => m.author === 'customer')?.text ?? '';
    setOffer({ conversationId: conversation.id, question, answer: sent });
  }

  function onKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (open) {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        const step = event.key === 'ArrowDown' ? 1 : -1;
        setActive((current) => (current + step + matches.length) % matches.length);
        return;
      }
      if ((event.key === 'Enter' && !event.ctrlKey && !event.metaKey) || event.key === 'Tab') {
        event.preventDefault();
        const reply = matches[active];
        if (reply) insert(reply);
        return;
      }
      if (event.key === 'Escape') {
        event.preventDefault();
        setQuery(null);
        return;
      }
    }
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      submit();
    }
  }

  return (
    <form onSubmit={submit} className="flex flex-col gap-3 border-t p-4">
      <SaveAsKnowledge key={offerKey} conversationId={conversation.id} />
      {panel === 'products' && (
        <ProductPicker conversationId={conversation.id} onClose={() => setPanel(null)} />
      )}
      {panel === 'order' && <CreateOrderModal conversation={conversation} onClose={() => setPanel(null)} />}
      <div className="relative">
        {open && <SavedReplyPicker id={listId} matches={matches} active={active} onPick={insert} />}
        <Textarea
          ref={box}
          aria-label={t('composer.reply')}
          placeholder={t('composer.replyPlaceholder')}
          value={text}
          aria-autocomplete="list"
          aria-expanded={open}
          aria-controls={open ? listId : undefined}
          aria-activedescendant={open ? `${listId}-${active}` : undefined}
          onChange={(event) => change(event.target.value, event.target.selectionStart)}
          onKeyDown={onKeyDown}
          rows={2}
        />
      </div>
      {send.error && (
        <FieldError>
          {isErrorCode(send.error, 'conversation.outside_window')
            ? t('composer.outsideWindow')
            : authErrorMessage(send.error, t)}
        </FieldError>
      )}
      <div className="flex items-center justify-end gap-3">
        <ActionsMenu onPick={pick} />
        <p aria-live="polite" className="flex-1 text-xs text-muted-foreground">
          {typingNames.length > 0 && t('composer.typing', { names: typingNames.join(', ') })}
        </p>
        <span className="text-xs text-muted-foreground">{t('composer.shortcuts')}</span>
        <Button type="submit" disabled={!text.trim() || send.isPending}>
          <Send aria-hidden="true" />
          {t(takesOver ? 'composer.sendAndTakeOver' : 'composer.send')}
        </Button>
      </div>
    </form>
  );
}
