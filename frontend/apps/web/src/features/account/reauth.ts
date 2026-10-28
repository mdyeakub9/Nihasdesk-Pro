import { create } from 'zustand';
import { ApiError } from '../../lib/api';

type ReauthState = {
  open: boolean;
  resolve: ((confirmed: boolean) => void) | null;
  /** Opens the password dialog; resolves true once the password was accepted. */
  request: () => Promise<boolean>;
  close: (confirmed: boolean) => void;
};

/** One password dialog for the whole app (ADR 0015). Client-only UI state, nothing persisted. */
export const useReauthStore = create<ReauthState>()((set, get) => ({
  open: false,
  resolve: null,
  request: () =>
    new Promise<boolean>((resolve) => {
      get().resolve?.(false);
      set({ open: true, resolve });
    }),
  close: (confirmed) => {
    get().resolve?.(confirmed);
    set({ open: false, resolve: null });
  },
}));

/**
 * Runs a risky action; if the API asks for the password again (auth.reauth_required), shows the
 * dialog and retries once after a successful re-check.
 */
export async function withReauth<T>(action: () => Promise<T>): Promise<T> {
  try {
    return await action();
  } catch (error) {
    if (
      error instanceof ApiError &&
      error.code === 'auth.reauth_required' &&
      (await useReauthStore.getState().request())
    ) {
      return action();
    }
    throw error;
  }
}
