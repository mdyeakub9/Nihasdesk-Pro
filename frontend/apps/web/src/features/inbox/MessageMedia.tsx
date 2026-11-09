import { Button, cn } from '@nihasdesk/ui';
import { AlertCircle, Download, FileText, Film, Loader2, Trash2, X } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatSize } from '../../lib/format';
import type { Attachment, AttachmentFailure } from './types';

/*
 * Media a customer sent (PRD 4.4, ADR 0031). Files come from the API's media endpoint, which checks
 * the session and the workspace and never serves HTML inline; video and files are downloads only.
 */

const failures: readonly AttachmentFailure[] = [
  'expired',
  'type_not_allowed',
  'too_large',
  'network',
  'source_not_allowed',
];

function failureOf(attachment: Attachment): AttachmentFailure | undefined {
  if (attachment.status === 'too_large') return 'too_large';
  return failures.find((code) => code === attachment.failureCode);
}

/** Full-size photo over the page; Escape or the close button returns to the conversation. */
function ImageViewer({ attachment, onClose }: { attachment: Attachment; onClose: () => void }) {
  const { t } = useTranslation();

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-foreground/70 p-4"
      onClick={onClose}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label={t('media.viewer')}
        className="flex max-h-full max-w-full flex-col items-end gap-2"
        onClick={(event) => event.stopPropagation()}
      >
        <Button size="sm" variant="outline" autoFocus onClick={onClose}>
          <X aria-hidden="true" />
          {t('media.close')}
        </Button>
        <img
          src={attachment.url}
          alt={t('media.kind.image')}
          className="max-h-[85vh] max-w-full rounded-lg bg-surface object-contain"
        />
      </div>
    </div>
  );
}

function ImageAttachment({ attachment }: { attachment: Attachment }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const opener = useRef<HTMLButtonElement>(null);
  const close = () => {
    setOpen(false);
    opener.current?.focus();
  };
  const src = attachment.thumbnailUrl ?? attachment.url;

  if (attachment.kind === 'sticker') {
    // Stickers stay small and don't open (PRD 4.4).
    return <img src={src} alt={t('media.kind.sticker')} className="size-20 object-contain" />;
  }

  return (
    <>
      <button
        ref={opener}
        type="button"
        aria-label={t('media.openPhoto')}
        onClick={() => setOpen(true)}
        className="block overflow-hidden rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        <img
          src={src}
          alt={t('media.kind.image')}
          width={attachment.width}
          height={attachment.height}
          loading="lazy"
          className="h-auto max-h-60 w-auto max-w-60 object-cover"
        />
      </button>
      {open && <ImageViewer attachment={attachment} onClose={close} />}
    </>
  );
}

function FileAttachment({ attachment }: { attachment: Attachment }) {
  const { t, i18n } = useTranslation();
  const Icon = attachment.kind === 'video' ? Film : FileText;
  const name = attachment.fileName ?? t(`media.kind.${attachment.kind}`);
  const size =
    attachment.sizeBytes !== undefined
      ? formatSize(attachment.sizeBytes, i18n.resolvedLanguage ?? 'en')
      : null;
  return (
    <a
      href={attachment.url}
      download={attachment.fileName ?? true}
      rel="noopener noreferrer"
      aria-label={t('media.download', { name })}
      className="flex w-full max-w-72 items-center gap-3 rounded-md border bg-surface px-3 py-2 hover:bg-subtle focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      <Icon className="size-5 shrink-0 text-secondary-foreground" aria-hidden="true" />
      <span className="flex min-w-0 flex-1 flex-col">
        <span className="truncate text-sm font-medium">{name}</span>
        {size && <span className="text-xs text-muted-foreground">{size}</span>}
      </span>
      <Download className="size-4 shrink-0 text-secondary-foreground" aria-hidden="true" />
    </a>
  );
}

function SavedAttachment({ attachment }: { attachment: Attachment }) {
  const { t } = useTranslation();
  switch (attachment.kind) {
    case 'image':
    case 'sticker':
      return <ImageAttachment attachment={attachment} />;
    case 'audio':
      // Duration comes from the player; the transcription follows with Issue #85.
      return (
        <audio
          controls
          preload="metadata"
          src={attachment.url}
          aria-label={t('media.kind.audio')}
          className="max-w-72"
        />
      );
    default:
      return <FileAttachment attachment={attachment} />;
  }
}

function AttachmentItem({ attachment }: { attachment: Attachment }) {
  const { t } = useTranslation();

  if (attachment.status === 'saved' && attachment.url) {
    return <SavedAttachment attachment={attachment} />;
  }

  if (attachment.status === 'pending') {
    return (
      <p role="status" className="flex items-center gap-2 text-[13px] text-secondary-foreground">
        <Loader2 className="size-4 animate-spin" aria-hidden="true" />
        {t(`media.loading.${attachment.kind}`)}
      </p>
    );
  }

  if (attachment.status === 'deleted') {
    // Retention (SEC-44): the chat still shows something was sent here.
    return (
      <p className="flex items-center gap-2 rounded-md border border-dashed px-3 py-2 text-[13px] text-muted-foreground">
        <Trash2 className="size-4 shrink-0" aria-hidden="true" />
        {t(`media.kind.${attachment.kind}`)} · {t('media.deleted')}
      </p>
    );
  }

  const failure = failureOf(attachment);
  return (
    <div className="flex items-start gap-2 rounded-md border border-dashed px-3 py-2">
      <AlertCircle className="mt-0.5 size-4 shrink-0 text-destructive" aria-hidden="true" />
      <div className="flex flex-col">
        <p className="text-sm font-medium">{t('media.failed')}</p>
        <p className="text-[13px] text-secondary-foreground">{t(`media.failure.${failure ?? 'unknown'}`)}</p>
      </div>
    </div>
  );
}

export function MessageMedia({ attachments, className }: { attachments: Attachment[]; className?: string }) {
  return (
    <ul className={cn('flex flex-col gap-2', className)}>
      {attachments.map((attachment) => (
        <li key={attachment.id}>
          <AttachmentItem attachment={attachment} />
        </li>
      ))}
    </ul>
  );
}
