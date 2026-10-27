import { Badge, Button } from '@nihasdesk/ui';
import { useTranslation } from 'react-i18next';
import { useApiHealth } from './useApiHealth';

/** "System online" badge in the top bar, driven by the API liveness check. */
export function ApiStatus() {
  const { t } = useTranslation();
  const { isPending, isError, refetch } = useApiHealth();

  const state = isPending ? 'checking' : isError ? 'offline' : 'online';

  return (
    <div className="flex items-center gap-2" role="status" aria-live="polite">
      <Badge tone={state === 'online' ? 'success' : state === 'offline' ? 'warning' : 'neutral'}>
        {t(`apiStatus.${state}`)}
      </Badge>
      {isError && (
        <Button size="sm" variant="outline" onClick={() => void refetch()}>
          {t('apiStatus.retry')}
        </Button>
      )}
    </div>
  );
}
