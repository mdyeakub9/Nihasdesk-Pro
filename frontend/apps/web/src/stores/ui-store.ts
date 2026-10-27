import { create } from 'zustand';

/** Client-only UI state. Server data belongs in TanStack Query, not here (ADR 0007). */
type UiState = {
  sidebarOpen: boolean;
  toggleSidebar: () => void;
};

export const useUiStore = create<UiState>()((set) => ({
  sidebarOpen: true,
  toggleSidebar: () => set((state) => ({ sidebarOpen: !state.sidebarOpen })),
}));
