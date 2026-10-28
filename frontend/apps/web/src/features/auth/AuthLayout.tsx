import { Card } from '@nihasdesk/ui';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Outlet } from 'react-router';
import { LanguageSwitcher } from '../../components/LanguageSwitcher';
import { ThemeToggle } from '../../components/ThemeToggle';
import { useDocumentSettings } from '../../components/useDocumentSettings';

/** Public pages (sign in, sign up, …): one centered card, no dashboard chrome. */
export function AuthLayout() {
  const { t } = useTranslation();
  useDocumentSettings();

  return (
    <div className="flex min-h-dvh flex-col bg-background">
      <header className="flex items-center justify-end gap-3 p-4">
        <LanguageSwitcher />
        <ThemeToggle />
      </header>
      <main className="flex flex-1 items-start justify-center px-4 pb-12 sm:items-center">
        <div className="flex w-full max-w-sm flex-col gap-6">
          <div className="flex items-center justify-center gap-3">
            <span
              aria-hidden="true"
              className="flex size-10 items-center justify-center rounded-lg bg-primary text-base font-semibold text-primary-foreground"
            >
              N
            </span>
            <span className="text-lg font-semibold">{t('app.name')}</span>
          </div>
          <Outlet />
        </div>
      </main>
    </div>
  );
}

/** Card with a heading and optional intro text, shared by all auth pages. */
export function AuthCard({
  title,
  intro,
  children,
}: {
  title: string;
  intro?: ReactNode;
  children: ReactNode;
}) {
  return (
    <Card className="flex flex-col gap-5">
      <div className="flex flex-col gap-1.5">
        <h1 className="text-xl font-semibold">{title}</h1>
        {intro && <p className="text-[13px] text-secondary-foreground">{intro}</p>}
      </div>
      {children}
    </Card>
  );
}
