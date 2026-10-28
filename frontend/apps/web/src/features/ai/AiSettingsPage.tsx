import { Badge, Button, Card, FieldError, Label, NativeSelect, Switch, Textarea } from '@nihasdesk/ui';
import { MessageCircleQuestion, Sparkles } from 'lucide-react';
import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { authErrorMessage } from '../auth/errors';
import { useSession } from '../session/useSession';
import {
  useAddAnswer,
  useAiSettings,
  useDismissQuestion,
  useSaveAiSettings,
  useTaughtAnswers,
  useTeach,
  useUnansweredQuestions,
  useUpdateAnswer,
  type AiSettings,
  type AiTone,
  type TaughtAnswer,
  type UnansweredQuestion,
} from './api';
import { TestChat } from './TestChat';

const canManage = (role: string | undefined) => role === 'owner' || role === 'admin';

function SettingsForm({ settings }: { settings: AiSettings }) {
  const { t } = useTranslation();
  const save = useSaveAiSettings();
  const ids = { enabled: useId(), tone: useId(), instructions: useId(), disclosure: useId() };
  const [enabled, setEnabled] = useState(settings.enabled);
  const [tone, setTone] = useState<AiTone>(settings.tone);
  const [instructions, setInstructions] = useState(settings.instructions ?? '');
  const [disclosure, setDisclosure] = useState(settings.disclosure ?? '');

  return (
    <Card className="flex flex-col gap-4">
      <div className="flex items-start gap-3">
        <Sparkles className="mt-0.5 size-5 text-secondary-foreground" aria-hidden="true" />
        <div className="flex flex-1 flex-col gap-1">
          <h2 className="text-base font-semibold">{t('aiSettings.title')}</h2>
          <p className="text-[13px] text-secondary-foreground">{t('aiSettings.intro')}</p>
        </div>
      </div>
      {!settings.providerConfigured && (
        <p role="note" className="rounded-md bg-warning px-3 py-2 text-[13px] text-warning-foreground">
          {t('aiSettings.notConfigured')}
        </p>
      )}
      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault();
          save.mutate({
            enabled,
            tone,
            instructions: instructions.trim() || null,
            disclosure: disclosure.trim() || null,
          });
        }}
      >
        <div className="flex items-center gap-3">
          <Switch id={ids.enabled} checked={enabled} onCheckedChange={setEnabled} />
          <Label htmlFor={ids.enabled}>{t('aiSettings.enabled')}</Label>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor={ids.tone}>{t('aiSettings.tone')}</Label>
          <NativeSelect
            id={ids.tone}
            className="h-9 md:w-60"
            value={tone}
            onChange={(e) => setTone(e.target.value as AiTone)}
          >
            <option value="friendly">{t('aiSettings.tones.friendly')}</option>
            <option value="formal">{t('aiSettings.tones.formal')}</option>
          </NativeSelect>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor={ids.instructions}>{t('aiSettings.instructions')}</Label>
          <Textarea
            id={ids.instructions}
            rows={4}
            maxLength={2000}
            placeholder={t('aiSettings.instructionsPlaceholder')}
            value={instructions}
            onChange={(e) => setInstructions(e.target.value)}
          />
          <p className="text-xs text-muted-foreground">{t('aiSettings.instructionsHint')}</p>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor={ids.disclosure}>{t('aiSettings.disclosure')}</Label>
          <Textarea
            id={ids.disclosure}
            rows={2}
            maxLength={300}
            placeholder={settings.defaultDisclosure}
            value={disclosure}
            onChange={(e) => setDisclosure(e.target.value)}
          />
          <p className="text-xs text-muted-foreground">{t('aiSettings.disclosureHint')}</p>
        </div>
        <div className="flex items-center gap-3">
          <Button type="submit" disabled={save.isPending}>
            {t('aiSettings.save')}
          </Button>
          {save.isSuccess && (
            <span className="text-[13px] text-success-foreground">{t('aiSettings.saved')}</span>
          )}
        </div>
        {save.error && <FieldError>{authErrorMessage(save.error, t)}</FieldError>}
      </form>
    </Card>
  );
}

function QuestionItem({ question }: { question: UnansweredQuestion }) {
  const { t } = useTranslation();
  const teach = useTeach();
  const dismiss = useDismissQuestion();
  const answerId = useId();
  const [answer, setAnswer] = useState('');

  return (
    <li className="flex flex-col gap-2 border-b px-4 py-3 last:border-b-0">
      <p className="text-sm font-medium">“{question.question}”</p>
      <Link to={`/inbox/${question.conversationId}`} className="text-xs text-info-foreground hover:underline">
        {t('aiSettings.questions.open')}
      </Link>
      <label htmlFor={answerId} className="text-[13px] font-medium">
        {t('aiSettings.questions.answerLabel')}
      </label>
      <Textarea
        id={answerId}
        rows={2}
        maxLength={4000}
        value={answer}
        onChange={(e) => setAnswer(e.target.value)}
      />
      <div className="flex flex-wrap gap-2">
        <Button
          size="sm"
          disabled={!answer.trim() || teach.isPending}
          onClick={() => teach.mutate({ id: question.id, answer: answer.trim() })}
        >
          {t('aiSettings.questions.teach')}
        </Button>
        <Button
          size="sm"
          variant="ghost"
          disabled={dismiss.isPending}
          onClick={() => dismiss.mutate(question.id)}
        >
          {t('aiSettings.questions.dismiss')}
        </Button>
      </div>
      {(teach.error ?? dismiss.error) && (
        <FieldError>{authErrorMessage(teach.error ?? dismiss.error, t)}</FieldError>
      )}
    </li>
  );
}

function Questions() {
  const { t } = useTranslation();
  const { data: questions = [], isPending } = useUnansweredQuestions();

  return (
    <section aria-labelledby="questions-heading" className="flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <MessageCircleQuestion className="size-4 text-secondary-foreground" aria-hidden="true" />
        <h2 id="questions-heading" className="text-sm font-semibold">
          {t('aiSettings.questions.title')}
        </h2>
        {questions.length > 0 && <Badge tone="warning">{questions.length}</Badge>}
      </div>
      <p className="text-[13px] text-secondary-foreground">{t('aiSettings.questions.intro')}</p>
      {!isPending && questions.length === 0 ? (
        <p className="rounded-lg border bg-surface px-4 py-6 text-center text-[13px] text-secondary-foreground">
          {t('aiSettings.questions.empty')}
        </p>
      ) : (
        <ul
          aria-busy={isPending}
          aria-label={t('aiSettings.questions.title')}
          className="rounded-lg border bg-surface"
        >
          {questions.map((question) => (
            <QuestionItem key={question.id} question={question} />
          ))}
        </ul>
      )}
    </section>
  );
}

function AnswerItem({ item }: { item: TaughtAnswer }) {
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
          <p className="text-sm font-medium">{item.question}</p>
          <p className="text-[13px] whitespace-pre-line text-secondary-foreground">{item.answer}</p>
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
        </>
      )}
      {update.error && <FieldError>{authErrorMessage(update.error, t)}</FieldError>}
    </li>
  );
}

function Answers() {
  const { t } = useTranslation();
  const { data: answers = [] } = useTaughtAnswers();
  const add = useAddAnswer();
  const ids = { question: useId(), answer: useId() };
  const [question, setQuestion] = useState('');
  const [answer, setAnswer] = useState('');

  return (
    <section aria-labelledby="answers-heading" className="flex flex-col gap-2">
      <h2 id="answers-heading" className="text-sm font-semibold">
        {t('aiSettings.answers.title', { count: answers.length })}
      </h2>
      <p className="text-[13px] text-secondary-foreground">{t('aiSettings.answers.intro')}</p>
      {answers.length > 0 && (
        <ul aria-label={t('aiSettings.answers.list')} className="rounded-lg border bg-surface">
          {answers.map((item) => (
            <AnswerItem key={item.id} item={item} />
          ))}
        </ul>
      )}
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
    </section>
  );
}

/** Settings → AI (PRD 4.8, ADR 0019): how the AI works, what it couldn't answer, what it learned, test chat. */
export function AiSettingsPage() {
  const { t } = useTranslation();
  const { data: session } = useSession();
  const { data: settings } = useAiSettings();

  if (!canManage(session?.role)) {
    return (
      <p role="note" className="max-w-3xl text-sm text-secondary-foreground">
        {t('aiSettings.ownersOnly')}
      </p>
    );
  }

  return (
    <div className="grid max-w-6xl gap-6 xl:grid-cols-[minmax(0,1fr)_380px]">
      <div className="flex min-w-0 flex-col gap-6">
        {settings && <SettingsForm key={String(settings.enabled) + settings.tone} settings={settings} />}
        <Questions />
        <Answers />
      </div>
      <TestChat providerConfigured={settings?.providerConfigured ?? false} />
    </div>
  );
}
