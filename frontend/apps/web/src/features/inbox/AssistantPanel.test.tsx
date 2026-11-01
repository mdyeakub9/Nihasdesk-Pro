import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { callsTo, json, problem, testSession } from '../../test/fake-api';
import { defaultSuggestion, fakeInboxApi } from '../../test/inbox-api';
import { renderApp } from '../../test/render-app';

const panel = () => screen.findByRole('complementary', { name: 'Customer details' });
const replyBox = () => screen.findByRole('textbox', { name: 'Reply' });
const sent = (fetchMock: ReturnType<typeof fakeInboxApi>['fetchMock']) =>
  callsTo(fetchMock, '/api/v1/conversations/c-giulia/messages')
    .map((c) => c.body)
    .filter(Boolean);

async function openAssistant() {
  const side = await panel();
  await userEvent.click(within(side).getByRole('tab', { name: 'AI assistant' }));
  return side;
}

describe('AI assistant', () => {
  it('switches the side panel between Customer and AI assistant', async () => {
    fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    const side = await panel();
    expect(within(side).getByRole('tab', { name: 'Customer' })).toHaveAttribute('aria-selected', 'true');
    expect(within(side).getByText('giulia.rossi@example.com')).toBeInTheDocument();

    await openAssistant();
    expect(within(side).getByRole('button', { name: 'Suggest a reply' })).toBeVisible();
    await userEvent.click(within(side).getByRole('tab', { name: 'Customer' }));
    expect(within(side).getByText('giulia.rossi@example.com')).toBeVisible();
  });

  it('shows the summary, the suggested reply and its sources; insert fills the composer and sends nothing', async () => {
    const { fetchMock } = fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    const side = await openAssistant();
    await userEvent.click(within(side).getByRole('button', { name: 'Suggest a reply' }));

    const result = await within(side).findByRole('region', { name: 'AI suggestion' });
    expect(result).toHaveTextContent(defaultSuggestion.summary);
    expect(result).toHaveTextContent(defaultSuggestion.reply);
    expect(result).toHaveTextContent('Based on: product catalog, return policy');
    expect(within(result).queryByRole('button', { name: /Send/ })).not.toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/conversations/c-giulia/assist')[0]?.body).toEqual({
      style: 'default',
      language: 'en',
    });

    await userEvent.click(within(result).getByRole('button', { name: 'Insert into reply' }));
    expect(await replyBox()).toHaveValue(defaultSuggestion.reply);
    expect(sent(fetchMock)).toEqual([]);
  });

  it('asks again for a shorter, friendlier or new suggestion', async () => {
    const { fetchMock } = fakeInboxApi({
      assist: (_c, body) => json({ ...defaultSuggestion, reply: `Reply (${body.style})` }),
    });
    await renderApp('/inbox/c-giulia');

    const side = await openAssistant();
    await userEvent.click(within(side).getByRole('button', { name: 'Suggest a reply' }));
    const result = await within(side).findByRole('region', { name: 'AI suggestion' });

    await userEvent.click(within(result).getByRole('button', { name: 'Shorter' }));
    expect(await within(result).findByText('Reply (shorter)')).toBeInTheDocument();
    await userEvent.click(within(result).getByRole('button', { name: 'Friendlier' }));
    expect(await within(result).findByText('Reply (friendlier)')).toBeInTheDocument();
    await userEvent.click(within(result).getByRole('button', { name: 'Regenerate' }));
    expect(await within(result).findByText('Reply (default)')).toBeInTheDocument();
    expect(
      callsTo(fetchMock, '/api/v1/conversations/c-giulia/assist').map(
        (c) => (c.body as { style: string }).style,
      ),
    ).toEqual(['default', 'shorter', 'friendlier', 'default']);
  });

  it('explains a held-back suggestion without an insert button', async () => {
    fakeInboxApi({
      assist: () =>
        json({ summary: null, reply: null, sources: [], blocked: 'price_not_from_tool', failed: null }),
    });
    await renderApp('/inbox/c-giulia');

    const side = await openAssistant();
    await userEvent.click(within(side).getByRole('button', { name: 'Suggest a reply' }));
    const result = await within(side).findByRole('region', { name: 'AI suggestion' });
    expect(within(result).getByRole('status')).toHaveTextContent('couldn’t write a safe suggestion');
    expect(within(result).queryByRole('button', { name: 'Insert into reply' })).not.toBeInTheDocument();
  });

  it('explains when the AI is off or the daily limit is reached', async () => {
    fakeInboxApi({ assist: () => problem(409, 'ai.off') });
    await renderApp('/inbox/c-giulia');

    const side = await openAssistant();
    await userEvent.click(within(side).getByRole('button', { name: 'Suggest a reply' }));
    expect(await within(side).findByText(/AI is switched off/)).toBeInTheDocument();
  });

  it('offers Save as knowledge to an owner after sending an edited suggestion', async () => {
    const { fetchMock } = fakeInboxApi({ routes: { 'POST /api/v1/ai/answers': () => json({}, 201) } });
    await renderApp('/inbox/c-giulia');

    const side = await openAssistant();
    await userEvent.click(within(side).getByRole('button', { name: 'Suggest a reply' }));
    await userEvent.click(await within(side).findByRole('button', { name: 'Insert into reply' }));
    const box = await replyBox();
    await userEvent.type(box, ' Spedizione gratis.{Control>}{Enter}{/Control}');

    const offer = await screen.findByRole('region', { name: 'Save as knowledge' });
    expect(within(offer).getByRole('textbox', { name: 'Customer question' })).toHaveValue(
      'Ciao! Avete la borsa in nero?',
    );
    expect(within(offer).getByRole('textbox', { name: 'Answer' })).toHaveValue(
      `${defaultSuggestion.reply} Spedizione gratis.`,
    );
    await userEvent.click(within(offer).getByRole('button', { name: 'Save as knowledge' }));

    await waitFor(() =>
      expect(screen.queryByRole('region', { name: 'Save as knowledge' })).not.toBeInTheDocument(),
    );
    expect(callsTo(fetchMock, '/api/v1/ai/answers')[0]?.body).toEqual({
      question: 'Ciao! Avete la borsa in nero?',
      answer: `${defaultSuggestion.reply} Spedizione gratis.`,
    });
  });

  it('does not offer Save as knowledge for an unedited suggestion or to members', async () => {
    fakeInboxApi({ session: { ...testSession, role: 'member' } });
    await renderApp('/inbox/c-giulia');

    const side = await openAssistant();
    await userEvent.click(within(side).getByRole('button', { name: 'Suggest a reply' }));
    await userEvent.click(await within(side).findByRole('button', { name: 'Insert into reply' }));
    await userEvent.type(await replyBox(), ' Edited{Control>}{Enter}{/Control}');

    await waitFor(async () => expect(await replyBox()).toHaveValue(''));
    expect(screen.queryByRole('region', { name: 'Save as knowledge' })).not.toBeInTheDocument();
  });
});
