import { Badge, Button, Card, Checkbox, FieldError, Input, Label } from '@nihasdesk/ui';
import { Globe, Sparkles, Trash2 } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { authErrorMessage } from '../auth/errors';
import {
  useAddSite,
  useAddSitePages,
  useDeleteSite,
  useSiteDifferences,
  useSites,
  useUseSite,
  type KnowledgeSite,
  type SitePage,
} from './api';
import { statusTone } from './sourceDisplay';

const pathOf = (url: string) => {
  try {
    return new URL(url).pathname;
  } catch {
    return url;
  }
};

function PageRow({
  page,
  ticked,
  canEdit,
  onToggle,
}: {
  page: SitePage;
  ticked: boolean;
  canEdit: boolean;
  onToggle: () => void;
}) {
  const { t } = useTranslation();
  const id = useId();
  return (
    <li className="flex items-start gap-2 py-1 text-[13px]">
      {page.sourceStatus ? null : (
        <Checkbox
          id={id}
          className="mt-0.5"
          checked={ticked}
          disabled={!canEdit}
          onCheckedChange={onToggle}
        />
      )}
      {/* The title comes from the website: plain text only (SEC-02). */}
      <label htmlFor={page.sourceStatus ? undefined : id} className="min-w-0 flex-1 break-words">
        <span className="font-medium">{page.title}</span>{' '}
        <span className="text-xs break-all text-muted-foreground">{pathOf(page.url)}</span>
      </label>
      {page.sourceStatus && (
        <Badge tone={statusTone[page.sourceStatus]}>{t(`sources.status.${page.sourceStatus}`)}</Badge>
      )}
    </li>
  );
}

function FoundPages({ site, canEdit }: { site: KnowledgeSite; canEdit: boolean }) {
  const { t } = useTranslation();
  const add = useAddSitePages(site.id);
  const use = useUseSite(site.id);
  const waiting = site.pages.filter((page) => !page.sourceStatus);
  const [ticked, setTicked] = useState(
    () => new Set(site.pages.filter((page) => page.chosen).map((page) => page.url)),
  );
  const toAdd = waiting.filter((page) => ticked.has(page.url)).map((page) => page.url);
  const canUse = site.pages.some((page) => page.sourceStatus === 'review');
  const differences = useSiteDifferences(site.id, canEdit && canUse).data?.count ?? 0;
  const main = site.pages.filter((page) => page.chosen);
  const others = site.pages.filter((page) => !page.chosen);

  function toggle(url: string) {
    setTicked((current) => {
      const next = new Set(current);
      if (next.has(url)) next.delete(url);
      else next.add(url);
      return next;
    });
  }

  const rows = (pages: SitePage[]) =>
    pages.map((page) => (
      <PageRow
        key={page.url}
        page={page}
        ticked={ticked.has(page.url)}
        canEdit={canEdit}
        onToggle={() => toggle(page.url)}
      />
    ));

  return (
    <div className="flex flex-col gap-2">
      <div>
        <h4 className="text-xs font-medium text-muted-foreground">{t('sources.sites.mainPages')}</h4>
        <ul aria-label={t('sources.sites.mainPages')}>{rows(main)}</ul>
      </div>
      {others.length > 0 && (
        <details>
          <summary className="cursor-pointer text-xs font-medium text-muted-foreground">
            {t('sources.sites.otherPages', { count: others.length })}
          </summary>
          <ul aria-label={t('sources.sites.otherPagesList')}>{rows(others)}</ul>
        </details>
      )}
      {canEdit && toAdd.length > 0 && (
        <div>
          <Button size="sm" disabled={add.isPending} onClick={() => add.mutate(toAdd)}>
            {t('sources.sites.addPages', { count: toAdd.length })}
          </Button>
        </div>
      )}
      {add.error && <FieldError>{authErrorMessage(add.error, t)}</FieldError>}
      {canEdit && canUse && (
        <div className="flex flex-col gap-1">
          <p className="text-[13px] text-secondary-foreground">{t('sources.sites.useHint')}</p>
          {differences > 0 && (
            <p className="text-[13px] text-warning-foreground">
              {t('sources.sites.useDifferences', { count: differences })}
            </p>
          )}
          <div>
            <Button size="sm" variant="outline" disabled={use.isPending} onClick={() => use.mutate()}>
              <Sparkles aria-hidden="true" />
              {t('sources.sites.useAll')}
            </Button>
          </div>
        </div>
      )}
      {use.error && <FieldError>{authErrorMessage(use.error, t)}</FieldError>}
    </div>
  );
}

function SiteItem({ site, canEdit }: { site: KnowledgeSite; canEdit: boolean }) {
  const { t } = useTranslation();
  const remove = useDeleteSite();
  const [confirming, setConfirming] = useState(false);
  const added = site.pages.filter((page) => page.sourceStatus).length;

  return (
    <li className="flex flex-col gap-2 border-b px-4 py-3 last:border-b-0">
      <div className="flex items-start gap-2">
        <Globe className="mt-0.5 size-4 shrink-0 text-secondary-foreground" aria-hidden="true" />
        <h3 className="min-w-0 flex-1 text-sm font-medium break-words">{site.host}</h3>
        {site.status === 'finding' && <Badge tone="neutral">{t('sources.sites.finding')}</Badge>}
        {site.status === 'failed' && <Badge tone="warning">{t('sources.status.failed')}</Badge>}
      </div>
      {site.status === 'failed' && (
        <p className="text-[13px] text-destructive">
          {t(`sources.sites.failures.${site.failureReason}`, {
            defaultValue: t('sources.sites.failures.site_unreachable'),
          })}
        </p>
      )}
      {site.status === 'ready' && <FoundPages site={site} canEdit={canEdit} />}
      {canEdit &&
        (confirming ? (
          <div className="flex flex-wrap items-center gap-2">
            <p className="text-[13px]">{t('sources.sites.deleteConfirm', { count: added })}</p>
            <Button
              size="sm"
              variant="destructive"
              disabled={remove.isPending}
              onClick={() => remove.mutate(site.id)}
            >
              {t('sources.delete')}
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setConfirming(false)}>
              {t('sources.cancel')}
            </Button>
          </div>
        ) : (
          <div>
            <Button
              size="sm"
              variant="ghost"
              aria-label={t('sources.deleteLabel', { title: site.host })}
              onClick={() => setConfirming(true)}
            >
              <Trash2 aria-hidden="true" />
              {t('sources.delete')}
            </Button>
          </div>
        ))}
      {remove.error && <FieldError>{authErrorMessage(remove.error, t)}</FieldError>}
    </li>
  );
}

function AddSite() {
  const { t } = useTranslation();
  const add = useAddSite();
  const domainId = useId();
  const confirmId = useId();
  const [domain, setDomain] = useState('');
  const [confirmed, setConfirmed] = useState(false);

  function submit(event: FormEvent) {
    event.preventDefault();
    const value = domain.trim();
    if (value && confirmed) {
      add.mutate(value, {
        onSuccess: () => {
          setDomain('');
          setConfirmed(false);
        },
      });
    }
  }

  return (
    <form onSubmit={submit} className="flex flex-col gap-2">
      <div className="flex flex-col gap-1">
        <Label htmlFor={domainId}>{t('sources.sites.domain')}</Label>
        <Input
          id={domainId}
          className="min-w-0"
          maxLength={255}
          placeholder={t('sources.sites.domainPlaceholder')}
          value={domain}
          onChange={(event) => setDomain(event.target.value)}
        />
      </div>
      <div className="flex items-start gap-2 text-[13px]">
        <Checkbox
          id={confirmId}
          className="mt-0.5"
          checked={confirmed}
          onCheckedChange={(value) => setConfirmed(value === true)}
        />
        <label htmlFor={confirmId}>{t('sources.sites.confirm')}</label>
      </div>
      <div>
        <Button type="submit" variant="outline" disabled={!domain.trim() || !confirmed || add.isPending}>
          <Globe aria-hidden="true" />
          {t('sources.sites.find')}
        </Button>
      </div>
      {add.error && <FieldError>{authErrorMessage(add.error, t)}</FieldError>}
    </form>
  );
}

/** Settings → Shop knowledge: a whole website from its domain; the owner confirms the pages (ADR 0030). */
export function Sites({ canEdit }: { canEdit: boolean }) {
  const { t } = useTranslation();
  const { data: sites = [] } = useSites();

  return (
    <Card className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <h2 className="text-base font-semibold">{t('sources.sites.title')}</h2>
        <p className="text-[13px] text-secondary-foreground">{t('sources.sites.intro')}</p>
      </div>
      {canEdit && <AddSite />}
      {sites.length > 0 && (
        <ul aria-label={t('sources.sites.list')} className="rounded-lg border">
          {sites.map((site) => (
            <SiteItem key={site.id} site={site} canEdit={canEdit} />
          ))}
        </ul>
      )}
    </Card>
  );
}
