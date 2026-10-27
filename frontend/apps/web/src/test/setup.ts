import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, beforeEach, vi } from 'vitest';
import { resetMockInbox } from '../mocks/inbox';
import { resetMockNotifications } from '../mocks/notifications';

beforeEach(() => {
  resetMockInbox();
  resetMockNotifications();
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});
