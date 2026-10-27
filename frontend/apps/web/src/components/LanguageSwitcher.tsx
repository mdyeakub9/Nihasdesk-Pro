import { supportedLanguages } from '@nihasdesk/i18n';
import { useId } from 'react';
import { useTranslation } from 'react-i18next';

export function LanguageSwitcher() {
  const { t, i18n } = useTranslation();
  const id = useId();

  return (
    <div className="flex items-center gap-2 text-sm">
      <label htmlFor={id}>{t('language.label')}</label>
      <select
        id={id}
        className="rounded-md border bg-background px-2 py-1"
        value={i18n.resolvedLanguage}
        onChange={(event) => void i18n.changeLanguage(event.target.value)}
      >
        {supportedLanguages.map((language) => (
          <option key={language} value={language}>
            {t(`language.${language}`)}
          </option>
        ))}
      </select>
    </div>
  );
}
