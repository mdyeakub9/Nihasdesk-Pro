import { describe, expect, it } from 'vitest';
import en from './locales/en.json';
import itLocale from './locales/it.json';

function keys(value: unknown, prefix = ''): string[] {
  if (typeof value !== 'object' || value === null) return [prefix];
  return Object.entries(value).flatMap(([key, child]) => keys(child, prefix ? `${prefix}.${key}` : key));
}

describe('locales', () => {
  it('Italian has exactly the same keys as English', () => {
    expect(keys(itLocale).sort()).toEqual(keys(en).sort());
  });
});
