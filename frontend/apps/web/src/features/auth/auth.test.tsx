import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { callsTo, fakeApi, json, problem, testSession } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';
import { safeReturnTo } from '../session/returnTo';

const password = 'tramonto sul lago verde';

async function fillLogin(email = 'owner@example.com', pass = password) {
  await userEvent.type(await screen.findByLabelText('Email'), email);
  await userEvent.type(screen.getByLabelText('Password'), pass);
}

describe('Sign in', () => {
  it('sends signed-out users to login and back to the page they wanted', async () => {
    const fetchMock = fakeApi({ session: null, post: { '/api/v1/auth/login': () => json(testSession) } });
    await renderApp('/notifications');

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    await fillLogin();
    await userEvent.click(screen.getByRole('checkbox', { name: 'Remember me' }));
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('heading', { name: 'Notifications', level: 1 })).toBeInTheDocument();
    const [login] = callsTo(fetchMock, '/api/v1/auth/login');
    expect(login?.body).toEqual({ email: 'owner@example.com', password, rememberMe: true });
    expect(login?.headers.get('X-XSRF-TOKEN')).toBe('test-csrf-token');
  });

  it('shows the same message for a wrong password, in the UI language', async () => {
    fakeApi({
      session: null,
      post: { '/api/v1/auth/login': () => problem(401, 'auth.invalid_credentials') },
    });
    await renderApp('/login', 'it');

    await userEvent.type(await screen.findByLabelText('Email'), 'owner@example.com');
    await userEvent.type(screen.getByLabelText('Password'), 'wrong');
    await userEvent.click(screen.getByRole('button', { name: 'Accedi' }));

    expect(await screen.findByText('Email o password non corretti.')).toBeInTheDocument();
  });

  it('offers a new confirmation link when the email is not verified yet', async () => {
    fakeApi({ session: null, post: { '/api/v1/auth/login': () => problem(403, 'auth.email_not_verified') } });
    await renderApp('/login');

    await fillLogin('new@example.com');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await userEvent.click(await screen.findByRole('link', { name: 'Send a new link' }));

    expect(await screen.findByText(/We sent a confirmation link to new@example.com/)).toBeInTheDocument();
  });

  it('tells the user when there were too many attempts', async () => {
    fakeApi({ session: null, post: { '/api/v1/auth/login': () => new Response(null, { status: 429 }) } });
    await renderApp('/login');

    await fillLogin();
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(
      await screen.findByText('Too many attempts. Wait a few minutes and try again.'),
    ).toBeInTheDocument();
  });

  it('retries once with a fresh CSRF token when the old one was rejected', async () => {
    let attempts = 0;
    const fetchMock = fakeApi({
      session: null,
      post: {
        '/api/v1/auth/login': () => (++attempts === 1 ? problem(400, 'csrf.invalid') : json(testSession)),
      },
    });
    await renderApp('/login');

    await fillLogin();
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('heading', { name: 'Conversations' })).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/auth/login')).toHaveLength(2);
    expect(callsTo(fetchMock, '/api/v1/csrf').length).toBeGreaterThanOrEqual(2);
  });

  it('validates before calling the API', async () => {
    const fetchMock = fakeApi({ session: null });
    await renderApp('/login');

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in' }));

    expect(await screen.findAllByText('Required.')).not.toHaveLength(0);
    expect(callsTo(fetchMock, '/api/v1/auth/login')).toHaveLength(0);
  });

  it('sends signed-in users away from the login page', async () => {
    fakeApi();
    await renderApp('/login');

    expect(await screen.findByRole('heading', { name: 'Conversations' })).toBeInTheDocument();
  });

  it('never redirects outside the app after login', () => {
    expect(safeReturnTo('/notifications?x=1')).toBe('/notifications?x=1');
    expect(safeReturnTo('//evil.example')).toBe('/inbox');
    expect(safeReturnTo('/\\evil.example')).toBe('/inbox');
    expect(safeReturnTo('https://evil.example')).toBe('/inbox');
    expect(safeReturnTo(null)).toBe('/inbox');
  });
});

describe('Sign up', () => {
  it('creates the account in the UI language and asks to check email', async () => {
    const fetchMock = fakeApi({
      session: null,
      post: { '/api/v1/auth/signup': () => new Response(null, { status: 202 }) },
    });
    await renderApp('/signup', 'it');

    await userEvent.type(await screen.findByLabelText('Il tuo nome'), 'Giulia');
    await userEvent.type(screen.getByLabelText("Nome dell'attività"), 'Bottega Verde');
    await userEvent.type(screen.getByLabelText('Email'), 'giulia@example.com');
    await userEvent.type(screen.getByLabelText('Password'), password);
    await userEvent.click(screen.getByRole('button', { name: 'Crea account' }));

    expect(await screen.findByText(/giulia@example.com/)).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/auth/signup')[0]?.body).toEqual({
      name: 'Giulia',
      businessName: 'Bottega Verde',
      email: 'giulia@example.com',
      password,
      language: 'it',
    });
  });

  it('enforces the 12-character minimum and shows breached-password errors', async () => {
    fakeApi({ session: null, post: { '/api/v1/auth/signup': () => problem(400, 'auth.breached_password') } });
    await renderApp('/signup');

    await userEvent.type(await screen.findByLabelText('Your name'), 'Giulia');
    await userEvent.type(screen.getByLabelText('Business name'), 'Bottega');
    await userEvent.type(screen.getByLabelText('Email'), 'giulia@example.com');
    await userEvent.type(screen.getByLabelText('Password'), 'short');
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(await screen.findByText('Use 12–128 characters.')).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Password'), ' but now long enough');
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(await screen.findByText(/appears in known data breaches/)).toBeInTheDocument();
  });
});

describe('Email links', () => {
  it('verifies from the link, signs in and opens the inbox', async () => {
    const fetchMock = fakeApi({
      session: null,
      post: { '/api/v1/auth/verify-email': () => json(testSession) },
    });
    await renderApp('/verify-email?user=u-1&token=abc%2Bdef');

    expect(await screen.findByRole('heading', { name: 'Conversations' })).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/auth/verify-email')).toEqual([
      expect.objectContaining({ body: { userId: 'u-1', token: 'abc+def' } }),
    ]);
  });

  it('explains an invalid verification link and offers a new one', async () => {
    fakeApi({
      session: null,
      post: { '/api/v1/auth/verify-email': () => problem(400, 'auth.invalid_token') },
    });
    await renderApp('/verify-email?user=u-1&token=old');

    expect(await screen.findByRole('heading', { name: 'Link not valid' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Request a new link' })).toBeInTheDocument();
  });

  it('answers forgot-password the same way for every email', async () => {
    fakeApi({
      session: null,
      post: { '/api/v1/auth/forgot-password': () => new Response(null, { status: 202 }) },
    });
    await renderApp('/forgot-password');

    await userEvent.type(await screen.findByLabelText('Email'), 'someone@example.com');
    await userEvent.click(screen.getByRole('button', { name: 'Send reset link' }));

    expect(await screen.findByText(/If an account exists for this email/)).toBeInTheDocument();
  });

  it('resets the password and asks to sign in again', async () => {
    const fetchMock = fakeApi({ session: null });
    await renderApp('/reset-password?user=u-1&token=t-1');

    await userEvent.type(await screen.findByLabelText('New password'), password);
    await userEvent.type(screen.getByLabelText('Repeat new password'), 'something else entirely');
    await userEvent.click(screen.getByRole('button', { name: 'Save password' }));
    expect(await screen.findByText('The passwords do not match.')).toBeInTheDocument();

    await userEvent.clear(screen.getByLabelText('Repeat new password'));
    await userEvent.type(screen.getByLabelText('Repeat new password'), password);
    await userEvent.click(screen.getByRole('button', { name: 'Save password' }));

    expect(await screen.findByText('Password changed. Sign in with your new password.')).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/auth/reset-password')[0]?.body).toEqual({
      userId: 'u-1',
      token: 't-1',
      newPassword: password,
    });
  });
});

describe('Session', () => {
  it('shows the signed-in user and signs out on the server', async () => {
    const fetchMock = fakeApi();
    await renderApp('/inbox');

    expect(await screen.findByText('Md Yeakub')).toBeInTheDocument();
    expect(screen.getByText('Workspace owner')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Sign out' }));

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    await waitFor(() => expect(callsTo(fetchMock, '/api/v1/auth/logout')).toHaveLength(1));
  });
});
