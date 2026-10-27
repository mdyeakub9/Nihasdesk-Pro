import { QueryClientProvider, type QueryClient } from '@tanstack/react-query';
import type { i18n } from 'i18next';
import { I18nextProvider } from 'react-i18next';
import { RouterProvider, type createBrowserRouter } from 'react-router';

type AppProps = {
  queryClient: QueryClient;
  i18n: i18n;
  router: ReturnType<typeof createBrowserRouter>;
};

export function App({ queryClient, i18n, router }: AppProps) {
  return (
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </I18nextProvider>
  );
}
