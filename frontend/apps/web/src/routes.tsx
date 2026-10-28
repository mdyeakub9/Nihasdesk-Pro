import { Navigate, type RouteObject } from 'react-router';
import { AuthLayout } from './features/auth/AuthLayout';
import { CheckEmailPage } from './features/auth/CheckEmailPage';
import { ForgotPasswordPage } from './features/auth/ForgotPasswordPage';
import { LoginPage } from './features/auth/LoginPage';
import { ResetPasswordPage } from './features/auth/ResetPasswordPage';
import { SignupPage } from './features/auth/SignupPage';
import { VerifyEmailPage } from './features/auth/VerifyEmailPage';
import { InboxPage } from './features/inbox/InboxPage';
import { NotificationsPage } from './features/notifications/NotificationsPage';
import { SecurityPage } from './features/account/SecurityPage';
import { RedirectIfSignedIn, RequireSession } from './features/session/guards';
import { SettingsLayout } from './features/settings/SettingsLayout';
import { AcceptInvitationPage } from './features/team/AcceptInvitationPage';
import { TeamPage } from './features/team/TeamPage';
import { AppShell } from './layouts/AppShell';
import { ComingSoonPage } from './pages/ComingSoonPage';
import { HomePage } from './pages/HomePage';
import { NotFoundPage } from './pages/NotFoundPage';

/** Phase 1 sections that only have a placeholder so far. */
const comingSoon = ['orders', 'leads', 'customers', 'products', 'analytics'] as const;

export const routes: RouteObject[] = [
  {
    // Public pages. Links from emails (verify, reset) work whether or not someone is signed in.
    element: <AuthLayout />,
    children: [
      {
        path: 'login',
        element: (
          <RedirectIfSignedIn>
            <LoginPage />
          </RedirectIfSignedIn>
        ),
      },
      {
        path: 'signup',
        element: (
          <RedirectIfSignedIn>
            <SignupPage />
          </RedirectIfSignedIn>
        ),
      },
      { path: 'check-email', element: <CheckEmailPage /> },
      { path: 'verify-email', element: <VerifyEmailPage /> },
      { path: 'forgot-password', element: <ForgotPasswordPage /> },
      { path: 'reset-password', element: <ResetPasswordPage /> },
      { path: 'accept-invitation', element: <AcceptInvitationPage /> },
    ],
  },
  {
    path: '/',
    element: (
      <RequireSession>
        <AppShell />
      </RequireSession>
    ),
    children: [
      { index: true, element: <HomePage /> },
      { path: 'inbox', element: <InboxPage /> },
      { path: 'inbox/:conversationId', element: <InboxPage /> },
      { path: 'notifications', element: <NotificationsPage /> },
      {
        path: 'settings',
        element: <SettingsLayout />,
        children: [
          { index: true, element: <Navigate to="team" replace /> },
          { path: 'team', element: <TeamPage /> },
          { path: 'security', element: <SecurityPage /> },
        ],
      },
      ...comingSoon.map((section) => ({ path: section, element: <ComingSoonPage section={section} /> })),
      { path: '*', element: <NotFoundPage /> },
    ],
  },
];
