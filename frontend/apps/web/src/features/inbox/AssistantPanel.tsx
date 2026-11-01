import { Button, FieldError } from '@nihasdesk/ui';
import { ArrowDownToLine, RefreshCw, Smile, Sparkles, Scissors } from 'lucide-react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { authErrorMessage, isErrorCode } from '../auth/errors';
import { useAssist, type AssistSource, type AssistStyle } from './api';
import { useDraftStore } from './drafts';
import { useSuggestionStore } from './suggestions';
import type { Conversation } from './types';

/**
 * The team's AI assistant (PRD 4.4, ADR 0023): a short summary and a suggested reply with the
 * sources used. "Insert into reply" puts it in the composer to edit and send with the normal Send;
 * there is no send button here. Text is plain (SEC-02).
 */
export function AssistantPanel({ conversation }: { conversation: Conversation }) {
  const { t } = useTranslation();
  const assist = useAssist(conversation.id);
  const draft = useDraftStore((state) => state.drafts[conversation.id] ?? '');
  const setDraft = useDraftStore((state) => state.setDraft);
  const setInserted = useSuggestionStore((state) => state.setInserted);
  const result = assist.data;

  function run(style: AssistStyle) {
    assist.mutate(style);
  }

  function insert(reply: string) {
    const text = draft.trim() ? `${draft.trimEnd()}\n\n${reply}` : reply;
    setDraft(conversation.id, text);
    setInserted(conversation.id, reply);
  }

  return (
    <div className="flex flex-col gap-3 px-4 py-3 text-[13px]">
      <p className="text-xs text-muted-foreground">{t('assistant.intro')}</p>

      {!result && (
        <Button size="sm" disabled={assist.isPending} onClick={() => run('default')}>
          <Sparkles aria-hidden="true" />
          {t(assist.isPending ? 'assistant.thinking' : 'assistant.suggest')}
        </Button>
      )}

      {assist.error && (
        <FieldError>
          {isErrorCode(assist.error, 'ai.off')
            ? t('assistant.errors.off')
            : isErrorCode(assist.error, 'ai.suggest_limit')
              ? t('assistant.errors.limit')
              : isErrorCode(assist.error, 'ai.not_configured')
                ? t('assistant.errors.notConfigured')
                : isErrorCode(assist.error, 'ai.no_messages')
                  ? t('assistant.errors.noMessages')
                  : authErrorMessage(assist.error, t)}
        </FieldError>
      )}

      {result && (
        <section
          aria-label={t('assistant.result')}
          aria-busy={assist.isPending}
          className="flex flex-col gap-3"
        >
          {result.summary && (
            <div>
              <h3 className="text-xs font-medium text-muted-foreground">{t('assistant.summary')}</h3>
              <p className="whitespace-pre-line text-secondary-foreground">{result.summary}</p>
            </div>
          )}

          <div>
            <h3 className="text-xs font-medium text-muted-foreground">{t('assistant.reply')}</h3>
            {result.reply ? (
              <p className="whitespace-pre-line rounded-md bg-selected px-3 py-2 text-foreground">
                {result.reply}
              </p>
            ) : (
              <p role="status" className="rounded-md bg-warning px-3 py-2 text-warning-foreground">
                {t(result.failed ? 'assistant.failed' : 'assistant.blocked')}
              </p>
            )}
          </div>

          {result.sources.length > 0 && (
            <p className="text-xs text-muted-foreground">
              {t('assistant.sources', { sources: result.sources.map((s) => SourceName(s, t)).join(', ') })}
            </p>
          )}

          <div className="flex flex-wrap gap-2">
            {result.reply && (
              <Button size="sm" onClick={() => result.reply && insert(result.reply)}>
                <ArrowDownToLine aria-hidden="true" />
                {t('assistant.insert')}
              </Button>
            )}
            <Button size="sm" variant="outline" disabled={assist.isPending} onClick={() => run('shorter')}>
              <Scissors aria-hidden="true" />
              {t('assistant.shorter')}
            </Button>
            <Button size="sm" variant="outline" disabled={assist.isPending} onClick={() => run('friendlier')}>
              <Smile aria-hidden="true" />
              {t('assistant.friendlier')}
            </Button>
            <Button size="sm" variant="ghost" disabled={assist.isPending} onClick={() => run('default')}>
              <RefreshCw aria-hidden="true" />
              {t('assistant.regenerate')}
            </Button>
          </div>
        </section>
      )}
    </div>
  );
}

function SourceName(source: AssistSource, t: TFunction) {
  switch (source.kind) {
    case 'catalog':
      return t('assistant.catalog');
    case 'answer':
      return `“${source.label ?? t('aiTest.sources.answer')}”`;
    case 'policy':
      return t(`aiTest.sources.${source.label ?? 'other'}`, { defaultValue: source.label ?? '' });
    default:
      return t('aiTest.sources.zones');
  }
}
