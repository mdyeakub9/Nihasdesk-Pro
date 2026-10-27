import { useTranslation } from 'react-i18next';

export function HomePage() {
  const { t } = useTranslation();

  return (
    <section className="max-w-2xl">
      <h1 className="text-2xl font-semibold">{t('home.title')}</h1>
      <p className="mt-2 text-muted-foreground">{t('home.subtitle')}</p>
    </section>
  );
}
