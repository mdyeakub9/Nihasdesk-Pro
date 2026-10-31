import { Button, FieldError, Textarea, cn } from '@nihasdesk/ui';
import { Lock, Pin, PinOff } from 'lucide-react';
import { useId, useState, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { formatTime } from '../../lib/format';
import { authErrorMessage, isErrorCode } from '../auth/errors';
import { useMembers, type Member } from '../team/api';
import { useAddNote, useNotes, usePinNote } from './api';
import type { Message } from './types';

/** "@giu" right before the caret: the start of a mention. */
const mentionQuery = /(?:^|\s)@([^\s@]{0,30})$/u;

/**
 * Team notes in the side panel (PRD 4.4): add a note with @mentions, pin the important ones.
 * Notes are team-only: the API never sends them to the channel or the AI. Text is rendered as
 * plain text (SEC-02).
 */
export function TeamNotes({ conversationId }: { conversationId: string }) {
  const { t } = useTranslation();
  const { data: notes } = useNotes(conversationId);
  const add = useAddNote(conversationId);
  const { data: members = [] } = useMembers();
  const [text, setText] = useState('');
  const [mentioned, setMentioned] = useState<Member[]>([]);
  const [query, setQuery] = useState<string | null>(null);
  const [active, setActive] = useState(0);
  const listId = useId();

  const matches =
    query === null
      ? []
      : members.filter((m) => m.name.toLowerCase().includes(query.toLowerCase())).slice(0, 6);
  const open = matches.length > 0;

  function onChange(value: string, caret: number) {
    setText(value);
    const match = mentionQuery.exec(value.slice(0, caret));
    setQuery(match ? (match[1] ?? '') : null);
    setActive(0);
  }

  function pick(member: Member) {
    setText((current) => current.replace(/@([^\s@]{0,30})$/u, `@${member.name} `));
    setMentioned((current) =>
      current.some((m) => m.userId === member.userId) ? current : [...current, member],
    );
    setQuery(null);
  }

  function onKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (!open) return;
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      const step = event.key === 'ArrowDown' ? 1 : -1;
      setActive((current) => (current + step + matches.length) % matches.length);
    } else if (event.key === 'Enter' || event.key === 'Tab') {
      event.preventDefault();
      const member = matches[active];
      if (member) pick(member);
    } else if (event.key === 'Escape') {
      event.preventDefault();
      setQuery(null);
    }
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    const value = text.trim();
    if (!value || add.isPending) return;
    // Only people whose @name is still in the text.
    const mentionedUserIds = mentioned.filter((m) => value.includes(`@${m.name}`)).map((m) => m.userId);
    add.mutate(
      { text: value, mentionedUserIds },
      {
        onSuccess: () => {
          setText('');
          setMentioned([]);
        },
      },
    );
  }

  return (
    <div className="flex flex-col gap-3">
      {notes && notes.pinned.length > 0 && (
        <ul aria-label={t('notes.pinned')} className="flex flex-col gap-2">
          {notes.pinned.map((note) => (
            <NoteItem key={note.id} note={note} conversationId={conversationId} />
          ))}
        </ul>
      )}

      <form onSubmit={submit} className="relative flex flex-col gap-2">
        <Textarea
          aria-label={t('notes.add')}
          placeholder={t('notes.placeholder')}
          value={text}
          rows={2}
          aria-autocomplete="list"
          aria-expanded={open}
          aria-controls={open ? listId : undefined}
          aria-activedescendant={open ? `${listId}-${active}` : undefined}
          onChange={(event) => onChange(event.target.value, event.target.selectionStart)}
          onKeyDown={onKeyDown}
        />
        {open && (
          <ul
            id={listId}
            role="listbox"
            aria-label={t('notes.mention')}
            className="absolute top-full z-10 mt-1 w-full rounded-md border bg-surface py-1 shadow-md"
          >
            {matches.map((member, index) => (
              <li
                key={member.userId}
                id={`${listId}-${index}`}
                role="option"
                aria-selected={index === active}
                className={cn('cursor-pointer px-3 py-1.5 text-[13px]', index === active && 'bg-selected')}
                // Keep focus in the text box.
                onMouseDown={(event) => {
                  event.preventDefault();
                  pick(member);
                }}
              >
                {member.name}
              </li>
            ))}
          </ul>
        )}
        {add.error && <FieldError>{authErrorMessage(add.error, t)}</FieldError>}
        <div className="flex items-center justify-between gap-2">
          <span className="flex items-center gap-1 text-xs text-muted-foreground">
            <Lock className="size-3.5" aria-hidden="true" />
            {t('notes.teamOnly')}
          </span>
          <Button type="submit" size="sm" disabled={!text.trim() || add.isPending}>
            {t('notes.save')}
          </Button>
        </div>
      </form>

      {notes && notes.recent.length > 0 && (
        <ul aria-label={t('notes.recent')} className="flex flex-col gap-2">
          {notes.recent.map((note) => (
            <NoteItem key={note.id} note={note} conversationId={conversationId} />
          ))}
        </ul>
      )}
    </div>
  );
}

function NoteItem({ note, conversationId }: { note: Message; conversationId: string }) {
  const { t, i18n } = useTranslation();
  const pin = usePinNote(conversationId);
  const author = note.author === 'ai' ? t('message.aiName') : (note.authorName ?? t('message.team'));

  return (
    <li className="flex items-start gap-2 rounded-md border border-dashed bg-warning px-3 py-2">
      <div className="min-w-0 flex-1">
        <p className="whitespace-pre-wrap break-words text-[13px] text-foreground">{note.text}</p>
        <p className="mt-1 text-xs text-muted-foreground">
          {author} ·{' '}
          <time dateTime={note.sentAt}>{formatTime(note.sentAt, i18n.resolvedLanguage ?? 'en')}</time>
        </p>
        {pin.error && (
          <FieldError>
            {isErrorCode(pin.error, 'note.pin_limit') ? t('notes.pinLimit') : authErrorMessage(pin.error, t)}
          </FieldError>
        )}
      </div>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="size-7 shrink-0"
        aria-label={t(note.pinned ? 'notes.unpin' : 'notes.pin')}
        aria-pressed={!!note.pinned}
        disabled={pin.isPending}
        onClick={() => pin.mutate({ noteId: note.id, pinned: !note.pinned })}
      >
        {note.pinned ? <PinOff aria-hidden="true" /> : <Pin aria-hidden="true" />}
      </Button>
    </li>
  );
}
