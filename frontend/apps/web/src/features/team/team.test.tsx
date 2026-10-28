import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { Role, Session } from '../session/types';
import { callsTo, fakeApi, json, problem, testSession } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';
import { can, canRemove } from './permissions';

const members = [
  {
    userId: 'u-1',
    name: 'Md Yeakub',
    email: 'owner@example.com',
    role: 'owner',
    joinedAt: '2026-10-01T10:00:00Z',
  },
  {
    userId: 'u-2',
    name: 'Anna Admin',
    email: 'anna@example.com',
    role: 'admin',
    joinedAt: '2026-10-02T10:00:00Z',
  },
  {
    userId: 'u-3',
    name: 'Marco Member',
    email: 'marco@example.com',
    role: 'member',
    joinedAt: '2026-10-03T10:00:00Z',
  },
];

const invitations = [
  {
    id: 'i-1',
    email: 'new@example.com',
    role: 'member',
    invitedAt: '2026-10-27T10:00:00Z',
    expiresAt: '2026-10-29T10:00:00Z',
  },
];

function sessionAs(role: Role, userId = { owner: 'u-1', admin: 'u-2', member: 'u-3' }[role]): Session {
  return { ...testSession, user: { ...testSession.user, id: userId }, role };
}

function teamApi(role: Role, extra: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    session: sessionAs(role),
    ...extra,
    routes: {
      'GET /api/v1/team/members': () => json(members),
      'GET /api/v1/team/invitations': () => json(invitations),
      ...extra.routes,
    },
  });
}

/** The list row for a person or invitation (the sidebar shows the signed-in name too). */
const row = async (name: string) =>
  (await screen.findAllByText(name)).map((element) => element.closest('li')).find(Boolean) as HTMLElement;

describe('Team permissions (mirror of docs/standards/permissions.md)', () => {
  it('matches the server matrix', () => {
    expect(can('owner', 'inviteAdmin')).toBe(true);
    expect(can('admin', 'invite')).toBe(true);
    expect(can('admin', 'inviteAdmin')).toBe(false);
    expect(can('admin', 'changeRole')).toBe(false);
    expect(can('member', 'invite')).toBe(false);
    expect(canRemove('admin', 'member')).toBe(true);
    expect(canRemove('admin', 'admin')).toBe(false);
    expect(canRemove('owner', 'admin')).toBe(true);
    expect(canRemove('owner', 'owner')).toBe(false);
  });
});

describe('Settings → Team', () => {
  it('Owner invites as Admin, changes roles and removes after confirming', async () => {
    const fetchMock = teamApi('owner', {
      post: { '/api/v1/team/invitations': () => json({ ...invitations[0], id: 'i-2' }, 201) },
      routes: {
        'PATCH /api/v1/team/members/u-3': () => new Response(null, { status: 204 }),
        'DELETE /api/v1/team/members/u-2': () => new Response(null, { status: 204 }),
      },
    });
    await renderApp('/settings');

    expect(await screen.findByRole('heading', { name: 'Invite a team member' })).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Email'), 'luca@example.com');
    await userEvent.selectOptions(screen.getByLabelText('Role'), 'admin');
    await userEvent.click(screen.getByRole('button', { name: 'Send invitation' }));
    expect(await screen.findByText(/Invitation sent/)).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/team/invitations').find((c) => c.body)?.body).toEqual({
      email: 'luca@example.com',
      role: 'admin',
    });

    await userEvent.selectOptions(screen.getByLabelText('Role of Marco Member'), 'admin');
    await waitFor(() =>
      expect(callsTo(fetchMock, '/api/v1/team/members/u-3')[0]?.body).toEqual({ role: 'admin' }),
    );

    const anna = await row('Anna Admin');
    await userEvent.click(within(anna).getByRole('button', { name: 'Remove' }));
    expect(callsTo(fetchMock, '/api/v1/team/members/u-2')).toHaveLength(0);
    await userEvent.click(within(anna).getByRole('button', { name: 'Yes, remove' }));
    await waitFor(() => expect(callsTo(fetchMock, '/api/v1/team/members/u-2')).toHaveLength(1));

    const owner = await row('Md Yeakub');
    expect(within(owner).queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument();
    expect(within(owner).getByText('Workspace owner')).toBeInTheDocument();
  });

  it('Admin can invite Members and remove Members only', async () => {
    teamApi('admin');
    await renderApp('/settings/team');

    const role = await screen.findByLabelText('Role');
    expect(within(role).queryByRole('option', { name: 'Admin' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Role of Marco Member')).not.toBeInTheDocument();
    expect(within(await row('Marco Member')).getByRole('button', { name: 'Remove' })).toBeInTheDocument();
    expect(within(await row('Anna Admin')).queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument();
  });

  it('Members see the team and nothing to change', async () => {
    const fetchMock = teamApi('member');
    await renderApp('/settings/team');

    expect(await screen.findByText('Anna Admin')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Invite a team member' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/team/invitations')).toHaveLength(0);
  });

  it('lists pending invitations with resend and revoke', async () => {
    const fetchMock = teamApi('owner', {
      routes: { 'DELETE /api/v1/team/invitations/i-1': () => new Response(null, { status: 204 }) },
      post: { '/api/v1/team/invitations/i-1/resend': () => json(invitations[0]) },
    });
    await renderApp('/settings/team');

    const pending = await row('new@example.com');
    await userEvent.click(within(pending).getByRole('button', { name: 'Resend' }));
    await userEvent.click(within(pending).getByRole('button', { name: 'Revoke' }));

    await waitFor(() => {
      expect(callsTo(fetchMock, '/api/v1/team/invitations/i-1/resend')).toHaveLength(1);
      expect(callsTo(fetchMock, '/api/v1/team/invitations/i-1')).toHaveLength(1);
    });
  });

  it('shows the API error when the server refuses', async () => {
    teamApi('owner', { post: { '/api/v1/team/invitations': () => problem(409, 'team.already_member') } });
    await renderApp('/settings/team');

    await userEvent.type(await screen.findByLabelText('Email'), 'anna@example.com');
    await userEvent.click(screen.getByRole('button', { name: 'Send invitation' }));

    expect(await screen.findByText('This person is already in the team.')).toBeInTheDocument();
  });
});

describe('Accept invitation', () => {
  const preview = {
    workspaceName: 'Bottega Verde',
    inviterName: 'Giulia',
    email: 'luca@example.com',
    role: 'member',
    expiresAt: '2026-10-30T10:00:00Z',
  };

  it('shows the invitation, creates the account and opens the inbox', async () => {
    const fetchMock = fakeApi({
      session: null,
      post: {
        '/api/v1/invitations/preview': () => json(preview),
        '/api/v1/invitations/accept': () => json(sessionAs('member')),
      },
    });
    await renderApp('/accept-invitation?token=tok-123');

    expect(await screen.findByRole('heading', { name: 'Join Bottega Verde' })).toBeInTheDocument();
    expect(screen.getByText(/Giulia invited luca@example.com as Team member/)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Your name'), 'Luca');
    await userEvent.type(screen.getByLabelText('Password'), 'tramonto sul lago verde');
    await userEvent.type(screen.getByLabelText('Repeat new password'), 'tramonto sul lago verde');
    await userEvent.click(screen.getByRole('button', { name: 'Join the team' }));

    expect(await screen.findByRole('heading', { name: 'Conversations' })).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/invitations/preview')[0]?.body).toEqual({ token: 'tok-123' });
    expect(callsTo(fetchMock, '/api/v1/invitations/accept')[0]?.body).toEqual({
      token: 'tok-123',
      name: 'Luca',
      password: 'tramonto sul lago verde',
    });
  });

  it('explains invalid or used links', async () => {
    fakeApi({
      session: null,
      post: { '/api/v1/invitations/preview': () => problem(404, 'team.invitation_invalid') },
    });
    await renderApp('/accept-invitation?token=old');

    expect(await screen.findByRole('heading', { name: 'Invitation not valid' })).toBeInTheDocument();
  });

  it('tells people with an existing account to sign in instead', async () => {
    fakeApi({
      session: null,
      post: {
        '/api/v1/invitations/preview': () => json(preview),
        '/api/v1/invitations/accept': () => problem(409, 'team.email_has_account'),
      },
    });
    await renderApp('/accept-invitation?token=tok-123');

    await userEvent.type(await screen.findByLabelText('Your name'), 'Luca');
    await userEvent.type(screen.getByLabelText('Password'), 'tramonto sul lago verde');
    await userEvent.type(screen.getByLabelText('Repeat new password'), 'tramonto sul lago verde');
    await userEvent.click(screen.getByRole('button', { name: 'Join the team' }));

    expect(await screen.findByText(/already has a Nihasdesk account/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Sign in' })).toBeInTheDocument();
  });
});
