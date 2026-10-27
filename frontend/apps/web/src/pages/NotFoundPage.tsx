import { Button } from '@nihasdesk/ui';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';

export function NotFoundPage() {
  const { t } = useTranslation();

  return (
    <section className="p-6 md:p-8">
      <h1 className="text-2xl font-semibold">{t('notFound.title')}</h1>
      <Button asChild variant="outline" className="mt-4">
        <Link to="/">{t('notFound.back')}</Link>
      </Button>
    </section>
  );
}
