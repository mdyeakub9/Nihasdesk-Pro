import { Badge, Button, Card, FieldError, Label, Switch, Textarea } from '@nihasdesk/ui';
import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { authErrorMessage } from '../auth/errors';
import { useAddAnswer, useTaughtAnswers, useUpdateAnswer, type TaughtAnswer } from './api';

function AnswerItem({ item, canEdit }: { item: TaughtAnswer; canEdit: boolean }) {
  const { t } = useTranslation();
  const update = useUpdateAnswer();
  const [editing, setEditing] = useState(false);
  const [question, setQuestion] = useState(item.question);
  const [answer, setAnswer] = useState(item.answer);
  const ids = { question: useId(), answer: useId(), active: useId() };

  return (
    <li className="flex flex-col gap-2 border-b px-4 py-3 last:border-b-0">
      {editing ? (
        <>
          <Label htmlFor={ids.question}>{t('aiSettings.answers.question')}</Label>
          <Textarea
            id={ids.question}
            rows={2}
            value={question}
            onChange={(e) => setQuestion(e.target.value)}
          />
          <Label htmlFor={ids.answer}>{t('aiSettings.answers.answer')}</Label>
          <Textarea id={ids.answer} rows={3} value={answer} onChange={(e) => setAnswer(e.target.value)} />
          <div className="flex gap-2">
            <Button
              size="sm"
              disabled={!question.trim() || !answer.trim() || update.isPending}
              onClick={() =>
                update.mutate(
                  { id: item.id, question: question.trim(), answer: answer.trim(), active: item.active },
                  { onSuccess: () => setEditing(false) },
                )
              }
            >
              {t('aiSettings.save')}
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setEditing(false)}>
              {t('team.cancel')}
            </Button>
          </div>
        </>
      ) : (
        <>
          <div className="flex items-start gap-2">
            <p className="flex-1 text-sm font-medium">{item.question}</p>
            <Badge tone={item.source === 'taught' ? 'info' : 'neutral'}>
              {t(`aiSettings.answers.sources.${item.source}`)}
            </Badge>
          </div>
          <p className="text-[13px] whitespace-pre-line text-secondary-foreground">{item.answer}</p>
          {canEdit ? (
            <div className="flex flex-wrap items-center gap-3">
              <Switch
                id={ids.active}
                checked={item.active}
                disabled={update.isPending}
                onCheckedChange={(active) =>
                  update.mutate({ id: item.id, question: item.question, answer: item.answer, active })
                }
              />
              <Label htmlFor={ids.active}>{t('aiSettings.answers.active')}</Label>
              <Button size="sm" variant="outline" onClick={() => setEditing(true)}>
                {t('aiSettings.answers.edit')}
              </Button>
            </div>
          ) : (
            !item.active && <p className="text-xs text-muted-foreground">{t('aiSettings.answers.off')}</p>
          )}
        </>
      )}
      {update.error && <FieldError>{authErrorMessage(update.error, t)}</FieldError>}
    </li>
  );
}

/**
 * The shop's FAQ (ADR 0020): answers taught from questions the AI couldn't answer and answers
 * written by hand, in one list. Owner and Admin edit; the API checks it again.
 */
export function Answers({ canEdit }: { canEdit: boolean }) {
  const { t } = useTranslation();
  const { data: answers = [] } = useTaughtAnswers();
  const add = useAddAnswer();
  const ids = { question: useId(), answer: useId() };
  const [question, setQuestion] = useState('');
  const [answer, setAnswer] = useState('');

  return (
    <section aria-labelledby="answers-heading" className="flex flex-col gap-2">
      <h2 id="answers-heading" className="text-base font-semibold">
        {t('aiSettings.answers.title', { count: answers.length })}
      </h2>
      <p className="text-[13px] text-secondary-foreground">{t('aiSettings.answers.intro')}</p>
      {answers.length > 0 && (
        <ul aria-label={t('aiSettings.answers.list')} className="rounded-lg border bg-surface">
          {answers.map((item) => (
            <AnswerItem key={item.id} item={item} canEdit={canEdit} />
          ))}
        </ul>
      )}
      {canEdit && (
        <Card className="flex flex-col gap-2 p-4">
          <h3 className="text-[13px] font-semibold">{t('aiSettings.answers.add')}</h3>
          <Label htmlFor={ids.question}>{t('aiSettings.answers.question')}</Label>
          <Textarea
            id={ids.question}
            rows={2}
            maxLength={1000}
            value={question}
            onChange={(e) => setQuestion(e.target.value)}
          />
          <Label htmlFor={ids.answer}>{t('aiSettings.answers.answer')}</Label>
          <Textarea
            id={ids.answer}
            rows={3}
            maxLength={4000}
            value={answer}
            onChange={(e) => setAnswer(e.target.value)}
          />
          <div>
            <Button
              size="sm"
              disabled={!question.trim() || !answer.trim() || add.isPending}
              onClick={() =>
                add.mutate(
                  { question: question.trim(), answer: answer.trim() },
                  {
                    onSuccess: () => {
                      setQuestion('');
                      setAnswer('');
                    },
                  },
                )
              }
            >
              {t('aiSettings.answers.save')}
            </Button>
          </div>
          {add.error && <FieldError>{authErrorMessage(add.error, t)}</FieldError>}
        </Card>
      )}
    </section>
  );
}
