import { Button, cn } from '@nihasdesk/ui';
import { ClipboardList, MessageSquareText, Paperclip, Plus, ShoppingBag } from 'lucide-react';
import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';

export type Action = 'products' | 'order' | 'savedReply';

/**
 * The composer's + menu (PRD 4.4): Send products, Create order, Saved reply; Send image or file
 * comes later (shown disabled). Arrow keys move, Enter picks, Esc closes.
 */
export function ActionsMenu({ onPick }: { onPick: (action: Action) => void }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const menuId = useId();
  const items = useRef<(HTMLButtonElement | null)[]>([]);
  const button = useRef<HTMLButtonElement>(null);

  const entries: { action: Action | null; label: string; icon: typeof Plus }[] = [
    { action: 'products', label: t('actions.sendProducts'), icon: ShoppingBag },
    { action: 'order', label: t('actions.createOrder'), icon: ClipboardList },
    { action: 'savedReply', label: t('actions.savedReply'), icon: MessageSquareText },
    { action: null, label: t('actions.sendFile'), icon: Paperclip },
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
          {entries.map(({ action, label, icon: Icon }, index) => (
            <button
              key={label}
              ref={(element) => {
                items.current[index] = element;
              }}
              type="button"
              role="menuitem"
              disabled={!action}
              title={action ? undefined : t('comingSoon.short')}
              className={cn(
                'flex items-center gap-2 px-3 py-2 text-left text-[13px] hover:bg-subtle focus-visible:bg-subtle focus-visible:outline-none',
                !action && 'cursor-not-allowed text-muted-foreground hover:bg-transparent',
              )}
              onClick={() => {
                if (!action) return;
                setOpen(false);
                onPick(action);
              }}
            >
              <Icon className="size-4" aria-hidden="true" />
              <span className="flex-1">{label}</span>
              {!action && <span className="text-xs">{t('comingSoon.short')}</span>}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
