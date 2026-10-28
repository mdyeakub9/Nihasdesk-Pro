import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

/** Local API (backend launchSettings.json). */
const apiTarget = 'https://localhost:5080';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    strictPort: true,
    // The browser talks only to this origin; Vite forwards API calls. Session and CSRF cookies are
    // SameSite and would not travel from http://localhost:5173 to https://localhost:5080 otherwise.
    // `secure: false` only skips verifying the local .NET dev certificate inside Node.
    proxy: {
      '/api': { target: apiTarget, secure: false },
      '/health': { target: apiTarget, secure: false },
    },
  },
  build: {
    // No public source maps in production (SEC-18).
    sourcemap: false,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
  },
});
