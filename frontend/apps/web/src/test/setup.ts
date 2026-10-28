import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, beforeEach, vi } from 'vitest';
import { resetCsrfToken } from '../lib/api';
import { resetMockInbox } from '../mocks/inbox';
import { resetMockNotifications } from '../mocks/notifications';

// jsdom has no ResizeObserver; Radix (e.g. Checkbox) needs one. Browsers have it.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
};

beforeEach(() => {
  resetCsrfToken();
  resetMockInbox();
  resetMockNotifications();
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});
