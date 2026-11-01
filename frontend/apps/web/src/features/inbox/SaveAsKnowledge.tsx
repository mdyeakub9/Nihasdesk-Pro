import { Button, FieldError, Label, Textarea } from '@nihasdesk/ui';
import { BookPlus } from 'lucide-react';
import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAddAnswer } from '../ai/api';
import { authErrorMessage } from '../auth/errors';
import { useSuggestionStore } from './suggestions';

/**
 * After the team sent an edited AI suggestion (ADR 0023): offer to keep the answer in the FAQ
 * the AI searches (PRD 4.7 learning loop). Owner and Admin only, like teaching the AI (`ai.teach`).
 */
export function SaveAsKnowledge({ conversationId }: { conversationId: string }) {
  const { t } = useTranslation();
  const offer = useSuggestionStore((state) => state.offer);
  const setOffer = useSuggestionStore((state) => state.setOffer);
  const add = useAddAnswer();
  const [question, setQuestion] = useState(offer?.question ?? '');
  const [answer, setAnswer] = useState(offer?.answer ?? '');
  const ids = { question: useId(), answer: useId() };

  if (!offer || offer.conversationId !== conversationId) return null;

  return (
    <section
      aria-label={t('assistant.knowledge.title')}
      className="flex flex-col gap-2 rounded-md border bg-subtle px-3 py-2 text-[13px]"
    >
      <p className="flex items-center gap-2 font-medium">
        <BookPlus className="size-4" aria-hidden="true" />
        {t('assistant.knowledge.offer')}
      </p>
      <Label htmlFor={ids.question}>{t('assistant.knowledge.question')}</Label>
      <Textarea id={ids.question} rows={2} value={question} onChange={(e) => setQuestion(e.target.value)} />
      <Label htmlFor={ids.answer}>{t('assistant.knowledge.answer')}</Label>
      <Textarea id={ids.answer} rows={3} value={answer} onChange={(e) => setAnswer(e.target.value)} />
      {add.error && <FieldError>{authErrorMessage(add.error, t)}</FieldError>}
      <div className="flex gap-2">
        {/* type="button": this sits inside the composer's form, which must not be submitted from here. */}
        <Button
          type="button"
          size="sm"
          disabled={!question.trim() || !answer.trim() || add.isPending}
          onClick={() =>
            add.mutate(
              { question: question.trim(), answer: answer.trim() },
              { onSuccess: () => setOffer(null) },
            )
          }
        >
          {t('assistant.knowledge.save')}
        </Button>
        <Button type="button" size="sm" variant="ghost" onClick={() => setOffer(null)}>
          {t('assistant.knowledge.notNow')}
        </Button>
      </div>
    </section>
  );
}
