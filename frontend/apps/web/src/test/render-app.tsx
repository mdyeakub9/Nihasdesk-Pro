import { initI18n, type Language } from '@nihasdesk/i18n';
import { QueryClient } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import { createMemoryRouter } from 'react-router';
import { App } from '../App';
import { routes } from '../routes';

/** Renders the whole app at a route with fresh providers and no query retries. */
export async function renderApp(path = '/', language: Language = 'en') {
  const i18n = await initI18n(language);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  return render(<App queryClient={queryClient} i18n={i18n} router={router} />);
}
