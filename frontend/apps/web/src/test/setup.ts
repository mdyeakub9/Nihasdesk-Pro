import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, beforeEach, vi } from 'vitest';
import { resetCsrfToken } from '../lib/api';
import { resetMockNotifications } from '../mocks/notifications';
import { hub } from './signalr-mock';

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
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});
