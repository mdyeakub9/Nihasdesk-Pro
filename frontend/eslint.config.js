import js from '@eslint/js';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['**/dist/**', '**/coverage/**', '**/node_modules/**'] },
  js.configs.recommended,
  ...tseslint.configs.strict,
  {
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      globals: globals.browser,
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      // SEC-02: raw HTML injection is forbidden; sanitize with DOMPurify in a reviewed component if ever needed.
      'no-restricted-syntax': [
        'error',
        {
          selector: "JSXAttribute[name.name='dangerouslySetInnerHTML']",
          message: 'dangerouslySetInnerHTML is forbidden (SEC-02).',
        },
      ],
      // SEC-14: sessions live in HttpOnly cookies; never keep tokens in web storage.
      'no-restricted-globals': [
        'error',
        { name: 'localStorage', message: 'Do not use localStorage for app data or tokens (SEC-14).' },
        { name: 'sessionStorage', message: 'Do not use sessionStorage for app data or tokens (SEC-14).' },
      ],
      'no-restricted-properties': [
        'error',
        { object: 'window', property: 'localStorage', message: 'Do not use localStorage (SEC-14).' },
        { object: 'window', property: 'sessionStorage', message: 'Do not use sessionStorage (SEC-14).' },
      ],
    },
  },
  {
    files: ['**/*.config.{js,ts}'],
    languageOptions: {
      globals: globals.node,
    },
  },
);
