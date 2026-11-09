import { Button, cn } from '@nihasdesk/ui';
import { ClipboardList, ImageUp, MessageSquareText, Plus, ShoppingBag } from 'lucide-react';
import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';

export type Action = 'products' | 'order' | 'savedReply' | 'photo';

/**
 * The composer's + menu (PRD 4.4): Send products, Create order, Saved reply, Send photo. An action
 * can be switched off with a reason, e.g. a photo after the 24-hour window. Arrow keys move, Enter
 * picks, Esc closes.
 */
export function ActionsMenu({
  onPick,
  unavailable = {},
}: {
  onPick: (action: Action) => void;
  /** Actions that can't be used now, with the reason shown instead. */
  unavailable?: Partial<Record<Action, string>>;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const menuId = useId();
  const items = useRef<(HTMLButtonElement | null)[]>([]);
  const button = useRef<HTMLButtonElement>(null);

  const entries: { action: Action; label: string; icon: typeof Plus }[] = [
    { action: 'products', label: t('actions.sendProducts'), icon: ShoppingBag },
    { action: 'order', label: t('actions.createOrder'), icon: ClipboardList },
    { action: 'savedReply', label: t('actions.savedReply'), icon: MessageSquareText },
    { action: 'photo', label: t('actions.sendPhoto'), icon: ImageUp },
  ];

  useEffect(() => {
    if (open) items.current[0]?.focus();
  }, [open]);

  function close() {
    setOpen(false);
    button.current?.focus();
  }

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const enabled = items.current.filter((item): item is HTMLButtonElement => !!item && !item.disabled);
    const index = enabled.indexOf(document.activeElement as HTMLButtonElement);
    if (event.key === 'Escape') {
      event.preventDefault();
      close();
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      const step = event.key === 'ArrowDown' ? 1 : -1;
      enabled[(index + step + enabled.length) % enabled.length]?.focus();
    }
  }

  return (
    <div className="relative">
      <Button
        ref={button}
        type="button"
        variant="outline"
        size="icon"
        aria-label={t('actions.open')}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={() => setOpen((current) => !current)}
      >
        <Plus aria-hidden="true" />
      </Button>
      {open && (
        <div
          id={menuId}
          role="menu"
          aria-label={t('actions.open')}
          onKeyDown={onKeyDown}
          className="absolute bottom-full left-0 z-20 mb-1 flex w-56 flex-col rounded-md border bg-surface py-1 shadow-md"
        >
          {entries.map(({ action, label, icon: Icon }, index) => {
            const reason = unavailable[action];
            return (
              <button
                key={action}
                ref={(element) => {
                  items.current[index] = element;
                }}
                type="button"
                role="menuitem"
                disabled={!!reason}
                title={reason}
                className={cn(
                  'flex items-center gap-2 px-3 py-2 text-left text-[13px] hover:bg-subtle focus-visible:bg-subtle focus-visible:outline-none',
                  reason && 'cursor-not-allowed text-muted-foreground hover:bg-transparent',
                )}
                onClick={() => {
                  if (reason) return;
                  setOpen(false);
                  onPick(action);
                }}
              >
                <Icon className="size-4" aria-hidden="true" />
                <span className="flex-1">{label}</span>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
