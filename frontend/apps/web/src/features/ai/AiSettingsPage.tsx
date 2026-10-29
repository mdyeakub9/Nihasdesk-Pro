import { Badge, Button, Card, FieldError, Label, NativeSelect, Switch, Textarea } from '@nihasdesk/ui';
import { MessageCircleQuestion, Sparkles } from 'lucide-react';
import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { authErrorMessage } from '../auth/errors';
import { useSession } from '../session/useSession';
import {
  useAiSettings,
  useDismissQuestion,
  useSaveAiSettings,
  useTeach,
  useUnansweredQuestions,
  type AiSettings,
  type AiTone,
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

/**
 * Settings → AI (PRD 4.8, ADR 0019): how the AI works, what it couldn't answer, test chat. What it
 * knows (policies, shipping, FAQ) is under Settings → Shop knowledge (ADR 0020).
 */
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
        <p className="rounded-lg border bg-surface px-4 py-3 text-[13px] text-secondary-foreground">
          {t('aiSettings.knowledgeHint')}{' '}
          <Link to="/settings/knowledge" className="font-medium text-info-foreground hover:underline">
            {t('aiSettings.knowledgeLink')}
          </Link>
        </p>
      </div>
      <TestChat providerConfigured={settings?.providerConfigured ?? false} />
    </div>
  );
}
