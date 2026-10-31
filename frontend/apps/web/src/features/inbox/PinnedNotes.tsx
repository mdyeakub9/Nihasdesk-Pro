import { Pin } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useNotes } from './api';
import { NoteItem } from './TeamNotes';

/** Pinned team notes, right above the composer (PRD 4.4), e.g. "VIP, call after 6 pm". */
export function PinnedNotes({ conversationId }: { conversationId: string }) {
  const { t } = useTranslation();
  const { data: notes } = useNotes(conversationId);
  if (!notes?.pinned.length) return null;

  return (
    <section aria-label={t('notes.pinned')} className="flex flex-col gap-1.5 border-t px-4 pt-3">
      <h3 className="flex items-center gap-1 text-xs font-medium text-muted-foreground">
        <Pin className="size-3.5" aria-hidden="true" />
        {t('notes.pinned')}
      </h3>
      <ul className="flex max-h-40 flex-col gap-1.5 overflow-y-auto">
        {notes.pinned.map((note) => (
          <NoteItem key={note.id} note={note} conversationId={conversationId} compact />
        ))}
      </ul>
    </section>
  );
}
