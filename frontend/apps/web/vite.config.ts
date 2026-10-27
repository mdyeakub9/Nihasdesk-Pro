import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // Must match the API's allowed CORS origin (backend appsettings.Development.json).
    port: 5173,
    strictPort: true,
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
