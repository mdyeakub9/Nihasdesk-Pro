# i18n

Translations (en, it first).

- `src/locales/<lang>.json` — one file per language; a test checks that all languages have the same keys.
- `initI18n()` — creates the i18next instance; default language from the browser, nothing cached in browser storage.
