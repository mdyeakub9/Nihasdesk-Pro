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
  /** "Why this answer" for this test reply. */
  trace: Trace;
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

/* "Why this answer", 👍/👎 and the 👎 export (PRD 4.8, ADR 0026 §6). */

export type Rating = 'up' | 'down';

export type TraceSource = { kind: 'shipping' | 'policy' | 'answer' | 'catalog'; label: string | null };

export type ToolTrace = { name: string; arguments: string; result: string };

export type Trace = {
  outcome: string;
  detail: string | null;
  model: string;
  instructionVersion: string | null;
  inputTokens: number;
  outputTokens: number;
  durationMs: number;
  sources: TraceSource[];
  toolNames: string[];
  /** Empty when the details were removed after the retention period (or the reply is older than traces). */
  tools: ToolTrace[];
  /** `passed`, or why the draft was held back. */
  check: string | null;
  hasDetails: boolean;
  createdAt: string;
  myRating: Rating | null;
};

/** An AI reply of a conversation that has a trace, with the current member's rating. */
export type RatedReply = { messageId: string; rating: Rating | null };

export type EvalCaseExport = {
  id: string;
  question: string | null;
  aiReply: string | null;
  expectedAnswer: string | null;
  expectHandoff: boolean | null;
  ratedAt: string;
};

export type FeedbackExport = { notice: string; cases: EvalCaseExport[] };

// Under the inbox's key, so the inbox's live updates refresh the ratings too.
const repliesKey = (conversationId: string) => ['inbox', 'aiReplies', conversationId] as const;
const traceKey = (messageId: string) => ['ai', 'trace', messageId] as const;

export function useAiReplies(conversationId: string) {
  return useQuery({
    queryKey: repliesKey(conversationId),
    queryFn: () =>
      apiGet<RatedReply[]>(`/api/v1/ai/conversations/${encodeURIComponent(conversationId)}/replies`),
  });
}

/** Loaded only when the team opens "Why this answer". */
export function useTrace(messageId: string, enabled: boolean) {
  return useQuery({
    queryKey: traceKey(messageId),
    queryFn: () => apiGet<Trace>(`/api/v1/ai/messages/${encodeURIComponent(messageId)}/trace`),
    enabled,
  });
}

export function useRateReply(conversationId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ messageId, rating }: { messageId: string; rating: Rating | null }) =>
      apiPut<RatedReply>(`/api/v1/ai/messages/${encodeURIComponent(messageId)}/feedback`, { rating }),
    onSuccess: (saved) => {
      queryClient.setQueryData<RatedReply[]>(repliesKey(conversationId), (current) =>
        current?.map((r) => (r.messageId === saved.messageId ? saved : r)),
      );
      queryClient.setQueryData<Trace>(traceKey(saved.messageId), (current) =>
        current ? { ...current, myRating: saved.rating } : current,
      );
    },
  });
}

/** Owner/Admin: 👎 replies as eval cases, to clean of personal data before use. */
export function useFeedbackExport() {
  return useMutation({ mutationFn: () => apiGet<FeedbackExport>('/api/v1/ai/feedback/export') });
}
