import type { RouteObject } from 'react-router';
import { InboxPage } from './features/inbox/InboxPage';
import { NotificationsPage } from './features/notifications/NotificationsPage';
import { AppShell } from './layouts/AppShell';
import { ComingSoonPage } from './pages/ComingSoonPage';
import { HomePage } from './pages/HomePage';
import { NotFoundPage } from './pages/NotFoundPage';

/** Phase 1 sections that only have a placeholder so far. */
const comingSoon = ['orders', 'leads', 'customers', 'products', 'analytics', 'settings'] as const;

export const routes: RouteObject[] = [
  {
    path: '/',
    element: <AppShell />,
    children: [
      { index: true, element: <HomePage /> },
      { path: 'inbox', element: <InboxPage /> },
      { path: 'inbox/:conversationId', element: <InboxPage /> },
      { path: 'notifications', element: <NotificationsPage /> },
      ...comingSoon.map((section) => ({ path: section, element: <ComingSoonPage section={section} /> })),
      { path: '*', element: <NotFoundPage /> },
    ],
  },
];
