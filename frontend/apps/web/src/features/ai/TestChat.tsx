import { Button, Card, cn, FieldError, Textarea } from '@nihasdesk/ui';
import { RotateCcw, Send } from 'lucide-react';
import { useState, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { authErrorMessage } from '../auth/errors';
import { useTestChat, type TestMessage } from './api';

type Line =
  | { kind: 'customer'; text: string }
  | { kind: 'ai'; text: string; disclosure?: boolean }
  | { kind: 'notice'; text: string };

/**
 * Try the AI before going live (PRD 4.8): type as a customer, see what the AI would answer with
 * the saved settings, the catalog and taught answers. Nothing is stored or sent to anyone.
 */
export function TestChat({ providerConfigured }: { providerConfigured: boolean }) {
  const { t } = useTranslation();
  const test = useTestChat();
  const [lines, setLines] = useState<Line[]>([]);
  const [text, setText] = useState('');

  function submit(event?: FormEvent) {
    event?.preventDefault();
    const value = text.trim();
    if (!value || test.isPending) return;

    const next: Line[] = [...lines, { kind: 'customer', text: value }];
    setLines(next);
    setText('');
    // The disclosure is the shop's fixed text, not part of what the AI said.
    const history: TestMessage[] = next.flatMap((line): TestMessage[] =>
      line.kind === 'customer'
        ? [{ author: 'customer', text: line.text }]
        : line.kind === 'ai' && !line.disclosure
          ? [{ author: 'ai', text: line.text }]
          : [],
    );
    test.mutate(history, {
      onSuccess: (result) => {
        const added: Line[] = [];
        if (result.disclosure) added.push({ kind: 'ai', text: result.disclosure, disclosure: true });
        if (result.reply) added.push({ kind: 'ai', text: result.reply });
        if (result.failed) {
          added.push({
            kind: 'notice',
            text: t('aiTest.failed', {
              reason: t(`aiTest.failedReasons.${result.failed}`, {
                defaultValue: t('aiTest.failedReasons.other', { code: result.failed }),
              }),
            }),
          });
        } else if (result.handoff) {
          added.push({
            kind: 'notice',
            text:
              t('aiTest.handoff', {
                reason: t(`aiTest.reasons.${result.handoff}`, { defaultValue: result.handoff }),
              }) + (result.handoff === 'unknown_answer' ? ` ${t('aiTest.unknownHint')}` : ''),
          });
        }
        if (result.blocked) {
          added.push({
            kind: 'notice',
            text: t('aiTest.blocked', {
              reason: t(`aiTest.blockedReasons.${result.blocked}`, { defaultValue: result.blocked }),
            }),
          });
        }
        if (result.tools.length > 0)
          added.push({ kind: 'notice', text: t('aiTest.tools', { tools: result.tools.join(', ') }) });
        setLines((current) => [...current, ...added]);
      },
    });
  }

  function onKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      submit();
    }
  }

  return (
    <Card className="flex h-fit flex-col gap-3 xl:sticky xl:top-0">
      <div className="flex items-center gap-2">
        <h2 className="flex-1 text-base font-semibold">{t('aiTest.title')}</h2>
        {lines.length > 0 && (
          <Button
            size="sm"
            variant="ghost"
            onClick={() => {
              setLines([]);
              test.reset();
            }}
          >
            <RotateCcw aria-hidden="true" />
            {t('aiTest.reset')}
          </Button>
        )}
      </div>
      <p className="text-[13px] text-secondary-foreground">{t('aiTest.intro')}</p>
      <ol
        aria-label={t('aiTest.messages')}
        aria-live="polite"
        className="flex max-h-120 min-h-40 flex-col gap-2 overflow-y-auto rounded-md bg-subtle p-3"
      >
        {lines.map((line, index) => (
          <li
            key={index}
            className={cn(
              'max-w-[85%] rounded-lg px-3 py-2 text-[13px]',
              line.kind === 'customer' && 'self-end bg-selected text-selected-foreground',
              line.kind === 'ai' && 'self-start bg-surface',
              line.kind === 'notice' && 'self-center bg-transparent text-xs text-muted-foreground',
            )}
          >
            {line.text}
          </li>
        ))}
        {test.isPending && (
          <li className="self-start text-xs text-muted-foreground">{t('aiTest.thinking')}</li>
        )}
      </ol>
      <form onSubmit={submit} className="flex flex-col gap-2">
        <Textarea
          aria-label={t('aiTest.input')}
          placeholder={t('aiTest.placeholder')}
          rows={2}
          maxLength={1000}
          disabled={!providerConfigured}
          value={text}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={onKeyDown}
        />
        <div className="flex justify-end">
          <Button type="submit" size="sm" disabled={!providerConfigured || !text.trim() || test.isPending}>
            <Send aria-hidden="true" />
            {t('aiTest.send')}
          </Button>
        </div>
      </form>
      {!providerConfigured && (
        <p className="text-xs text-muted-foreground">{t('aiSettings.notConfigured')}</p>
      )}
      {test.error && <FieldError>{authErrorMessage(test.error, t)}</FieldError>}
    </Card>
  );
}
