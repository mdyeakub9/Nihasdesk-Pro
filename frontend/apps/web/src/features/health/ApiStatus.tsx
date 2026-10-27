import { Button, cn } from '@nihasdesk/ui';
import { useTranslation } from 'react-i18next';
import { useApiHealth } from './useApiHealth';

export function ApiStatus() {
  const { t } = useTranslation();
  const { isPending, isError, refetch } = useApiHealth();

  const state = isPending ? 'checking' : isError ? 'unreachable' : 'healthy';

  return (
    <div className="flex items-center gap-3 text-sm" role="status" aria-live="polite">
      <span
        aria-hidden="true"
        className={cn(
          'size-2.5 rounded-full',
          state === 'healthy' && 'bg-success',
          state === 'unreachable' && 'bg-destructive',
          state === 'checking' && 'bg-muted-foreground',
        )}
      />
      <span>
        {t('apiStatus.label')}: {t(`apiStatus.${state}`)}
      </span>
      {isError && (
        <Button size="sm" variant="outline" onClick={() => void refetch()}>
          {t('apiStatus.retry')}
        </Button>
      )}
    </div>
  );
}
