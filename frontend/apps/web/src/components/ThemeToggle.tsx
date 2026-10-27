import { cn } from '@nihasdesk/ui';
import { Moon, Sun } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useUiStore, type Theme } from '../stores/ui-store';

const options: { value: Theme; icon: typeof Sun }[] = [
  { value: 'light', icon: Sun },
  { value: 'dark', icon: Moon },
];

export function ThemeToggle() {
  const { t } = useTranslation();
  const theme = useUiStore((state) => state.theme);
  const setTheme = useUiStore((state) => state.setTheme);

  return (
    <div role="group" aria-label={t('theme.label')} className="flex rounded-md bg-background">
      {options.map(({ value, icon: Icon }) => (
        <button
          key={value}
          type="button"
          aria-pressed={theme === value}
          onClick={() => setTheme(value)}
          className={cn(
            'flex h-8 items-center gap-2 rounded-md border border-transparent px-3 text-[13px] font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
            theme === value && 'border-border bg-surface',
          )}
        >
          <Icon className="size-4" aria-hidden="true" />
          <span className="hidden sm:inline">{t(`theme.${value}`)}</span>
        </button>
      ))}
    </div>
  );
}
