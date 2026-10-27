import i18next, { type i18n } from 'i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import { initReactI18next } from 'react-i18next';
import en from './locales/en.json';
import it from './locales/it.json';

/** Dashboard languages at launch (PRD 4.13). */
export const supportedLanguages = ['it', 'en'] as const;
export type Language = (typeof supportedLanguages)[number];

export const resources = {
  en: { translation: en },
  it: { translation: it },
} as const;

/**
 * Initializes i18next for React. The default comes from the browser; the user's choice will be
 * stored on their profile (server side) once Identity exists, so nothing is cached in the browser.
 */
export async function initI18n(language?: Language): Promise<i18n> {
  const instance = i18next.createInstance();
  await instance
    .use(LanguageDetector)
    .use(initReactI18next)
    .init({
      resources,
      ...(language ? { lng: language } : {}),
      fallbackLng: 'en',
      supportedLngs: supportedLanguages,
      nonExplicitSupportedLngs: true,
      interpolation: { escapeValue: false },
      detection: { order: ['navigator'], caches: [] },
    });
  return instance;
}
