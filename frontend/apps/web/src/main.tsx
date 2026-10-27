import { initI18n } from '@nihasdesk/i18n';
import { QueryClient } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createBrowserRouter } from 'react-router';
import { App } from './App';
import { routes } from './routes';
import '@fontsource-variable/inter';
import './styles.css';

const root = document.getElementById('root');
if (!root) {
  throw new Error('Root element #root is missing.');
}

const i18n = await initI18n();
const queryClient = new QueryClient({ defaultOptions: { queries: { retry: 1 } } });
const router = createBrowserRouter(routes);

createRoot(root).render(
  <StrictMode>
    <App queryClient={queryClient} i18n={i18n} router={router} />
  </StrictMode>,
);
