import { Button, Card, FieldError, Input } from '@nihasdesk/ui';
import { Search } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { authErrorMessage } from '../auth/errors';
import { useSetSourceUsed, useTestQuestion, type KnowledgeSource } from './api';

const maxQuestionLength = 300;

/** Puts a read source in use for customers, or takes it out, at once (Issue #53; draft and publish come with #55). */
export function SourceUse({ source, conflicts }: { source: KnowledgeSource; conflicts: number }) {
  const { t } = useTranslation();
  const setUsed = useSetSourceUsed(source.id);
  const live = source.status === 'live';

  return (
    <div className="flex flex-col gap-2 border-t pt-3">
      <p className="text-[13px] text-secondary-foreground">
        {t(live ? 'sources.review.useLive' : 'sources.review.useReview')}
      </p>
      {!live && conflicts > 0 && (
        <p className="text-[13px] text-warning-foreground">
          {t('sources.review.useConflicts', { count: conflicts })}
        </p>
      )}
      <Button
        size="sm"
        variant={live ? 'ghost' : 'default'}
        className="self-start"
        disabled={setUsed.isPending}
        onClick={() => setUsed.mutate(!live)}
      >
        {t(live ? 'sources.review.stop' : 'sources.review.use')}
      </Button>
      {setUsed.error && <FieldError>{authErrorMessage(setUsed.error, t)}</FieldError>}
    </div>
  );
}

/** What the search finds in this source for a question. It shows the sections; the AI writes no reply here. */
export function TestQuestion({ sourceId }: { sourceId: string }) {
  const { t } = useTranslation();
  const inputId = useId();
  const [question, setQuestion] = useState('');
  const test = useTestQuestion(sourceId);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const value = question.trim();
    if (value) test.mutate(value);
  };

  return (
    <Card className="flex flex-col gap-2">
      <h2 className="text-sm font-semibold">{t('sources.review.testTitle')}</h2>
      <p className="text-[13px] text-secondary-foreground">{t('sources.review.testIntro')}</p>
      <form onSubmit={submit} className="flex items-end gap-2">
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <label htmlFor={inputId} className="text-xs font-medium">
            {t('sources.review.testLabel')}
          </label>
          <Input
            id={inputId}
            value={question}
            maxLength={maxQuestionLength}
            onChange={(event) => setQuestion(event.target.value)}
          />
        </div>
        <Button type="submit" size="sm" disabled={test.isPending || question.trim().length === 0}>
          <Search aria-hidden="true" />
          {t('sources.review.testButton')}
        </Button>
      </form>
      {test.error && <FieldError>{authErrorMessage(test.error, t)}</FieldError>}
      {test.data &&
        (test.data.length === 0 ? (
          <p className="text-[13px] text-secondary-foreground">{t('sources.review.testNone')}</p>
        ) : (
          <ol aria-label={t('sources.review.testFound')} className="flex flex-col gap-2">
            {test.data.map((passage) => (
              <li key={passage.sectionId} className="rounded-md bg-subtle px-3 py-2">
                <p className="text-xs text-muted-foreground">
                  {[
                    passage.headingPath ?? t('sources.review.noHeading'),
                    passage.page ? t('sources.review.page', { page: passage.page }) : null,
                  ]
                    .filter(Boolean)
                    .join(' · ')}
                </p>
                {/* The shop's imported text: plain text only (SEC-02). */}
                <p className="mt-1 text-[13px] break-words whitespace-pre-wrap">{passage.text}</p>
              </li>
            ))}
          </ol>
        ))}
    </Card>
  );
}
