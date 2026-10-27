import { supportedLanguages } from '@nihasdesk/i18n';
import { NativeSelect } from '@nihasdesk/ui';
import { useTranslation } from 'react-i18next';

export function LanguageSwitcher() {
  const { t, i18n } = useTranslation();

  return (
    <NativeSelect
      aria-label={t('language.label')}
      className="w-auto"
      value={i18n.resolvedLanguage}
      onChange={(event) => void i18n.changeLanguage(event.target.value)}
    >
      {supportedLanguages.map((language) => (
        <option key={language} value={language}>
          {t(`language.${language}`)}
        </option>
      ))}
    </NativeSelect>
  );
}
