import { Badge, Button, FieldError, Textarea, cn } from '@nihasdesk/ui';
import { CircleCheck, Lock, RotateCcw, StickyNote, Trash2 } from 'lucide-react';
import { useId, useState, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { formatTime } from '../../lib/format';
import { authErrorMessage } from '../auth/errors';
import { useSession } from '../session/useSession';
import { useMembers, type Member } from '../team/api';
import { useAddNote, useDeleteNote, useNotes, useResolveNote } from './api';
import type { Message } from './types';

/** Next to the "Team notes" title: a warning-coloured count of open notes, so nobody misses them. */
export function NoteCount({ conversationId }: { conversationId: string }) {
  const { t } = useTranslation();
  const { data: notes } = useNotes(conversationId);
  const count = notes?.items.filter((n) => !n.resolvedAt).length ?? 0;
  if (count === 0) return null;

  return (
    <Badge tone="warning">
      <StickyNote aria-hidden="true" />
      {t('notes.count', { count })}
    </Badge>
  );
}

/** "@giu" right before the caret: the start of a mention. */
const mentionQuery = /(?:^|\s)@([^\s@]{0,30})$/u;

/**
 * Team notes in the side panel's Notes section only (PRD 4.4, ADR 0021): add a note with
 * @mentions, resolve it when done, delete it. Notes are not shown in the chat. Notes are team-only: the API never sends them to the channel or the AI. Text is rendered as
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

  // Newest first; resolved notes are grouped under a collapsed "Resolved (n)".
  const openNotes = notes?.items.filter((n) => !n.resolvedAt) ?? [];
  const resolved = notes?.items.filter((n) => n.resolvedAt) ?? [];
  const nameOf = (userId?: string) => members.find((m) => m.userId === userId)?.name ?? t('message.team');

  return (
    <div className="flex flex-col gap-3">
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

      {openNotes.length > 0 && (
        <ul aria-label={t('notes.list')} className="flex flex-col gap-2">
          {openNotes.map((note) => (
            <NoteItem key={note.id} note={note} conversationId={conversationId} />
          ))}
        </ul>
      )}

      {resolved.length > 0 && (
        <details className="group">
          <summary className="cursor-pointer text-xs font-medium text-muted-foreground">
            {t('notes.resolvedCount', { count: resolved.length })}
          </summary>
          <ul aria-label={t('notes.resolvedList')} className="mt-2 flex flex-col gap-2">
            {resolved.map((note) => (
              <NoteItem
                key={note.id}
                note={note}
                conversationId={conversationId}
                resolvedBy={nameOf(note.resolvedByUserId)}
              />
            ))}
          </ul>
        </details>
      )}
    </div>
  );
}

/** One note: resolve it (dimmed, moved to "Resolved") or delete it for good. */
function NoteItem({
  note,
  conversationId,
  resolvedBy,
}: {
  note: Message;
  conversationId: string;
  resolvedBy?: string;
}) {
  const { t, i18n } = useTranslation();
  const { data: session } = useSession();
  const resolve = useResolveNote(conversationId);
  const remove = useDeleteNote(conversationId);
  const [confirming, setConfirming] = useState(false);
  const author = note.author === 'ai' ? t('message.aiName') : (note.authorName ?? t('message.team'));
  const isResolved = !!note.resolvedAt;
  // Only hides the button; the API checks the same rule (SEC-10).
  const canDelete =
    note.authorUserId === session?.user.id || session?.role === 'owner' || session?.role === 'admin';
  const error = resolve.error ?? remove.error;

  return (
    <li
      className={cn(
        'flex items-start gap-2 rounded-md border border-dashed bg-warning px-3 py-2',
        isResolved && 'opacity-60',
      )}
    >
      <div className="min-w-0 flex-1">
        <p className="whitespace-pre-wrap break-words text-[13px] text-foreground">{note.text}</p>
        <p className="mt-1 text-xs text-muted-foreground">
          {author} ·{' '}
          <time dateTime={note.sentAt}>{formatTime(note.sentAt, i18n.resolvedLanguage ?? 'en')}</time>
          {isResolved && resolvedBy && <> · {t('notes.resolvedBy', { name: resolvedBy })}</>}
        </p>
        {confirming && (
          <div className="mt-2 flex items-center gap-2 text-xs">
            <span>{t('notes.deleteConfirm')}</span>
            <Button
              type="button"
              size="sm"
              variant="destructive"
              disabled={remove.isPending}
              onClick={() => remove.mutate(note.id)}
            >
              {t('notes.delete')}
            </Button>
            <Button type="button" size="sm" variant="ghost" onClick={() => setConfirming(false)}>
              {t('notes.cancel')}
            </Button>
          </div>
        )}
        {error && <FieldError>{authErrorMessage(error, t)}</FieldError>}
      </div>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="size-7 shrink-0"
        aria-label={t(isResolved ? 'notes.reopen' : 'notes.resolve')}
        aria-pressed={isResolved}
        disabled={resolve.isPending}
        onClick={() => resolve.mutate({ noteId: note.id, resolved: !isResolved })}
      >
        {isResolved ? <RotateCcw aria-hidden="true" /> : <CircleCheck aria-hidden="true" />}
      </Button>
      {canDelete && (
        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="size-7 shrink-0"
          aria-label={t('notes.deleteNote')}
          aria-expanded={confirming}
          onClick={() => setConfirming(true)}
        >
          <Trash2 aria-hidden="true" />
        </Button>
      )}
    </li>
  );
}
