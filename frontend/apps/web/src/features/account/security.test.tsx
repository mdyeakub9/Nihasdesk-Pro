import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { Session } from '../session/types';
import { callsTo, fakeApi, json, problem, testSession } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';
import { deviceLabel } from './device';

const ownerWithoutMfa: Session = { ...testSession, user: { ...testSession.user, mfaEnabled: false } };
const password = 'tramonto sul lago verde';

/** First call answers "password again", later calls succeed — like the API after a re-auth. */
function reauthThen(ok: () => Response) {
  let calls = 0;
  return () => (++calls === 1 ? problem(403, 'auth.reauth_required') : ok());
}

describe('Two-step sign-in at login', () => {
  it('asks for the code after the password and then signs in', async () => {
    const fetchMock = fakeApi({
      session: null,
      post: {
        '/api/v1/auth/login': () => json({ mfaRequired: true }),
        '/api/v1/auth/login/mfa': () => json(testSession),
      },
    });
    await renderApp('/login');

    await userEvent.type(await screen.findByLabelText('Email'), 'owner@example.com');
    await userEvent.type(screen.getByLabelText('Password'), password);
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByRole('heading', { name: 'Two-step sign-in' })).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Code'), '123456');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByRole('heading', { name: 'Conversations' })).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/auth/login/mfa')[0]?.body).toEqual({ code: '123456' });
  });

  it('accepts a recovery code and explains wrong or expired codes', async () => {
    let attempt = 0;
    const fetchMock = fakeApi({
      session: null,
      post: {
        '/api/v1/auth/login': () => json({ mfaRequired: true }),
        '/api/v1/auth/login/mfa': () =>
          ++attempt === 1
            ? problem(401, 'auth.invalid_mfa_code')
            : problem(401, 'auth.mfa_challenge_expired'),
      },
    });
    await renderApp('/login');
    await userEvent.type(await screen.findByLabelText('Email'), 'owner@example.com');
    await userEvent.type(screen.getByLabelText('Password'), password);
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await userEvent.click(await screen.findByRole('button', { name: 'Use a recovery code instead' }));
    await userEvent.type(screen.getByLabelText('Recovery code'), 'ABCDE-12345');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByText(/That code isn’t right/)).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/auth/login/mfa')[0]?.body).toEqual({ recoveryCode: 'ABCDE-12345' });

    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Back to sign in' }));
    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
  });
});

describe('Owner reminder', () => {
  it('reminds Owners without two-step sign-in and can be dismissed', async () => {
    fakeApi({ session: ownerWithoutMfa });
    await renderApp('/inbox');

    const reminder = await screen.findByRole('note');
    expect(within(reminder).getByRole('link', { name: 'Turn it on' })).toHaveAttribute(
      'href',
      '/settings/security',
    );
    await userEvent.click(within(reminder).getByRole('button', { name: 'Not now' }));
    expect(screen.queryByRole('note')).not.toBeInTheDocument();
  });

  it('is not shown to Members or to Owners who already use it', async () => {
    fakeApi({ session: { ...ownerWithoutMfa, role: 'member' } });
    await renderApp('/inbox');
    await screen.findByRole('heading', { name: 'Conversations' });
    expect(screen.queryByRole('note')).not.toBeInTheDocument();
  });
});

describe('Settings → Security', () => {
  it('sets up two-step sign-in with a QR code and shows recovery codes once', async () => {
    let enabled = false;
    const codes = Array.from({ length: 10 }, (_, i) => `CODE${i}-ABCDE`);
    const fetchMock = fakeApi({
      session: ownerWithoutMfa,
      routes: { 'GET /api/v1/account/mfa': () => json({ enabled, recoveryCodesLeft: enabled ? 10 : 0 }) },
      post: {
        '/api/v1/account/mfa/setup': () =>
          json({
            secret: 'JBSWY3DPEHPK3PXP',
            otpauthUri: 'otpauth://totp/Nihasdesk:owner%40example.com?secret=JBSWY3DPEHPK3PXP',
          }),
        '/api/v1/account/mfa/enable': () => {
          enabled = true;
          return json({ recoveryCodes: codes });
        },
      },
    });
    await renderApp('/settings/security');

    await userEvent.click(await screen.findByRole('button', { name: 'Set up' }));
    const qr = await screen.findByRole('img', { name: 'QR code for your authenticator app' });
    expect(qr.getAttribute('src')).toMatch(/^data:image\/svg\+xml/);
    expect(screen.getByText('JBSW Y3DP EHPK 3PXP')).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Code'), '654321');
    await userEvent.click(screen.getByRole('button', { name: 'Turn on' }));

    const list = await screen.findByRole('list', { name: 'Recovery codes' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(10);
    expect(callsTo(fetchMock, '/api/v1/account/mfa/enable')[0]?.body).toEqual({ code: '654321' });
    await userEvent.click(screen.getByRole('button', { name: 'I’ve saved them' }));
    expect(await screen.findByText('10 recovery codes left')).toBeInTheDocument();
  });

  it('asks for the password before turning it off, then retries', async () => {
    const fetchMock = fakeApi({
      routes: { 'GET /api/v1/account/mfa': () => json({ enabled: true, recoveryCodesLeft: 8 }) },
      post: { '/api/v1/account/mfa/disable': reauthThen(() => new Response(null, { status: 204 })) },
    });
    await renderApp('/settings/security');

    await userEvent.click(await screen.findByRole('button', { name: 'Turn off' }));
    await userEvent.click(screen.getByRole('button', { name: 'Yes, turn off' }));

    const dialog = await screen.findByRole('dialog', { name: 'Confirm it’s you' });
    await userEvent.type(within(dialog).getByLabelText('Password'), password);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    await waitFor(() => expect(callsTo(fetchMock, '/api/v1/account/mfa/disable')).toHaveLength(2));
    expect(callsTo(fetchMock, '/api/v1/account/reauth')[0]?.body).toEqual({ password });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('lists devices and signs out other ones', async () => {
    const fetchMock = fakeApi({
      routes: {
        'GET /api/v1/account/mfa': () => json({ enabled: true, recoveryCodesLeft: 10 }),
        'GET /api/v1/account/sessions': () =>
          json([
            {
              id: 's-1',
              userAgent: 'Mozilla/5.0 (Windows NT 10.0) Chrome/130.0 Safari/537.36',
              remember: true,
              createdAt: '2026-10-27T10:00:00Z',
              lastSeenAt: '2026-10-28T10:00:00Z',
              current: true,
            },
            {
              id: 's-2',
              userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Safari/604.1',
              remember: false,
              createdAt: '2026-10-26T10:00:00Z',
              lastSeenAt: '2026-10-26T12:00:00Z',
              current: false,
            },
          ]),
        'DELETE /api/v1/account/sessions/s-2': () => new Response(null, { status: 204 }),
      },
    });
    await renderApp('/settings/security');

    const current = (await screen.findByText('Chrome on Windows')).closest('li') as HTMLElement;
    expect(within(current).getByText('This device')).toBeInTheDocument();
    expect(within(current).queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument();
    const phone = screen.getByText('Safari on iOS').closest('li') as HTMLElement;
    await userEvent.click(within(phone).getByRole('button', { name: 'Sign out' }));
    await userEvent.click(screen.getByRole('button', { name: 'Sign out all other devices' }));

    await waitFor(() => {
      expect(callsTo(fetchMock, '/api/v1/account/sessions/s-2')).toHaveLength(1);
      expect(callsTo(fetchMock, '/api/v1/account/sessions/revoke-others')).toHaveLength(1);
    });
  });

  it('changes the password with the current one', async () => {
    const fetchMock = fakeApi({
      routes: { 'GET /api/v1/account/mfa': () => json({ enabled: true, recoveryCodesLeft: 10 }) },
    });
    await renderApp('/settings/security');

    await userEvent.type(await screen.findByLabelText('Current password'), 'old password long enough');
    await userEvent.type(screen.getByLabelText('New password'), password);
    await userEvent.type(screen.getByLabelText('Repeat new password'), password);
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }));

    expect(await screen.findByText('Password changed. Other devices were signed out.')).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/account/password')[0]?.body).toEqual({
      currentPassword: 'old password long enough',
      newPassword: password,
    });
  });
});

describe('Re-auth for team changes', () => {
  it('asks for the password before removing a member and then removes', async () => {
    const fetchMock = fakeApi({
      routes: {
        'GET /api/v1/team/members': () =>
          json([
            {
              userId: 'u-1',
              name: 'Md Yeakub',
              email: 'owner@example.com',
              role: 'owner',
              joinedAt: '2026-10-01T10:00:00Z',
            },
            {
              userId: 'u-3',
              name: 'Marco Member',
              email: 'marco@example.com',
              role: 'member',
              joinedAt: '2026-10-03T10:00:00Z',
            },
          ]),
        'GET /api/v1/team/invitations': () => json([]),
        'DELETE /api/v1/team/members/u-3': reauthThen(() => new Response(null, { status: 204 })),
      },
    });
    await renderApp('/settings/team');

    const marco = (await screen.findAllByText('Marco Member'))
      .map((e) => e.closest('li'))
      .find(Boolean) as HTMLElement;
    await userEvent.click(within(marco).getByRole('button', { name: 'Remove' }));
    await userEvent.click(within(marco).getByRole('button', { name: 'Yes, remove' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText('Password'), password);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    await waitFor(() => expect(callsTo(fetchMock, '/api/v1/team/members/u-3')).toHaveLength(2));
  });
});

describe('deviceLabel', () => {
  it('names common browsers and systems', () => {
    expect(deviceLabel('Mozilla/5.0 (Windows NT 10.0) AppleWebKit Chrome/130 Safari/537 Edg/130')).toEqual({
      browser: 'Edge',
      os: 'Windows',
    });
    expect(deviceLabel('Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) Firefox/131.0')).toEqual({
      browser: 'Firefox',
      os: 'macOS',
    });
    expect(deviceLabel(null)).toEqual({ browser: null, os: null });
  });
});
