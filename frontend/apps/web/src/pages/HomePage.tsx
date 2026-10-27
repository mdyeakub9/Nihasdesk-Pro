import { useTranslation } from 'react-i18next';

export function HomePage() {
  const { t } = useTranslation();

  return (
    <section className="max-w-2xl overflow-y-auto p-6 md:p-8">
      <h1 className="text-[28px] leading-9 font-semibold">{t('home.title')}</h1>
      <p className="mt-2 text-secondary-foreground">{t('home.subtitle')}</p>
    </section>
  );
}
