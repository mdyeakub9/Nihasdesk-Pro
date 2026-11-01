import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { afterEach, beforeEach, vi } from 'vitest';
import { useDraftStore } from '../features/inbox/drafts';
import { useSuggestionStore } from '../features/inbox/suggestions';
import { useTypingStore } from '../features/inbox/typing';
import { resetCsrfToken } from '../lib/api';
import { resetMockNotifications } from '../mocks/notifications';
import { hub } from './signalr-mock';

// findBy*/waitFor wait up to 3 s: whole-app renders can take over the 1 s default on a busy machine or CI.
configure({ asyncUtilTimeout: 3000 });

// No real hub connections in tests; tests play the server through `hub`.
vi.mock('@microsoft/signalr', () => import('./signalr-mock'));

// jsdom has no ResizeObserver; Radix (e.g. Checkbox) needs one. Browsers have it.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
};

beforeEach(() => {
  resetCsrfToken();
  hub.reset();
  resetMockNotifications();
  // Client-only inbox state lives in module stores: start each test empty.
  useDraftStore.setState({ drafts: {} });
  useTypingStore.setState({ typing: {} });
  useSuggestionStore.setState({ inserted: {}, offer: null });
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});
