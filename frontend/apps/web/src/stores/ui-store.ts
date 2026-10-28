import { create } from 'zustand';

/** Client-only UI state. Server data belongs in TanStack Query, not here (ADR 0007). */
export type Theme = 'light' | 'dark';

type UiState = {
  /** Mobile navigation drawer; the sidebar is always visible on wide screens. */
  sidebarOpen: boolean;
  /** Starts from the system setting; not persisted until user preferences exist server side (SEC-14). */
  theme: Theme;
  notificationBannerDismissed: boolean;
  /** Owner reminder to turn on two-step sign-in (ADR 0015); session only. */
  mfaReminderDismissed: boolean;
  toggleSidebar: () => void;
  closeSidebar: () => void;
  setTheme: (theme: Theme) => void;
  dismissNotificationBanner: () => void;
  dismissMfaReminder: () => void;
};

function systemTheme(): Theme {
  return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-color-scheme: dark)').matches
    ? 'dark'
    : 'light';
}

export const useUiStore = create<UiState>()((set) => ({
  sidebarOpen: false,
  theme: systemTheme(),
  notificationBannerDismissed: false,
  mfaReminderDismissed: false,
  toggleSidebar: () => set((state) => ({ sidebarOpen: !state.sidebarOpen })),
  closeSidebar: () => set({ sidebarOpen: false }),
  setTheme: (theme) => set({ theme }),
  dismissNotificationBanner: () => set({ notificationBannerDismissed: true }),
  dismissMfaReminder: () => set({ mfaReminderDismissed: true }),
}));
