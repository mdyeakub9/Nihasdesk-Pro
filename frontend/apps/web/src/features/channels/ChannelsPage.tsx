import { Badge, Button, Card, cn, FieldError } from '@nihasdesk/ui';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router';
import { ChannelIcon } from '../../components/ChannelIcon';
import { authErrorMessage } from '../auth/errors';
import { useSession } from '../session/useSession';
import {
  isFacebookLoginUrl,
  useChannelConnections,
  useDisconnectChannel,
  useStartMetaConnect,
  type ChannelConnection,
} from './api';
import { leaveTo } from './navigate';

/** Owner and Admin manage channels (docs/standards/permissions.md); the API checks it again. */
const canManage = (role: string | undefined) => role === 'owner' || role === 'admin';

const callbackErrors = ['invalid_state', 'cancelled', 'meta_failed'] as const;

function formatDate(iso: string, language: string) {
  return new Intl.DateTimeFormat(language, { day: 'numeric', month: 'short', year: 'numeric' }).format(
    new Date(iso),
  );
}

/** What Facebook Login came back with (`?connected=…&elsewhere=…` or `?error=…`). */
function ConnectResult() {
  const { t } = useTranslation();
  const [params, setParams] = useSearchParams();
  const error = params.get('error');
  const connected = Number(params.get('connected') ?? Number.NaN);
  const elsewhere = Number(params.get('elsewhere') ?? 0);

  let message: string | null = null;
  let failed = false;
  if (error) {
    failed = true;
    const known = callbackErrors.find((code) => code === error);
    message = t(`channelSettings.result.${known ?? 'meta_failed'}`);
  } else if (Number.isInteger(connected)) {
    failed = connected === 0;
    message = [
      connected > 0 ? t('channelSettings.result.connected', { count: connected }) : null,
      elsewhere > 0 ? t('channelSettings.result.elsewhere', { count: elsewhere }) : null,
      connected === 0 && elsewhere === 0 ? t('channelSettings.result.none') : null,
    ]
      .filter(Boolean)
      .join(' ');
  }

  if (!message) return null;

  return (
    <div
      role="status"
      aria-label={t('channelSettings.resultLabel')}
      className={cn(
        'flex items-start gap-3 rounded-lg border px-4 py-3 text-[13px]',
        failed ? 'bg-warning text-warning-foreground' : 'bg-success text-success-foreground',
      )}
    >
      <p className="flex-1">{message}</p>
      <Button size="sm" variant="ghost" onClick={() => setParams({}, { replace: true })}>
        {t('channelSettings.dismiss')}
      </Button>
    </div>
  );
}

function ConnectionRow({ connection }: { connection: ChannelConnection }) {
  const { t, i18n } = useTranslation();
  const disconnect = useDisconnectChannel();
  const [confirming, setConfirming] = useState(false);

  return (
    <li className="flex flex-wrap items-center gap-3 border-b px-4 py-3 last:border-b-0">
      <ChannelIcon channel="messenger" size={20} />
      <div className="flex min-w-0 flex-1 flex-col">
        <span className="truncate text-sm font-medium">{connection.name}</span>
        <span className="text-xs text-muted-foreground">
          {t('channelSettings.connectedOn', {
            date: formatDate(connection.connectedAt, i18n.resolvedLanguage ?? 'en'),
          })}
        </span>
      </div>
      {confirming ? (
        <div className="flex items-center gap-2">
          <Button
            size="sm"
            variant="destructive"
            disabled={disconnect.isPending}
            onClick={() => disconnect.mutate(connection.id)}
          >
            {t('channelSettings.disconnectConfirm')}
          </Button>
          <Button size="sm" variant="ghost" onClick={() => setConfirming(false)}>
            {t('team.cancel')}
          </Button>
        </div>
      ) : (
        <Button
          size="sm"
          variant="outline"
          aria-label={t('channelSettings.disconnectPage', { name: connection.name })}
          onClick={() => setConfirming(true)}
        >
          {t('channelSettings.disconnect')}
        </Button>
      )}
      {confirming && (
        <p className="w-full text-xs text-muted-foreground">{t('channelSettings.disconnectHint')}</p>
      )}
      {disconnect.error && (
        <FieldError className="w-full">{authErrorMessage(disconnect.error, t)}</FieldError>
      )}
    </li>
  );
}

function MessengerCard() {
  const { t } = useTranslation();
  const { data: connections = [], isPending } = useChannelConnections(true);
  const start = useStartMetaConnect();
  const [badUrl, setBadUrl] = useState(false);

  const connect = () => {
    setBadUrl(false);
    start.mutate(undefined, {
      onSuccess: ({ url }) => (isFacebookLoginUrl(url) ? leaveTo(url) : setBadUrl(true)),
    });
  };

  return (
    <Card className="flex flex-col gap-4">
      <div className="flex items-start gap-3">
        <ChannelIcon channel="messenger" size={24} />
        <div className="flex flex-1 flex-col gap-1">
          <h2 className="text-base font-semibold">{t('channelSettings.messenger.title')}</h2>
          <p className="text-[13px] text-secondary-foreground">{t('channelSettings.messenger.intro')}</p>
        </div>
      </div>
      {connections.length > 0 && (
        <ul aria-label={t('channelSettings.connectedPages')} className="rounded-lg border">
          {connections.map((connection) => (
            <ConnectionRow key={connection.id} connection={connection} />
          ))}
        </ul>
      )}
      <div className="flex flex-col items-start gap-2">
        <Button disabled={isPending || start.isPending} onClick={connect}>
          {connections.length > 0
            ? t('channelSettings.messenger.connectAnother')
            : t('channelSettings.messenger.connect')}
        </Button>
        <p className="text-xs text-muted-foreground">{t('channelSettings.messenger.hint')}</p>
        {start.error && <FieldError>{authErrorMessage(start.error, t)}</FieldError>}
        {badUrl && <FieldError>{t('authErrors.generic')}</FieldError>}
      </div>
    </Card>
  );
}

function ComingSoonCard({ channel }: { channel: 'instagram' | 'whatsapp' }) {
  const { t } = useTranslation();
  return (
    <Card className="flex items-center gap-3 py-4">
      <ChannelIcon channel={channel} size={24} />
      <span className="flex-1 text-sm font-medium">{t(`channels.${channel}`)}</span>
      <Badge tone="neutral">{t('channelSettings.comingSoon')}</Badge>
    </Card>
  );
}

/** Settings → Channels (ADR 0017): connect Facebook Pages; Instagram and WhatsApp follow. */
export function ChannelsPage() {
  const { t } = useTranslation();
  const { data: session } = useSession();

  if (!canManage(session?.role)) {
    return (
      <p role="note" className="max-w-3xl text-sm text-secondary-foreground">
        {t('channelSettings.ownersOnly')}
      </p>
    );
  }

  return (
    <div className="flex max-w-3xl flex-col gap-4">
      <p className="text-sm text-secondary-foreground">{t('channelSettings.intro')}</p>
      <ConnectResult />
      <MessengerCard />
      <ComingSoonCard channel="instagram" />
      <ComingSoonCard channel="whatsapp" />
    </div>
  );
}
