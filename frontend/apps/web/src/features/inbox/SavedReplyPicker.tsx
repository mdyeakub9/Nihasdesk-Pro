import { cn } from '@nihasdesk/ui';
import { useTranslation } from 'react-i18next';
import type { SavedReply } from '../saved-replies/api';

/**
 * The list that opens above the composer when the text starts a `/` command (PRD 4.4). The
 * composer owns the keyboard (↑↓, Enter, Esc); this only shows the options.
 */
export function SavedReplyPicker({
  id,
  matches,
  active,
  onPick,
}: {
  id: string;
  matches: SavedReply[];
  active: number;
  onPick: (reply: SavedReply) => void;
}) {
  const { t } = useTranslation();

  return (
    <ul
      id={id}
      role="listbox"
      aria-label={t('savedReplies.picker')}
      className="absolute bottom-full left-0 z-10 mb-1 max-h-64 w-full overflow-y-auto rounded-md border bg-surface py-1 shadow-md"
    >
      {matches.map((reply, index) => (
        <li
          key={reply.id}
          id={`${id}-${index}`}
          role="option"
          aria-selected={index === active}
          className={cn('flex cursor-pointer flex-col px-3 py-1.5', index === active && 'bg-selected')}
          // Keep focus in the composer.
          onMouseDown={(event) => {
            event.preventDefault();
            onPick(reply);
          }}
        >
          <span className="text-[13px] font-medium">
            /{reply.shortcut} · {reply.title}
          </span>
          <span className="truncate text-xs text-muted-foreground">{reply.textEn ?? reply.textIt}</span>
        </li>
      ))}
    </ul>
  );
}
