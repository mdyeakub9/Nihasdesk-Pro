import { Button, cn } from '@nihasdesk/ui';
import { CircleHelp, ThumbsDown, ThumbsUp } from 'lucide-react';
import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useRateReply, useTrace, type Rating, type Trace, type TraceSource } from './api';

/*
 * "Why this answer" (PRD 4.8, ADR 0026 §6): what an AI reply was based on — sources, tool calls
 * with their results, the reply check — for the team only. Shown inline below the reply.
 */

function useSourceName() {
  const { t } = useTranslation();
  return (source: TraceSource) =>
    source.kind === 'answer'
      ? `“${source.label ?? t('aiTest.sources.answer')}”`
      : source.kind === 'policy'
        ? t(`aiTest.sources.${source.label ?? 'other'}`, { defaultValue: source.label ?? '' })
        : source.kind === 'catalog'
          ? t('trace.catalog')
          : t('aiTest.sources.zones');
}

/** The trace itself; the same view for inbox replies and test chat replies. */
export function TraceDetails({ trace }: { trace: Trace }) {
  const { t, i18n } = useTranslation();
  const sourceName = useSourceName();
  const seconds = (trace.durationMs / 1000).toLocaleString(i18n.resolvedLanguage ?? 'en', {
    maximumFractionDigits: 1,
  });

  return (
    <div className="flex w-full flex-col gap-3 rounded-md border bg-surface p-3 text-left text-[13px]">
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
        <dt className="text-muted-foreground">{t('trace.basedOn')}</dt>
        <dd>{trace.sources.length > 0 ? trace.sources.map(sourceName).join(', ') : t('trace.noSources')}</dd>
        <dt className="text-muted-foreground">{t('trace.check')}</dt>
        <dd className={cn(trace.check && trace.check !== 'passed' && 'font-medium text-destructive')}>
          {trace.check === 'passed'
            ? t('trace.passed')
            : trace.check
              ? t('trace.heldBack', {
                  reason: t(`aiTest.blockedReasons.${trace.check}`, { defaultValue: trace.check }),
                })
              : t('trace.noCheck')}
        </dd>
      </dl>

      <div className="flex flex-col gap-1">
        <h4 className="text-xs font-medium text-muted-foreground">{t('trace.tools')}</h4>
        {trace.hasDetails ? (
          trace.tools.length > 0 ? (
            <ol className="flex flex-col gap-1">
              {trace.tools.map((tool, index) => (
                <li key={index}>
                  <details className="rounded-md bg-subtle px-2 py-1">
                    <summary className="cursor-pointer font-mono text-xs">{tool.name}</summary>
                    <p className="mt-1 text-xs text-muted-foreground">{t('trace.arguments')}</p>
                    <pre className="whitespace-pre-wrap break-all font-mono text-xs">
                      {tool.arguments || '{}'}
                    </pre>
                    <p className="mt-1 text-xs text-muted-foreground">{t('trace.result')}</p>
                    <pre className="whitespace-pre-wrap break-all font-mono text-xs">{tool.result}</pre>
                  </details>
                </li>
              ))}
            </ol>
          ) : (
            <p className="text-muted-foreground">{t('trace.noTools')}</p>
          )
        ) : (
          <>
            {trace.toolNames.length > 0 && <p className="font-mono text-xs">{trace.toolNames.join(', ')}</p>}
            <p className="text-xs text-muted-foreground">{t('trace.removed')}</p>
          </>
        )}
      </div>

      <p className="text-xs text-muted-foreground">
        {t('trace.meta', {
          model: trace.model,
          tokens: trace.inputTokens + trace.outputTokens,
          seconds,
        })}
        {trace.instructionVersion && ` · ${t('trace.prompt', { version: trace.instructionVersion })}`}
      </p>
    </div>
  );
}

/** The "Why this answer" button; the details are rendered only while open. */
function useWhyToggle() {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const id = useId();
  const button = (
    <Button
      size="sm"
      variant="ghost"
      className="h-6 px-1.5 text-xs"
      aria-expanded={open}
      aria-controls={id}
      onClick={() => setOpen((value) => !value)}
    >
      <CircleHelp aria-hidden="true" />
      {t('trace.why')}
    </Button>
  );
  return { open, id, button };
}

/** For the test chat: the trace came with the reply. */
export function WhyThisTestAnswer({ trace }: { trace: Trace }) {
  const why = useWhyToggle();
  return (
    <div className="flex flex-col items-start gap-1">
      {why.button}
      <div id={why.id} className="w-full">
        {why.open && <TraceDetails trace={trace} />}
      </div>
    </div>
  );
}

function LoadedTrace({ messageId }: { messageId: string }) {
  const { t } = useTranslation();
  const trace = useTrace(messageId, true);
  if (trace.isPending) return <p className="text-xs text-muted-foreground">{t('trace.loading')}</p>;
  if (trace.isError) return <p className="text-xs text-destructive">{t('trace.error')}</p>;
  return <TraceDetails trace={trace.data} />;
}

/** 👍/👎 and "Why this answer" under an AI reply in the inbox. */
export function AiReplyTools({
  conversationId,
  messageId,
  rating,
}: {
  conversationId: string;
  messageId: string;
  rating: Rating | null;
}) {
  const { t } = useTranslation();
  const rate = useRateReply(conversationId);
  const why = useWhyToggle();
  const toggle = (value: Rating) => rate.mutate({ messageId, rating: rating === value ? null : value });

  return (
    <div className="flex w-full flex-col items-end gap-1">
      <div role="group" aria-label={t('trace.feedback')} className="flex items-center gap-1">
        <Button
          size="icon"
          variant="ghost"
          className={cn('size-6', rating === 'up' && 'text-primary')}
          aria-label={t('trace.good')}
          aria-pressed={rating === 'up'}
          disabled={rate.isPending}
          onClick={() => toggle('up')}
        >
          <ThumbsUp aria-hidden="true" />
        </Button>
        <Button
          size="icon"
          variant="ghost"
          className={cn('size-6', rating === 'down' && 'text-destructive')}
          aria-label={t('trace.bad')}
          aria-pressed={rating === 'down'}
          disabled={rate.isPending}
          onClick={() => toggle('down')}
        >
          <ThumbsDown aria-hidden="true" />
        </Button>
        {why.button}
      </div>
      <div id={why.id} className="w-full max-w-125">
        {why.open && <LoadedTrace messageId={messageId} />}
      </div>
    </div>
  );
}
