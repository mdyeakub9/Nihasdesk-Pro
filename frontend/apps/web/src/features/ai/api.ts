import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiGet, apiPost, apiPut } from '../../lib/api';

/* The AI settings, teaching and test chat API (ADR 0019). */

export type AiTone = 'friendly' | 'formal';

export type AiSettings = {
  enabled: boolean;
  tone: AiTone;
  instructions: string | null;
  disclosure: string | null;
  defaultDisclosure: string;
  providerConfigured: boolean;
};

export type UnansweredQuestion = {
  id: string;
  conversationId: string;
  question: string;
  status: 'open' | 'answered' | 'dismissed';
  answerId: string | null;
  createdAt: string;
};

export type TaughtAnswer = {
  id: string;
  question: string;
  answer: string;
  active: boolean;
  updatedAt: string;
  /** taught: from a question the AI couldn't answer; added: written as an FAQ. */
  source: 'taught' | 'added';
};

export type TestMessage = { author: 'customer' | 'ai'; text: string };

export type TestReply = {
  disclosure: string | null;
  reply: string | null;
  handoff: string | null;
  question: string | null;
  blocked: string | null;
  /** Why the AI provider failed, e.g. http_401 (key rejected). */
  failed: string | null;
  tools: string[];
  /** What the answer was based on: shipping zones, a policy (label = topic) or an answer (label = its question). */
  sources: { kind: 'shipping' | 'policy' | 'answer'; label: string | null }[];
};

const keys = {
  settings: ['ai', 'settings'] as const,
  questions: ['ai', 'questions'] as const,
  answers: ['ai', 'answers'] as const,
};

export function useAiSettings() {
  return useQuery({ queryKey: keys.settings, queryFn: () => apiGet<AiSettings>('/api/v1/ai/settings') });
}

export function useSaveAiSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (settings: Pick<AiSettings, 'enabled' | 'tone' | 'instructions' | 'disclosure'>) =>
      apiPut<AiSettings>('/api/v1/ai/settings', settings),
    onSuccess: (saved) => queryClient.setQueryData(keys.settings, saved),
  });
}

export function useUnansweredQuestions() {
  return useQuery({
    queryKey: keys.questions,
    queryFn: () => apiGet<UnansweredQuestion[]>('/api/v1/ai/questions'),
  });
}

export function useTaughtAnswers() {
  return useQuery({ queryKey: keys.answers, queryFn: () => apiGet<TaughtAnswer[]>('/api/v1/ai/answers') });
}

/** Teaching changes both lists: the question leaves, the answer arrives. */
function useTeachingMutation<TArgs>(mutationFn: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: keys.questions }),
        queryClient.invalidateQueries({ queryKey: keys.answers }),
      ]),
  });
}

export const useTeach = () =>
  useTeachingMutation(({ id, answer, question }: { id: string; answer: string; question?: string }) =>
    apiPost(`/api/v1/ai/questions/${encodeURIComponent(id)}/answer`, { answer, question }),
  );

export const useDismissQuestion = () =>
  useTeachingMutation((id: string) => apiPost(`/api/v1/ai/questions/${encodeURIComponent(id)}/dismiss`));

export const useAddAnswer = () =>
  useTeachingMutation((input: { question: string; answer: string }) => apiPost('/api/v1/ai/answers', input));

export const useUpdateAnswer = () =>
  useTeachingMutation(({ id, ...input }: { id: string; question: string; answer: string; active: boolean }) =>
    apiPut(`/api/v1/ai/answers/${encodeURIComponent(id)}`, input),
  );

/** One test turn: the whole typed conversation goes along; nothing is stored. */
export function useTestChat() {
  return useMutation({
    mutationFn: (messages: TestMessage[]) => apiPost<TestReply>('/api/v1/ai/test', { messages }),
  });
}
