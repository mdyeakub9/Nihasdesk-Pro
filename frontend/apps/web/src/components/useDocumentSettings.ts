import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useUiStore } from '../stores/ui-store';

/** Keeps `<html lang>` and the dark class in sync with the UI language and theme. */
export function useDocumentSettings() {
  const { i18n } = useTranslation();
  const theme = useUiStore((state) => state.theme);

  useEffect(() => {
    document.documentElement.lang = i18n.resolvedLanguage ?? 'en';
  }, [i18n.resolvedLanguage]);

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
  }, [theme]);
}
