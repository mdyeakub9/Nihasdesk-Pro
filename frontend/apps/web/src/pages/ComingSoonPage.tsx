import { useTranslation } from 'react-i18next';

/** Placeholder for Phase 1 sections whose screens are not designed or built yet. */
export function ComingSoonPage({ section }: { section: string }) {
  const { t } = useTranslation();

  return (
    <section className="p-6 md:p-8">
      <h1 className="text-[28px] leading-9 font-semibold">{t(`nav.${section}`)}</h1>
      <p className="mt-2 text-secondary-foreground">{t('comingSoon.text')}</p>
    </section>
  );
}
