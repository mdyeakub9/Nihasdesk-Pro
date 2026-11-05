import { Badge, Button, Card, FieldError, Input, Label, type BadgeProps } from '@nihasdesk/ui';
import { FileText, Globe, Trash2 } from 'lucide-react';
import { useId, useRef, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { authErrorMessage } from '../auth/errors';
import {
  useAddWebPage,
  useDeleteSource,
  useSources,
  useUploadSource,
  type KnowledgeSource,
  type SourceStatus,
} from './api';

const maxFileBytes = 20 * 1024 * 1024;

const statusTone: Record<SourceStatus, BadgeProps['tone']> = {
  reading: 'neutral',
  review: 'info',
  live: 'success',
  failed: 'warning',
  archived: 'neutral',
};

/** A warning code in plain words: `pages_without_text:3,4`, `no_embeddings`. */
function useWarningText() {
  const { t } = useTranslation();
  return (code: string) => {
    const [name, detail] = code.split(':');
    if (name === 'pages_without_text')
      return t('sources.warnings.pagesWithoutText', { pages: (detail ?? '').replaceAll(',', ', ') });
    if (name === 'no_embeddings') return t('sources.warnings.noEmbeddings');
    return code;
  };
}

function SourceItem({ source, canEdit }: { source: KnowledgeSource; canEdit: boolean }) {
  const { t } = useTranslation();
  const remove = useDeleteSource();
  const warningText = useWarningText();
  const [confirming, setConfirming] = useState(false);
  const Icon = source.kind === 'webpage' ? Globe : FileText;
  const details = [
    t(`sources.kinds.${source.kind}`),
    source.pageCount ? t('sources.pages', { count: source.pageCount }) : null,
    source.status === 'review' || source.status === 'live'
      ? t('sources.sections', { count: source.sectionCount })
      : null,
  ].filter(Boolean);

  return (
    <li className="flex flex-col gap-1 border-b px-4 py-3 last:border-b-0">
      <div className="flex items-start gap-2">
        <Icon className="mt-0.5 size-4 shrink-0 text-secondary-foreground" aria-hidden="true" />
        <p className="min-w-0 flex-1 text-sm font-medium break-words">{source.title}</p>
        <Badge tone={statusTone[source.status]}>{t(`sources.status.${source.status}`)}</Badge>
      </div>
      <p className="text-xs text-muted-foreground">{details.join(' · ')}</p>
      {source.kind === 'webpage' && source.origin !== source.title && (
        <p className="text-xs break-all text-muted-foreground">{source.origin}</p>
      )}
      {source.failureReason && (
        <p className="text-[13px] text-destructive">
          {t(`sources.failures.${source.failureReason}`, { defaultValue: t('sources.failures.read_failed') })}
        </p>
      )}
      {source.warnings.map((warning) => (
        <p key={warning} className="text-[13px] text-warning-foreground">
          {warningText(warning)}
        </p>
      ))}
      {canEdit &&
        (confirming ? (
          <div className="flex flex-wrap items-center gap-2">
            <p className="text-[13px]">{t('sources.deleteConfirm')}</p>
            <Button
              size="sm"
              variant="destructive"
              disabled={remove.isPending}
              onClick={() => remove.mutate(source.id)}
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
              aria-label={t('sources.deleteLabel', { title: source.title })}
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

function AddSource() {
  const { t } = useTranslation();
  const upload = useUploadSource();
  const addPage = useAddWebPage();
  const fileId = useId();
  const urlId = useId();
  const input = useRef<HTMLInputElement>(null);
  const [url, setUrl] = useState('');
  const [tooLarge, setTooLarge] = useState(false);

  function choose(file: File | undefined) {
    upload.reset();
    setTooLarge(false);
    if (!file) return;
    if (file.size > maxFileBytes) {
      setTooLarge(true);
    } else {
      upload.mutate(file);
    }

    // The same file can be chosen again after a failure.
    if (input.current) input.current.value = '';
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    const value = url.trim();
    if (value) addPage.mutate(value, { onSuccess: () => setUrl('') });
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-1">
        <Label htmlFor={fileId}>{t('sources.file')}</Label>
        <input
          id={fileId}
          ref={input}
          type="file"
          accept=".pdf,.docx,.txt"
          disabled={upload.isPending}
          className="text-[13px]"
          onChange={(event) => choose(event.target.files?.[0])}
        />
        {upload.isPending && <p className="text-xs text-muted-foreground">{t('sources.uploading')}</p>}
        {tooLarge && <FieldError>{t('sources.errors.tooLarge')}</FieldError>}
        {upload.error && <FieldError>{authErrorMessage(upload.error, t)}</FieldError>}
      </div>
      <form onSubmit={submit} className="flex flex-col gap-1">
        <Label htmlFor={urlId}>{t('sources.url')}</Label>
        <div className="flex flex-wrap gap-2">
          <Input
            id={urlId}
            type="url"
            className="min-w-0 flex-1"
            maxLength={2048}
            placeholder={t('sources.urlPlaceholder')}
            value={url}
            onChange={(event) => setUrl(event.target.value)}
          />
          <Button type="submit" variant="outline" disabled={!url.trim() || addPage.isPending}>
            <Globe aria-hidden="true" />
            {t('sources.addPage')}
          </Button>
        </div>
        {addPage.error && <FieldError>{authErrorMessage(addPage.error, t)}</FieldError>}
      </form>
    </div>
  );
}

/** Settings → Shop knowledge: documents and pages the AI may learn from (PRD 4.7, ADR 0029). */
export function Sources({ canEdit }: { canEdit: boolean }) {
  const { t } = useTranslation();
  const { data: sources = [], isPending } = useSources();

  return (
    <Card className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <h2 className="text-base font-semibold">{t('sources.title')}</h2>
        <p className="text-[13px] text-secondary-foreground">{t('sources.intro')}</p>
        <p className="text-[13px] text-secondary-foreground">{t('sources.notUsedYet')}</p>
      </div>
      {canEdit && <AddSource />}
      {!isPending && sources.length === 0 ? (
        <p className="rounded-lg border px-4 py-6 text-center text-[13px] text-secondary-foreground">
          {t('sources.empty')}
        </p>
      ) : (
        <ul aria-busy={isPending} aria-label={t('sources.list')} className="rounded-lg border">
          {sources.map((source) => (
            <SourceItem key={source.id} source={source} canEdit={canEdit} />
          ))}
        </ul>
      )}
    </Card>
  );
}
